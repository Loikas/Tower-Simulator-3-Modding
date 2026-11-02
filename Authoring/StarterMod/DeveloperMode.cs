using System;
using System.Diagnostics;
using System.IO;

namespace TS3Modding.Preferences
{
    // Portable starter helper, not a public framework service. Never changes the preference.
    internal static class DeveloperMode
    {
        private static long nextRead;
        private static bool cached;
        internal static bool Enabled
        {
            get
            {
                var now = Stopwatch.GetTimestamp();
                if (now < nextRead) return cached;
                nextRead = now + 2 * Stopwatch.Frequency;
                try
                {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "TS3Modding", "preferences", "developer-mode.txt");
                    cached = File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(),
                        "true", StringComparison.OrdinalIgnoreCase);
                }
                catch { cached = false; }
                return cached;
            }
        }
    }
}
