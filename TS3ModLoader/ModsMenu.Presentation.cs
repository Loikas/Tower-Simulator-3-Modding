using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace TS3Modding;

public sealed partial class ModsMenu
{
    [Serializable] private sealed class Presentation { public string Name = null, Description = null, Icon = null; }
    private readonly Dictionary<string, Sprite> icons = [];
    private readonly Dictionary<string, Presentation> presentations = [];
    private static string CleanName(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", @"^TS3\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private Presentation Describe(Mod mod)
    {
        if (presentations.TryGetValue(mod.Id, out Presentation info)) return info;
        var descriptions = new Dictionary<string, string>{
            {"loader","Shared mod services and settings for your installed mods."},
            {"strips","Organises flight strips by runway and flight progress."},
            {"tugs","Tow aircraft between stands, manage requests and clear runway crossings."},
            {"pilotreadiness","Pilot holding reports and dashed ADIRS routes beyond holds."},
            {"camera","Follow aircraft with mouse orbit, extended zoom and camera shortcuts."},
            {"input","Customise game and mod key bindings, including modifier combinations."},
            {"effects","Aircraft smoke, wet-weather spray and airport ambient sounds."},
            {"externaladirs","Keeps added ADIRS windows in sync with mod route displays."}};
        var key = mod.Id.StartsWith("nl.ts3modding.") ? mod.Id.Substring(14) : "";
        descriptions.TryGetValue(key, out string description);
        info = new Presentation { Name = CleanName(mod.Name), Description = description ?? "Settings and options for this mod." };
        try
        {
            var dir = ModDirectory(mod); var path = Path.Combine(dir, "mod-info.json");
            if (File.Exists(path) && new FileInfo(path).Length <= 16384)
            {
                var custom = JsonUtility.FromJson<Presentation>(File.ReadAllText(path));
                if (custom != null) { if (!string.IsNullOrWhiteSpace(custom.Name)) info.Name = CleanName(custom.Name); if (!string.IsNullOrWhiteSpace(custom.Description)) info.Description = custom.Description; info.Icon = custom.Icon; }
            }
        }
        catch (Exception e) { log("Mod presentation fallback for " + mod.Id + ": " + e.Message); }
        if (mod.Id == "nl.ts3modding.loader" && info.Name == "Mod Loader") info.Name = "Mod Framework";
        info.Name = info.Name.Length > 80 ? info.Name.Substring(0, 80) : info.Name;
        info.Description = info.Description.Length > 180 ? info.Description.Substring(0, 177) + "…" : info.Description;
        presentations[mod.Id] = info; return info;
    }
    private string ModDirectory(Mod mod)
    {
        BepInEx.PluginInfo plugin;
        if (Chainloader.PluginInfos.TryGetValue(mod.Id, out plugin) && !string.IsNullOrEmpty(plugin.Location)) return Path.GetDirectoryName(plugin.Location);
        return Path.Combine(Paths.PluginPath, mod.Id == "nl.ts3modding.loader" ? "TS3ModLoader" : mod.Id);
    }
    private Sprite Icon(Mod mod, Presentation info)
    {
        Sprite icon; if (icons.TryGetValue(mod.Id, out icon)) return icon;
        byte[] bytes = null;
        try
        {
            var dir = Path.GetFullPath(ModDirectory(mod)); var path = Path.GetFullPath(Path.Combine(dir, info.Icon ?? "icon.png"));
            if (path.StartsWith(dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path) && new FileInfo(path).Length <= 262144)
            {
                var candidate = File.ReadAllBytes(path);
                // Bound PNG dimensions before asking Unity to decode third-party content.
                if (candidate.Length > 24 && candidate[0] == 137 && candidate[1] == 80 && candidate[2] == 78 && candidate[3] == 71)
                { var w = ((long)candidate[16] << 24) | ((long)candidate[17] << 16) | ((long)candidate[18] << 8) | candidate[19]; var h = ((long)candidate[20] << 24) | ((long)candidate[21] << 16) | ((long)candidate[22] << 8) | candidate[23]; if (w > 0 && h > 0 && w <= 1024 && h <= 1024) bytes = candidate; }
            }
        }
        catch (Exception e) { log("Optional mod icon ignored: " + e.Message); }
        if (bytes == null)
        {
            var assembly = typeof(ModsMenu).Assembly; var key = mod.Id.StartsWith("nl.ts3modding.") ? mod.Id.Substring(14) : "generic";
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".ModIcons." + key + ".png", StringComparison.Ordinal)) ?? assembly.GetManifestResourceNames().First(n => n.EndsWith(".ModIcons.generic.png", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resource); using var buffer = new MemoryStream();
            stream.CopyTo(buffer); bytes = buffer.ToArray();
        }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes)) { Destroy(texture); icons[mod.Id] = null; return null; }
        icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f)); icons[mod.Id] = icon; return icon;
    }
    private static RectTransform Element(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; return rect;
    }
    private static Text CardText(Transform parent, Font font, string value, int size, Color color, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rect = Element(parent, "Text", min, max, offsetMin, offsetMax); var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.resizeTextForBestFit = true; text.resizeTextMinSize = Math.Min(size, 16); text.resizeTextMaxSize = size; text.color = color; text.supportRichText = false; text.raycastTarget = false; text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
    }
    private Component CardBase(Page page, int height, out Font font, out RectTransform body)
    {
        var row = Row(page, "AddInfoItem", "\n\n"); var original = (Text)AccessTools.Field(row.GetType(), "txtTitle").GetValue(row); font = original.font;
        foreach (var graphic in row.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
        var rect = row.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        var layout = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>(); layout.minHeight = height; layout.preferredHeight = height;
        body = Element(row.transform, "Mod card", Vector2.zero, Vector2.one, new Vector2(0, 7), new Vector2(0, -7));
        var image = body.gameObject.AddComponent<Image>(); image.color = new Color(.055f, .085f, .115f, .95f); image.raycastTarget = false;
        var accent = Element(body, "Accent", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(4, 0)); accent.gameObject.AddComponent<Image>().color = new Color(.28f, .76f, .85f, 1);
        return row;
    }
    private void CardButton(Page page, Transform body, Font font, string caption, Action click)
    {
        var rect = Element(body, "Action", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-218, -32), new Vector2(-24, 32));
        var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.12f, .25f, .32f, 1);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(.65f, .9f, 1); colors.pressedColor = new Color(.4f, .7f, .8f); button.colors = colors;
        var label = CardText(rect, font, caption, 30, new Color(.55f, .88f, 1), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); label.alignment = TextAnchor.MiddleCenter;
        button.onClick.AddListener(() => { try { click(); } catch (Exception e) { if (page.Status) page.Status.text = e.Message; log("Mods card action failed: " + e); } });
    }
    private void HeaderCard(Page page, string title, string subtitle, string caption, Action click)
    {
        CardBase(page, 160, out Font font, out RectTransform body);
        body.GetComponent<Image>().color = new Color(.10f, .21f, .28f, .98f);
        CardText(body, font, title, 42, Color.white, Vector2.zero, Vector2.one, new Vector2(28, 70), new Vector2(-240, -16));
        CardText(body, font, subtitle, 27, new Color(.77f, .86f, .9f), Vector2.zero, Vector2.one, new Vector2(28, 16), new Vector2(-240, -78));
        CardButton(page, body, font, caption, click);
    }
    private void ModCard(Page page, Mod mod, string caption, Action click)
    {
        var info = Describe(mod); CardBase(page, 216, out Font font, out RectTransform body);
        var rect = Element(body, "Icon", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(22, -50), new Vector2(122, 50)); var image = rect.gameObject.AddComponent<Image>(); image.sprite = Icon(mod, info); image.preserveAspect = true; image.raycastTarget = false;
        CardText(body, font, info.Name, 36, Color.white, Vector2.zero, Vector2.one, new Vector2(144, 136), new Vector2(-240, -16));
        CardText(body, font, "v" + mod.Version + "  ·  " + mod.Status, 23, new Color(.6f, .73f, .8f), Vector2.zero, Vector2.one, new Vector2(144, 99), new Vector2(-240, -68));
        CardText(body, font, info.Description, 28, new Color(.78f, .83f, .87f), Vector2.zero, Vector2.one, new Vector2(144, 16), new Vector2(-240, -111));
        CardButton(page, body, font, caption, click);
    }
    private void SectionHeader(Page page, string title)
    {
        Font font; RectTransform body; CardBase(page, 92, out font, out body);
        body.GetComponent<Image>().color = new Color(.10f, .21f, .28f, .98f);
        CardText(body, font, title, 30, new Color(.63f, .91f, .97f), Vector2.zero, Vector2.one, new Vector2(26, 10), new Vector2(-22, -10));
    }
    private void SettingCard(Component row, BepInEx.Configuration.ConfigEntryBase entry, string title, TS3Modding.API.ModSettingChangeMode mode, RectTransform control)
    {
        var original = (Text)AccessTools.Field(row.GetType(), "txtTitle").GetValue(row); var font = original.font; original.enabled = false;
        var height = 220f; var rect = row.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        var layout = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>(); layout.minHeight = height; layout.preferredHeight = height;
        var body = Element(row.transform, "Setting card", Vector2.zero, Vector2.one, new Vector2(0, 8), new Vector2(0, -8)); body.SetAsFirstSibling();
        var background = body.gameObject.AddComponent<Image>(); background.color = new Color(.055f, .085f, .115f, .92f); background.raycastTarget = false;
        CardText(body, font, title, 34, Color.white, Vector2.zero, Vector2.one, new Vector2(26, 123), new Vector2(-370, -18));
        CardText(body, font, mode == TS3Modding.API.ModSettingChangeMode.MainMenuOnly ? "Main menu only — no restart needed" : "Applies immediately", 22, new Color(.55f, .8f, .88f), Vector2.zero, Vector2.one, new Vector2(26, 95), new Vector2(-370, -79));
        var description = CardText(body, font, SettingDescription(entry.Description.Description), 28, new Color(.79f, .84f, .88f), Vector2.zero, Vector2.one, new Vector2(26, 18), new Vector2(-26, -115)); description.alignment = TextAnchor.UpperLeft;
        control.SetParent(body, false); control.anchorMin = control.anchorMax = Vector2.one; control.pivot = new Vector2(1, 1); control.offsetMin = new Vector2(-344, -86); control.offsetMax = new Vector2(-24, -22);
    }
    private static void FitInputText(InputField input)
    {
        var root = input.GetComponent<RectTransform>();
        foreach (var text in new[] { input.textComponent, input.placeholder as Text })
        {
            if (!text) continue;
            // Native input text/viewport offsets were designed for the original wide control.
            // Re-anchor the complete viewport chain after fitting the control into a card.
            var parent = text.rectTransform.parent as RectTransform;
            while (parent && parent != root)
            {
                parent.anchorMin = Vector2.zero; parent.anchorMax = Vector2.one; parent.offsetMin = parent.offsetMax = Vector2.zero;
                parent = parent.parent as RectTransform;
            }
            var rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16, 4); rect.offsetMax = new Vector2(-16, -4);
            text.enabled = true; text.fontSize = 32; text.resizeTextForBestFit = true; text.resizeTextMinSize = 20; text.resizeTextMaxSize = 32;
            text.alignment = TextAnchor.MiddleLeft; text.color = text == input.textComponent ? new Color(.95f, .97f, 1, 1) : new Color(.6f, .7f, .78f, 1);
            text.raycastTarget = false;
        }
        input.ForceLabelUpdate();
    }
    private void ReadOnlySettingCard(Page page, BepInEx.Configuration.ConfigEntryBase entry, string title, string note)
    {
        CardBase(page, 220, out Font font, out RectTransform body);
        CardText(body, font, title + ": " + entry.GetSerializedValue(), 34, Color.white, Vector2.zero, Vector2.one, new Vector2(26, 125), new Vector2(-26, -18));
        CardText(body, font, note, 22, new Color(.6f, .77f, .84f), Vector2.zero, Vector2.one, new Vector2(26, 94), new Vector2(-26, -78));
        var description = CardText(body, font, SettingDescription(entry.Description.Description), 28, new Color(.79f, .84f, .88f), Vector2.zero, Vector2.one, new Vector2(26, 18), new Vector2(-26, -115)); description.alignment = TextAnchor.UpperLeft;
    }
    private void DisposeIcons() { foreach (var icon in icons.Values) if (icon) { Destroy(icon.texture); Destroy(icon); } icons.Clear(); presentations.Clear(); }
}

