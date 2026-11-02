using System;
using System.Collections.Generic;
namespace TS3Modding.Tugs
{
    internal static class TowParkingGeometry
    {
        // Native Router can append steering/look-ahead points beyond trgpoint. Those are not parking positions.
        internal static TowPoint[] Trim(TowPoint[] route,TowPoint stop)
        {
            double best=2;int segment=-1;
            for(int i=0;i<route.Length-1;i++)
            {
                var a=route[i];var b=route[i+1];double x=b.X-a.X,y=b.Y-a.Y,z=b.Z-a.Z;
                var size=x*x+y*y+z*z;if(size<.000001)continue;
                var t=Math.Max(0,Math.Min(1,((stop.X-a.X)*x+(stop.Y-a.Y)*y+(stop.Z-a.Z)*z)/size));
                var projected=new TowPoint((float)(a.X+t*x),(float)(a.Y+t*y),(float)(a.Z+t*z));
                var distance=stop.Distance(projected);if(distance<=best){best=distance;segment=i;}
            }
            if(segment<0)throw new InvalidOperationException("Native route does not reach the aircraft's stand stop position.");
            var result=new List<TowPoint>();for(int i=0;i<=segment;i++)if(route[i].Distance(stop)>.01)result.Add(route[i]);result.Add(stop);
            if(result.Count<2)throw new InvalidOperationException("Stand approach is too short.");return result.ToArray();
        }
        // Native turn scaffolding can precede the actual starting position.
        // Remove it only when the real position lies on its first 100 units.
        internal static TowPoint[] TrimStart(TowPoint[] route,TowPoint start)
        {
            double distance=0;
            for(var i=1;i<route.Length&&distance<=100;i++)
            {
                var a=route[i-1];var b=route[i];var length=a.Distance(b);distance+=length;
                if(length<.000001)continue;
                double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z;
                var t=Math.Max(0,Math.Min(1,((start.X-a.X)*dx+(start.Y-a.Y)*dy+(start.Z-a.Z)*dz)/(length*length)));
                if(distance-length+t*length>100)break;
                var projected=new TowPoint((float)(a.X+t*dx),(float)(a.Y+t*dy),(float)(a.Z+t*dz));
                if(start.Distance(projected)>2)continue;
                var result=new List<TowPoint>{start};
                for(var j=i;j<route.Length;j++)if(result[result.Count-1].Distance(route[j])>.01)result.Add(route[j]);
                if(result.Count>=2)return result.ToArray();
            }
            throw new InvalidOperationException("Native reroute does not connect to the aircraft's actual position.");
        }
        internal static bool Brake(float speed,float dt,double distance,float headingDifference,bool finalSegment)
            =>finalSegment&&speed>=0&&Math.Abs(headingDifference)<=25&&distance<=speed*speed/6d+speed*Math.Max(0,Math.Min(.1f,dt))+.35;
    }
}
