using System;
using System.Collections.Generic;

namespace TS3Modding.PilotReadiness
{
    internal sealed class StartupSpeechQueue<T>
    {
        private readonly Queue<Tuple<double, T>> queue = new Queue<Tuple<double, T>>();
        public int Count => queue.Count;
        public bool Enqueue(T item, double now)
        {
            if (queue.Count >= 256) return false;
            queue.Enqueue(Tuple.Create(now, item)); return true;
        }
        public bool TryTake(bool ready, double now, out T item)
        {
            while (queue.Count > 0 && now - queue.Peek().Item1 > 90) queue.Dequeue();
            if (!ready || queue.Count == 0) { item = default(T); return false; }
            item = queue.Dequeue().Item2; return true;
        }
        public void Clear() { queue.Clear(); }
    }
}

