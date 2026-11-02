using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TS3Modding.API;
using UnityEngine;
namespace TS3Modding.Tugs
{
    internal sealed class TowPlanningSession
    {
        public string Callsign,Source;public int SourceRoad,SourceIndex;
        public TowRoadSnapshot[] Roads;public TowDestination[] Destinations;
        public string DiagnosticSummary;
        public object Game,Schedule;public Component Aircraft;public object[] NativeRoads;
    }
    internal sealed class NativeTowPlanning
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private readonly FieldInfo loading,airfield,container,roads,roadType,roadName,roadWidth,direction,points,knots,occupied,locked,crosses,crossRoad,crossIndex,thisIndex,crossContact,knotType,knotIndex,knotName;
        private readonly FieldInfo planeTerminal,planePosition,planeParked,planeDeleted;
        public NativeTowPlanning(Assembly assembly)
        {
            var game=assembly.GetType("Game",true);var road=assembly.GetType("Road",true);var knot=assembly.GetType("Road+Knots_t",true);var cross=assembly.GetType("Road+FFNEIEEFNKF",true);var plane=assembly.GetType("Airplane",true);
            loading=Required(game,"loading");airfield=Required(game,"airfield");container=Required(airfield.FieldType,"aircont");roads=Required(container.FieldType,"roads");
            roadWidth=Required(road,"width");roadType=Required(road,"type");roadName=Required(road,"name");direction=Required(road,"dir");points=Required(road,"points");knots=Required(road,"knots");occupied=Required(road,"occupied");locked=Required(road,"locked");crosses=Required(road,"crosses");
            crossContact=Required(cross,"contact");crossRoad=Required(cross,"cross");crossIndex=Required(cross,"crossidx");thisIndex=Required(cross,"thisidx");knotType=Required(knot,"gatetype");knotIndex=Required(knot,"idx");knotName=Required(knot,"name");
            planeTerminal=Required(plane,"_terminal");planePosition=Required(plane,"_terminal_start_pos");planeParked=Required(plane,"_imatterminal");planeDeleted=Required(plane,"_deleted");
        }
        private static FieldInfo Required(Type type,string name)=>type.GetField(name,Flags)??throw new MissingFieldException(type.FullName,name);
        private static TowPoint Point(Vector3 p)=>new TowPoint(p.x,p.y,p.z);
        private static IEnumerable<T> OptionalItems<T>(object value)=>value==null?Enumerable.Empty<T>():((IEnumerable)value).Cast<T>();
        public TowPlanningSession Inspect(object game,Component aircraft,string callsign,object sourceOverride=null)
        {
            if(sourceOverride==null&&(bool)loading.GetValue(game))throw new InvalidOperationException("Wait for the airport session to finish loading.");
            if(!aircraft||(bool)planeDeleted.GetValue(aircraft)||sourceOverride==null&&!(bool)planeParked.GetValue(aircraft))throw new InvalidOperationException("Select an aircraft parked at a stand.");
            var source=sourceOverride??planeTerminal.GetValue(aircraft);var field=airfield.GetValue(game);var data=field==null?null:container.GetValue(field);
            if(source==null||data==null)throw new InvalidOperationException("Airport stand data is unavailable.");
            var nativeRoads=roads.GetValue(data);
            if(nativeRoads==null)throw new InvalidOperationException("Airport roads are not available yet. Wait for loading to finish, then refresh destinations.");
            var native=((IEnumerable)nativeRoads).Cast<object>().Where(r=>r!=null).ToArray();
            if(native.Length>4096)throw new InvalidOperationException("Airport has too many roads for this preview.");
            var ids=new Dictionary<object,int>();for(int i=0;i<native.Length;i++)ids.Add(native[i],i);
            int sourceId;if(!ids.TryGetValue(source,out sourceId))throw new InvalidOperationException("Aircraft stand does not belong to this airport.");
            var snapshots=new List<TowRoadSnapshot>();var destinations=new List<TowDestination>();
            foreach(var road in native)
            {
                var type=Convert.ToInt32(roadType.GetValue(road));var roadId=ids[road];var geometry=OptionalItems<Vector3>(points.GetValue(road)).Select(Point).ToArray();
                if(geometry.Length>20000)throw new InvalidOperationException("Road geometry is too large.");
                var links=new List<TowConnection>();
                foreach(var item in OptionalItems<object>(crosses.GetValue(road)).Where(item=>item!=null))
                {var other=crossRoad.GetValue(item);int otherId;if(other!=null&&ids.TryGetValue(other,out otherId))links.Add(new TowConnection{Index=(int)thisIndex.GetValue(item),OtherRoad=otherId,OtherIndex=(int)crossIndex.GetValue(item),HasContact=true,Contact=Point((Vector3)crossContact.GetValue(item))});}
                snapshots.Add(new TowRoadSnapshot{Id=roadId,Name=(string)roadName.GetValue(road),Type=type,ProtectionRadius=(type==2||type==0)?(float)roadWidth.GetValue(road)*.3048:0,Direction=Convert.ToInt32(direction.GetValue(road)),Points=geometry,Connections=links.ToArray()});
                if(roadId==sourceId||(type!=1&&type!=3))continue;
                if(type==1)
                {
                    // Native GetGateKnot starts from terminal.points.Count-1; named gate knots
                    // are not required on terminal roads. Exact aircraft alignment is a later phase.
                    if(geometry.Length>=2)
                        destinations.Add(new TowDestination{Road=roadId,Index=geometry.Length-1,Name=(string)roadName.GetValue(road),Kind="Stand endpoint",Occupied=(bool)occupied.GetValue(road),Reserved=locked.GetValue(road)!=null});
                    continue;
                }
                foreach(var knot in OptionalItems<object>(knots.GetValue(road)).Where(knot=>knot!=null))
                {
                    if(Convert.ToInt32(knotType.GetValue(knot))!=4)continue; // Native GT_POINT parking endpoint only.
                    var index=(int)knotIndex.GetValue(knot);if(index<0||index>=geometry.Length)continue;
                    var name=(string)knotName.GetValue(knot);var label=(string)roadName.GetValue(road);
                    destinations.Add(new TowDestination{Road=roadId,Index=index,Name=string.IsNullOrEmpty(name)?label:label+" / "+name,Kind=type==1?"Stand":"Parking road",Occupied=(bool)occupied.GetValue(road),Reserved=locked.GetValue(road)!=null});
                }
            }
            var aliases=new TowStandAliases(snapshots.Where(r=>r.Type==1).Select(r=>r.Name).Concat(destinations.Select(d=>d.Name)));
            foreach(var destination in destinations)destination.Alias=aliases.Alias(destination.Name);
            var sourcePoints=snapshots[sourceId].Points;if(sourcePoints.Length==0)throw new InvalidOperationException("Source stand has no loaded geometry; a transfer preview cannot be calculated.");
            var position=Point((Vector3)planePosition.GetValue(aircraft));var sourceIndex=Enumerable.Range(0,sourcePoints.Length).OrderBy(i=>position.Distance(sourcePoints[i])).First();
            if(sourceOverride==null&&position.Distance(sourcePoints[sourceIndex])>25)throw new InvalidOperationException("Source parking position does not match the road geometry.");
            return new TowPlanningSession{Game=game,Aircraft=aircraft,Schedule=Required(aircraft.GetType(),"_timetable").GetValue(aircraft),NativeRoads=native,Callsign=callsign,Source=(string)roadName.GetValue(source),SourceRoad=sourceId,SourceIndex=sourceIndex,Roads=snapshots.ToArray(),Destinations=destinations.OrderBy(d=>d.Name).ToArray(),DiagnosticSummary="terminal roads="+snapshots.Count(r=>r.Type==1)+", terminal roads with geometry="+snapshots.Count(r=>r.Type==1&&r.Points.Length>=2)+", parking roads="+snapshots.Count(r=>r.Type==3)+", native crossings="+snapshots.Sum(r=>r.Connections.Length)+", source crossings="+snapshots[sourceId].Connections.Length};
        }
    }
}


