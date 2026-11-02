using System;
namespace TS3Modding.Tugs {
internal static class TowRequestCadence {
internal static float Bound(float value)=>float.IsNaN(value)||float.IsInfinity(value)?0:Math.Max(0,Math.Min(120,value));
internal static double Delay(float minimum,float maximum,double sample) {
maximum=Bound(maximum);minimum=Math.Min(Bound(minimum),maximum);
if(maximum==0)return double.PositiveInfinity;
return minimum+(maximum-minimum)*Math.Max(0,Math.Min(1,sample));
}
}}
