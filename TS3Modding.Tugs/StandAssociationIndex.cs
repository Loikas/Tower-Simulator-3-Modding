using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace TS3Modding.Tugs
{
    // One immutable observation snapshot, never retained for an issued command.
    internal sealed class StandAssociationIndex<T> where T:class
    {
        internal sealed class Entry { internal T Aircraft;internal object Stand,OldStand,Gate;internal bool Parked; }
        private sealed class ReferenceComparer:IEqualityComparer<object>
        { public new bool Equals(object a,object b)=>ReferenceEquals(a,b);public int GetHashCode(object value)=>RuntimeHelpers.GetHashCode(value); }
        private readonly Dictionary<object,List<Entry>> associated=new Dictionary<object,List<Entry>>(new ReferenceComparer());
        private readonly Dictionary<object,List<Entry>> current=new Dictionary<object,List<Entry>>(new ReferenceComparer());
        internal StandAssociationIndex(IEnumerable<Entry> entries)
        {
            foreach(var entry in entries){Add(current,entry.Stand,entry);Add(associated,entry.Stand,entry);if(!ReferenceEquals(entry.Stand,entry.OldStand))Add(associated,entry.OldStand,entry);}
        }
        private static void Add(Dictionary<object,List<Entry>> index,object stand,Entry entry)
        {if(stand==null)return;List<Entry> rows;if(!index.TryGetValue(stand,out rows))index.Add(stand,rows=new List<Entry>());rows.Add(entry);}
        internal T Candidate(object stand,object gate)
        {
            List<Entry> rows;if(stand==null||gate==null||!associated.TryGetValue(stand,out rows))return null;
            T single=null,parked=null;int count=0,parkedCount=0;
            foreach(var row in rows){if(row.Gate!=null&&!ReferenceEquals(row.Gate,gate))continue;single=row.Aircraft;count++;if(ReferenceEquals(row.Stand,stand)&&row.Parked){parked=row.Aircraft;parkedCount++;}}
            return parkedCount>0?(parkedCount==1?parked:null):(count==1?single:null);
        }
        internal bool ClaimedByOther(object stand,T plane)
        {List<Entry> rows;return stand!=null&&current.TryGetValue(stand,out rows)&&rows.Any(row=>!ReferenceEquals(row.Aircraft,plane));}
    }
}
