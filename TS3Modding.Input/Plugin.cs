using System;
using BepInEx;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding.InputMod
{
    [BepInPlugin("nl.ts3modding.input","TS3 Input Panel","0.2.3")]
    [BepInDependency("nl.ts3modding.loader","0.7.0")]
    [BepInProcess("Tower! Simulator 3.exe")]
    public sealed class Plugin:BaseUnityPlugin
    {
        private void Awake()
        {
            GameObject obj=null;
            try
            {
                if(FrameworkServices.Current?.GameAssemblySha256!="26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7")throw new NotSupportedException("Unreviewed game assembly.");
                obj=new GameObject("TS3Modding.Input");obj.hideFlags=HideFlags.HideAndDontSave;DontDestroyOnLoad(obj);
                obj.AddComponent<InputDriver>().Initialize(Config,message=>Logger.LogInfo(message));
            }
            catch(Exception error){if(obj)Destroy(obj);Logger.LogError("Input mod disabled: "+error);}
        }
    }
}




