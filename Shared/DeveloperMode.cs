using System;
using System.IO;

namespace TS3Modding.Preferences;

internal static class DeveloperMode
{
    internal static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TS3Modding", "preferences", "developer-mode.txt");
    private static long nextRead;
    private static bool cached;
    internal static bool Enabled
    {
        get
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now >= nextRead)
            {
                cached = Read(FilePath);
                nextRead = now + 2 * System.Diagnostics.Stopwatch.Frequency;
            }
            return cached;
        }
        set
        {
            Write(FilePath, value);
            cached = value;
            nextRead = System.Diagnostics.Stopwatch.GetTimestamp() + 2 * System.Diagnostics.Stopwatch.Frequency;
        }
    }

    internal static bool Read(string path)
    {
        try
        {
            return File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static void Write(string path, bool enabled)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(folder))
            throw new IOException("Preference directory unavailable.");
        Directory.CreateDirectory(folder);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, enabled ? "true" : "false");
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static bool Visible(string id, string section, string key, bool enabled)
        => section == "Diagnostics" ? enabled : enabled || id != "nl.ts3modding.rejectedtakeoffs" || section != "Testing" || key != "RejectNextEligibleTakeoff";
}
