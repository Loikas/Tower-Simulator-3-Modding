using System;
using System.IO;
using System.Linq;
using BepInEx;
using TS3Modding.DisplayBridge;
using UnityEngine;
namespace TS3Modding.ExternalAdirs
{
    public sealed class ExternalTowStandDriver:MonoBehaviour
    {
        private TowRouteDisplay routes;private TowRoutePreview[] routePreviews=new TowRoutePreview[0];private string routePath;private TowStandDisplay display;private TowStripDisplay strips;private TowStandMarker[] marker=new TowStandMarker[0];private string path;private float nextRead;
        internal void Initialize(Action<string> log)
        {
            path=TowStandTelemetry.PathForGame(Directory.GetParent(Paths.GameRootPath.TrimEnd('\\','/')).FullName);
            routePath=TowRouteTelemetry.PathForGame(Directory.GetParent(Paths.GameRootPath.TrimEnd('\\','/')).FullName);
            routes=new TowRouteDisplay(AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp"),()=>routePreviews,log);
            strips=new TowStripDisplay(AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp"),()=>marker);
            display=new TowStandDisplay(AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp"),()=>marker,log);
        }
        private void Update(){if(path!=null&&Time.realtimeSinceStartup>=nextRead){nextRead=Time.realtimeSinceStartup+.2f;marker=TowStandTelemetry.ReadMany(path,DateTime.UtcNow).Concat(TowStandTelemetry.ReadMany(path+".rto",DateTime.UtcNow)).ToArray();routePreviews=TowRouteTelemetry.Read(routePath,DateTime.UtcNow);}}
        private void OnDestroy(){display?.Dispose();strips?.Dispose();routes?.Dispose();}
    }
}
