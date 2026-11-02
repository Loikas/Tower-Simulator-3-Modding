using BepInEx.Configuration;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed class MaintenanceSettings
    {
        // Preserve existing breakdown keys so installed settings survive the update.
        internal readonly ConfigEntry<float> Minimum,Maximum,TaxiMinimum,TaxiMaximum;
        internal MaintenanceSettings(ConfigFile config)
        {
            BindRange(config,"Maintenance",60,180,"breakdown recovery tow",out Minimum,out Maximum);
            BindRange(config,"Taxi-back Maintenance",15,45,"rejected-takeoff taxi back",out TaxiMinimum,out TaxiMaximum);
        }
        private static void BindRange(ConfigFile config,string section,float minimum,float maximum,string outcome,out ConfigEntry<float> min,out ConfigEntry<float> max)
        {
            var low=config.Bind(section,"MinimumMinutes",minimum,new ConfigDescription("Minimum maintenance time in simulation minutes after a "+outcome+" reaches its stand. A random duration between minimum and maximum is sampled once. Further tows do not reset it; completed repairs wait until parked before normal push/start readiness returns. Saved games preserve remaining time. Changes apply to new maintenance periods.",new AcceptableValueRange<float>(1,1440)));
            var high=config.Bind(section,"MaximumMinutes",maximum,new ConfigDescription("Maximum maintenance time in simulation minutes after a "+outcome+". Must be at least the minimum; changing either bound adjusts the other when needed. Duration is sampled once on arrival at stand. Changes apply to new maintenance periods.",new AcceptableValueRange<float>(1,1440)));
            bool updating=false;
            System.Action<bool> normalize=minimumChanged=>{if(updating)return;updating=true;try{low.Value=(float)MaintenanceClock.Limit(low.Value);high.Value=(float)MaintenanceClock.Limit(high.Value);if(low.Value>high.Value){if(minimumChanged)high.Value=low.Value;else low.Value=high.Value;}}finally{updating=false;}};
            low.SettingChanged+=(_,__)=>normalize(true);high.SettingChanged+=(_,__)=>normalize(false);normalize(true);
            min=low;max=high;
        }
    }
}
