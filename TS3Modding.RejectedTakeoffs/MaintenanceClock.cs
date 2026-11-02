using System;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed class MaintenanceClock
    {
        internal double RemainingSeconds {get;private set;}
        private double lastDay;private bool started;
        internal MaintenanceClock(double seconds){if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0||seconds>86400)throw new ArgumentOutOfRangeException(nameof(seconds));RemainingSeconds=seconds;}
        internal void Advance(double day,bool running)
        {
            if(double.IsNaN(day)||double.IsInfinity(day))return;
            if(started&&running){var delta=day-lastDay;if(delta<-.5)delta+=1;if(delta>=0&&delta<.5)RemainingSeconds=Math.Max(0,RemainingSeconds-delta*86400);}
            lastDay=day;started=true;
        }
        internal static double Duration(double minimum,double maximum,double random)
        {
            minimum=Limit(minimum);maximum=Math.Max(minimum,Limit(maximum));
            return (minimum+(maximum-minimum)*Math.Max(0,Math.Min(1,random)))*60;
        }
        internal static double Limit(double value)=>double.IsNaN(value)||double.IsInfinity(value)?60:Math.Max(1,Math.Min(1440,value));
    }
}
