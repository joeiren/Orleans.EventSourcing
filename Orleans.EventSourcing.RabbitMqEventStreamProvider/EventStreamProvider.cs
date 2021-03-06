﻿using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Content;

namespace Orleans.EventSourcing.RabbitMqEventStreamProvider
{
    public class EventStreamProvider : IEventStreamProvider
    {
        private static int _queueCount;
        private static string _deployId;
        private static readonly BlockingCollection<IModel> ChannelCollection = new BlockingCollection<IModel>();
        private const string EXCHANGE = "EventStreamRabbitMqExchange";
        private const string QUEUE = "EventStreamRabbitMqQueue";
        private static IConnectionFactory _connectionFactory;

        internal static void SetConnectionFactory(IConnectionFactory connectionFactory, string deployId, int queueCount)
        {
            _connectionFactory = connectionFactory;
            if (queueCount < 1)
                throw new ArgumentOutOfRangeException("queueCount", "queueCount must generate then 1");
            if (string.IsNullOrEmpty(deployId))
                throw new ArgumentOutOfRangeException("deployId", "deployId must not null or empty");

            _queueCount = queueCount;
            _deployId = deployId;
            var i = 10;
            var connection = _connectionFactory.CreateConnection();

            while (i > 0)
            {
                --i;
                ChannelCollection.Add(connection.CreateModel());
            }
            RegisterMq(queueCount);
        }

        internal EventStreamProvider()
        {
        }
        public Task<bool> PublishAsync(IEvent @event)
        {
            var tcs = new TaskCompletionSource<bool>();
            Task.Factory.StartNew(() =>
            {
                IModel channel = null;
                try
                {
                    var exchangeName = EXCHANGE + _deployId;
                    channel = ChannelCollection.Take();
                    var eventJson = JsonConvert.SerializeObject(@event);
                    var bytes = Encoding.UTF8.GetBytes(eventJson);
                    var build = new BytesMessageBuilder(channel);
                    build.WriteBytes(bytes);
                    var contentHeader = ((IBasicProperties)build.GetContentHeader());
                    var routeKey = (Math.Abs(@event.GrainId.GetHashCode()) % _queueCount).ToString();

                    contentHeader.DeliveryMode = 2;
                    contentHeader.Type = @event.TypeCode.ToString();
                    channel.ConfirmSelect();
                    channel.BasicPublish(exchangeName, routeKey, contentHeader, build.GetContentBody());
                    if (channel.WaitForConfirms(TimeSpan.FromSeconds(10)))
                    {
                        tcs.SetResult(true);
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetResult(true);
                }

                if (channel != null)
                {
                    //用完返回队列
                    ChannelCollection.TryAdd(channel);
                }

            });

            return tcs.Task;
        }

        private static void RegisterMq(int queueCount)
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                using (var channel = conn.CreateModel())
                {
                    for (int i = 0; i < queueCount; i++)
                    {
                        var exchangeName = EXCHANGE + _deployId;
                        var queueName = QUEUE + _deployId + i;
                        channel.ExchangeDeclare(exchangeName, ExchangeType.Direct, true, false, null);
                        channel.QueueDeclare(queueName, true, false, false, null);
                        channel.QueueBind(queueName, exchangeName, i.ToString());
                    }

                }
            }
        }
    }
}
