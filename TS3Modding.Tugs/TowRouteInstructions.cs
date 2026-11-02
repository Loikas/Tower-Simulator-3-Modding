using System;
using System.Linq;

namespace TS3Modding.Tugs
{
    internal static class TowRouteInstructions
    {
        internal static TowDestination WithOwnedReservation(TowDestination target,bool owner)=>new TowDestination{Road=target.Road,Index=target.Index,Name=target.Name,Kind=target.Kind,Alias=target.Alias,Occupied=target.Occupied,Reserved=target.Reserved&&!owner};
        internal static TowRoadSnapshot Resolve(TowRoadSnapshot[] roads,string identifier)
        {
            var matches=ResolveAll(roads,identifier);
            if(matches.Length!=1)throw new InvalidOperationException("Taxiway "+identifier.ToUpperInvariant()+" is unknown or ambiguous.");
            return matches[0];
        }
        internal static TowRoadSnapshot[] ResolveAll(TowRoadSnapshot[] roads,string identifier)
        {
            var normalized=TowVoiceIntent.Identifier(identifier);
            var matches=roads.Where(r=>r.Type==0&&normalized!=null&&TowVoiceIntent.Identifier(r.Name)==normalized).ToArray();
            if(matches.Length==0)throw new InvalidOperationException("Taxiway "+identifier.ToUpperInvariant()+" is unknown.");
            return matches;
        }
        internal static TowRoadSnapshot[] ResolveVias(TowRoadSnapshot[] roads,int source,int index,TowDestination destination,string[] identifiers)
        {
            if(identifiers==null||identifiers.Length==0)return new TowRoadSnapshot[0];
            var groups=identifiers.Select(id=>ResolveAll(roads,id)).ToArray();
            var preview=TowPreviewPlanner.Plan(roads,source,index,destination,true,groups);
            if(!preview.Found||preview.ViaRoads.Length!=groups.Length)throw new InvalidOperationException("No connected route follows taxiways "+string.Join(", ",identifiers).ToUpperInvariant()+" in the requested order.");
            return preview.ViaRoads.Select(id=>roads.Single(r=>r.Id==id)).ToArray();
        }
        internal static string ValidateNamedVias(TowPoint[] path,TowRoadSnapshot[] roads,string[] names)=>ValidateGroups(path,(names??new string[0]).Select(n=>ResolveAll(roads,n)).ToArray());
        internal static TowCrossingPreview HoldNamed(TowPoint[] path,TowRoadSnapshot[] roads,string identifier,double margin,double progress,out TowRoadSnapshot road)
        {
            var matches=ResolveAll(roads,identifier);
            // Airport road objects may be separate pieces of the same named taxiway. Inspect
            // each piece independently: concatenating their points would invent a connection.
            var envelopes=matches.Select(r=>new TowRoadSnapshot{Id=r.Id,Name=r.Name,Type=2,Points=r.Points,ProtectionRadius=r.ProtectionRadius}).ToArray();
            var encounters=TowCrossingGeometry.Inspect(path,envelopes,margin);
            if(encounters.Any(e=>progress>=e.Entry&&progress<=e.Exit))throw new InvalidOperationException("Already inside taxiway "+identifier.ToUpperInvariant()+"; cannot hold short.");
            var next=encounters.Where(e=>e.Entry>progress).OrderBy(e=>e.Hold).ThenBy(e=>e.Entry).FirstOrDefault();
            if(next==null||next.Hold-progress<5)throw new InvalidOperationException("No reachable hold-short position remains before taxiway "+identifier.ToUpperInvariant()+".");
            road=matches.Single(r=>r.Id==next.Runway);return next;
        }
        internal static string ValidateVias(TowPoint[] path,TowRoadSnapshot[] vias)
            =>ValidateGroups(path,vias.Select(r=>new[]{r}).ToArray());
        private static string ValidateGroups(TowPoint[] path,TowRoadSnapshot[][] groups)
        {
            double previous=0;
            foreach(var group in groups)
            {
                double progress=0,matched=0,last=-100;bool found=false;
                for(int i=1;i<path.Length&&!found;i++)
                {
                    var length=path[i-1].Distance(path[i]);
                    var spans=new System.Collections.Generic.List<Tuple<double,double>>();
                    foreach(var road in group)for(var j=1;j<road.Points.Length;j++)
                    {
                        var a=path[i-1];var b=path[i];var c=road.Points[j-1];var d=road.Points[j];
                        double x=d.X-c.X,z=d.Z-c.Z,roadLength=Math.Sqrt(x*x+z*z);
                        if(length<.001||roadLength<.001)continue;
                        var alignment=(x*(b.X-a.X)+z*(b.Z-a.Z))/(roadLength*length);
                        if(road.Direction==1?alignment<.8:road.Direction==2?alignment>-.8:Math.Abs(alignment)<.8)continue;
                        foreach(var hit in TowCrossingGeometry.Capsule(a,b,c,d,2))
                        {var start=Math.Max(previous,progress+hit.Item1*length);var end=progress+hit.Item2*length;if(end>start)spans.Add(Tuple.Create(start,end));}
                    }
                    foreach(var span in spans.OrderBy(v=>v.Item1))
                    {
                        if(span.Item1>last+2)matched=0;
                        matched+=Math.Max(0,span.Item2-Math.Max(span.Item1,last));last=Math.Max(last,span.Item2);
                        if(matched>=3){previous=last;found=true;break;}
                    }
                    progress+=length;
                }
                if(!found)return "Native route does not follow taxiway "+group[0].Name+" in the requested order.";
            }
            return null;
        }
        internal static double Progress(TowPoint[] path,TowPoint position,double minimum=0)
        {
            double nearest=double.PositiveInfinity,result=0,prefix=0;
            for(int i=1;i<path.Length;i++)
            {
                var a=path[i-1];var b=path[i];double x=b.X-a.X,z=b.Z-a.Z,den=x*x+z*z;
                var t=den<.000001?0:Math.Max(0,Math.Min(1,((position.X-a.X)*x+(position.Z-a.Z)*z)/den));
                var projected=new TowPoint((float)(a.X+x*t),(float)(a.Y+(b.Y-a.Y)*t),(float)(a.Z+z*t));
                var distance=position.Distance(projected);if(prefix+a.Distance(b)>=minimum&&distance<nearest){nearest=distance;result=prefix+a.Distance(b)*t;}
                prefix+=a.Distance(b);
            }
            if(nearest>15)throw new InvalidOperationException("Aircraft is too far from its checked tow route.");
            return result;
        }
        internal static double ProgressNear(TowPoint[] path,TowPoint position,double minimum,double maximum,int nativeIndex)
        {
            if(path==null||path.Length<2||!position.Finite||double.IsNaN(minimum)||double.IsNaN(maximum)||minimum<0||maximum<minimum)throw new InvalidOperationException("Invalid local tow progress window.");
            double nearest=double.PositiveInfinity,result=minimum,prefix=0;int bestHint=int.MaxValue;
            for(int i=1;i<path.Length;i++)
            {
                var a=path[i-1];var b=path[i];var length=a.Distance(b);
                if(length<.000001)continue;
                if(prefix+length>=minimum&&prefix<=maximum)
                {
                    double x=b.X-a.X,z=b.Z-a.Z,den=x*x+z*z;
                    var low=Math.Max(0,(minimum-prefix)/length);var high=Math.Min(1,(maximum-prefix)/length);
                    var t=den<.000001?low:Math.Max(low,Math.Min(high,((position.X-a.X)*x+(position.Z-a.Z)*z)/den));
                    var projected=new TowPoint((float)(a.X+x*t),(float)(a.Y+(b.Y-a.Y)*t),(float)(a.Z+z*t));
                    var distance=position.Distance(projected);var hint=Math.Abs(i-nativeIndex);
                    if(distance<nearest-.001||Math.Abs(distance-nearest)<=.001&&hint<bestHint){nearest=distance;result=prefix+length*t;bestHint=hint;}
                }
                prefix+=length;
            }
            if(nearest>15)throw new InvalidOperationException("Aircraft is outside the locally reachable portion of its checked tow route.");
            return result;
        }
        internal static double ProgressAtWaypoint(TowPoint[] path,TowPoint position,int nativeIndex)
        {
            if(path==null||path.Length<2||nativeIndex<0||nativeIndex>=path.Length)throw new InvalidOperationException("Native tow waypoint is outside its checked route.");
            var prefix=new double[path.Length];for(int i=1;i<path.Length;i++)prefix[i]=prefix[i-1]+path[i-1].Distance(path[i]);
            return ProgressNear(path,position,prefix[Math.Max(0,nativeIndex-6)],prefix[Math.Min(path.Length-1,nativeIndex+6)],nativeIndex);
        }
        private static double Distance(TowPoint p,TowPoint a,TowPoint b)
        {
            double x=b.X-a.X,z=b.Z-a.Z,den=x*x+z*z;
            var t=den<.000001?0:Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Z-a.Z)*z)/den));
            x=p.X-a.X-x*t;z=p.Z-a.Z-z*t;return Math.Sqrt(x*x+z*z);
        }
        internal static TowCrossingPreview Hold(TowPoint[] path,TowRoadSnapshot road,double margin,double progress)
        {
            var envelope=new TowRoadSnapshot{Id=road.Id,Name=road.Name,Type=2,Points=road.Points,ProtectionRadius=road.ProtectionRadius};
            var encounters=TowCrossingGeometry.Inspect(path,new[]{envelope},margin);
            if(encounters.Any(e=>progress>=e.Entry&&progress<=e.Exit))throw new InvalidOperationException("Already inside taxiway "+road.Name+"; cannot hold short.");
            var next=encounters.FirstOrDefault(e=>e.Entry>progress);
            if(next==null||next.Hold-progress<5)throw new InvalidOperationException("No reachable hold-short position remains before taxiway "+road.Name+".");
            return next;
        }
    }
}
