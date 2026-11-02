using System;
using System.Collections.Generic;
using System.Linq;
namespace TS3Modding.Tugs
{
    internal sealed class TowRoadSnapshot
    {
        public int Id,Type,Direction;public double ProtectionRadius;public string Name;public TowPoint[] Points;public TowConnection[] Connections;
    }
    internal struct TowPoint
    {
        public float X,Y,Z;
        public TowPoint(float x,float y,float z){X=x;Y=y;Z=z;}
        public double Distance(TowPoint other){double x=X-other.X,y=Y-other.Y,z=Z-other.Z;return Math.Sqrt(x*x+y*y+z*z);}
        public bool Finite=>!float.IsNaN(X)&&!float.IsInfinity(X)&&!float.IsNaN(Y)&&!float.IsInfinity(Y)&&!float.IsNaN(Z)&&!float.IsInfinity(Z);
    }
    internal sealed class TowConnection {public int Index,OtherRoad,OtherIndex;public bool HasContact;public TowPoint Contact;}
    internal sealed class TowDestination
    {
        public int Road,Index;public string Name,Kind,Alias;public bool Occupied,Reserved;
        public string Availability=>Occupied?"Occupied":Reserved?"Reserved":"No occupancy/reservation observed";
    }
    internal sealed class TowPreview
    {
        public int[] ViaRoads=new int[0];public int[] RoadIds=new int[0];public int[] ViaOffsets=new int[0];public bool Found;public TowPoint[] Points=new TowPoint[0];public string Message;public double Length;public string[] Roads=new string[0];
    }
    internal static class TowPreviewPlanner
    {
        // Read-only topology preview. Native crossing indices identify SEGMENTS, not vertices.
        public static TowPreview Plan(TowRoadSnapshot[] snapshot,int sourceRoad,int sourceIndex,TowDestination destination)=>Plan(snapshot,sourceRoad,sourceIndex,destination,false);
        public static TowPreview Plan(TowRoadSnapshot[] snapshot,int sourceRoad,int sourceIndex,TowDestination destination,bool crossingPreview,TowRoadSnapshot[][] viaGroups=null,bool tugApproach=false,TowPoint? sourcePosition=null,TowPoint? targetPosition=null,bool nativeStandExit=false)
        {
            Func<string,TowPreview> reject=message=>new TowPreview{Message=message};
            if(destination.Occupied||destination.Reserved)return reject("Destination is occupied or reserved.");
            if(snapshot==null||snapshot.Length>4096||snapshot.Any(r=>r==null)||snapshot.Select(r=>r.Id).Distinct().Count()!=snapshot.Length)return reject("Airport graph is unavailable or ambiguous.");
            var roads=snapshot.ToDictionary(r=>r.Id);
            Func<int,bool> allowed=id=>roads.ContainsKey(id)&&(roads[id].Type==0||(!tugApproach&&roads[id].Type==3)||(crossingPreview&&roads[id].Type==2)||(roads[id].Type==1&&(id==sourceRoad||id==destination.Road)))&&roads[id].Direction>=0&&roads[id].Direction<=2;
            if(!allowed(sourceRoad)||!allowed(destination.Road))return reject("Source or destination is not a supported ground road.");
            if(snapshot.Any(r=>r.Points==null||r.Points.Length>20000||r.Points.Any(p=>!p.Finite))||snapshot.Sum(r=>(long)r.Points.Length)>100000)return reject("Airport graph has invalid or excessive geometry.");
            if(sourceIndex<0||sourceIndex>=roads[sourceRoad].Points.Length||destination.Index<0||destination.Index>=roads[destination.Road].Points.Length)return reject("Parking point index is unavailable.");
            if(sourceRoad==destination.Road)return reject("Choose a different stand or parking road.");
            Func<int,int,long> key=(road,index)=>((long)road<<32)|(uint)index;
            var positions=new Dictionary<long,TowPoint>();var ordered=new Dictionary<int,List<Tuple<double,long>>>();var edges=new Dictionary<long,List<Tuple<long,double>>>();var nextIndex=new Dictionary<int,int>();
            foreach(var road in snapshot.Where(r=>allowed(r.Id)))
            {
                var nodes=new List<Tuple<double,long>>();ordered[road.Id]=nodes;nextIndex[road.Id]=road.Points.Length;
                for(int i=0;i<road.Points.Length;i++){var node=key(road.Id,i);positions[node]=road.Points[i];nodes.Add(Tuple.Create((double)i,node));}
            }
            Action<long,long> connect=(from,to)=>{List<Tuple<long,double>> list;if(!edges.TryGetValue(from,out list))edges[from]=list=new List<Tuple<long,double>>();list.Add(Tuple.Create(to,positions[from].Distance(positions[to])));};
            Func<int,int,TowPoint,long?> crossingNode=(roadId,segment,contact)=>{
                var geometry=roads[roadId].Points;
                if(segment<0||segment+1>=geometry.Length||!contact.Finite)return null;
                var a=geometry[segment];var b=geometry[segment+1];double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z;
                var denominator=dx*dx+dy*dy+dz*dz;
                var t=denominator==0?0:((contact.X-a.X)*dx+(contact.Y-a.Y)*dy+(contact.Z-a.Z)*dz)/denominator;
                if(t<-.001||t>1.001)return null;t=Math.Max(0,Math.Min(1,t));
                var projected=new TowPoint((float)(a.X+t*dx),(float)(a.Y+t*dy),(float)(a.Z+t*dz));
                if(projected.Distance(contact)>2)return null;
                if(t<.000001)return key(roadId,segment);if(t>.999999)return key(roadId,segment+1);
                var node=key(roadId,nextIndex[roadId]++);positions[node]=contact;ordered[roadId].Add(Tuple.Create(segment+t,node));return node;
            };
            Func<int,TowPoint?,long?> endpoint=(id,actual)=>{
                if(!actual.HasValue)return null;
                var points=roads[id].Points;var best=double.PositiveInfinity;var segment=-1;TowPoint projected=default(TowPoint);
                for(var i=0;i<points.Length-1;i++){
                    var a=points[i];var b=points[i+1];double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z;var length=dx*dx+dy*dy+dz*dz;
                    var t=length<=.000001?0:Math.Max(0,Math.Min(1,((actual.Value.X-a.X)*dx+(actual.Value.Y-a.Y)*dy+(actual.Value.Z-a.Z)*dz)/length));
                    var point=new TowPoint((float)(a.X+t*dx),(float)(a.Y+t*dy),(float)(a.Z+t*dz));var gap=point.Distance(actual.Value);
                    if(gap<best){best=gap;segment=i;projected=point;}
                }
                if(segment<0||best>(roads[id].Type==1?50:25))return null;
                return crossingNode(id,segment,projected);
            };
            var approachStart=tugApproach?endpoint(sourceRoad,sourcePosition):null;var approachEnd=tugApproach?endpoint(destination.Road,targetPosition):null;
            if(tugApproach&&(!approachStart.HasValue||!approachEnd.HasValue))return reject("Tug is outside its stand connector or aircraft is outside its runway connection.");
            int accepted=0,invalid=0;
            foreach(var road in snapshot.Where(r=>allowed(r.Id)))foreach(var link in road.Connections??new TowConnection[0])
            {
                if(link==null||!allowed(link.OtherRoad))continue;
                long? from,to;
                if(link.HasContact){from=crossingNode(road.Id,link.Index,link.Contact);to=crossingNode(link.OtherRoad,link.OtherIndex,link.Contact);}
                else
                {
                    from=link.Index>=0&&link.Index<road.Points.Length?(long?)key(road.Id,link.Index):null;
                    to=link.OtherIndex>=0&&link.OtherIndex<roads[link.OtherRoad].Points.Length?(long?)key(link.OtherRoad,link.OtherIndex):null;
                    if(from.HasValue&&to.HasValue&&positions[from.Value].Distance(positions[to.Value])>100){invalid++;continue;}
                }
                if(!from.HasValue||!to.HasValue){invalid++;continue;}
                connect(from.Value,to.Value);accepted++;
                if(positions.Count>200000)return reject("Airport crossing graph exceeds the preview limit.");
            }
            foreach(var road in snapshot.Where(r=>allowed(r.Id)))
            {
                var nodes=ordered[road.Id].OrderBy(n=>n.Item1).ThenBy(n=>n.Item2).ToArray();
                // Leaving a terminal includes the reverse push-out leg; terminal direction is not a taxiway prohibition.
                var forward=road.Type==1?road.Id==destination.Road||nativeStandExit&&road.Id==sourceRoad:road.Direction!=2;
                var backward=road.Type==1?road.Id==sourceRoad:road.Direction!=1;
                for(int i=1;i<nodes.Length;i++){if(forward)connect(nodes[i-1].Item2,nodes[i].Item2);if(backward)connect(nodes[i].Item2,nodes[i-1].Item2);}
            }
            viaGroups=viaGroups??new TowRoadSnapshot[0][];
            if(viaGroups.Length>8||viaGroups.Any(g=>g==null||g.Length==0))return reject("Via taxiway groups are unavailable.");
            const long nodeMask=0x0FFFFFFFFFFFFFFF;
            Func<long,int,long> state=(node,stage)=>node|((long)stage<<60);
            var start=approachStart??key(sourceRoad,sourceIndex);var end=approachEnd??key(destination.Road,destination.Index);
            var costs=new Dictionary<long,double>{{start,0}};var previous=new Dictionary<long,long>();
            var queue=new SortedSet<Tuple<double,long>>(Comparer<Tuple<double,long>>.Create((a,b)=>{var order=a.Item1.CompareTo(b.Item1);return order!=0?order:a.Item2.CompareTo(b.Item2);}));queue.Add(Tuple.Create(0d,start));
            int visits=0;
            while(queue.Count>0&&visits++<200000)
            {
                var head=queue.Min;queue.Remove(head);var node=head.Item2;if(head.Item1!=costs[node])continue;
                if((node&nodeMask)==end&&(int)((ulong)node>>60)==viaGroups.Length)
                {
                    var path=new List<long>{node};while(path[path.Count-1]!=start)path.Add(previous[path[path.Count-1]]);path.Reverse();
                    var chosen=new List<int>();for(int i=1;i<path.Count;i++)if((ulong)path[i]>>60!=(ulong)path[i-1]>>60)chosen.Add((int)((path[i-1]&nodeMask)>>32));
                    var names=new List<string>();foreach(var step in path){var name=roads[(int)((step&nodeMask)>>32)].Name;if(names.Count==0||names[names.Count-1]!=name)names.Add(name);}
                    return new TowPreview{Found=true,ViaRoads=chosen.ToArray(),RoadIds=path.Select(n=>(int)((n&nodeMask)>>32)).ToArray(),ViaOffsets=Enumerable.Range(1,path.Count-1).Where(i=>((ulong)path[i]>>60)!=((ulong)path[i-1]>>60)).ToArray(),Length=head.Item1,Roads=names.ToArray(),Points=path.Select(n=>positions[n&nodeMask]).ToArray(),Message=crossingPreview?"Topology preview including runway links. Native route and full tow protection envelope are checked before coupling; each crossing needs separate clearance.":"Ground-only topology preview including the stand exit leg. This is not a tow clearance or a validated drivable tug route."};
                }
                List<Tuple<long,double>> outgoing;if(!edges.TryGetValue(node&nodeMask,out outgoing))continue;
                foreach(var edge in outgoing)
                {
                    var stage=(int)((ulong)node>>60);var fromRoad=(int)((node&nodeMask)>>32);var toRoad=(int)(edge.Item1>>32);
                    // Only travel along a real piece satisfies a via. Merely touching
                    // its crossing node never counts, and separate pieces stay separate.
                    if(stage<viaGroups.Length&&fromRoad==toRoad&&edge.Item2>.001&&viaGroups[stage].Any(r=>r.Id==fromRoad))stage++;
                    var next=state(edge.Item1,stage);var cost=head.Item1+edge.Item2;double old;if(costs.TryGetValue(next,out old)&&old<=cost)continue;
                    costs[next]=cost;previous[next]=node;queue.Add(Tuple.Create(cost,next));
                }
            }
            return reject("No supported connection found. Traffic roads and through-routing across other stands are excluded; runway links require crossing-aware planning. Preview crossing links="+accepted+", invalid crossings="+invalid+".");
        }
    }
}
