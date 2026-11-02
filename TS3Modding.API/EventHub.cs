using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.API
{
    // Dispatch occurs on the caller's thread. The framework calls it on Unity's main thread.
    public sealed class EventHub : IEventStream
    {
        private readonly Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();
        private readonly Action<Exception> onError;
        public EventHub(Action<Exception> onError) { this.onError = onError ?? throw new ArgumentNullException(nameof(onError)); }
        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            List<Delegate> list;
            if (!handlers.TryGetValue(typeof(T), out list)) handlers.Add(typeof(T), list = new List<Delegate>());
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }
        public void Publish<T>(T value)
        {
            List<Delegate> list;
            if (!handlers.TryGetValue(typeof(T), out list)) return;
            // Snapshot supports handlers disposing subscriptions during dispatch.
            foreach (var handler in list.ToArray())
            {
                try { ((Action<T>)handler)(value); }
                catch (Exception error) { onError(error); }
            }
        }
        private sealed class Subscription : IDisposable
        {
            private Action remove;
            public Subscription(Action remove) { this.remove = remove; }
            public void Dispose() { var action = remove; remove = null; action?.Invoke(); }
        }
    }
}
