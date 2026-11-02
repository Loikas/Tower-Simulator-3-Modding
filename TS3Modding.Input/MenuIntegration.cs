using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TS3Modding.InputMod
{
    public sealed partial class InputDriver
    {
        private readonly Dictionary<Selectable,bool> disabledControls=new Dictionary<Selectable,bool>();
        private readonly Dictionary<GameObject,bool> hiddenStockRows=new Dictionary<GameObject,bool>();
        private readonly List<GameObject> menuItems=new List<GameObject>();
        private readonly Dictionary<Component,object> menuOwners=new Dictionary<Component,object>();
        private readonly Dictionary<string,Button> nativeBindingButtons=new Dictionary<string,Button>();
        private const BindingFlags MenuFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private Text nativeStatus;
        private object nativeGroup;
        private Transform nativeParent;
        private string nativeActionSignature="";
        private bool initialRowsVisible;
        private bool NativeMenuOpen => nativeStatus && nativeStatus.gameObject.activeInHierarchy;
        private void InitializeMenu()
        {
            var menu=assembly.GetType("MenuSettings",true);
            var build=menu.GetMethod("OLPJEJPJLEO",MenuFlags,null,new[]{typeof(int)},null);
            if(build==null)throw new NotSupportedException("Input settings menu changed.");
            harmony.Patch(build,postfix:new HarmonyMethod(typeof(InputDriver),nameof(MenuBuilt)));
            foreach(var obj in Resources.FindObjectsOfTypeAll(menu))
                try{AttachMenu(obj,null);}catch(Exception error){log("Existing input menu not yet ready: "+error.Message);}
        }
        private static void MenuBuilt(object __instance,int __0)
        {try{current?.AttachMenu(__instance,__0);}catch(Exception error){current?.log("Input menu integration failed: "+error);}}
        private Component AddNativeItem(object group,string factory,string label)
        {
            var row=(Component)group.GetType().GetMethod(factory,MenuFlags).Invoke(group,null);
            row.gameObject.name="TS3Modding.Input."+label; menuItems.Add(row.gameObject);menuOwners[row]=group;
            row.GetType().GetMethod("Setup",MenuFlags,null,new[]{typeof(string)},null).Invoke(row,new object[]{label});
            row.gameObject.SetActive(true);return row;
        }
        private void AttachMenu(object menu,int? groupIndex)
        {
            Component first=null;
            foreach(var name in new[]{"DGDLEFEFNNH","KFMODPPJBMJ"})
            {
                var item=menu.GetType().GetField(name,MenuFlags).GetValue(menu) as Component;if(!item)continue;if(!first)first=item;
                if(!hiddenStockRows.ContainsKey(item.gameObject))hiddenStockRows[item.gameObject]=item.gameObject.activeSelf;
                var dropdown=item.GetType().GetField("drpDrop",MenuFlags).GetValue(item) as Selectable;
                if(dropdown&&!disabledControls.ContainsKey(dropdown)){disabledControls.Add(dropdown,dropdown.interactable);dropdown.interactable=false;}
            }
            if(!first)return;
            var parent=first.transform.parent;
            if(parent.Find("TS3Modding.Input.Key bindings"))return;
            object group=first.GetComponentInParent(assembly.GetType("MenuSettingsGroupItem",true));
            if(group==null&&groupIndex.HasValue)group=((Array)menu.GetType().GetField("groups",MenuFlags).GetValue(menu)).GetValue(groupIndex.Value);
            if(group==null)return;
            nativeGroup=group;nativeParent=parent;initialRowsVisible=first.gameObject.activeSelf;BuildNativeBindings();
            log("Full binding editor added to Settings > Input using native menu rows. Stock voice/look selectors disabled.");
        }
        private void BuildNativeBindings()
        {
            var show=nativeStatus?nativeStatus.gameObject.activeSelf:initialRowsVisible;
            RemoveOwnedRows();nativeBindingButtons.Clear();
            var heading=AddNativeItem(nativeGroup,"AddInfoItem","Key bindings");
            var title=heading.GetType().GetField("txtTitle",MenuFlags).GetValue(heading) as Text;
            nativeStatus=title;
            if(title){title.text="KEY BINDINGS • Changes save immediately";title.resizeTextForBestFit=true;title.resizeTextMinSize=12;}
            foreach(var group in bindings.Actions.GroupBy(a=>a.Definition.Group))
            {
                var header=AddNativeItem(nativeGroup,"AddInfoItem",group.Key);
                var label=header.GetType().GetField("txtTitle",MenuFlags).GetValue(header) as Text;
                if(label){label.text=group.Key.ToUpperInvariant();label.color=new Color(.4f,.8f,.95f);label.fontStyle=FontStyle.Bold;}
                foreach(var action in group)
                {
                    var id=action.Definition.Id;var row=AddNativeItem(nativeGroup,"AddButtonItem",action.Definition.Label);
                    var bind=(Button)row.GetType().GetField("btnButton",MenuFlags).GetValue(row);
                    bind.onClick=new Button.ButtonClickedEvent();
                    bind.onClick.AddListener(()=>{visible=false;status="";listening=id;listenFrame=Time.frameCount;consumedFrame=Time.frameCount;RefreshNativeLabels();});
                    nativeBindingButtons[id]=bind;
                    var reset=Instantiate(bind,bind.transform.parent);reset.name="Default";reset.onClick=new Button.ButtonClickedEvent();
                    reset.onClick.AddListener(()=>{TryBind(id,null);RefreshNativeLabels();});
                    var clear=Instantiate(bind,bind.transform.parent);clear.name="Unbind";clear.onClick=new Button.ButtonClickedEvent();
                    clear.onClick.AddListener(()=>{TryBind(id,new TS3Modding.API.KeyChord("None"));RefreshNativeLabels();});
                    PositionButton(bind,-155,155);PositionButton(reset,-75,75);PositionButton(clear,0,70);
                    Caption(reset,"Default");Caption(clear,"Unbind");
                    var rowTitle=row.GetType().GetField("txtTitle",MenuFlags).GetValue(row) as Text;
                    if(rowTitle){var rt=rowTitle.rectTransform;rt.anchorMax=new Vector2(1,rt.anchorMax.y);var offset=rt.offsetMax;offset.x=-320;rt.offsetMax=offset;}
                }
            }
            foreach(var row in menuItems)if(row)row.SetActive(show);
            nativeActionSignature=string.Join("|",bindings.Actions.Select(a=>a.Definition.Id));RefreshNativeLabels();
        }
        private static void PositionButton(Button button,float right,float width)
        {
            var rect=button.GetComponent<RectTransform>();rect.anchorMin=new Vector2(1,.5f);rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);
            rect.anchoredPosition=new Vector2(right,0);rect.sizeDelta=new Vector2(width,Mathf.Max(30,rect.sizeDelta.y));
        }
        private static void Caption(Button button,string caption)
        {
            var text=button.GetComponentInChildren<Text>(true);if(!text)return;
            // The stock prefab uses a fixed caption rectangle. Resizing its button alone leaves
            // three full-size captions on top of each other, even though the buttons don't overlap.
            if(text.transform!=button.transform)
            {
                var rect=text.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
                rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=new Vector2(4,2);rect.offsetMax=new Vector2(-4,-2);
            }
            text.text=caption;text.alignment=TextAnchor.MiddleCenter;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            text.resizeTextMaxSize=Mathf.Max(12,Mathf.Min(22,text.fontSize));
            text.resizeTextForBestFit=true;text.resizeTextMinSize=10;
        }
        private void RefreshNativeLabels()
        {
            if(nativeStatus)nativeStatus.text=listening!=null?"PRESS A KEY • Escape cancels"+(string.IsNullOrEmpty(status)?"":" • "+status):string.IsNullOrEmpty(status)?"KEY BINDINGS • Changes save immediately":status;
            foreach(var action in bindings.Actions)
            {Button button;if(nativeBindingButtons.TryGetValue(action.Definition.Id,out button)&&button)Caption(button,listening==action.Definition.Id?"Press a key…":action.Binding.ToString());}
        }
        private void RefreshNativeMenu()
        {
            foreach(var row in hiddenStockRows.Keys)if(row&&row.activeSelf)row.SetActive(false);
            if(!nativeParent||!(nativeGroup as Component))return;
            if(string.Join("|",bindings.Actions.Select(a=>a.Definition.Id))!=nativeActionSignature)BuildNativeBindings();
            RefreshNativeLabels();
            if(listening!=null&&!visible&&!NativeMenuOpen)listening=null;
        }
        private void RemoveOwnedRows()
        {
            foreach(var pair in menuOwners)if(pair.Key&&(pair.Value as Component))
                ((IList)pair.Value.GetType().GetField("PDFOGOIGHMN",MenuFlags).GetValue(pair.Value)).Remove(pair.Key);
            menuOwners.Clear();foreach(var obj in menuItems)if(obj){obj.SetActive(false);Destroy(obj);}menuItems.Clear();
        }
        private void RestoreMenu()
        {
            foreach(var pair in disabledControls)if(pair.Key)pair.Key.interactable=pair.Value;disabledControls.Clear();RemoveOwnedRows();nativeBindingButtons.Clear();
            foreach(var pair in hiddenStockRows)if(pair.Key)pair.Key.SetActive(pair.Value);hiddenStockRows.Clear();
        }
    }
}
