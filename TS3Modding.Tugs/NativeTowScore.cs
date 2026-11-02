using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace TS3Modding.Tugs
{
    // Reuse the native positive-arrival amount and SCORE branch, without incrementing landing/takeoff counts.
    internal sealed class NativeTowScore
    {
        const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly MethodInfo increment,points,multiplayer;
        readonly FieldInfo instance;
        readonly object scoreKind,arrivalKind;
        readonly Action<string> log;
        internal NativeTowScore(Assembly assembly,Action<string> log)
        {
            this.log=log;var settings=assembly.GetType("Settings",true);var penalty=assembly.GetType("PenaltyHandler",true);
            increment=settings.GetMethods(F).Single(m=>m.Name=="IncStat"&&m.IsStatic&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType.IsEnum&&m.GetParameters()[1].ParameterType==typeof(int));
            points=penalty.GetMethods(F).Single(m=>m.Name=="GetPts"&&m.IsStatic&&m.ReturnType==typeof(int)&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.IsEnum);
            scoreKind=Enum.Parse(increment.GetParameters()[0].ParameterType,"SCORE");arrivalKind=Enum.Parse(points.GetParameters()[0].ParameterType,"POSITIVE_LAND");
            multiplayer=assembly.GetType("TCP",true).GetMethod("get_mp_state",F);instance=assembly.GetType("Game",true).GetField("instance",F);
            if(Convert.ToInt32(scoreKind)!=0||Convert.ToInt32(arrivalKind)!=0)throw new NotSupportedException("Native scoring profile changed.");
        }
        internal void Award(Component aircraft)
        {
            try
            {
                var game=instance.GetValue(null);if(!aircraft||game==null||Convert.ToInt32(multiplayer.Invoke(null,null))!=0)return;
                object Get(object value,string key)=>value.GetType().GetField(key,F).GetValue(value);
                if((bool)Get(game,"loading")||(bool)Get(aircraft,"_deleted")||!(bool)Get(aircraft,"_imatterminal")||Math.Abs((float)Get(aircraft,"_lv"))>=.1f||Convert.ToInt32(Get(aircraft,"_state"))!=8)return;
                var live=(System.Collections.IEnumerable)Get(game,"airplanes");if(!live.Cast<object>().Any(p=>ReferenceEquals(p,aircraft)))return;
                var reward=(int)points.Invoke(null,new[]{arrivalKind});if(reward<=0)return;
                increment.Invoke(null,new object[]{scoreKind,reward});
                log("[Tugs] Completed tow reward: "+Get(aircraft,"_name")+" +"+reward+" native score points.");
            }catch(Exception error){log("[Tugs] Tow score unavailable; completed parking retained: "+error.Message);}
        }
    }
}
