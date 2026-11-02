using System;
namespace TS3Modding.Tugs
{
    // Admission state only: native geometry/holding and command adapters must validate first.
    internal sealed class TowCrossingClearance
    {
        private readonly int[] runways;private int next;private bool cleared,entered;
        internal TowCrossingClearance(int[] physicalRunwayIds)
        {if(physicalRunwayIds==null)throw new ArgumentNullException(nameof(physicalRunwayIds));runways=(int[])physicalRunwayIds.Clone();}
        internal int? NextRunway=>next<runways.Length?(int?)runways[next]:null;
        internal bool MayEnter=>NextRunway.HasValue&&cleared;
        internal bool Occupying=>entered;
        internal bool Complete=>next==runways.Length;
        internal int Event=>next;
        internal bool Clear(int physicalRunwayId,int crossingEvent)
        {if(crossingEvent!=next||NextRunway!=physicalRunwayId||entered)return false;cleared=true;return true;}
        internal void Enter()
        {if(!MayEnter||entered)throw new InvalidOperationException("No clearance for this crossing.");entered=true;}
        internal void TailExited()
        {if(!entered)throw new InvalidOperationException("Crossing entry has not been observed.");entered=false;cleared=false;next++;}
        internal void RevokeBeforeEntry(){if(!entered)cleared=false;}
        // Manual hold/continue deliberately has no operation here: it cannot grant clearance.
    }
}
