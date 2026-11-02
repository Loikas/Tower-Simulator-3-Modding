using System;
using System.Collections.Generic;
using System.Linq;
using TS3Modding.DisplayBridge;
using UnityEngine;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed partial class RejectedTakeoffModule
    {
        private TowStandDisplay standDisplay;private string standMarkerPath;private float nextStandMarker;
        private TowStandMarker[] ReturnStandMarkers()
        {
            if(!enabled.Value)return new TowStandMarker[0];
            var markers=new List<TowStandMarker>();
            foreach(var pair in incidents)
            {
                var incident=pair.Value;if(incident.ReturnStand==null||incident.TugRequested||incident.Returned||!Same(pair.Key,incident))continue;
                var points=Get(incident.ReturnStand,"points") as List<Vector3>;if(points==null||points.Count<2)continue;
                var first=points.Count/2;var last=Math.Min(first+2,points.Count-1);if(first==last)first--;
                var position=points[first];var delta=points[last]-position;
                markers.Add(new TowStandMarker{CreatedUtc=DateTime.UtcNow,NetworkId=(int)Get(pair.Key,"_netidx"),Callsign=incident.Callsign,X=position.x,Y=position.y,Z=position.z,Heading=-Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,Destination=""});
            }
            return markers.ToArray();
        }
        private void PublishReturnStandMarkers()
        {
            if(standMarkerPath==null||Time.realtimeSinceStartup<nextStandMarker)return;nextStandMarker=Time.realtimeSinceStartup+.2f;
            try{TowStandTelemetry.WriteMany(standMarkerPath,ReturnStandMarkers());}catch(Exception error){context.Log("Taxi-back stand marker publication failed: "+error.Message);}
        }
    }
}
