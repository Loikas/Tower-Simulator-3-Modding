using System;
using System.Collections.Generic;
using System.Linq;
namespace TS3Modding.Tugs
{
    internal static class RecoveryApproachPath
    {
        private static TowPoint Lerp(TowPoint a,TowPoint b,double t)=>new TowPoint((float)(a.X+(b.X-a.X)*t),(float)(a.Y+(b.Y-a.Y)*t),(float)(a.Z+(b.Z-a.Z)*t));
        private sealed class Band
        {
            internal TowRoadSnapshot Road;internal double MinX,MaxX,MinZ,MaxZ;
            internal bool Near(TowPoint p)=>p.X>=MinX&&p.X<=MaxX&&p.Z>=MinZ&&p.Z<=MaxZ;
        }
        private static TowPoint[] Sample(TowPoint[] path,double spacing)
        {
            var result=new List<TowPoint>{path[0]};
            for(var i=1;i<path.Length;i++){var length=path[i-1].Distance(path[i]);if(length<.001)continue;var count=(int)Math.Ceiling(length/spacing);if(count>10000||result.Count+count>20000)throw new InvalidOperationException("Tug approach geometry exceeds its sample limit");for(var j=1;j<=count;j++)result.Add(Lerp(path[i-1],path[i],(double)j/count));}
            return result.ToArray();
        }
        internal static TowPoint[] Build(TowPoint[] original,TowRoadSnapshot[] roads)
        {
            if(original==null||original.Length<2||original.Any(p=>!p.Finite))throw new InvalidOperationException("Invalid tug approach geometry");
            var bands=roads.Where(r=>r.Points!=null&&r.Points.Length>=2&&r.Type<=2).Select(r=>{var margin=Math.Max(20,r.ProtectionRadius+25);return new Band{Road=r,MinX=r.Points.Min(p=>p.X)-margin,MaxX=r.Points.Max(p=>p.X)+margin,MinZ=r.Points.Min(p=>p.Z)-margin,MaxZ=r.Points.Max(p=>p.Z)+margin};}).ToArray();
            var centre=Sample(Simplify(original),20);var shifted=centre.ToArray();var total=centre.Zip(centre.Skip(1),(a,b)=>a.Distance(b)).Sum();double travelled=0;
            for(var i=1;i<centre.Length-1;i++)
            {
                travelled+=centre[i-1].Distance(centre[i]);var p=centre[i];var nearby=bands.Where(b=>b.Near(p)).ToArray();
                var road=nearby.Where(b=>b.Road.Type==0&&b.Road.ProtectionRadius>0).Select(b=>new{b.Road,Distance=TowRerouteAnchor.Distance(b.Road,p)}).Where(b=>b.Distance<=3).OrderBy(b=>b.Distance).FirstOrDefault();
                if(road==null)continue;
                var offset=Math.Min(4,Math.Max(0,road.Road.ProtectionRadius/2-4));
                offset*=Math.Min(1,Math.Min(travelled/20,Math.Max(0,(total-travelled-35)/20)));
                foreach(var runway in nearby.Where(b=>b.Road.Type==2))offset*=Math.Max(0,Math.Min(1,(TowRerouteAnchor.Distance(runway.Road,p)-runway.Road.ProtectionRadius-10)/15));
                var dx=centre[i+1].X-centre[i-1].X;var dz=centre[i+1].Z-centre[i-1].Z;var length=Math.Sqrt(dx*dx+dz*dz);if(length<.001)continue;
                shifted[i]=new TowPoint((float)(p.X+dz/length*offset),p.Y,(float)(p.Z-dx/length*offset));
            }
            // Remove sub-sample jogs before rounding. Native sample spacing must not
            // cap an otherwise broad turn or introduce a turn back at a junction.
            shifted=Simplify(shifted);
            var rounded=new List<TowPoint>{shifted[0]};
            for(var i=1;i<shifted.Length-1;i++)
            {
                var a=shifted[i-1];var b=shifted[i];var c=shifted[i+1];var left=a.Distance(b);var right=b.Distance(c);if(left<.01||right<.01)continue;
                var cosine=((b.X-a.X)*(c.X-b.X)+(b.Z-a.Z)*(c.Z-b.Z))/(left*right);
                if(cosine>.9999||cosine<-.95){rounded.Add(b);continue;}
                var trim=Math.Min(16,Math.Min(left,right)*.45);var start=Lerp(b,a,trim/left);var end=Lerp(b,c,trim/right);
                TowPoint[] curve=null;
                var near=bands.Where(band=>band.Near(b)).ToArray();
                // Prefer a broad curve; reduce its radius only when pavement
                // constraints require it. Retain the checked path if none fits.
                for(var attempt=0;attempt<5;attempt++)
                {
                    start=Lerp(b,a,trim/left);end=Lerp(b,c,trim/right);
                    var candidate=Enumerable.Range(0,25).Select(n=>{var t=n/24d;return Lerp(Lerp(start,b,t),Lerp(b,end,t),t);}).ToArray();
                    if(candidate.All(p=>near.Any(band=>TowRerouteAnchor.Distance(band.Road,p)<=Math.Max(0,band.Road.ProtectionRadius/2-2))||DistanceToPolyline(p,centre)<=2)){curve=candidate;break;}
                    trim*=.65;
                }
                if(curve==null){rounded.Add(b);continue;}
                rounded.AddRange(curve);
            }
            rounded.Add(shifted[shifted.Length-1]);return Sample(rounded.ToArray(),1);
        }
        private static double DistanceToPolyline(TowPoint p,TowPoint[] path)=>TowRerouteAnchor.Distance(new TowRoadSnapshot{Points=path},p);
        // Discard redundant native straight-line samples before constructing
        // turns, so their spacing does not dictate the corner radius.
        private static TowPoint[] Simplify(TowPoint[] path)
        {
            var result=new List<TowPoint>{path[0]};
            for(var i=1;i<path.Length-1;i++)
            {
                var a=result[result.Count-1];var b=path[i];var c=path[i+1];
                var direct=a.Distance(c);
                if(direct>.001&&a.Distance(b)+b.Distance(c)-direct<.5&&DistanceToPolyline(b,new[]{a,c})<.25)continue;
                result.Add(b);
            }
            result.Add(path[path.Length-1]);return result.ToArray();
        }
        // Heading comes from a fixed spatial window on the same checked path,
        // independent of frame duration and the last frame's displacement.
        internal static TowPoint Heading(TowPoint[] path,int next,TowPoint position)
        {
            next=Math.Max(1,Math.Min(path.Length-1,next));
            var behind=Along(path,position,next-1,-1,1.5);
            var ahead=Along(path,position,next,1,1.5);
            var dx=ahead.X-behind.X;var dz=ahead.Z-behind.Z;var length=Math.Sqrt(dx*dx+dz*dz);
            if(length<.00001){dx=path[next].X-path[next-1].X;dz=path[next].Z-path[next-1].Z;length=Math.Sqrt(dx*dx+dz*dz);}
            return length<.00001?new TowPoint(0,0,1):new TowPoint((float)(dx/length),0,(float)(dz/length));
        }
        private static TowPoint Along(TowPoint[] path,TowPoint position,int index,int direction,double distance)
        {
            while(index>=0&&index<path.Length){var target=path[index];var length=position.Distance(target);if(length>=distance&&length>.00001)return Lerp(position,target,distance/length);distance-=length;position=target;index+=direction;}
            return position;
        }
        internal static double SpeedLimit(TowPoint[] path,int index)=>6;
        internal static bool TrafficConflict(TowPoint tug,TowPoint ahead,TowPoint aircraft,double length,double width)
        {
            if(!tug.Finite||!aircraft.Finite||double.IsNaN(length)||double.IsNaN(width)||length<=0||width<=0)return false;
            var radius=Math.Sqrt(length*length+width*width)/2+8;
            return TowCrossingGeometry.Capsule(aircraft,aircraft,tug,ahead,radius).Any();
        }
    }
}
