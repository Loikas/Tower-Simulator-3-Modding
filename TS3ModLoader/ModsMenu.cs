using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using TS3Modding.API;
using UnityEngine;
using UnityEngine.UI;

namespace TS3Modding
{
    // Owned by the persistent framework host, never by the short-lived bootstrap component.
    public sealed partial class ModsMenu:MonoBehaviour
    {
        private readonly Harmony harmony=new Harmony("nl.ts3modding.loader.settings");
        private float nextVisibilityPoll;
        private ModSettingsRegistry registry;private FieldInfo gameInstance;private bool lastAirportActive;
        private readonly List<Page> pages=new List<Page>();
        private Action<string> log;
        private Type menuType;
        private static ModsMenu current;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed class Page
        {
            public Component Menu,Group;
            public FieldInfo GroupsField;
            public Type GroupType;
            public readonly List<Component> Rows=new List<Component>();
            public Text Status;public Mod SelectedMod;public string VisibilityKey;
        }
        private sealed class Mod
        {public string Id,Name,Version,Status;public ConfigFile Config;}
        [Serializable] private sealed class StateList {public StateItem[] Items=new StateItem[0];}
        [Serializable] private sealed class StateItem {public StatePackage Package=null;public bool Enabled=false;}
        [Serializable] private sealed class StatePackage {public string Id=null,Name=null,Version=null;}
        internal void Initialize(Action<string> logger)
        {
            log=logger;current=this;
            var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp");
            menuType=assembly.GetType("MenuSettings",true);
            gameInstance=assembly.GetType("Game",true).GetField("instance",BindingFlags.Static|Flags);
            registry=new ModSettingsRegistry(AirportActive,id=>Chainloader.PluginInfos.TryGetValue(id,out var info)&&Settings.PluginLifetime.IsLoaded(info.Instance));lastAirportActive=AirportActive();
            ModSettingsServices.Attach(registry);
            foreach(var name in new[]{"Start","OnEnable"})harmony.Patch(AccessTools.Method(menuType,name),postfix:new HarmonyMethod(typeof(ModsMenu),nameof(MenuReady)));
            foreach(var menu in Resources.FindObjectsOfTypeAll(menuType).OfType<Component>())TryAttach(menu);
            log("Framework Mods tab ready; settings distinguish live, main-menu-only and restart-required changes.");
        }
        private bool AirportActive()
        {
            if(gameInstance==null)return true;
            return gameInstance.GetValue(null) is UnityEngine.Object game && game;
        }
        private void Update()
        {
            if(registry==null||Time.realtimeSinceStartup<nextVisibilityPoll)return;nextVisibilityPoll=Time.realtimeSinceStartup+.5f;
            var active=AirportActive();var sessionChanged=active!=lastAirportActive;lastAirportActive=active;
            foreach(var page in pages.ToArray())if(page.Menu&&page.SelectedMod!=null&&(sessionChanged||page.VisibilityKey!=VisibilityKey(page.SelectedMod)))ShowSettings(page,page.SelectedMod);
        }
        private static void MenuReady(Component __instance){current?.TryAttach(__instance);}
        private void TryAttach(Component menu)
        {
            try{Attach(menu);}catch(Exception error){log("Mods tab connection failed: "+error);}
        }
        private void Attach(Component menu)
        {
            if(!menu||!menu.gameObject.scene.IsValid()||pages.Any(p=>p.Menu==menu))return;
            var groupsField=AccessTools.Field(menuType,"groups");var groups=(Array)groupsField.GetValue(menu);
            if(groups==null||groups.Length==0)return;
            var original=groups.GetValue(0) as Component;if(!original)return;
            var clone=Instantiate(original.gameObject,original.transform.parent);clone.name="TS3Modding.ModsTab";
            var type=original.GetType();var group=(Component)clone.GetComponent(type);
            var items=AccessTools.Field(type,"PDFOGOIGHMN");items.SetValue(group,Activator.CreateInstance(items.FieldType));
            var page=new Page{Menu=menu,Group=group,GroupType=type,GroupsField=groupsField};pages.Add(page);
            var title=(Text)AccessTools.Field(type,"txt_title").GetValue(group);title.text="Mods";title.supportRichText=false;
            var button=clone.GetComponent<Button>();button.onClick=new Button.ButtonClickedEvent();
            button.onClick.AddListener(()=>{AccessTools.Method(menuType,"btnSelectGroup").Invoke(menu,new object[]{group});ShowList(page);});
            var expanded=Array.CreateInstance(type,groups.Length+1);Array.Copy(groups,expanded,groups.Length);expanded.SetValue(group,groups.Length);groupsField.SetValue(menu,expanded);
            AccessTools.Method(type,"Select").Invoke(group,new object[]{false});clone.SetActive(true);clone.transform.SetAsLastSibling();
            if(!clone.transform.parent.GetComponent<LayoutGroup>()&&groups.Length>1)
            {
                var last=((Component)groups.GetValue(groups.Length-1)).GetComponent<RectTransform>();
                var previous=((Component)groups.GetValue(groups.Length-2)).GetComponent<RectTransform>();
                clone.GetComponent<RectTransform>().anchoredPosition=last.anchoredPosition+(last.anchoredPosition-previous.anchoredPosition);
            }
            log("Mods category connected to native Settings ("+groups.Length+" original categories preserved).");
        }
        private Component Row(Page page,string factory,string label)
        {
            var row=(Component)AccessTools.Method(page.GroupType,factory).Invoke(page.Group,null);page.Rows.Add(row);
            row.gameObject.name="TS3Modding.ModSetting";
            AccessTools.Method(row.GetType(),"Setup",new[]{typeof(string)}).Invoke(row,new object[]{label});
            foreach(var text in row.GetComponentsInChildren<Text>(true))text.supportRichText=false;
            row.gameObject.SetActive(true);return row;
        }
        private void Info(Page page,string text)
        {
            // The native row factory sizes rows by explicit line count, not wrapped text height.
            var lines=new List<string>();
            foreach(var paragraph in text.Split('\n'))
            {
                var line=new StringBuilder();
                foreach(var word in paragraph.Split(' '))
                {
                    if(line.Length>0&&line.Length+word.Length+1>72){lines.Add(line.ToString());line.Clear();}
                    if(line.Length>0)line.Append(' ');line.Append(word);
                }
                lines.Add(line.ToString());
            }
            Row(page,"AddInfoItem",string.Join("\n",lines));
        }
        private void ButtonRow(Page page,string label,string caption,Action click)
        {
            var row=Row(page,"AddButtonItem",label);var button=(Button)AccessTools.Field(row.GetType(),"btnButton").GetValue(row);
            var text=button.GetComponentInChildren<Text>(true);if(text){text.text=caption;text.resizeTextForBestFit=true;text.resizeTextMinSize=12;text.resizeTextMaxSize=Math.Max(12,text.fontSize);}
            button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{try{click();}catch(Exception e){if(page.Status)page.Status.text=e.Message;log("Mods menu action failed: "+e);}});
        }
        private void Clear(Page page)
        {
            var owned=(IList)AccessTools.Field(page.GroupType,"PDFOGOIGHMN").GetValue(page.Group);
            foreach(var row in page.Rows)if(row)
            {
                foreach(var input in row.GetComponentsInChildren<InputField>(true))input.onEndEdit.RemoveAllListeners();
                owned.Remove(row);row.gameObject.SetActive(false);Destroy(row.gameObject);
            }
            page.Rows.Clear();page.Status=null;
        }
        private List<Mod> Installed()
        {
            var mods=new Dictionary<string,Mod>(StringComparer.Ordinal);
            var statePath=Path.Combine(Paths.GameRootPath,".ts3-mod-manager","state.json");
            if(File.Exists(statePath))
            {
                try
                {
                    var state=JsonUtility.FromJson<StateList>("{\"Items\":"+File.ReadAllText(statePath)+"}");
                    foreach(var item in state.Items??new StateItem[0])if(item?.Package!=null&&Regex.IsMatch(item.Package.Id??"",@"^[A-Za-z0-9._-]+$"))
                        mods[item.Package.Id]=new Mod{Id=item.Package.Id,Name=item.Package.Name,Version=item.Package.Version,Status=item.Enabled?"Enabled":"Disabled — restart required to enable"};
                }
                catch(Exception error){log("Could not read managed mod list; showing BepInEx plugins: "+error.Message);}
            }
            foreach(var pair in Chainloader.PluginInfos)
            {
                Mod mod;if(!mods.TryGetValue(pair.Key,out mod))mods[pair.Key]=mod=new Mod{Id=pair.Key};
                mod.Name=pair.Value.Metadata.Name;mod.Version=pair.Value.Metadata.Version.ToString();
                // Unity destroys bootstrap components; their managed ConfigFile still owns the entries.
                if(!ReferenceEquals(pair.Value.Instance,null))
                {mod.Config=pair.Value.Instance.Config;mod.Status="Enabled";}
                else if(mod.Status==null)mod.Status="Not loaded this session";
            }
            foreach(var mod in mods.Values)mod.Name=CleanName(mod.Name??mod.Id);
            return mods.Values.OrderBy(m=>m.Name??m.Id,StringComparer.OrdinalIgnoreCase).ToList();
        }
        private void ShowList(Page page)
        {
            page.SelectedMod=null;
            Clear(page);HeaderCard(page,"Installed mods","Browse your mods and configure their features.","Refresh",()=>ShowList(page));
            foreach(var mod in Installed())
            {var selected=mod;ModCard(page,mod,"Settings",()=>ShowSettings(page,selected));}
        }
        private static string Label(string key)=>Regex.Replace(key,@"(?<=[a-z0-9])([A-Z])"," $1");
        private string VisibilityKey(Mod mod)=>mod.Config==null?"":string.Join("\n",mod.Config.Keys.Select(k=>k.Section+"/"+k.Key+":"+Visible(mod.Id,k.Section,k.Key)).OrderBy(k=>k,StringComparer.Ordinal).ToArray());
        private void ShowSettings(Page page,Mod mod)
        {
            page.SelectedMod=mod;page.VisibilityKey=VisibilityKey(mod);
            Clear(page);ModCard(page,mod,"Back",()=>ShowList(page));
            var notice=Row(page,"AddInfoItem","Live: applies immediately. Main menu only: change before loading an airport.\nRestart required: use the Mod Manager and restart TS3.");
            page.Status=(Text)AccessTools.Field(notice.GetType(),"txtTitle").GetValue(notice);
            if(mod.Id=="nl.ts3modding.input")
            {
                Info(page,"Key bindings save and apply immediately through the Input editor.");
                ButtonRow(page,"Configure key bindings","Open",()=>
                {
                    var groups=(Array)page.GroupsField.GetValue(page.Menu);
                    AccessTools.Method(menuType,"btnSelectGroup").Invoke(page.Menu,new[]{groups.GetValue(3)});
                });
                return;
            }
            if(mod.Config!=null)
            {
                var entries=mod.Config.Keys.Select(k=>mod.Config[k]).Where(e=>Visible(mod.Id,e.Definition.Section,e.Definition.Key)).ToArray();
                foreach(var group in entries.GroupBy(e=>e.Definition.Section))
                {
                    SectionHeader(page,mod.Id=="nl.ts3modding.rejectedtakeoffs"&&group.Key=="Maintenance"?"BREAKDOWN MAINTENANCE":group.Key=="Reports"?"HOLDING REPORTS":Label(group.Key).ToUpperInvariant());
                    foreach(var entry in group)SettingRow(page,mod,entry);
                }
                if(entries.Length==0)Info(page,"No settings exposed for this mod.");
            }
            else ReadOnlyFile(page,mod);
        }
        private bool Visible(string id,string section,string key)
        {
            if(!registry.IsVisible(id,section,key))return false;
            if(id=="nl.ts3modding.pilotreadiness"&&(key=="BufferStartupSpeech"||key=="ReportHoldingPoints"))return false;
            return Preferences.DeveloperMode.Visible(id,section,key,Preferences.DeveloperMode.Enabled);
        }
        private static string SettingDescription(string text)
        {
            return Regex.Replace(text ?? "", @"\s*(Applies immediately\.|Change from the main menu without restarting\.)", "", RegexOptions.IgnoreCase).Trim();
        }
        private void SettingRow(Page page,Mod mod,ConfigEntryBase entry)
        {
            var mode=registry.ChangeMode(mod.Id,entry.Definition.Section,entry.Definition.Key);
            var editable=registry.CanEdit(mod.Id,entry.Definition.Section,entry.Definition.Key);
            var type=entry.SettingType;var label=Label(entry.Definition.Key);
            var supported=type==typeof(bool)||type==typeof(string)||type==typeof(float)||type==typeof(int)||type==typeof(double)||type.IsEnum;
            if(!editable||!supported)
            {ReadOnlySettingCard(page,entry,label,!supported?"Change through the Mod Manager.":mode==ModSettingChangeMode.MainMenuOnly?"Main menu only — no restart needed.":"Restart required — use the Mod Manager.");return;}
            if(type==typeof(bool)||type.IsEnum)
            {
                var choices=type==typeof(bool)?new[]{"Off","On"}:Enum.GetNames(type);
                var index=type==typeof(bool)?((bool)entry.BoxedValue?1:0):Array.IndexOf(choices,entry.BoxedValue.ToString());
                var row=(Component)AccessTools.Method(page.GroupType,"AddDropdownItem").Invoke(page.Group,null);page.Rows.Add(row);
                AccessTools.Method(row.GetType(),"Setup",new[]{typeof(string),typeof(int),typeof(string[]),typeof(bool)}).Invoke(row,new object[]{label,index,choices,true});
                var dropdown=(Dropdown)AccessTools.Field(row.GetType(),"drpDrop").GetValue(row);dropdown.onValueChanged=new Dropdown.DropdownEvent();
                bool reverting=false;
                dropdown.onValueChanged.AddListener(value=>{if(reverting)return;var previous=entry.BoxedValue;try{Save(mod.Id,entry,type==typeof(bool)?(object)(value==1):Enum.Parse(type,choices[value]));AccessTools.Method(row.GetType(),"Apply").Invoke(row,null);page.Status.text=(mode==ModSettingChangeMode.MainMenuOnly?"Saved — applies before the next airport loads: ":"Saved and applied: ")+label;}catch(Exception e){reverting=true;dropdown.value=type==typeof(bool)?((bool)previous?1:0):Array.IndexOf(choices,previous.ToString());reverting=false;page.Status.text=e.Message;}});
                SettingCard(row,entry,label,mode,dropdown.GetComponent<RectTransform>());
                row.gameObject.SetActive(true);
            }
            else
            {
                var row=(Component)AccessTools.Method(page.GroupType,"AddInputFieldItem").Invoke(page.Group,null);page.Rows.Add(row);
                AccessTools.Method(row.GetType(),"Setup",new[]{typeof(string),typeof(string),typeof(string)}).Invoke(row,new object[]{label,"Value",Convert.ToString(entry.BoxedValue,CultureInfo.InvariantCulture)});
                var input=(InputField)AccessTools.Field(row.GetType(),"ifTextBox").GetValue(row);input.characterLimit=512;input.interactable=true;input.readOnly=false;input.onEndEdit=new InputField.EndEditEvent();
                input.onEndEdit.AddListener(text=>{var previous=entry.BoxedValue;try{var value=type==typeof(string)?(object)text:Convert.ChangeType(text,type,CultureInfo.InvariantCulture);Save(mod.Id,entry,value);input.text=Convert.ToString(entry.BoxedValue,CultureInfo.InvariantCulture);AccessTools.Method(row.GetType(),"Apply").Invoke(row,null);page.Status.text=(mode==ModSettingChangeMode.MainMenuOnly?"Saved — applies before the next airport loads: ":"Saved and applied: ")+label;}catch(Exception e){input.text=Convert.ToString(previous,CultureInfo.InvariantCulture);page.Status.text=e.Message;}});
                SettingCard(row,entry,label,mode,input.GetComponent<RectTransform>());
                FitInputText(input);
                row.gameObject.SetActive(true);
            }
        }
        private void Save(string pluginId,ConfigEntryBase entry,object value)
        {
            if(!registry.CanEdit(pluginId,entry.Definition.Section,entry.Definition.Key))throw new InvalidOperationException("This setting cannot change during an airport session. Return to the main menu or restart as indicated.");
            if(value is float f&&(float.IsNaN(f)||float.IsInfinity(f))||value is double d&&(double.IsNaN(d)||double.IsInfinity(d)))throw new ArgumentException("Enter a finite number.");
            if(entry.Description.AcceptableValues!=null&&!entry.Description.AcceptableValues.IsValid(value))throw new ArgumentException("Value outside the allowed range: "+entry.Description.AcceptableValues.ToDescriptionString());
            var old=entry.BoxedValue;try{entry.BoxedValue=value;entry.ConfigFile.Save();}catch{try{entry.BoxedValue=old;}catch{}throw;}
        }
        private void ReadOnlyFile(Page page,Mod mod)
        {
            var path=Path.Combine(Paths.ConfigPath,mod.Id+".cfg");
            if(!File.Exists(path)){Info(page,"No configuration available in this session. Use the Mod Manager to enable this mod, then restart TS3.");return;}
            Info(page,"Mod not loaded. Current saved settings are read-only; use the Mod Manager and restart TS3.");
            var section="";
            foreach(var line in File.ReadAllLines(path))
            {
                var text=line.Trim();if(text.StartsWith("[")){section=text.Trim('[',']');if((section!="Testing"||Preferences.DeveloperMode.Enabled||mod.Id!="nl.ts3modding.rejectedtakeoffs")&&Visible(mod.Id,section,""))Info(page,text);continue;}
                if(text.Length==0||text.StartsWith("#")||text.StartsWith(";"))continue;
                if(text.Contains("=")&&Visible(mod.Id,section,text.Split('=')[0].Trim()))Info(page,text);
            }
        }
        private void OnDestroy()
        {
            harmony.UnpatchSelf();ModSettingsServices.Detach(registry);
            foreach(var page in pages)
            {
                if(page.Group)Clear(page);
                if(page.Menu&&page.Group)
                {
                    var groups=(Array)page.GroupsField.GetValue(page.Menu);var remaining=groups.Cast<object>().Where(g=>!ReferenceEquals(g,page.Group)).ToArray();
                    var restored=Array.CreateInstance(page.GroupType,remaining.Length);for(int i=0;i<remaining.Length;i++)restored.SetValue(remaining[i],i);page.GroupsField.SetValue(page.Menu,restored);
                }
                if(page.Group)Destroy(page.Group.gameObject);
            }
            pages.Clear();DisposeIcons();if(ReferenceEquals(current,this))current=null;
        }
    }
}
