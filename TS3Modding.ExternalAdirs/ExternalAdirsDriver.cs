using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TS3Modding.DisplayBridge;
using TS3Modding.PilotReadiness;
using UnityEngine;

namespace TS3Modding.ExternalAdirs
{
    public sealed class ExternalAdirsDriver : MonoBehaviour
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static ExternalAdirsDriver current;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.externaladirs");
        private readonly Dictionary<object,List<Vector3>> routes=new Dictionary<object,List<Vector3>>();
        private readonly Dictionary<object,Vector2[]> originals=new Dictionary<object,Vector2[]>();
        private FieldInfo owner,renderer,primary,selected,id,callsign,cameraField,relative,lineList,thickness,caps;
        private MethodInfo convert,setPoints;
        private string path,requestPath;
        private HoldDisplaySnapshot snapshot;
        private Action<string> log;
        private float nextRead,nextRequest;
        private int renderFrame=-1;
        private bool failed,received;
        private static FieldInfo Field(Type type,string field)=>type.GetField(field,Flags)??throw new MissingFieldException(type.FullName,field);
        internal void Initialize(Action<string> logger)
        {
            log=logger;
            var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp");
            var route=assembly.GetType("AdirsRoute",true); var display=assembly.GetType("Adirs",true);
            owner=Field(route,"adirs");
            renderer=route.GetFields(Flags).Single(field=>field.FieldType.Name=="UILineRenderer");
            primary=Field(display,"route"); selected=Field(display,"prevplane"); cameraField=Field(display,"cam");
            id=Field(selected.FieldType,"netidx"); callsign=Field(selected.FieldType,"name");
            relative=Field(renderer.FieldType,"relativeSize"); lineList=Field(renderer.FieldType,"lineList");
            thickness=Field(renderer.FieldType,"lineThickness"); caps=Field(renderer.FieldType,"lineCaps");
            convert=display.GetMethod("ConvertPosition2Screen",Flags,null,new[]{typeof(Vector3),typeof(float)},null);
            setPoints=renderer.FieldType.GetMethod("set_Points",Flags,null,new[]{typeof(Vector2[])},null);
            if(convert==null||setPoints==null) throw new NotSupportedException("External ADIRS signatures changed.");
            var gameRoot=Directory.GetParent(Paths.GameRootPath.TrimEnd('\\','/')).FullName;
            path=HoldDisplayTelemetry.PathForGame(gameRoot);
            requestPath=path+".request-"+System.Diagnostics.Process.GetCurrentProcess().Id;
            current=this;
            harmony.Patch(route.GetMethod("SetRoute",Flags,null,new[]{typeof(List<Vector3>),typeof(bool)},null),postfix:new HarmonyMethod(typeof(ExternalAdirsDriver),nameof(RoutePosted)));
            Canvas.willRenderCanvases+=Render;
            log("External ADIRS companion ready; hold bridge="+path);
        }
        private static void RoutePosted(object __instance,List<Vector3> __0,bool __1)
        {
            var self=current; if(!self) return;
            if(__1||__0==null||__0.Count<2||__0.Count>4096) { self.routes.Remove(__instance); return; }
            self.routes[__instance]=new List<Vector3>(__0);
        }
        private void Update()
        {
            if(log==null) return;
            if(Time.realtimeSinceStartup>=nextRead)
            { nextRead=Time.realtimeSinceStartup+0.2f; snapshot=HoldDisplayTelemetry.Read(path,DateTime.UtcNow); }
            if(Time.realtimeSinceStartup<nextRequest) return; nextRequest=Time.realtimeSinceStartup+0.5f;
            try
            {
                var requests=new Dictionary<string,HoldMarker>();
                foreach(var pair in routes.ToArray())
                {
                    var component=pair.Key as Component;
                    if(!component) { routes.Remove(pair.Key); continue; }
                    if(!component.gameObject.activeInHierarchy) continue;
                    var display=owner.GetValue(pair.Key); var plane=display==null?null:selected.GetValue(display);
                    if(plane==null) continue;
                    var row=new HoldMarker{NetworkId=(int)id.GetValue(plane),Callsign=(string)callsign.GetValue(plane)};
                    requests[row.Key]=row;
                }
                HoldDisplayTelemetry.Write(requestPath,requests.Values,DateTime.UtcNow);
            }
            catch(Exception error) { Report(error); }
        }
        private void Render()
        {
            if(renderFrame==Time.frameCount) return; renderFrame=Time.frameCount;
            foreach(var pair in routes.ToArray())
            {
                var component=pair.Key as Component; if(!component||!component.gameObject.activeInHierarchy) continue;
                try { Draw(pair.Key,pair.Value); }
                catch(Exception error) { Report(error); }
            }
        }
        private void Draw(object route,List<Vector3> world)
        {
            var display=owner.GetValue(route); if(display==null||!ReferenceEquals(primary.GetValue(display),route)) return;
            var line=renderer.GetValue(route); if(line==null||!(bool)lineList.GetValue(line)) return;
            var input=world.Select(p=>(Vector3)convert.Invoke(display,new object[]{p,0f})).Select(p=>new RoutePoint(p.x,p.y)).ToList();
            var solid=new List<Vector2>(); for(int i=1;i<input.Count;i++) { solid.Add(new Vector2((float)input[i-1].X,(float)input[i-1].Y)); solid.Add(new Vector2((float)input[i].X,(float)input[i].Y)); }
            originals[line]=solid.ToArray();
            var plane=selected.GetValue(display); HoldMarker marker=null;
            var key=plane==null?null:new HoldMarker{NetworkId=(int)id.GetValue(plane),Callsign=(string)callsign.GetValue(plane)}.Key;
            double boundary;
            var flat=world.Select(p=>new RoutePoint(p.x,p.z)).ToList();
            if(key==null||snapshot==null||!snapshot.Fresh(DateTime.UtcNow)||!snapshot.Markers.TryGetValue(key,out marker)||
                !RouteDashGeometry.Project(flat,new RoutePoint(marker.X,marker.Z),15,out boundary))
            { setPoints.Invoke(line,new object[]{solid.ToArray()}); return; }
            var component=(Component)line; var rect=component.transform as RectTransform; var canvas=component.GetComponentInParent<Canvas>();
            if(!rect||!canvas) return;
            var root=canvas.rootCanvas?canvas.rootCanvas:canvas; var own=cameraField.GetValue(display) as Camera;
            var camera=root.renderMode==RenderMode.ScreenSpaceOverlay?null:(own?own:(canvas.worldCamera?canvas.worldCamera:root.worldCamera));
            if(root.renderMode!=RenderMode.ScreenSpaceOverlay&&!camera) return;
            double sx=(bool)relative.GetValue(line)?rect.rect.width:1,sy=(bool)relative.GetValue(line)?rect.rect.height:1;
            if(Math.Abs(sx)<0.000001||Math.Abs(sy)<0.000001) return;
            var pivot=rect.pivot;
            Func<RoutePoint,Vector2> project=p=>RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3((float)p.X,(float)p.Y,0)));
            var pixels=input.Select(p=>LineRendererCoordinates.ToMesh(p,sx,sy,pivot.x,pivot.y)).Select(project).Select(p=>new RoutePoint(p.x,p.y)).ToList();
            var width=Math.Abs((float)thickness.GetValue(line)); var origin=project(new RoutePoint(0,0));
            var stroke=Math.Max(Vector2.Distance(origin,project(new RoutePoint(width,0))),Vector2.Distance(origin,project(new RoutePoint(0,width))));
            double dash,gap; LineRendererCoordinates.Pattern(stroke,(bool)caps.GetValue(line),out dash,out gap);
            var segments=RouteDashGeometry.Build(pixels,RouteDashGeometry.ScreenSplit(flat,pixels,boundary),dash,gap); if(segments==null) return;
            var result=new List<Vector2>();
            foreach(var endpoint in segments)
            {
                Vector2 local;
                if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,new Vector2((float)endpoint.X,(float)endpoint.Y),camera,out local)) return;
                var point=LineRendererCoordinates.FromMesh(new RoutePoint(local.x,local.y),sx,sy,pivot.x,pivot.y);
                result.Add(new Vector2((float)point.X,(float)point.Y));
            }
            setPoints.Invoke(line,new object[]{result.ToArray()});
            if(!received) { received=true; log("External ADIRS hold geometry received and rendered locally; dash/gap="+dash+"/"+gap+" pixels."); }
        }
        private void Report(Exception error) { if(!failed) { failed=true; log("External ADIRS error: "+error); } }
        private void OnDestroy()
        {
            Canvas.willRenderCanvases-=Render; harmony.UnpatchSelf();
            foreach(var pair in originals) try { if((UnityEngine.Object)pair.Key) setPoints.Invoke(pair.Key,new object[]{pair.Value}); } catch { }
            if(ReferenceEquals(current,this)) current=null;
            try { if(requestPath!=null&&File.Exists(requestPath)) File.Delete(requestPath); } catch(IOException) { }
        }
    }
}
