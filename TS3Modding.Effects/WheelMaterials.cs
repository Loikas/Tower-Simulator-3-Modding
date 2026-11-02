using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TS3Modding.Effects
{
    // The legacy wheel prefabs carry materials incompatible with the game's HDRP.
    // These materials/textures belong only to our instantiated emitters.
    internal sealed class WheelMaterials : IDisposable
    {
        private Material smoke,spray;
        private Texture2D texture;
        private readonly Action<string> log;
        private bool warned;
        internal WheelMaterials(Action<string> log){this.log=log;}
        internal bool Apply(ParticleSystem ps,bool wet)
        {
            var renderer=ps.GetComponent<ParticleSystemRenderer>();if(!renderer)return false;
            var material=wet?spray:smoke;
            if(!material)
            {
                var shader=Shader.Find("HDRP/Unlit");
                if(!shader||!shader.isSupported)
                {
                    renderer.enabled=false;
                    if(!warned){warned=true;log("Wheel particles hidden: supported HDRP/Unlit shader unavailable; refusing magenta fallback.");}
                    return false;
                }
                material=new Material(shader){name="TS3 Effects "+(wet?"spray":"smoke"),hideFlags=HideFlags.HideAndDontSave};
                if(!texture)texture=CreateTexture();
                material.SetFloat("_SurfaceType",1);material.SetFloat("_BlendMode",0);
                material.SetFloat("_AlphaCutoffEnable",0);material.SetFloat("_TransparentZWrite",0);
                material.SetFloat("_ZWrite",0);material.SetFloat("_DoubleSidedEnable",1);
                material.SetColor("_UnlitColor",wet?new Color(.82f,.94f,1f,.16f):new Color(.64f,.66f,.68f,.3f));
                material.SetTexture("_UnlitColorMap",texture);
                var hd=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial")).FirstOrDefault(t=>t!=null);
                var validate=hd?.GetMethod("ValidateMaterial",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(Material)},null);
                if(validate==null){UnityEngine.Object.Destroy(material);renderer.enabled=false;throw new NotSupportedException("HDRP material validation unavailable.");}
                validate.Invoke(null,new object[]{material});
                material.renderQueue=3000;
                if(wet)spray=material;else smoke=material;
                log("Wheel material ready: "+material.name+", shader="+shader.name+", transparent HDRP, owned soft particle texture.");
            }
            renderer.sharedMaterial=material;renderer.enabled=true;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
            // Replace the prefab's atlas layout as well as its texture. Reusing its
            // old tile coordinates crops a soft sprite into square patches.
            var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.mode=ParticleSystemAnimationMode.Grid;
            sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.numTilesX=8;sheet.numTilesY=8;
            sheet.timeMode=ParticleSystemAnimationTimeMode.Lifetime;sheet.startFrame=0;sheet.cycleCount=1;
            sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,1));
            var main=ps.main;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=512;
            // Remove prefab vertex tint so water keeps its own cool, translucent colour.
            main.startColor=Color.white;
            var colour=ps.colorOverLifetime;colour.enabled=false;
            main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
            var emission=ps.emission;emission.enabled=true;emission.rateOverTime=0;
            main.startLifetime=wet?new ParticleSystem.MinMaxCurve(.35f,.8f):new ParticleSystem.MinMaxCurve(1.2f,2.5f);
            main.startSize=wet?new ParticleSystem.MinMaxCurve(.12f,.35f):new ParticleSystem.MinMaxCurve(.6f,1.4f);
            var growth=ps.sizeOverLifetime;growth.enabled=true;
            growth.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.6f,1,wet?1.2f:2.2f));
            return true;
        }
        private static Texture2D CreateTexture()
        {
            const int tile=64,size=tile*8;
            var result=new Texture2D(size,size,TextureFormat.RGBA32,false){name="TS3 soft wheel particle",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                // Pixel coordinates start at the bottom; particle sheet frames start
                // at the upper-left tile.
                int frame=ParticleFade.Frame(x/tile,y/tile);
                float dx=((x%tile)+.5f)/tile*2-1,dy=((y%tile)+.5f)/tile*2-1;
                var alpha=Mathf.Pow(Mathf.Clamp01(1-(dx*dx+dy*dy)/.85f),2)*(.7f+.3f*Mathf.PerlinNoise(dx*2+7,dy*2+13))*ParticleFade.At(frame/63f);
                pixels[y*size+x]=new Color(1,1,1,alpha);
            }
            result.SetPixels(pixels);result.Apply(false,true);return result;
        }
        public void Dispose()
        {if(smoke)UnityEngine.Object.Destroy(smoke);if(spray)UnityEngine.Object.Destroy(spray);if(texture)UnityEngine.Object.Destroy(texture);smoke=spray=null;texture=null;}
    }
}
