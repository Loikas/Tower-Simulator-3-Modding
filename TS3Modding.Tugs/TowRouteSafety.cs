using System;
using System.Linq;
namespace TS3Modding.Tugs
{
    internal static class TowRouteSafety
    {
        // Request previews are estimates. Native turns and controller vias need headroom.
        internal const double MaximumRequestLength=6000;
        internal const double MaximumLength=MaximumRequestLength*2;
        // A native route is still only a candidate: this does not assert swept-wing clearance.
        internal static string Check(TowPoint[] path,TowPoint start,TowPoint end,TowRoadSnapshot[] roads,bool allowRunwayCrossings=false)
        {
            if(path==null||path.Length<2||path.Length>100000||path.Any(p=>!p.Finite))return "Native planner returned missing or invalid geometry.";
            if(start.Distance(path[0])>25||end.Distance(path[path.Length-1])>25)return "Native route does not match the requested endpoints.";
            double length=0;int comparisons=0;
            // Crossing-enabled routes are checked by TowRunwayCrossings, including envelopes and clearance.
            // Do not spend a quadratic budget on intersections whose result is unused here.
            var runways=allowRunwayCrossings?new TowRoadSnapshot[0]:roads.Where(r=>r.Type==2).ToArray();
            for(int i=1;i<path.Length;i++)
            {
                var distance=path[i-1].Distance(path[i]);length+=distance;
                if(distance>100)return "Native route contains a geometry gap over 100 world units.";
                foreach(var road in runways)
                    for(int j=1;j<road.Points.Length;j++)
                    {
                        if(++comparisons>2000000)return "Runway geometry is too complex for this route check.";
                        if(!allowRunwayCrossings&&Intersects(path[i-1],path[i],road.Points[j-1],road.Points[j]))return "Native route crosses a runway. Runway-crossing tow clearance is not implemented.";
                    }
            }
            if(length>MaximumLength)return "Native route length "+length.ToString("F1")+" exceeds the tow route limit (12000 world units).";
            return null;
        }
        private static bool Intersects(TowPoint a,TowPoint b,TowPoint c,TowPoint d)
        {
            const double epsilon=.001;
            Func<TowPoint,TowPoint,TowPoint,double> side=(p,q,r)=>((double)q.X-p.X)*(r.Z-p.Z)-((double)q.Z-p.Z)*(r.X-p.X);
            Func<TowPoint,TowPoint,TowPoint,bool> on=(p,q,r)=>r.X>=Math.Min(p.X,q.X)-epsilon&&r.X<=Math.Max(p.X,q.X)+epsilon&&r.Z>=Math.Min(p.Z,q.Z)-epsilon&&r.Z<=Math.Max(p.Z,q.Z)+epsilon;
            var x=side(a,b,c);var y=side(a,b,d);var u=side(c,d,a);var v=side(c,d,b);
            if(Math.Abs(x)<epsilon&&on(a,b,c)||Math.Abs(y)<epsilon&&on(a,b,d)||Math.Abs(u)<epsilon&&on(c,d,a)||Math.Abs(v)<epsilon&&on(c,d,b))return true;
            return (x>0)!=(y>0)&&(u>0)!=(v>0);
        }
    }
}
