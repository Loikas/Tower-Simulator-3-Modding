using System;
using BepInEx.Configuration;
namespace TS3Modding.RejectedTakeoffs
{
    // Bind the second outcome only once an actual tug recovery provider is available.
    // An installed tug mod alone does not imply support for runway collection.
    internal sealed class RecoveryOutcomeSettings
    {
        internal readonly ConfigEntry<float> Taxi;
        internal ConfigEntry<float> Tug {get;private set;}
        private bool updating;
        private readonly Func<bool> tugAvailable;
        internal RecoveryOutcomeSettings(ConfigFile config,Func<bool> tugAvailable=null)
        {
            this.tugAvailable=tugAvailable??(()=>true);
            Taxi=config.Bind("Recovery","ReturnToStandPercent",25f,new ConfigDescription("Chance after checks of requesting taxi back to a free compatible stand. Taxi-back and tug recovery share a maximum of 100%; the remainder means retry departure. A free stand is required.",new AcceptableValueRange<float>(0,100)));
            Taxi.SettingChanged+=(_,__)=>Normalize(true);
            Normalize(true);
        }
        internal void BindTugRecovery(ConfigFile config)
        {
            if(Tug!=null)return;
            Tug=config.Bind("Recovery","TugRecoveryPercent",10f,new ConfigDescription("Chance after checks of a breakdown requiring a tug to a free compatible stand. Available only with enabled tug recovery support. Together with taxi-back this cannot exceed 100%; the remainder means retry departure.",new AcceptableValueRange<float>(0,100)));
            Tug.SettingChanged+=(_,__)=>Normalize(false);
            Normalize(false);
        }
        private void Normalize(bool taxiChanged)
        {
            if(updating)return;
            updating=true;
            try
            {
                if(Tug==null){Taxi.Value=RecoveryOutcomePolicy.Percent(Taxi.Value);return;}
                if(taxiChanged&&tugAvailable()){Tug.Value=RecoveryOutcomePolicy.Percent(Tug.Value);Taxi.Value=RecoveryOutcomePolicy.Limit(Taxi.Value,Tug.Value);}
                else{Taxi.Value=RecoveryOutcomePolicy.Percent(Taxi.Value);Tug.Value=RecoveryOutcomePolicy.Limit(Tug.Value,Taxi.Value);}
            }
            finally{updating=false;}
        }
    }
}
