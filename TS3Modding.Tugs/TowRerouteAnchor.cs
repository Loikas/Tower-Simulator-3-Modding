using System;
using System.Linq;
namespace TS3Modding.Tugs
{
    internal static class TowRerouteAnchor
    {
        internal static double Distance(TowRoadSnapshot road,TowPoint p)
        {
            double best=double.PositiveInfinity;
            for(int i=1;i<road.Points.Length;i++)
            {
                var a=road.Points[i-1];var b=road.Points[i];double x=b.X-a.X,y=b.Y-a.Y,z=b.Z-a.Z,den=x*x+y*y+z*z;
                var t=den<.000001?0:Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Y-a.Y)*y+(p.Z-a.Z)*z)/den));
                best=Math.Min(best,p.Distance(new TowPoint((float)(a.X+x*t),(float)(a.Y+y*t),(float)(a.Z+z*t))));
            }
            return best;
        }
        internal static TowRoadSnapshot Resolve(TowRoadSnapshot[] roads,TowPoint position,int preferred)
        {
            var candidates=roads.Where(r=>(r.Type==0||r.Type==1||r.Type==3)&&r.Points!=null&&r.Points.Length>=2).Select(r=>new{Road=r,Distance=Distance(r,position)}).Where(r=>r.Distance<=15).OrderBy(r=>r.Distance).ThenBy(r=>r.Road.Id).ToArray();
            if(candidates.Length==0)throw new InvalidOperationException("No connected ground road is close enough to check the new tow route.");
            var native=candidates.FirstOrDefault(r=>r.Road.Id==preferred&&r.Distance<=candidates[0].Distance+1);
            return (native??candidates[0]).Road;
        }
    }
}
