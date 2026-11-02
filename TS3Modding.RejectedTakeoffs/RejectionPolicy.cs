using System;
namespace TS3Modding.RejectedTakeoffs
{
    internal static class RejectionPolicy
    {
        internal static bool Eligible(int state,int substate,bool active,bool deleted,bool takeoff,bool lineup,float altitude,float speedKnots,float speed,float dt,double runwayRemaining,double runwayOffset,double headingAlignment,double maximumKnots=110)
        {
            var values=new[]{(double)altitude,speedKnots,speed,dt,runwayRemaining,runwayOffset,headingAlignment,maximumKnots};
            foreach(var value in values)if(double.IsNaN(value)||double.IsInfinity(value))return false;
            return state==14&&substate==1&&active&&!deleted&&takeoff&&!lineup&&altitude<=.1&&altitude>=0&&speedKnots>=10&&speedKnots<=maximumKnots&&speed>0&&dt>0&&dt<=.2&&runwayOffset<=15&&headingAlignment>=.95&&runwayRemaining>=speed*speed/6+speed*2+500;
        }
        internal static double TriggerKnots(double takeoffKnots,double sample)
        {
            if(double.IsNaN(sample)||double.IsInfinity(sample)||double.IsNaN(takeoffKnots)||double.IsInfinity(takeoffKnots)||takeoffKnots<25||takeoffKnots>400)return 0;
            var maximum=Math.Min(110,takeoffKnots*.7);var minimum=Math.Min(35,takeoffKnots*.3);
            return minimum+(maximum-minimum)*Math.Max(0,Math.Min(1,sample));
        }
        internal static double ChecksDuration(double setting,double sample)=>Math.Max(10,Math.Min(600,setting))*(.75+.5*Math.Max(0,Math.Min(1,sample)));
        internal static bool RequiresChecks(int command)=>TakeoffClearance(command)||command==1||command==2||command==18||command==21||command==32;
        internal static bool TakeoffClearance(int command)=>command==39||command==40;
        internal static bool Ready(float speed,double stoppedSeconds,double checksSeconds)=>speed>=0&&speed<=.1&&stoppedSeconds>=checksSeconds;
    }
}
