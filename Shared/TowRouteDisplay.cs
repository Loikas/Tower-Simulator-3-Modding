using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TS3Modding.PilotReadiness;
namespace TS3Modding.DisplayBridge
{
    // Own graphic: never replaces native route data, colors or hold-route dash meshes.
    public sealed class TowRouteGraphic:MaskableGraphic
    {
        private Vector2[] vertices=new Vector2[0];
        internal void Set(Vector2[] points){vertices=points;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();for(int i=0;i+3<vertices.Length;i+=4){var offset=mesh.currentVertCount;for(int j=0;j<4;j++)mesh.AddVert(vertices[i+j],color,Vector2.zero);mesh.AddTriangle(offset,offset+1,offset+2);mesh.AddTriangle(offset,offset+2,offset+3);}
        }
    }
    internal sealed class TowRouteDisplay:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static TowRouteDisplay current;private readonly Harmony harmony=new Harmony("nl.ts3modding.towroute.display");
        private readonly Func<TowRoutePreview[]> routes;private readonly Action<string> log;
        private readonly FieldInfo selected,id,name,primary,renderer,relative,camera;private readonly MethodInfo convert;
        private readonly Dictionary<Component,TowRouteGraphic> views=new Dictionary<Component,TowRouteGraphic>();private bool failed;private int frame=-1;
        internal TowRouteDisplay(Assembly assembly,Func<TowRoutePreview[]> routes,Action<string> log)
        {
            this.routes=routes;this.log=log;var adirs=assembly.GetType("Adirs",true);var route=assembly.GetType("AdirsRoute",true);
            selected=adirs.GetField("prevplane",Flags);id=selected.FieldType.GetField("netidx",Flags);name=selected.FieldType.GetField("name",Flags);
            primary=adirs.GetField("route",Flags);renderer=route.GetFields(Flags).Single(f=>f.FieldType.Name=="UILineRenderer");relative=renderer.FieldType.GetField("relativeSize",Flags);camera=adirs.GetField("cam",Flags);
            convert=adirs.GetMethod("ConvertPosition2Screen",Flags,null,new[]{typeof(Vector3),typeof(float)},null);
            if(current!=null)throw new InvalidOperationException("Tow route display already registered.");current=this;
            try{harmony.Patch(adirs.GetMethod("LateUpdate",Flags),postfix:new HarmonyMethod(typeof(TowRouteDisplay),nameof(Observe)));Canvas.willRenderCanvases+=Draw;}catch{Dispose();throw;}
        }
        private static void Observe(object __instance){var self=current;var view=__instance as Component;if(self!=null&&view&&!self.views.ContainsKey(view))self.views.Add(view,null);}
        private void Draw()
        {
            if(frame==Time.frameCount)return;frame=Time.frameCount;
            foreach(var entry in views.ToArray())
            {
                if(!entry.Key){if(entry.Value)UnityEngine.Object.Destroy(entry.Value.gameObject);views.Remove(entry.Key);continue;}
                try{DrawView(entry.Key);}catch(Exception e){if(entry.Value)entry.Value.Set(new Vector2[0]);if(!failed){failed=true;log("Tow route preview unavailable: "+e.Message);}}
            }
        }
        private void DrawView(Component view)
        {
            var chosen=selected.GetValue(view);var preview=chosen==null?null:TowRouteTelemetry.Selected(routes(),(int)id.GetValue(chosen),(string)name.GetValue(chosen),DateTime.UtcNow);
            var graphic=views[view];if(preview==null||!view.gameObject.activeInHierarchy){if(graphic)graphic.Set(new Vector2[0]);return;}
            var route=primary.GetValue(view);var line=route==null?null:renderer.GetValue(route) as Component;var rect=line?line.transform as RectTransform:null;
            if(!rect||!rect.parent){if(graphic)graphic.Set(new Vector2[0]);return;}
            if(!graphic){var go=new GameObject("TS3 Proposed Tow Route",typeof(RectTransform),typeof(CanvasRenderer),typeof(TowRouteGraphic));graphic=go.GetComponent<TowRouteGraphic>();graphic.raycastTarget=false;views[view]=graphic;}
            graphic.gameObject.layer=line.gameObject.layer;
            var target=graphic.rectTransform;target.SetParent(rect.parent,false);target.anchorMin=rect.anchorMin;target.anchorMax=rect.anchorMax;target.pivot=rect.pivot;target.sizeDelta=rect.sizeDelta;target.anchoredPosition3D=rect.anchoredPosition3D;target.localRotation=rect.localRotation;target.localScale=rect.localScale;target.SetSiblingIndex(rect.GetSiblingIndex()+1);
            graphic.color=preview.NativeChecked?new Color(.25f,.95f,1f,.95f):new Color(1f,.72f,.22f,.95f);
            var canvas=rect.GetComponentInParent<Canvas>();if(!canvas){graphic.Set(new Vector2[0]);return;}var root=canvas.rootCanvas?canvas.rootCanvas:canvas;var own=camera.GetValue(view) as Camera;
            var cam=root.renderMode==RenderMode.ScreenSpaceOverlay?null:(own?own:canvas.worldCamera?canvas.worldCamera:root.worldCamera);if(root.renderMode!=RenderMode.ScreenSpaceOverlay&&!cam){graphic.Set(new Vector2[0]);return;}
            var scaled=(bool)relative.GetValue(line);double sx=scaled?rect.rect.width:1,sy=scaled?rect.rect.height:1;
            var pixels=preview.Points.Select(p=>(Vector3)convert.Invoke(view,new object[]{new Vector3(p.X,p.Y,p.Z),0f})).Select(p=>LineRendererCoordinates.ToMesh(new RoutePoint(p.x,p.y),sx,sy,rect.pivot.x,rect.pivot.y)).Select(p=>RectTransformUtility.WorldToScreenPoint(cam,rect.TransformPoint(new Vector3((float)p.X,(float)p.Y,0)))).Select(p=>new RoutePoint(p.x,p.y)).ToArray();
            var segments=RouteDashGeometry.Build(pixels,0,9,6);if(segments==null){graphic.Set(new Vector2[0]);return;}
            var quads=new List<Vector2>();
            for(int i=0;i+1<segments.Count;i+=2)
            {
                var a=new Vector2((float)segments[i].X,(float)segments[i].Y);var b=new Vector2((float)segments[i+1].X,(float)segments[i+1].Y);var d=b-a;if(d.sqrMagnitude<.0001f)continue;var normal=new Vector2(-d.y,d.x).normalized*1.25f;
                foreach(var point in new[]{a+normal,b+normal,b-normal,a-normal}){Vector2 local;if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(target,point,cam,out local)){graphic.Set(new Vector2[0]);return;}quads.Add(local);}
            }
            graphic.Set(quads.ToArray());
        }
        public void Dispose(){Canvas.willRenderCanvases-=Draw;harmony.UnpatchSelf();foreach(var g in views.Values)if(g)UnityEngine.Object.Destroy(g.gameObject);views.Clear();if(ReferenceEquals(current,this))current=null;}
    }
}
