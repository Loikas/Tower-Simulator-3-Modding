using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TS3Modding.DisplayBridge
{
    internal static class ExternalWindowLaunch
    {
        internal static ProcessStartInfo Create(string gameRoot)
        {
            var root=Path.GetFullPath(gameRoot);
            var info=new ProcessStartInfo(Path.Combine(root,"extwin","extwin.exe"))
                {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=false};
            // Parent Doorstop state belongs to the main runtime. Never mutate the parent's environment.
            foreach(var key in info.EnvironmentVariables.Keys.Cast<string>().Where(k=>k.StartsWith("DOORSTOP_",StringComparison.OrdinalIgnoreCase)).ToArray())
                info.EnvironmentVariables.Remove(key);
            return info;
        }
    }
}
