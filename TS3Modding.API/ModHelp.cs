#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.API
{
    // Metadata only: registering help never installs commands or changes key bindings.
    [Serializable] public sealed class ModHelpItem
    {
        public string Syntax;
        public string Description;
        public ModHelpItem(string syntax, string description) { Syntax=syntax; Description=description; }
    }
    [Serializable] public sealed class ModHelpPage
    {
        public int SchemaVersion=1;
        public string Id;
        public string Version;
        public string Description;
        public ModHelpItem[] Keybinds;
        public ModHelpItem[] Commands;
        public ModHelpPage(string id,string version,string description,ModHelpItem[] keybinds=null,ModHelpItem[] commands=null)
        { Id=id; Version=version; Description=description; Keybinds=keybinds??new ModHelpItem[0]; Commands=commands??new ModHelpItem[0]; }
        public static ModHelpPage Copy(ModHelpPage page)
        {
            if(page==null||page.SchemaVersion!=1||string.IsNullOrWhiteSpace(page.Id)||page.Id.Length>120||page.Id.Any(c=>!char.IsLetterOrDigit(c)&&c!='.'&&c!='-'&&c!='_')||page.Id=="."||page.Id=="..")
                throw new ArgumentException("Use a valid mod ID and help schema 1.");
            System.Version version;if(!System.Version.TryParse(page.Version,out version))throw new ArgumentException("Use the package version for help metadata.");
            if((page.Description??"").Length>4096)throw new ArgumentException("Help description is too long.");
            var keybinds=CopyItems(page.Keybinds);var commands=CopyItems(page.Commands);
            if((page.Description??"").Length+keybinds.Concat(commands).Sum(i=>i.Syntax.Length+i.Description.Length)>32768)throw new ArgumentException("Combined help text exceeds 32768 characters.");
            return new ModHelpPage(page.Id,page.Version,page.Description,keybinds,commands);
        }
        private static ModHelpItem[] CopyItems(ModHelpItem[] items)
        {
            items=items??new ModHelpItem[0];if(items.Length>64)throw new ArgumentException("At most 64 help items per category.");
            return items.Select(item=>{if(item==null||string.IsNullOrWhiteSpace(item.Syntax)||item.Syntax.Length>512||(item.Description??"").Length>2048)throw new ArgumentException("Invalid help item.");return new ModHelpItem(item.Syntax,item.Description??"");}).ToArray();
        }
    }
    public static class ModHelp
    {
        private static readonly Dictionary<string,ModHelpPage> pages=new Dictionary<string,ModHelpPage>(StringComparer.Ordinal);
        // Main-thread API. The framework exports these snapshots for the launcher.
        public static event Action Changed;
        public static ModHelpPage[] Snapshot()=>pages.Values.Select(ModHelpPage.Copy).ToArray();
        public static IDisposable Register(ModHelpPage page)
        {
            var copy=ModHelpPage.Copy(page);
            if(pages.ContainsKey(copy.Id))throw new InvalidOperationException("Help is already registered for "+copy.Id);
            pages.Add(copy.Id,copy);Changed?.Invoke();return new Registration(()=>{if(pages.Remove(copy.Id))Changed?.Invoke();});
        }
        private sealed class Registration:IDisposable
        { private Action release; public Registration(Action release){this.release=release;}public void Dispose(){var action=release;release=null;action?.Invoke();} }
    }
}
