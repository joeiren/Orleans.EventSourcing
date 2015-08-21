﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Orleans.EventSourcing
{
    public class EventStore<TGrain, TState>
        where TGrain : IEventSourcingGrain<TState>
        where TState : EventSourcingState
    {
        private static readonly IEventStoreProvider eventStoreProvider = EventStoreProviderManager.GetProvider<TGrain>();
        private long afterSnapshotsEventCount;
        private TGrain grain;
        private IEventStore eventStore;
        private static JsonSerializerSettings jsonsetting = new JsonSerializerSettings()
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };
        private TState State
        {
            get
            {
                return this.grain.GetState();
            }
        }

        private EventStore() { }

        public static async Task<EventStore<TGrain, TState>> Initialize(TGrain grain, long afterSnapshotsEventCount = 100)
        {
            var instance = new EventStore<TGrain, TState>();
            instance.grain = grain;
            instance.afterSnapshotsEventCount = afterSnapshotsEventCount;
            instance.eventStore = await eventStoreProvider.Create<TGrain>();

            return instance;
        }
        public async Task WriteEvent(IEvent @event)
        {
            if (@event != null)
            {
                await eventStore.Append(@event);
                HandleEvent(@event);
            }

            if (@event.Version % this.afterSnapshotsEventCount == 0)
                await this.WriteSnapshot();
        }

        private Task WriteSnapshot()
        {
            return this.State.WriteStateAsync();
        }
        public async Task ReplayEvents()
        {
            var events = await eventStore.ReadFrom(this.grain.GetGrainId(), this.grain.GetState().Version + 1);

            if (events.Any())
            {
                events = events.OrderBy(et => et.Version);

                foreach (var evnt in events)
                {
                    HandleEvent(evnt);
                }
            }
        }

        private void HandleEvent(IEvent @event)
        {
            VerifyEvent(@event);
            var eventHandler = GrainInternalEventHandlerProvider.GetInternalEventHandler(this.grain.GetType(), @event.GetType());

            if (eventHandler == null)
            {
                throw new Exception(string.Format("Could not find event handler for [{0}] of [{1}]", @event.GetType().FullName, this.GetType().FullName));
            }

            eventHandler.Invoke(this.grain, @event);
            this.grain.GetState().Version = @event.Version;
        }
        private void VerifyEvent(IEvent @event)
        {
            if (@event.Version != this.grain.GetState().Version + 1)
            {
                throw new Exception(string.Format("invlid event version for [{0}] of [{1}]", @event.GetType().FullName, this.GetType().FullName));
            }
        }

    }
}
