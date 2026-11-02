using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.API
{
    public sealed class ModuleManager : IDisposable
    {
        private readonly IFrameworkContext context;
        private readonly Action<string, Exception> onError;
        private readonly Dictionary<string, Entry> modules = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool disposed;
        public ModuleManager(IFrameworkContext context, Action<string, Exception> onError)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.onError = onError ?? throw new ArgumentNullException(nameof(onError));
        }
        public int Count => modules.Count;
        public IDisposable Register(ITS3Module module)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ModuleManager));
            if (module == null) throw new ArgumentNullException(nameof(module));
            var id = module.Id;
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module ID must be nonempty.");
            if (modules.ContainsKey(id)) throw new InvalidOperationException("Duplicate module ID: " + id);
            var scope = new ScopedContext(context);
            var entry = new Entry(module, scope);
            modules.Add(id, entry);
            try { module.Initialize(scope); }
            catch { Remove(id, entry); throw; }
            return new Registration(() => Remove(id, entry));
        }
        public void Tick() => Tick(null);
        public void Tick(Action<string,double> timing)
        {
            foreach (var pair in modules.ToArray())
            {
                Entry current;
                if (!modules.TryGetValue(pair.Key, out current) || !ReferenceEquals(current, pair.Value)) continue;
                var started=timing==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
                try { current.Module.Tick(); }
                catch (Exception error) { Remove(pair.Key, current); onError(pair.Key, error); }
                finally { if(timing!=null)try{timing(pair.Key,(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency);}catch{/* Diagnostic observers must not disable modules. */} }
            }
        }
        private void Remove(string id, Entry expected)
        {
            Entry current;
            if (!modules.TryGetValue(id, out current) || !ReferenceEquals(current, expected)) return;
            modules.Remove(id);
            expected.Scope.Dispose();
            try { expected.Module.Dispose(); } catch (Exception error) { onError(id, error); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var pair in modules.ToArray()) Remove(pair.Key, pair.Value);
        }
        private sealed class Entry
        {
            public Entry(ITS3Module module, ScopedContext scope) { Module = module; Scope = scope; }
            public ITS3Module Module { get; }
            public ScopedContext Scope { get; }
        }
        private sealed class Registration : IDisposable
        {
            private Action remove;
            public Registration(Action remove) { this.remove = remove; }
            public void Dispose() { var action = remove; remove = null; action?.Invoke(); }
        }
        private sealed class ScopedContext : IFrameworkContext, IEventStream, IDisposable
        {
            private readonly IFrameworkContext parent;
            private readonly List<IDisposable> subscriptions = new List<IDisposable>();
            private bool disposed;
            public ScopedContext(IFrameworkContext parent) { this.parent = parent; }
            public IEventStream Events => this;
            public IAircraftRegistry Aircraft => parent.Aircraft;
            public string GameAssemblySha256 => parent.GameAssemblySha256;
            public bool ObservationAvailable => parent.ObservationAvailable;
            public void Log(string message) => parent.Log(message);
            public IDisposable Subscribe<T>(Action<T> handler)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ScopedContext));
                if (handler == null) throw new ArgumentNullException(nameof(handler));
                var subscription = parent.Events.Subscribe<T>(value => { if (!disposed) handler(value); });
                subscriptions.Add(subscription);
                return subscription;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (var subscription in subscriptions) subscription.Dispose();
                subscriptions.Clear();
            }
        }
    }
}
