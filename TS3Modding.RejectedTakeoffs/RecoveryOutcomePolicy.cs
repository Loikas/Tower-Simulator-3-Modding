using System;
namespace TS3Modding.RejectedTakeoffs
{
    // Both outcomes share one draw; the remaining probability is another departure.
    internal static class RecoveryOutcomePolicy
    {
        internal static float Percent(float value)=>float.IsNaN(value)||float.IsInfinity(value)?0:Math.Max(0,Math.Min(100,value));
        internal static float Limit(float value,float other)=>Math.Min(Percent(value),100-Percent(other));
        internal static bool TaxiBack(float taxi,float tug,bool tugAvailable,double sample)
        {
            var taxiChance=tugAvailable?Limit(taxi,tug):Percent(taxi);
            return !double.IsNaN(sample)&&sample>=0&&sample<1&&sample*100<taxiChance;
        }
    }
}
