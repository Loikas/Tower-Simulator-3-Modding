using System;
using System.Linq;
namespace TS3Modding.Tugs
{
    internal sealed class TowNativeViaPlan
    {
        internal TowRoadSnapshot[] Requested,Planner;
        internal TowPreview Preview;
        internal static TowNativeViaPlan Create(TowRoadSnapshot[] roads,int source,int index,TowDestination target,string[] identifiers)
        {
            var groups=(identifiers??new string[0]).Select(id=>TowRouteInstructions.ResolveAll(roads,id)).ToArray();
            var preview=TowPreviewPlanner.Plan(roads,source,index,target,true,groups);
            if(!preview.Found||preview.ViaRoads.Length!=groups.Length)throw new InvalidOperationException(groups.Length==0?"No connected ground route reaches the requested stand.":"No connected route follows the requested taxiways in order.");
            var requested=preview.ViaRoads.Select(id=>roads.Single(r=>r.Id==id)).ToArray();
            // Crossing a runway geometrically does not require routing ON its road.
            // Match normal taxi planning: only controller-requested taxiways belong in _via.
            return new TowNativeViaPlan{Preview=preview,Requested=requested,Planner=requested};
        }
    }
}
