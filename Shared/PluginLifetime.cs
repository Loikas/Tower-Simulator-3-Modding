namespace TS3Modding.Settings
{
    internal static class PluginLifetime
    {
        // The framework module outlives TS3's destroyed Unity bootstrap component.
        // A registered managed plugin instance still identifies a loaded plugin.
        internal static bool IsLoaded(object bootstrap)=>!object.ReferenceEquals(bootstrap,null);
    }
}
