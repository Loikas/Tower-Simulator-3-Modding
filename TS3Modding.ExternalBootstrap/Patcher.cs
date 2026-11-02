using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace TS3Modding.ExternalBootstrap
{
    // Initialization only: no game assembly is targeted or rewritten, even in memory.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new string[0];
        public static void Patch(AssemblyDefinition assembly) { }
        public static void Initialize()
        {
            var directory=Path.GetDirectoryName(typeof(Patcher).Assembly.Location);
            var external=Path.GetFullPath(Path.Combine(directory,"..","..",".."));
            var root=Directory.GetParent(external).FullName;
            if(!File.Exists(Path.Combine(external,"extwin.exe"))||!Directory.Exists(Path.Combine(root,"Airports")))
                throw new InvalidOperationException("External window game-data directory could not be verified.");
            Directory.SetCurrentDirectory(root);
        }
    }
}
