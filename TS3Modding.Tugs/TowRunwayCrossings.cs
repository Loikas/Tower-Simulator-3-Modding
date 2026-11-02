using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TS3Modding.Tugs
{
    // The envelope is expanded by the complete aircraft/tug footprint, so Exit means
    // the entire combination is outside, regardless of its heading through a bend.
    internal sealed class TowRunwayCrossings
    {
        internal readonly TowCrossingPreview[] Events;
        private readonly string[] previousVacated;
        internal string OccupiedOrigin=>occupiedOrigin;
        internal string[] InitialVacatedRunways=>previousVacated.ToArray();
        internal string[] VacatedRunways=>previousVacated.Concat(Events.Take(Event).Select(e=>e.Name)).Distinct().ToArray();
        private TowCrossingClearance admission;private readonly TowRoadSnapshot[] nativeRoads;private readonly double footprint;
        private readonly string occupiedOrigin;private readonly double originExit;
        internal bool Holding { get; private set; }
        internal bool Reported;
        internal bool Complete => admission.Complete;
        internal bool Occupying => admission.Occupying;
        internal bool Cleared => admission.MayEnter;
        internal int Event => admission.Event;
        internal TowCrossingPreview Next => Complete ? null : Events[Event];
        internal string Name => Next?.Name;
        private static bool BoundedOppositeSideCrossing(TowPoint[] path,TowRoadSnapshot runway,TowCrossingPreview encounter)
        {
            if(encounter.Exit-encounter.Entry>Math.Max(150,runway.ProtectionRadius*6))return false;
            TowPoint At(double distance)
            {
                for(var n=1;n<path.Length;n++){var length=path[n-1].Distance(path[n]);if(distance<=length&&length>.000001){var t=distance/length;return new TowPoint((float)(path[n-1].X+(path[n].X-path[n-1].X)*t),(float)(path[n-1].Y+(path[n].Y-path[n-1].Y)*t),(float)(path[n-1].Z+(path[n].Z-path[n-1].Z)*t));}distance-=length;}return path[path.Length-1];
            }
            var entry=At(encounter.Entry);var exit=At(encounter.Exit);var mid=At((encounter.Entry+encounter.Exit)/2);
            var best=double.PositiveInfinity;double entrySide=0,exitSide=0,along=0;
            for(var n=1;n<runway.Points.Length;n++)
            {
                var a=runway.Points[n-1];var b=runway.Points[n];double x=b.X-a.X,z=b.Z-a.Z,den=x*x+z*z;if(den<.000001)continue;
                var t=Math.Max(0,Math.Min(1,((mid.X-a.X)*x+(mid.Z-a.Z)*z)/den));var dx=mid.X-a.X-t*x;var dz=mid.Z-a.Z-t*z;var distance=dx*dx+dz*dz;
                if(distance>=best)continue;best=distance;var length=Math.Sqrt(den);
                entrySide=(x*(entry.Z-a.Z)-z*(entry.X-a.X))/length;exitSide=(x*(exit.Z-a.Z)-z*(exit.X-a.X))/length;
                along=Math.Abs((exit.X-entry.X)*x+(exit.Z-entry.Z)*z)/length;
            }
            return entrySide*exitSide<0&&Math.Abs(entrySide)>=runway.ProtectionRadius*.8&&Math.Abs(exitSide)>=runway.ProtectionRadius*.8&&along<=Math.Max(80,runway.ProtectionRadius*4);
        }
        internal TowRunwayCrossings(TowPoint[] route,TowRoadSnapshot[] roads,double footprint,string occupiedOrigin=null,string occupiedDestination=null,string[] vacatedRunways=null)
        {
            previousVacated=(vacatedRunways??new string[0]).ToArray();
            nativeRoads=roads;this.footprint=footprint;
            if(double.IsNaN(footprint)||double.IsInfinity(footprint)||footprint<30||footprint>450)
                throw new InvalidOperationException("Aircraft/tug protection dimensions are unavailable.");
            var expanded=roads.Select(r=>r.Type!=2?r:new TowRoadSnapshot{Id=r.Id,Name=r.Name,Type=2,Points=r.Points,ProtectionRadius=r.ProtectionRadius+footprint}).ToArray();
            // Inspect validates the original widths separately: expansion cannot repair missing data.
            var core=TowCrossingGeometry.Inspect(route,roads,0);
            var origin=Key(occupiedOrigin);
            this.occupiedOrigin=origin;
            var destination=Key(occupiedDestination);var totalLength=route.Zip(route.Skip(1),(a,b)=>a.Distance(b)).Sum();
            foreach(var e in core)
            {
                var road=roads.Single(r=>r.Id==e.Runway);
                if(Key(road.Name)==null)throw new InvalidOperationException("Runway has no unambiguous numeric designator: "+road.Name);
                // Tow approval permits vacating the already occupied origin only.
                if(origin!=null&&Key(road.Name)==origin&&e.Entry<=.001)continue;
                if(destination!=null&&Key(road.Name)==destination&&e.Exit>=totalLength-.001)continue;
                // A short native steering arc may align with the centreline while still
                // crossing opposite sides. It is not permission to route along the runway.
                var centrelineLimit=BoundedOppositeSideCrossing(route,road,e)?Math.Max(40,road.ProtectionRadius):Math.Max(20,road.ProtectionRadius);
                double following=0,pathDistance=0;
                for(int i=1;i<route.Length;i++)
                {
                    var segmentStart=pathDistance;var segmentLength=route[i-1].Distance(route[i]);pathDistance+=segmentLength;
                    if(segmentLength<.00001||pathDistance<e.Entry||segmentStart>e.Exit)continue;
                    var entry=Math.Max(0,(e.Entry-segmentStart)/segmentLength);var exit=Math.Min(1,(e.Exit-segmentStart)/segmentLength);
                    double longest=0;
                    for(int j=1;j<road.Points.Length;j++)
                    {
                        double x=route[i].X-route[i-1].X,z=route[i].Z-route[i-1].Z,rx=road.Points[j].X-road.Points[j-1].X,rz=road.Points[j].Z-road.Points[j-1].Z;
                        var length=Math.Sqrt(x*x+z*z);var den=length*Math.Sqrt(rx*rx+rz*rz);
                        if(den<=.001||Math.Abs((x*rx+z*rz)/den)<=.7072)continue;
                        // A diagonal crossing or a curve beside the runway is not runway
                        // use. Count only aligned travel inside the narrow centre corridor,
                        // clipped to its actual overlap, not the full protection envelope.
                        var overlaps=TowCrossingGeometry.Capsule(route[i-1],route[i],road.Points[j-1],road.Points[j],Math.Min(3,road.ProtectionRadius*.15));
                        var amount=overlaps.Select(v=>Math.Max(0,Math.Min(exit,v.Item2)-Math.Max(entry,v.Item1))*length).DefaultIfEmpty(0).Max();
                        longest=Math.Max(longest,amount);
                    }
                    following+=longest;

                }
                if(following>centrelineLimit)throw new InvalidOperationException("Tow route follows runway "+road.Name+" centreline for "+following.ToString("F1")+" world units instead of a bounded crossing. Choose different via taxiways.");
            }
            var hits=TowCrossingGeometry.Inspect(route,expanded,20);
            var merged=new System.Collections.Generic.List<TowCrossingPreview>();
            foreach(var e in hits)
            {
                var key=Key(e.Name);if(key==null)throw new InvalidOperationException("Runway designator is unavailable: "+e.Name);
                var previous=merged.LastOrDefault(v=>v.Name==key&&e.Entry<=v.Exit+.001);
                if(previous!=null){previous.Exit=Math.Max(previous.Exit,e.Exit);previous.HasApproachSpace&=e.HasApproachSpace;}
                else{e.Name=key;merged.Add(e);}
            }
            // A reroute can begin inside the conservative envelope of a crossing
            // whose physical tail-exit was already proved by the previous route.
            // Ignore only that initial fringe, never a core contact or later return.
            Events=merged.Where(e=>!(e.Entry<=.001&&(vacatedRunways??new string[0]).Any(n=>Key(n)==e.Name)&&!core.Any(c=>Key(c.Name)==e.Name&&c.Entry<=e.Exit+.001))).OrderBy(e=>e.Entry).ToArray();
            var routeLength=route.Zip(route.Skip(1),(a,b)=>a.Distance(b)).Sum();
            var leavingOrigin=origin!=null&&Events.Length>0&&Events[0].Name==origin&&Events[0].Entry<=.001&&Events[0].Exit+20<=routeLength;
            var arrivingDestination=destination!=null&&Events.Length>0&&Events[Events.Length-1].Name==destination&&Events[Events.Length-1].Exit>=routeLength-.001&&Events[Events.Length-1].Hold>=0;
            if(destination!=null&&!arrivingDestination)throw new InvalidOperationException("Tug approach must finish on the occupied recovery runway with a protected approach.");
            if(origin!=null&&!leavingOrigin)throw new InvalidOperationException("Recovery route must start on its occupied runway and fully vacate it.");
            originExit=origin==null?0:core.Where(e=>Key(e.Name)==origin&&e.Entry<=Events[0].Exit).Select(e=>e.Exit).DefaultIfEmpty(0).Max();
            for(int i=0;i<Events.Length;i++)
            {
                if(!Events[i].HasApproachSpace&&!(i==0&&leavingOrigin)&&!(i==Events.Length-1&&arrivingDestination))throw new InvalidOperationException("Insufficient protected holding or tail-clear space at runway "+Events[i].Name);
                if(i>0&&Events[i].Hold<=Events[i-1].Exit+20)throw new InvalidOperationException("Runway protection areas overlap without a safe intermediate tow hold.");
            }
            admission=new TowCrossingClearance(Events.Select(e=>e.Runway).ToArray());
            if(leavingOrigin){admission.Clear(Events[0].Runway,0);admission.Enter();}
        }
        internal bool Update(double progress,double speed,double step,bool tailOutside=true)
        {
            if(double.IsNaN(progress)||double.IsInfinity(progress)||double.IsNaN(speed)||double.IsInfinity(speed)||double.IsNaN(step)||step<=0||step>2)
                throw new InvalidOperationException("Invalid tow crossing movement sample.");
            if(Complete){Holding=false;return false;}
            var e=Next;
            if(progress>=e.Entry&&!admission.Occupying)
            {
                if(!admission.MayEnter)throw new InvalidOperationException("Tow reached the runway protection area without clearance.");
                admission.Enter();
            }
            if(admission.Occupying&&progress>e.Exit+2&&tailOutside)
            {admission.TailExited();Holding=false;Reported=false;return Update(progress,speed,step,tailOutside);}
            // Native braking is at least 3 speed units/s; extra distance also covers the next step.
            var brake=Math.Max(5,speed*speed/6+Math.Abs(speed)*step*2+3);
            // Once stopped, keep this encounter held until clearance. Recomputing
            // with zero speed must not release a saved (or newly reached) hold.
            Holding=!admission.MayEnter&&(Holding||progress>=e.Hold-brake);
            return Holding;
        }
        internal bool OutsideNext(TowPoint aircraft,TowPoint tug)
        {
            if(Complete)return true;
            foreach(var road in nativeRoads.Where(r=>r.Type==2&&Key(r.Name)==Name))
                for(int i=1;i<road.Points.Length;i++)
                    if(TowCrossingGeometry.Capsule(aircraft,aircraft,road.Points[i-1],road.Points[i],road.ProtectionRadius+footprint).Any()||TowCrossingGeometry.Capsule(tug,tug,road.Points[i-1],road.Points[i],road.ProtectionRadius+15).Any())return false;
            return true;
        }
        internal bool ReleaseOccupiedOrigin(double progress,TowPoint aircraft,TowPoint tug)
        {
            if(occupiedOrigin==null||Event!=0||!Occupying||progress<=originExit+2)return false;
            // Admission margins must not trap a recovery that has physically vacated.
            foreach(var road in nativeRoads.Where(r=>r.Type==2&&Key(r.Name)==occupiedOrigin))
                for(var i=1;i<road.Points.Length;i++)
                    if(TowCrossingGeometry.Capsule(aircraft,aircraft,road.Points[i-1],road.Points[i],road.ProtectionRadius/2+(footprint-30)/2+5).Any()||TowCrossingGeometry.Capsule(tug,tug,road.Points[i-1],road.Points[i],road.ProtectionRadius/2+15).Any())return false;
            admission.TailExited();Holding=false;Reported=false;return true;
        }
        internal bool CanReanchor(double previous,double candidate,bool tailOutside=false)
        {
            if(candidate<previous)return false;
            foreach(var e in Events.Where(e=>e.Hold<=candidate&&e.Exit+2>=previous))
            {
                // Small local projection corrections are permitted inside the one cleared
                // encounter. Exiting it also requires actual aircraft/tug geometry outside.
                if(Complete||!ReferenceEquals(e,Next)||!Cleared||candidate-previous>50||candidate>e.Exit+2&&!tailOutside)return false;
            }
            return true;
        }
        internal void Clear(string runway,bool stationary)
        {
            if(Complete||Occupying)throw new InvalidOperationException("Runway clearance is available for the next crossing before entry.");
            if(!Matches(Name,runway))throw new InvalidOperationException("This tow is holding for runway "+Name+", not "+runway);
            if(!admission.Clear(Next.Runway,Event))throw new InvalidOperationException("Crossing clearance no longer matches this tow.");
            Holding=false;Reported=false;
        }
        internal void Revoke()
        {
            if(Occupying)throw new InvalidOperationException("Runway crossing is already in progress; hold tow if an immediate stop is required.");
            admission.RevokeBeforeEntry();
        }
        internal void Restore(int next,bool occupying,bool cleared,double progress,bool holding)
        {
            if(next<0||next>Events.Length||double.IsNaN(progress)||double.IsInfinity(progress)||progress<0||occupying&&!cleared||next==Events.Length&&(cleared||occupying))throw new InvalidOperationException("Invalid saved crossing state.");
            // Constructor admission may already occupy the incident origin. Rebuild
            // it before replaying the saved encounters, avoiding a second Enter.
            admission=new TowCrossingClearance(Events.Select(e=>e.Runway).ToArray());
            for(int i=0;i<next;i++)
            {
                if(progress<=Events[i].Exit+2)throw new InvalidOperationException("Saved tow did not clear the completed runway.");
                admission.Clear(Next.Runway,Event);admission.Enter();admission.TailExited();
            }
            if(!Complete)
            {
                if(occupying&&(progress<Next.Entry||progress>Next.Exit+52)||!occupying&&progress>=Next.Entry)throw new InvalidOperationException("Saved crossing progress is inconsistent.");
                // The saved permission belongs to this exact encounter and checked
                // route. The adapter validates ownership and conflicts before resuming.
                if(cleared)admission.Clear(Next.Runway,Event);
                if(occupying)admission.Enter();
            }
            Holding=holding&&!Cleared;Reported=false;
        }
        internal bool CanRecover(double previous,double candidate)
        {
            if(candidate<previous-2)return false;
            if(Occupying)return candidate>=Next.Entry&&candidate<=Next.Exit+2;
            foreach(var e in Events.Skip(Event))if(previous<e.Entry&&candidate>=e.Entry||previous<=e.Exit+2&&candidate>e.Exit+2)return false;
            return true;
        }
        internal static string Designator(string value)
        {
            var id=TowVoiceIntent.Identifier(Regex.Replace(value??"",@"\b(left|right|center|centre)\b",m=>m.Value.StartsWith("l",StringComparison.OrdinalIgnoreCase)?"l":m.Value.StartsWith("r",StringComparison.OrdinalIgnoreCase)?"r":"c",RegexOptions.IgnoreCase));
            var m=Regex.Match(id??"",@"^(0?[1-9]|[12][0-9]|3[0-6])([lrc]?)$");
            return m.Success?int.Parse(m.Groups[1].Value).ToString("D2")+m.Groups[2].Value.ToUpperInvariant():null;
        }
        private static string Reciprocal(string id)
        {
            var number=int.Parse(id.Substring(0,2));var suffix=id.Substring(2);
            return ((number+17)%36+1).ToString("D2")+(suffix=="L"?"R":suffix=="R"?"L":suffix);
        }
        internal static string Key(string name)
        {
            var parts=(name??"").Split('/').Select(Designator).ToArray();
            if(parts.Length<1||parts.Length>2||parts.Any(p=>p==null))return null;
            var reverse=Reciprocal(parts[0]);if(parts.Length==2&&parts[1]!=reverse)return null;
            return string.CompareOrdinal(parts[0],reverse)<0?parts[0]+"/"+reverse:reverse+"/"+parts[0];
        }
        internal static bool Matches(string label,string spoken)=>Key(label)!=null&&Key(label)==Key(spoken);
        internal static string Spoken(string label)=>string.Join(", ",label.Split('/').Select(id=>{
            var suffix=id.Last();return TowVoiceIntent.Spoken(char.IsLetter(suffix)?id.Substring(0,id.Length-1):id)+(char.IsLetter(suffix)?suffix=='L'?" left":suffix=='R'?" right":" center":"");}));
    }
}
