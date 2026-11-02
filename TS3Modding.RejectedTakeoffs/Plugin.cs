using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using TS3Modding.API;
namespace TS3Modding.RejectedTakeoffs
{
    [BepInPlugin("nl.ts3modding.rejectedtakeoffs","Rejected Takeoffs","0.5.2")]
    [BepInDependency("nl.ts3modding.loader", "0.8.14")]
    [BepInProcess("Tower! Simulator 3.exe")]
    public sealed class Plugin:BaseUnityPlugin
    {
        private void Awake()
        {
            try
            {
                var enabled=Config.Bind("Incidents","Enabled",true,"Allow occasional early rejected takeoffs. Aircraft may retry departure or request taxi back to a free stand after checks.");
                var chance=Config.Bind("Incidents","ProbabilityPercent",1f,new ConfigDescription("Chance per eligible takeoff, sampled once. Trigger speed varies with aircraft takeoff speed and remaining stopping distance.",new AcceptableValueRange<float>(0,25)));
                var interval=Config.Bind("Incidents","MinimumIntervalMinutes",15f,new ConfigDescription("Minimum simulation minutes between random rejections at this airport.",new AcceptableValueRange<float>(0,240)));
                var checks=Config.Bind("Recovery","ChecksSeconds",60f,new ConfigDescription("Central duration in simulation seconds for pilot checks. Each new incident randomly uses 75-125% of this value (60 means 45-75 seconds), sampled once when the abort occurs. Changes apply to future incidents. Taxi and takeoff clearances are refused until checks finish.",new AcceptableValueRange<float>(10,600)));
                var outcomes=new RecoveryOutcomeSettings(Config,()=>TowRecoveryServices.Available);
                var test=Config.Bind("Testing","RejectNextEligibleTakeoff",false,"Developer mode only (enable in the Mod Manager). Reject the next eligible early takeoff once, bypassing random probability and interval. Resets after activation. No effect outside the reviewed speed/runway conditions.");
                FrameworkServices.Current.RegisterModule(new RejectedTakeoffModule(Config,enabled,chance,interval,checks,test,outcomes));
            }
            catch(Exception e){Logger.LogError("Rejected Takeoffs disabled: "+e);}
        }
    }
}
