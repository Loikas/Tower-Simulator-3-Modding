using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.Tugs
{
    internal sealed class TowCrossingPreview
    {
        internal int Runway;
        internal string Name;
        internal double Entry, Exit, Hold;
        internal TowPoint HoldPosition;
        internal bool HasApproachSpace;
    }

    // Read-only protection-envelope preview. This does not authorize physical movement.
    internal static class TowCrossingGeometry
    {
        private const double CellSize=128;
        private static int Cell(double coordinate)
        {
            var value=Math.Floor(coordinate/CellSize);
            if(value<int.MinValue+1d||value>int.MaxValue-1d)throw new InvalidOperationException("Crossing geometry coordinates exceed the spatial index limit.");
            return (int)value;
        }
        private static long CellKey(int x,int z)=>((long)x<<32)|(uint)z;
        internal static TowCrossingPreview[] Inspect(TowPoint[] route, TowRoadSnapshot[] roads, double combinationMargin)
        {
            if(route==null||route.Length<2||route.Length>100000||route.Any(p=>!p.Finite)||
                roads==null||roads.Any(r=>r==null)||!Finite(combinationMargin)||combinationMargin<0||combinationMargin>300)
                throw new InvalidOperationException("Crossing preview has invalid route or clearance margin.");
            var prefix=new double[route.Length];
            for(int i=1;i<route.Length;i++)prefix[i]=prefix[i-1]+route[i-1].Distance(route[i]);
            var result=new List<TowCrossingPreview>();int comparisons=0,indexEntries=0,queries=0;
            foreach(var runway in roads.Where(r=>r.Type==2))
            {
                if(runway.Points==null||runway.Points.Length<2||runway.Points.Any(p=>!p.Finite)||!Finite(runway.ProtectionRadius)||runway.ProtectionRadius<=0||runway.ProtectionRadius>500)
                    throw new InvalidOperationException("Runway protection geometry unavailable for "+runway.Name+".");
                var intervals=new List<Tuple<double,double>>();
                // Dense steering samples must not multiply against every segment
                // of every runway. Index the complete protection capsule's bounds;
                // all possible contacts still use the original exact intersection.
                var cells=new Dictionary<long,List<int>>();var radius=runway.ProtectionRadius;
                for(var j=1;j<runway.Points.Length;j++)
                {
                    var a=runway.Points[j-1];var b=runway.Points[j];
                    var lowX=Cell(Math.Min(a.X,b.X)-radius);var highX=Cell(Math.Max(a.X,b.X)+radius);var lowZ=Cell(Math.Min(a.Z,b.Z)-radius);var highZ=Cell(Math.Max(a.Z,b.Z)+radius);
                    if((highX-(double)lowX+1)*(highZ-(double)lowZ+1)>2000000-indexEntries)throw new InvalidOperationException("Crossing preview spatial index exceeds its geometry budget.");
                    for(var x=lowX;x<=highX;x++)for(var z=lowZ;z<=highZ;z++)
                    {indexEntries++;var key=CellKey(x,z);List<int> list;if(!cells.TryGetValue(key,out list))cells[key]=list=new List<int>();list.Add(j);}
                }
                var seen=new int[runway.Points.Length];
                for(int i=1;i<route.Length;i++)
                {
                    var a=route[i-1];var b=route[i];var lowX=Cell(Math.Min(a.X,b.X));var highX=Cell(Math.Max(a.X,b.X));var lowZ=Cell(Math.Min(a.Z,b.Z));var highZ=Cell(Math.Max(a.Z,b.Z));
                    if((highX-(double)lowX+1)*(highZ-(double)lowZ+1)>2000000-queries)throw new InvalidOperationException("Crossing preview spatial query exceeds its geometry budget.");
                    for(var x=lowX;x<=highX;x++)for(var z=lowZ;z<=highZ;z++)
                    {
                        queries++;List<int> candidates;if(!cells.TryGetValue(CellKey(x,z),out candidates))continue;
                        foreach(var j in candidates)
                        {
                            if(seen[j]==i)continue;seen[j]=i;
                            var c=runway.Points[j-1];var d=runway.Points[j];
                            if(Math.Max(a.X,b.X)<Math.Min(c.X,d.X)-radius||Math.Min(a.X,b.X)>Math.Max(c.X,d.X)+radius||Math.Max(a.Z,b.Z)<Math.Min(c.Z,d.Z)-radius||Math.Min(a.Z,b.Z)>Math.Max(c.Z,d.Z)+radius)continue;
                            if(++comparisons>2000000)throw new InvalidOperationException("Crossing preview exceeds the geometry budget.");
                            foreach(var hit in Capsule(a,b,c,d,radius))
                            {if(intervals.Count>=200000)throw new InvalidOperationException("Too many crossing intervals.");intervals.Add(Tuple.Create(prefix[i-1]+hit.Item1*(prefix[i]-prefix[i-1]),prefix[i-1]+hit.Item2*(prefix[i]-prefix[i-1])));}
                        }
                    }
                }
                double start=-1,end=-1;
                Action finish=()=>{
                    if(start<0)return;
                    if(result.Count>=128)throw new InvalidOperationException("Too many runway encounters.");
                    var hold=start-combinationMargin;
                    result.Add(new TowCrossingPreview{Runway=runway.Id,Name=runway.Name,Entry=start,Exit=end,Hold=hold,
                        HasApproachSpace=hold>=0&&end+combinationMargin<=prefix[prefix.Length-1],HoldPosition=At(route,prefix,Math.Max(0,hold))});
                };
                foreach(var interval in intervals.OrderBy(v=>v.Item1))
                {
                    if(start<0){start=interval.Item1;end=interval.Item2;}
                    else if(interval.Item1<=end+.001)end=Math.Max(end,interval.Item2);
                    else{finish();start=interval.Item1;end=interval.Item2;}
                }
                finish();
            }
            return result.OrderBy(e=>e.Entry).ThenBy(e=>e.Runway).ToArray();
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        private static TowPoint At(TowPoint[] path,double[] prefix,double distance)
        {
            for(int i=1;i<path.Length;i++)if(prefix[i]>=distance)
            {
                var t=prefix[i]==prefix[i-1]?0:(distance-prefix[i-1])/(prefix[i]-prefix[i-1]);
                return new TowPoint((float)(path[i-1].X+(path[i].X-path[i-1].X)*t),(float)(path[i-1].Y+(path[i].Y-path[i-1].Y)*t),(float)(path[i-1].Z+(path[i].Z-path[i-1].Z)*t));
            }
            return path[path.Length-1];
        }
        internal static IEnumerable<Tuple<double,double>> Capsule(TowPoint a,TowPoint b,TowPoint c,TowPoint d,double radius)
        {
            double ux=d.X-c.X,uz=d.Z-c.Z,length=Math.Sqrt(ux*ux+uz*uz);
            if(length>.00001)
            {
                ux/=length;uz/=length;
                double x=(a.X-c.X)*ux+(a.Z-c.Z)*uz,y=-(a.X-c.X)*uz+(a.Z-c.Z)*ux;
                double dx=(b.X-a.X)*ux+(b.Z-a.Z)*uz,dy=-(b.X-a.X)*uz+(b.Z-a.Z)*ux;
                double low=0,high=1;
                if(Slab(x,dx,0,length,ref low,ref high)&&Slab(y,dy,-radius,radius,ref low,ref high))yield return Tuple.Create(low,high);
            }
            foreach(var center in new[]{c,d})
            {
                double dx=b.X-a.X,dz=b.Z-a.Z,x=a.X-center.X,z=a.Z-center.Z;
                var aa=dx*dx+dz*dz;var cc=x*x+z*z-radius*radius;
                if(aa<1e-12){if(cc<=0)yield return Tuple.Create(0d,1d);continue;}
                var bb=2*(x*dx+z*dz);var discriminant=bb*bb-4*aa*cc;
                if(discriminant<0)continue;
                var root=Math.Sqrt(discriminant);var low=Math.Max(0,(-bb-root)/(2*aa));var high=Math.Min(1,(-bb+root)/(2*aa));
                if(low<=high)yield return Tuple.Create(low,high);
            }
        }
        private static bool Slab(double value,double delta,double min,double max,ref double low,ref double high)
        {
            if(Math.Abs(delta)<1e-12)return value>=min&&value<=max;
            var a=(min-value)/delta;var b=(max-value)/delta;
            low=Math.Max(low,Math.Min(a,b));high=Math.Min(high,Math.Max(a,b));return low<=high;
        }
    }
}
