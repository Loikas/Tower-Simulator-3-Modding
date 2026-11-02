using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TS3Modding.Diagnostics
{
    // Main-thread counters. No per-sample formatting, file I/O, or Stopwatch allocation.
    internal sealed class PerformanceWindow
    {
        private struct Counter { internal long Count,Over8,Over33; internal double Sum,Maximum; }
        private struct Spike { internal double Time,Milliseconds; internal string Name; }
        private readonly Dictionary<string,Counter> counters=new Dictionary<string,Counter>(StringComparer.Ordinal);
        private readonly Spike[] spikes=new Spike[32];
        private int spikeIndex,spikeCount;
        private long previousFrame;
        private readonly long origin=Stopwatch.GetTimestamp();
        private readonly int[] collections={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};
        private readonly long[] gcTotals=new long[3];
        internal static double Elapsed(long started)=>(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
        internal void Reset()
        {counters.Clear();previousFrame=0;spikeIndex=spikeCount=0;Array.Clear(gcTotals,0,gcTotals.Length);for(var i=0;i<3;i++)collections[i]=GC.CollectionCount(i);}
        internal void BeginFrame()
        {
            var now=Stopwatch.GetTimestamp();
            if(previousFrame!=0)Record("frame.interval",(now-previousFrame)*1000.0/Stopwatch.Frequency);
            previousFrame=now;
            for(var generation=0;generation<3;generation++){var count=GC.CollectionCount(generation);gcTotals[generation]+=Math.Max(0,count-collections[generation]);collections[generation]=count;}
        }
        internal void Record(string name,double milliseconds)
        {
            if(string.IsNullOrEmpty(name)||double.IsNaN(milliseconds)||double.IsInfinity(milliseconds)||milliseconds<0)return;
            Counter counter;if(!counters.TryGetValue(name,out counter)&&counters.Count>=64)return;
            counter.Count++;counter.Sum+=milliseconds;counter.Maximum=Math.Max(counter.Maximum,milliseconds);
            if(milliseconds>=8)counter.Over8++;if(milliseconds>=33)counter.Over33++;counters[name]=counter;
            if(milliseconds>=33){spikes[spikeIndex]=new Spike{Name=name,Milliseconds=milliseconds,Time=(Stopwatch.GetTimestamp()-origin)/(double)Stopwatch.Frequency};spikeIndex=(spikeIndex+1)%spikes.Length;spikeCount=Math.Min(spikeCount+1,spikes.Length);}
        }
        internal string Drain()
        {
            var text=new StringBuilder("UTC="+DateTime.UtcNow.ToString("O")+"\nWindow=approximately 30 real-time seconds; means include idle/cheap calls\nFrame intervals include native work, rendering, loading and focus/pause delays; they are not mod CPU time.\nNested timings overlap: do not add totals to their children.\n");
            text.AppendLine("GC collections gen0/gen1/gen2="+string.Join("/",gcTotals));
            text.AppendLine("Metric,Calls,MeanMs,MaxMs,CallsAtLeast8Ms,CallsAtLeast33Ms");
            foreach(var pair in counters){var c=pair.Value;text.Append(pair.Key).Append(',').Append(c.Count).Append(',').Append((c.Sum/c.Count).ToString("F3",CultureInfo.InvariantCulture)).Append(',').Append(c.Maximum.ToString("F3",CultureInfo.InvariantCulture)).Append(',').Append(c.Over8).Append(',').Append(c.Over33).AppendLine();}
            text.AppendLine("Recent >=33ms samples: seconds since profiler start,metric,milliseconds");
            for(var i=0;i<spikeCount;i++){var s=spikes[(spikeIndex-spikeCount+i+spikes.Length)%spikes.Length];text.Append(s.Time.ToString("F3",CultureInfo.InvariantCulture)).Append(',').Append(s.Name).Append(',').Append(s.Milliseconds.ToString("F3",CultureInfo.InvariantCulture)).AppendLine();}
            counters.Clear();Array.Clear(gcTotals,0,gcTotals.Length);spikeIndex=spikeCount=0;
            return text.ToString();
        }
    }
}
