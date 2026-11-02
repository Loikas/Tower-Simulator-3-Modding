using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TS3Modding.API;
using UnityEngine;
using BepInEx;
using TS3Modding.DisplayBridge;

namespace TS3Modding.PilotReadiness
{
    internal sealed class AdirsDashModule : ITS3Module
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static AdirsDashModule current;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.pilotreadiness.adirs");
        private readonly Dictionary<int,List<object>> planes=new Dictionary<int,List<object>>();
        private readonly Dictionary<object,Vector2[]> originals=new Dictionary<object,Vector2[]>();
        private readonly Dictionary<object,List<Vector3>> routes=new Dictionary<object,List<Vector3>>();
        private readonly Dictionary<object,string> projectionProfiles=new Dictionary<object,string>();
        private int lastRenderFrame=-1;
        private Type planeType;
        private FieldInfo routeAdirs, selectedPlane, primaryRoute, renderer, net, selectedNet, selectedName, callsign;
        private FieldInfo active, deleted, altitude, holdShort, targetCross, targetKnot, previousCross, previousGate, hold, gateHold;
        private FieldInfo type, length, position, crossContact, knotPos, gateDir, game, airfield, gates;
        private FieldInfo lastGate, takeoff, lineup, state, crossSourceRoad,roadName,gateRoads,roadType;
        private MethodInfo convert, canCross;
        private MethodInfo points;
        private FieldInfo lineList,relativeSize,lineThickness,lineCaps,displayCamera,secondary;
        private FieldInfo aircraftRoute;
        private string bridgePath;
        private bool bridgeFailed;
        private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
        private double nextScan;
        private IFrameworkContext context;
        private bool reportedFailure;
        public string Id=>"nl.ts3modding.pilotreadiness.adirs";
        private static FieldInfo Field(Type t,string n)=>t.GetField(n,Flags)??throw new MissingFieldException(t.FullName,n);
        public void Initialize(IFrameworkContext context)
        {
            this.context=context;
            if(current!=null) throw new InvalidOperationException("ADIRS dashes already installed.");
            var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp");
            planeType=assembly.GetType("Airplane",true);
            var route=assembly.GetType("AdirsRoute",true); var adirs=assembly.GetType("Adirs",true);
            routeAdirs=Field(route,"adirs"); renderer=Field(route,"ODMBCNFKHAE");
            primaryRoute=Field(adirs,"route"); selectedPlane=Field(adirs,"prevplane");
            displayCamera=Field(adirs,"cam");
            secondary=Field(adirs,"Secondary");
            selectedNet=Field(selectedPlane.FieldType,"netidx"); selectedName=Field(selectedPlane.FieldType,"name");
            net=Field(planeType,"_netidx"); callsign=Field(planeType,"_name");
            active=Field(planeType,"_active"); deleted=Field(planeType,"_deleted"); altitude=Field(planeType,"_alt");
            holdShort=Field(planeType,"_holdshort"); targetCross=Field(planeType,"_target_intersection"); targetKnot=Field(planeType,"_target_knot");
            previousCross=Field(planeType,"_prev_holdshort"); previousGate=Field(planeType,"_prevgate");
            hold=Field(planeType,"_hold_position_taxistop"); gateHold=Field(planeType,"_gate_taxistop");
            position=Field(planeType,"_pos"); type=Field(planeType,"_type"); length=Field(type.FieldType,"_length");
            aircraftRoute=Field(planeType,"_route"); bridgePath=HoldDisplayTelemetry.PathForGame(Paths.GameRootPath);
            crossContact=Field(previousCross.FieldType,"contact"); knotPos=Field(previousGate.FieldType,"pos"); gateDir=Field(previousGate.FieldType,"gatedir");
            game=Field(planeType,"BMCCOLLPALC"); airfield=Field(game.FieldType,"airfield"); gates=Field(airfield.FieldType,"gates");
            crossSourceRoad=Field(previousCross.FieldType,"thisroad");roadName=Field(crossSourceRoad.FieldType,"name");roadType=Field(crossSourceRoad.FieldType,"type");gateRoads=Field(previousGate.FieldType,"gateroads");
            lastGate=Field(planeType,"_last_gate");
            takeoff=Field(planeType,"_takeoff"); lineup=Field(planeType,"_lineup_and_wait"); state=Field(planeType,"_state");
            canCross=planeType.GetMethod("MCJNHJMOKEF",Flags,null,new[]{previousGate.FieldType},null);
            convert=adirs.GetMethod("ConvertPosition2Screen",Flags,null,new[]{typeof(Vector3),typeof(float)},null);
            points=renderer.FieldType.GetMethod("set_Points",Flags,null,new[]{typeof(Vector2[])},null); lineList=Field(renderer.FieldType,"lineList");
            relativeSize=Field(renderer.FieldType,"relativeSize"); lineThickness=Field(renderer.FieldType,"lineThickness"); lineCaps=Field(renderer.FieldType,"lineCaps");
            if(canCross==null||convert==null||points==null) throw new NotSupportedException("ADIRS profile changed.");
            current=this;
            try { harmony.Patch(route.GetMethod("SetRoute",Flags,null,new[]{typeof(List<Vector3>),typeof(bool)},null),postfix:new HarmonyMethod(typeof(AdirsDashModule),nameof(RoutePostfix))); Canvas.willRenderCanvases+=Redraw; context.Log("ADIRS hold-route dashes 0.4.5 enabled; per-view camera and renderer mesh coordinates."); }
            catch { Dispose(); throw; }
        }
        public void Tick()
        {
            if(clock.Elapsed.TotalSeconds<nextScan) return; nextScan=clock.Elapsed.TotalSeconds+0.5;
            planes.Clear();
            foreach(var plane in Resources.FindObjectsOfTypeAll(planeType))
            {
                if(plane==null||!(bool)active.GetValue(plane)||(bool)deleted.GetValue(plane)||(float)altitude.GetValue(plane)>0) continue;
                var id=(int)net.GetValue(plane); List<object> matches;
                if(!planes.TryGetValue(id,out matches)) planes.Add(id,matches=new List<object>());
                matches.Add(plane);
            }
            try
            {
                var markers=new List<HoldMarker>();
                foreach(var request in HoldDisplayTelemetry.Requests(bridgePath,DateTime.UtcNow))
                {
                    List<object> matches;
                    if(!planes.TryGetValue(request.NetworkId,out matches)||matches.Count!=1||
                        !string.Equals((string)callsign.GetValue(matches[0]),request.Callsign,StringComparison.OrdinalIgnoreCase)) continue;
                    var route=aircraftRoute.GetValue(matches[0]) as List<Vector3>;
                    if(route==null||route.Count<2||route.Count>4096) continue;
                    var flat=route.Select(Flat).ToList(); var at=Boundary(matches[0],route,flat);
                    if(double.IsNaN(at)||double.IsInfinity(at)) continue;
                    double travelled=0;
                    for(int i=1;i<flat.Count;i++)
                    {
                        var size=RoutePoint.Distance(flat[i-1],flat[i]);
                        if(travelled+size>=at&&size>0)
                        {
                            var point=RoutePoint.Lerp(flat[i-1],flat[i],Math.Max(0,Math.Min(1,(at-travelled)/size)));
                            markers.Add(new HoldMarker{NetworkId=request.NetworkId,Callsign=request.Callsign,X=point.X,Z=point.Y}); break;
                        }
                        travelled+=size;
                    }
                }
                HoldDisplayTelemetry.Write(bridgePath,markers,DateTime.UtcNow); bridgeFailed=false;
            }
            catch(Exception error) { if(!bridgeFailed) { context.Log("External ADIRS bridge failed: "+error.Message); bridgeFailed=true; } }
        }
        private static RoutePoint Flat(Vector3 p)=>new RoutePoint(p.x,p.z);
        private double Boundary(object plane,List<Vector3> route,List<RoutePoint> flat)
        {
            double aircraftAt; if(!RouteDashGeometry.Project(flat,Flat((Vector3)position.GetValue(plane)),30,out aircraftAt)) return double.PositiveInfinity;
            var aircraftType=type.GetValue(plane); if(aircraftType==null) return double.PositiveInfinity;
            var size=(float)length.GetValue(aircraftType);
            double best=double.PositiveInfinity;
            Action<Vector3,double> candidate=(p,offset)=>
            {
                double at;
                if(RouteDashGeometry.Project(flat,Flat(p),15,out at)&&at>=aircraftAt-1)
                    best=Math.Min(best,Math.Max(aircraftAt,at-offset));
            };
            var previous=previousCross.GetValue(plane);var previousRoad=previous==null?null:crossSourceRoad.GetValue(previous);
            var held=(bool)hold.GetValue(plane);
            Func<object,bool> pending=cross=>{var road=crossSourceRoad.GetValue(cross);return HoldDashPolicy.PendingCross(held,previousRoad==null?null:(string)roadName.GetValue(previousRoad),road==null?null:(string)roadName.GetValue(road));};
            foreach(var cross in (IEnumerable)holdShort.GetValue(plane)) if(cross!=null&&pending(cross)) candidate((Vector3)crossContact.GetValue(cross),230*0.3048+size/2);
            var explicitCross=targetCross.GetValue(plane); if(explicitCross!=null&&pending(explicitCross)) candidate((Vector3)crossContact.GetValue(explicitCross),230*0.3048+size/2);
            var explicitKnot=targetKnot.GetValue(plane); if(explicitKnot!=null) candidate((Vector3)knotPos.GetValue(explicitKnot),10*0.3048+size/6);
            if((bool)hold.GetValue(plane))
            {
                var cross=previousCross.GetValue(plane); if(cross!=null) best=aircraftAt;
            }
            if((bool)gateHold.GetValue(plane)) best=aircraftAt;
            // Native crossing permission is a verified read-only lookup over _crossrunways.
            // Gate direction excludes exit-side markings; cleared departure gates remain solid.
            var gameObject=game.GetValue(plane); var airport=gameObject==null?null:airfield.GetValue(gameObject);
            if(airport!=null && state.GetValue(plane).ToString()!="STATE_ESCAPE_RUNWAY")
            foreach(var gate in (IEnumerable)gates.GetValue(airport))
            {
                if(gate==null||!((IEnumerable)gateRoads.GetValue(gate)).Cast<object>().Any(r=>r!=null&&Convert.ToInt32(roadType.GetValue(r))==2)||(bool)canCross.Invoke(plane,new[]{gate})) continue;
                if(ReferenceEquals(lastGate.GetValue(plane),gate)&&((bool)takeoff.GetValue(plane)||(bool)lineup.GetValue(plane))) continue;
                var p=(Vector3)knotPos.GetValue(gate); double at;
                if(!RouteDashGeometry.Project(flat,Flat(p),15,out at)||at<aircraftAt+1) continue;
                var direction=(Vector3)gateDir.GetValue(gate);
                if(direction.sqrMagnitude>0.001f)
                {
                    // Find the local forward route tangent rather than aircraft heading on a bend.
                    double travelled=0; Vector3 tangent=Vector3.zero;
                    for(int i=1;i<route.Count;i++) { var d=RoutePoint.Distance(flat[i-1],flat[i]); if(travelled+d>=at) { tangent=(route[i]-route[i-1]).normalized; break; } travelled+=d; }
                    if(Vector3.Dot(tangent,direction)>-0.1f) continue;
                }
                candidate(p,0);
            }
            return best;
        }
        private static void RoutePostfix(object __instance,List<Vector3> __0,bool __1)
        {
            var self=current; if(self==null) return;
            try
            {
                if(__1||__0==null||__0.Count<2||__0.Count>4096)
                {
                    self.routes.Remove(__instance);
                    var line=self.renderer.GetValue(__instance);
                    if(line!=null) { self.originals.Remove(line); self.projectionProfiles.Remove(line); }
                    return;
                }
                self.routes[__instance]=new List<Vector3>(__0);
            }
            catch(Exception error) { if(!self.reportedFailure) { self.reportedFailure=true; self.context.Log("ADIRS dashes skipped after error: "+error); } self.Restore(__instance); }
        }
        private void Redraw()
        {
            if(lastRenderFrame==Time.frameCount) return; lastRenderFrame=Time.frameCount;
            foreach(var pair in routes.ToArray())
            {
                var component=pair.Key as Component;
                if(!component) { routes.Remove(pair.Key); continue; }
                if(!component.gameObject.activeInHierarchy) continue;
                try { Draw(pair.Key,pair.Value); }
                catch(Exception error) { if(!reportedFailure) { reportedFailure=true; context.Log("ADIRS dashes skipped after error: "+error); } Restore(pair.Key); }
            }
        }
        private void Draw(object instance,List<Vector3> route)
        {
            var display=routeAdirs.GetValue(instance); if(display==null||!ReferenceEquals(primaryRoute.GetValue(display),instance)) return;
            var line=renderer.GetValue(instance); if(line==null||!(bool)lineList.GetValue(line)) return;
            if(route==null||route.Count<2||route.Count>4096) { originals.Remove(line); return; }
            var screen=route.Select(p=>(Vector3)convert.Invoke(display,new object[]{p,0f})).Select(p=>new RoutePoint(p.x,p.y)).ToList();
            var flat=route.Select(Flat).ToList();
            var solid=new List<Vector2>(); for(int i=1;i<screen.Count;i++) { solid.Add(new Vector2((float)screen[i-1].X,(float)screen[i-1].Y)); solid.Add(new Vector2((float)screen[i].X,(float)screen[i].Y)); }
            originals[line]=solid.ToArray();
            var selected=selectedPlane.GetValue(display); List<object> matches;
            if(selected==null||!planes.TryGetValue((int)selectedNet.GetValue(selected),out matches)||matches.Count!=1||
                !string.Equals((string)selectedName.GetValue(selected),(string)callsign.GetValue(matches[0]),StringComparison.OrdinalIgnoreCase)) { points.Invoke(line,new object[]{solid.ToArray()}); return; }
            var boundary=Boundary(matches[0],route,flat);
            if(double.IsInfinity(boundary)) { points.Invoke(line,new object[]{solid.ToArray()}); return; }
            var component=(Component)line;
            var canvas=component.GetComponentInParent<Canvas>();
            var rect=component.transform as RectTransform;
            if(!canvas||!rect) { points.Invoke(line,new object[]{solid.ToArray()}); return; }
            var root=canvas.rootCanvas?canvas.rootCanvas:canvas;
            var ownCamera=displayCamera.GetValue(display) as Camera;
            var camera=root.renderMode==RenderMode.ScreenSpaceOverlay?null:(ownCamera?ownCamera:(canvas.worldCamera?canvas.worldCamera:root.worldCamera));
            if(root.renderMode!=RenderMode.ScreenSpaceOverlay&&!camera) { points.Invoke(line,new object[]{solid.ToArray()}); return; }
            bool relative=(bool)relativeSize.GetValue(line);
            double sx=relative?rect.rect.width:1, sy=relative?rect.rect.height:1;
            if(Math.Abs(sx)<0.000001||Math.Abs(sy)<0.000001) { points.Invoke(line,new object[]{solid.ToArray()}); return; }
            var pivot=rect.pivot;
            Func<RoutePoint,Vector2> project=p=>RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3((float)p.X,(float)p.Y,0)));
            var pixels=screen.Select(p=>LineRendererCoordinates.ToMesh(p,sx,sy,pivot.x,pivot.y)).Select(project).Select(p=>new RoutePoint(p.x,p.y)).ToList();
            var stroke=Math.Abs((float)lineThickness.GetValue(line));
            var origin=project(new RoutePoint(0,0));
            var pixelStroke=Math.Max(Vector2.Distance(origin,project(new RoutePoint(stroke,0))),Vector2.Distance(origin,project(new RoutePoint(0,stroke))));
            var isSecondary=(bool)secondary.GetValue(display);
            double dash,gap; LineRendererCoordinates.Pattern(isSecondary?pixelStroke:0,(bool)lineCaps.GetValue(line),out dash,out gap);
            var segments=RouteDashGeometry.Build(pixels,RouteDashGeometry.ScreenSplit(flat,pixels,boundary),dash,gap);
            if(segments==null) { points.Invoke(line,new object[]{solid.ToArray()}); return; }
            var local=new List<Vector2>();
            foreach(var p in segments)
            {
                Vector2 point;
                if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,new Vector2((float)p.X,(float)p.Y),camera,out point)) { points.Invoke(line,new object[]{solid.ToArray()}); return; }
                var input=LineRendererCoordinates.FromMesh(new RoutePoint(point.x,point.y),sx,sy,pivot.x,pivot.y);
                local.Add(new Vector2((float)input.X,(float)input.Y));
            }
            points.Invoke(line,new object[]{local.ToArray()});
            if(!TS3Modding.Preferences.DeveloperMode.Enabled)return;
            var profile="camera="+(camera?camera.name:"overlay")+" root="+root.GetInstanceID()+" mode="+root.renderMode+
                " viewport="+(camera?camera.pixelRect.ToString():Screen.width+"x"+Screen.height)+" relative="+relative+" secondary="+isSecondary;
            string prior;
            if(!projectionProfiles.TryGetValue(line,out prior)||prior!=profile)
            {
                projectionProfiles[line]=profile;
                context.Log("ADIRS dash projection view="+((Component)display).GetInstanceID()+" "+profile+" strokePixels="+pixelStroke+" dash/gap="+dash+"/"+gap);
            }
        }
        private void Restore(object instance) { try { var line=renderer.GetValue(instance); Vector2[] original; if(line!=null&&originals.TryGetValue(line,out original)) points.Invoke(line,new object[]{original}); } catch { } }
        public void Dispose() { Canvas.willRenderCanvases-=Redraw; harmony.UnpatchSelf(); foreach(var pair in originals) try { if((UnityEngine.Object)pair.Key!=null) points.Invoke(pair.Key,new object[]{pair.Value}); } catch { } originals.Clear(); planes.Clear(); routes.Clear(); projectionProfiles.Clear(); if(ReferenceEquals(current,this)) current=null; }
    }
}


