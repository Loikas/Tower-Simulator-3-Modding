using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using UnityEngine;

namespace TS3Modding.ExternalAdirs
{
    [BepInPlugin("nl.ts3modding.externaladirs","TS3 External ADIRS","0.1.5")]
    [BepInProcess("extwin.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            GameObject driver=null;
            try
            {
                var assembly=Path.Combine(Paths.ManagedPath,"Assembly-CSharp.dll");
                using(var sha=SHA256.Create()) using(var input=File.OpenRead(assembly))
                    if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","")!="2F33C87E516E5213FE171CE74C1DEB77548450C010B90FFC9C5733D5B937FDB4")
                        throw new NotSupportedException("Unreviewed extwin assembly.");
                driver=new GameObject("TS3Modding.ExternalAdirs"); driver.hideFlags=HideFlags.HideAndDontSave;
                DontDestroyOnLoad(driver);
                driver.AddComponent<ExternalTowStandDriver>().Initialize(message=>Logger.LogInfo(message));
                if(Config.Bind("Display","DashedRoutesBeyondHold",true,"Render hold-route dashes in this external window using fresh main-game telemetry.").Value)
                    driver.AddComponent<ExternalAdirsDriver>().Initialize(message=>Logger.LogInfo(message));
            }
            catch(Exception error) { if(driver) Destroy(driver); Logger.LogError("External ADIRS disabled: "+error); }
        }
    }
}
