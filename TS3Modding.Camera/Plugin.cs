using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TS3Modding.API;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UCamera = UnityEngine.Camera;

namespace TS3Modding.CameraMod;

[BepInPlugin("nl.ts3modding.camera", "TS3 Camera Tools", "0.2.4")]
[BepInDependency("nl.ts3modding.loader", "0.8.0")]
[BepInDependency("nl.ts3modding.input", BepInDependency.DependencyFlags.SoftDependency)]
[BepInProcess("Tower! Simulator 3.exe")]
public sealed class Plugin : BaseUnityPlugin
{
    private CameraDriver driver;
    private void Awake()
    {
        try
        {
            if (FrameworkServices.Current?.GameAssemblySha256 != CameraDriver.Hash) throw new NotSupportedException("Unreviewed game assembly.");
            var host = new GameObject("TS3Modding.Camera"); host.hideFlags = HideFlags.HideAndDontSave; DontDestroyOnLoad(host);
            driver = host.AddComponent<CameraDriver>();
            driver.Initialize(Config, s => Logger.LogInfo(s));
        }
        catch (Exception e) { if (driver) Destroy(driver.gameObject); Logger.LogError("Camera Tools disabled: " + e); }
    }
    // TS3 removes bootstrap components after startup, before the driver's first Update.
    // The independent persistent driver owns cleanup; bootstrap removal is not game exit.
    private void OnDestroy() { Logger.LogInfo("Camera bootstrap removed; independent camera runtime retained."); }
}

public sealed class CameraDriver : MonoBehaviour
{
    internal const string Hash = "26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7";
    private Type planeType, deskType, freeType, gameType;
    private FieldInfo deleted, active, callsign, loading, gameInstance, fovTarget, deskPosition, deskRotation, commandWindow, selectedPlane, previousSelectedPlane;
    private ConfigEntry<bool> chaseEnabled, zoomEnabled, mouseControls, invertDragX, invertDragY;
    private ConfigEntry<float> minFov, maxFov, distance, height, angle, pitch, orbitSensitivity, wheelStrength;
    private ConfigFile configFile; private bool orbitDragging, mouseDirty; private Vector3 previousMouse; private float saveMouseAt;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    private Action<string> log;
    private IDisposable panelBinding, stopBinding, followBinding;
    private readonly CameraExitGate exitGate = new CameraExitGate();
    private IInputBindings provider;
    private UCamera camera;
    private Behaviour desk, free;
    private Component target;
    private bool deskWasEnabled, freeWasEnabled, visible, zoomOverride, captured, returning, chasing;
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private float savedFov, savedTargetFov, chosenFov = 45;
    private int scene;
    private GameObject blocker;
    private Rect bounds = new Rect(30, 60, 460, 580);
    private Vector2 scroll;
    private Component[] aircraft = new Component[0];
    private float nextScan;
    private string search = "", status = "Choose an aircraft to begin. Escape restores your view.";
    private Harmony harmony;
    private static CameraDriver instance;
    private readonly Dictionary<Component, object> menuRows = new Dictionary<Component, object>();
    private readonly List<IDisposable> liveSettings = new List<IDisposable>();
    private bool updateLogged;
    public void Initialize(ConfigFile config, Action<string> logger)
    {
        log = logger;
        configFile = config;
        chaseEnabled = config.Bind("Camera", "EnableChase", true, "Allow aircraft chase views.");
        zoomEnabled = config.Bind("Camera", "EnableExtendedZoom", true, "Allow the extended FOV slider. Opt in per view with the panel checkbox.");
        minFov = config.Bind("Zoom", "MinimumFov", 2f, new ConfigDescription("Minimum vertical field of view in degrees.", new AcceptableValueRange<float>(1, 30)));
        maxFov = config.Bind("Zoom", "MaximumFov", 100f, new ConfigDescription("Maximum vertical field of view in degrees.", new AcceptableValueRange<float>(40, 120)));
        distance = config.Bind("Chase", "Distance", 90f, new ConfigDescription("Distance behind the aircraft, in world units.", new AcceptableValueRange<float>(20, 600)));
        height = config.Bind("Chase", "Height", 25f, new ConfigDescription("Height above the aircraft.", new AcceptableValueRange<float>(5, 200)));
        angle = config.Bind("Chase", "OrbitAngle", 0f, new ConfigDescription("Horizontal orbit angle relative to the aircraft.", new AcceptableValueRange<float>(-180, 180)));
        mouseControls = config.Bind("Camera", "EnableMouseControls", true, "Mouse wheel changes follow distance (or FOV in extended-zoom mode). Hold right mouse button to orbit while following. View settings are retained.");
        pitch = config.Bind("Chase", "ElevationAngle", 0f, new ConfigDescription("Additional camera elevation in degrees; retained after mouse orbit.", new AcceptableValueRange<float>(0, 75)));
        orbitSensitivity = config.Bind("Mouse", "OrbitSensitivity", .25f, new ConfigDescription("Orbit degrees per mouse pixel.", new AcceptableValueRange<float>(.05f, 1f)));
        wheelStrength = config.Bind("Mouse", "WheelStrength", .12f, new ConfigDescription("Proportional distance/FOV change per wheel step.", new AcceptableValueRange<float>(.02f, .5f)));
        invertDragX = config.Bind("Mouse", "InvertDragX", false, "Reverse horizontal mouse dragging while following an aircraft. Applies immediately.");
        invertDragY = config.Bind("Mouse", "InvertDragY", false, "Reverse vertical mouse dragging while following an aircraft. Applies immediately.");
        if (ModSettingsServices.Current != null) foreach (var key in config.Keys)
            liveSettings.Add(ModSettingsServices.Current.RegisterLiveSetting("nl.ts3modding.camera", key.Section, key.Key));
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
        planeType = assembly.GetType("Airplane", true); deskType = assembly.GetType("DeskCameraController", true);
        freeType = assembly.GetType("FreeCamController", true); gameType = assembly.GetType("Game", true);
        deleted = Required(planeType, "_deleted"); active = Required(planeType, "_active"); callsign = Required(planeType, "_name");
        loading = Required(gameType, "loading"); gameInstance = Required(gameType, "instance");
        commandWindow = Required(gameType, "cmdwin"); selectedPlane = Required(commandWindow.FieldType, "cap"); previousSelectedPlane = Required(commandWindow.FieldType, "prev_cap");
        fovTarget = Required(deskType, "IOAABOJHJIB"); deskPosition = Required(deskType, "target_pos"); deskRotation = Required(deskType, "target_rot");
        // Skip only camera Update while our panel owns input or chase owns the transform.
        // Other patches (including Input Panel transpilers) stay attached and resume unchanged.
        instance = this; harmony = new Harmony("nl.ts3modding.camera");
        foreach (var t in new[] { deskType, freeType })
            harmony.Patch(AccessTools.Method(t, "Update"), prefix: new HarmonyMethod(typeof(CameraDriver), nameof(CameraInputPrefix)));
        harmony.Patch(AccessTools.Method(assembly.GetType("LegacyWindows", true), "Update"), transpiler: new HarmonyMethod(typeof(CameraDriver), nameof(PauseKeys)));
        blocker = new GameObject("Camera Tools pointer blocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        blocker.transform.SetParent(transform, false);
        var canvas = blocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32699;
        var mask = new GameObject("Pointer mask", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); mask.transform.SetParent(blocker.transform, false);
        var rect = mask.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        mask.GetComponent<Image>().color = Color.clear; blocker.SetActive(false);
        RegisterInput(); InitializeMenu(); log("Camera Tools 0.2.4 ready: Settings > Input > Open Camera Tools, or Ctrl+Shift+C.");
    }
    private void InitializeMenu()
    {
        var menuType = planeType.Assembly.GetType("MenuSettings", true);
        var hook = new HarmonyMethod(typeof(CameraDriver), nameof(MenuBuilt)) { after = new[] { "nl.ts3modding.input" } };
        harmony.Patch(AccessTools.Method(menuType, "OLPJEJPJLEO"), postfix: hook);
        foreach (var menu in Resources.FindObjectsOfTypeAll(menuType)) AttachMenu(menu);
    }
    private static void MenuBuilt(object __instance)
    { try { instance?.AttachMenu(__instance); } catch (Exception e) { instance?.log("Camera menu connection failed: " + e); } }
    private void AttachMenu(object menu)
    {
        var first = AccessTools.Field(menu.GetType(), "DGDLEFEFNNH").GetValue(menu) as Component;
        if (!first || first.transform.parent.Find("TS3Modding.Camera.Open")) return;
        var groups = (Array)AccessTools.Field(menu.GetType(), "groups").GetValue(menu);
        var group = groups.Cast<object>().FirstOrDefault(g => ((IList)AccessTools.Field(g.GetType(), "PDFOGOIGHMN").GetValue(g)).Contains(first));
        if (group == null) return;
        var row = (Component)AccessTools.Method(group.GetType(), "AddButtonItem").Invoke(group, null);
        menuRows[row] = group; row.name = "TS3Modding.Camera.Open"; row.transform.SetAsFirstSibling(); row.gameObject.SetActive(first.gameObject.activeSelf);
        AccessTools.Method(row.GetType(), "Setup", new[] { typeof(string) }).Invoke(row, new object[] { "Open Camera Tools" });
        var button = (Button)AccessTools.Field(row.GetType(), "btnButton").GetValue(row);
        var text = button.GetComponentInChildren<Text>(); if (text) text.text = "Open";
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => { AccessTools.Method(menu.GetType(), "Close").Invoke(menu, null); visible = true; if (blocker) blocker.SetActive(true); log("Camera panel opened from Settings > Input."); });
        log("Camera Tools button added at the top of Settings > Input.");
    }
    private static FieldInfo Required(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
    private bool Ready()
    {
        var game = gameInstance?.GetValue(null) as UnityEngine.Object;
        return game && !(bool)loading.GetValue(game) && FrameworkServices.Current?.ObservationAvailable == true;
    }
    private static bool CameraInputPrefix(Behaviour __instance)
    {
        var self = instance;
        return !self || !self.Ready() || (!self.visible && !self.target) || __instance.GetComponent<UCamera>() != UCamera.main;
    }
    private void RegisterInput()
    {
        if (ReferenceEquals(provider, InputServices.Current)) return;
        panelBinding?.Dispose(); stopBinding?.Dispose(); followBinding?.Dispose(); provider = InputServices.Current;
        if (provider == null) return;
        panelBinding = provider.Register(new InputActionDefinition("camera.panel", "Toggle camera panel", "Camera Tools", new KeyChord("C", KeyModifiers.Control | KeyModifiers.Shift)), Toggle);
        stopBinding = provider.Register(new InputActionDefinition("camera.restore", "Restore normal camera", "Camera Tools", new KeyChord("R", KeyModifiers.Control | KeyModifiers.Shift)), Restore);
        followBinding = provider.Register(new InputActionDefinition("camera.follow_selected", "Toggle follow selected aircraft", "Camera Tools", new KeyChord("F", KeyModifiers.Control | KeyModifiers.Shift)), ToggleSelected);
        log("Camera shortcuts registered; input actions=" + provider.Actions.Count);
    }
    private void Toggle() { visible = !visible; if (blocker) blocker.SetActive(visible); log("Camera shortcut toggled panel: " + visible); }
    private void ToggleSelected()
    {
        if (chasing) { Restore(); visible = false; if (blocker) blocker.SetActive(false); return; }
        if (!Ready() || !chaseEnabled.Value) return;
        var window = commandWindow.GetValue(gameInstance.GetValue(null));
        var selected = window == null ? null : selectedPlane.GetValue(window) as Component;
        if (!Valid(selected) && window != null) selected = previousSelectedPlane.GetValue(window) as Component;
        if (!Valid(selected)) { status = "No active aircraft selected."; log(status); return; }
        Follow(selected);
    }
    private bool HandleEscape() => exitGate.Consume(Time.frameCount, Input.GetKeyDown(KeyCode.Escape), visible || captured, () => { Restore(); visible = false; if (blocker) blocker.SetActive(false); });
    public static bool NativePauseKeyDown(KeyCode key)
    { return !(key == KeyCode.Escape && instance && instance.HandleEscape()) && Input.GetKeyDown(key); }
    private static IEnumerable<CodeInstruction> PauseKeys(IEnumerable<CodeInstruction> source)
    {
        var code = source.ToList(); int changed = 0;
        for (int i = 1; i < code.Count; i++)
            if (code[i].operand is MethodInfo method && method.DeclaringType == typeof(Input) && method.Name == "GetKeyDown" &&
                (code[i - 1].opcode == OpCodes.Ldc_I4_S || code[i - 1].opcode == OpCodes.Ldc_I4) && Convert.ToInt32(code[i - 1].operand) == (int)KeyCode.Escape)
            { code[i].operand = AccessTools.Method(typeof(CameraDriver), nameof(NativePauseKeyDown)); changed++; }
        if (changed != 1) throw new NotSupportedException("Native pause Escape reader changed: " + changed);
        return code;
    }
    private void Update()
    {
        if (!updateLogged) { updateLogged = true; log("Camera runtime update active; input provider=" + (InputServices.Current != null)); }
        RegisterInput();
        if (provider == null && Application.isFocused && Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.C)) Toggle();
        if (provider == null && Application.isFocused && Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.F)) ToggleSelected();
        HandleEscape();
        if (captured && (!Ready() || !camera || UCamera.main != camera || camera.gameObject.scene.handle != scene || (chasing && !Valid(target)) || (!target && !zoomOverride))) Restore();
        if (!chaseEnabled.Value && target) Restore();
        if (!zoomEnabled.Value && zoomOverride) { if (target) zoomOverride = false; else Restore(); }
        if (visible && Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1; aircraft = UnityEngine.Object.FindObjectsOfType(planeType).OfType<Component>().Where(Valid).OrderBy(Name).ToArray(); }
        if (blocker && blocker.activeSelf != visible) blocker.SetActive(visible);
        HandleMouse(); if (mouseDirty && Time.unscaledTime >= saveMouseAt && !orbitDragging) SaveMouseSettings();
    }
    private bool PointerOverControls()
    {
        var mouse = Input.mousePosition;
        if (visible && (bounds.Contains(new Vector2(mouse.x, Screen.height - mouse.y)) || GUIUtility.hotControl != 0)) return true;
        if (!EventSystem.current) return false;
        pointerHits.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = mouse }, pointerHits);
        return pointerHits.Any(hit => hit.gameObject && !hit.gameObject.transform.IsChildOf(transform));
    }
    private void HandleMouse()
    {
        if (!mouseControls.Value || !Application.isFocused || !captured || !Ready() || Time.timeScale == 0)
        { orbitDragging = false; return; }
        var position = Input.mousePosition; var blocked = PointerOverControls();
        if (Input.GetMouseButtonDown(1))
        { orbitDragging = chasing && !blocked; previousMouse = position; }
        if (!Input.GetMouseButton(1)) { orbitDragging = false; }
        if (blocked) { orbitDragging = false; return; }
        var delta = position - previousMouse; previousMouse = position;
        var wheel = Input.mouseScrollDelta.y;
        var nextDistance = distance.Value; var nextAngle = angle.Value; var nextPitch = pitch.Value;
        if (chasing)
        {
            if (wheel != 0) nextDistance = CameraMouseMath.Wheel(nextDistance, wheel, wheelStrength.Value, 20, 600);
            if (orbitDragging) { nextAngle = CameraMouseMath.Orbit(nextAngle, (invertDragX.Value ? -delta.x : delta.x), orbitSensitivity.Value); nextPitch = CameraMouseMath.Pitch(nextPitch, (invertDragY.Value ? -delta.y : delta.y), orbitSensitivity.Value); }
        }
        else if (zoomOverride && wheel != 0) chosenFov = CameraMouseMath.Wheel(chosenFov, wheel, wheelStrength.Value, minFov.Value, maxFov.Value);
        if (nextDistance == distance.Value && nextAngle == angle.Value && nextPitch == pitch.Value) return;
        // Keep the settings themselves authoritative: LateUpdate cannot snap back to a stale orbit.
        // Avoid disk writes for each mouse sample; save once after the gesture settles.
        var autosave = configFile.SaveOnConfigSet; configFile.SaveOnConfigSet = false;
        try { distance.Value = nextDistance; angle.Value = nextAngle; pitch.Value = nextPitch; }
        finally { configFile.SaveOnConfigSet = autosave; }
        mouseDirty = true; saveMouseAt = Time.unscaledTime + .5f;
    }
    private void SaveMouseSettings()
    {
        if (!mouseDirty) return; mouseDirty = false;
        try { configFile.Save(); } catch (Exception error) { log?.Invoke("Camera mouse settings remain active for this session, but could not be saved: " + error.Message); }
    }
    private bool Valid(Component p) => p && p.gameObject.activeInHierarchy && !(bool)deleted.GetValue(p) && (bool)active.GetValue(p);
    private string Name(Component p) => p ? (callsign.GetValue(p) as string ?? p.name) : "Aircraft gone";
    private bool Capture()
    {
        if (captured) return true;
        if (!Ready()) { status = "Load an airport session first."; return false; }
        camera = UCamera.main; if (!camera) return false;
        desk = camera.GetComponent(deskType) as Behaviour; free = camera.GetComponent(freeType) as Behaviour;
        if (!desk || !free) { status = "Main camera controllers unavailable."; return false; }
        savedPosition = camera.transform.position; savedRotation = camera.transform.rotation; savedFov = camera.fieldOfView;
        savedTargetFov = (float)fovTarget.GetValue(desk); deskWasEnabled = desk.enabled; freeWasEnabled = free.enabled;
        scene = camera.gameObject.scene.handle; chosenFov = camera.fieldOfView; captured = true; return true;
    }
    private void Follow(Component plane)
    {
        if (!chaseEnabled.Value || !Valid(plane) || !Capture()) return;
        target = plane; chasing = true; desk.enabled = false; free.enabled = false;
        status = "Following " + Name(plane) + ". Escape returns to your previous view."; log(status);
    }
    private void LateUpdate()
    {
        if (!captured || !camera || !Ready()) return;
        if (target && Valid(target))
        {
            var rotation = Quaternion.Euler(0, target.transform.eulerAngles.y + angle.Value, 0);
            var point = target.transform.position;
            camera.transform.position = point + rotation * new Vector3(0, height.Value + distance.Value * Mathf.Sin(pitch.Value * Mathf.Deg2Rad), -distance.Value * Mathf.Cos(pitch.Value * Mathf.Deg2Rad));
            camera.transform.rotation = Quaternion.LookRotation(point - camera.transform.position, Vector3.up);
        }
        if (zoomOverride) { chosenFov = Mathf.Clamp(chosenFov, minFov.Value, maxFov.Value); camera.fieldOfView = chosenFov; fovTarget.SetValue(desk, chosenFov); }
    }
    private void Restore()
    {
        if (returning) return; returning = true;
        try
        {
            SaveMouseSettings(); orbitDragging = false; target = null; chasing = false; zoomOverride = false;
            if (captured)
            {
                if (camera) { camera.transform.position = savedPosition; camera.transform.rotation = savedRotation; camera.fieldOfView = savedFov; }
                if (desk) { fovTarget.SetValue(desk, savedTargetFov); desk.enabled = deskWasEnabled; }
                if (free) free.enabled = freeWasEnabled;
                log?.Invoke("Camera Tools restored the previous view.");
            }
            captured = false; status = "Normal camera restored.";
        }
        finally { returning = false; }
    }
    private void OnDisable() { Restore(); visible = false; if (blocker) blocker.SetActive(false); }
    private void OnDestroy()
    {
        Restore(); panelBinding?.Dispose(); stopBinding?.Dispose(); followBinding?.Dispose(); harmony?.UnpatchSelf();
        foreach (var setting in liveSettings) setting.Dispose(); liveSettings.Clear();
        foreach (var pair in menuRows) if (pair.Key)
        {
            if (pair.Value is Component owner && owner) ((IList)AccessTools.Field(pair.Value.GetType(), "PDFOGOIGHMN").GetValue(pair.Value)).Remove(pair.Key);
            Destroy(pair.Key.gameObject);
        }
        menuRows.Clear(); theme?.Dispose();
        if (instance == this) instance = null; if (blocker) Destroy(blocker);
    }
    private TS3Modding.UI.OverlayTheme theme;
    private void OnGUI()
    {
        if (!visible) return;
        var oldSkin = GUI.skin; var oldColor = GUI.color; var oldBackground = GUI.backgroundColor; var oldContent = GUI.contentColor; var oldEnabled = GUI.enabled;
        try
        {
            if (theme == null) theme = new TS3Modding.UI.OverlayTheme(oldSkin);
            GUI.skin = theme.Skin; GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            bounds.width = Mathf.Min(510, Screen.width - 24); bounds.height = Mathf.Min(780, Screen.height - 24);
            bounds.x = Mathf.Clamp(bounds.x, 12, Mathf.Max(12, Screen.width - bounds.width - 12)); bounds.y = Mathf.Clamp(bounds.y, 12, Mathf.Max(12, Screen.height - bounds.height - 12));
            bounds = GUILayout.Window(GetInstanceID(), bounds, Draw, GUIContent.none, theme.Window);
        }
        finally { GUI.skin = oldSkin; GUI.color = oldColor; GUI.backgroundColor = oldBackground; GUI.contentColor = oldContent; GUI.enabled = oldEnabled; }
    }
    private void Draw(int id)
    {
        theme.Heading("Camera tools", "AIRCRAFT FOLLOW  /  VIEW CONTROLS", () => Toggle());
        theme.BeginCard();
        GUILayout.BeginHorizontal(); GUILayout.Label(chasing && target ? Name(target) : "Normal view", theme.Title); GUILayout.FlexibleSpace(); theme.Badge(chasing ? "FOLLOWING" : "STANDBY", chasing ? "good" : "muted"); GUILayout.EndHorizontal();
        GUILayout.Label(status, theme.Muted); GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        var enabled = GUI.enabled; GUI.enabled = enabled && chaseEnabled.Value;
        if (GUILayout.Button(chasing ? "Stop following" : "Follow selected", theme.Primary)) ToggleSelected();
        GUI.enabled = enabled && captured; if (GUILayout.Button("Restore view", theme.Secondary)) Restore(); GUI.enabled = enabled;
        GUILayout.EndHorizontal(); theme.EndCard();
        theme.BeginCard("CHASE POSITION");
        distance.Value = theme.Slider("Distance", distance.Value, 20, 600);
        height.Value = theme.Slider("Height", height.Value, 5, 200);
        angle.Value = theme.Slider("Orbit", angle.Value, -180, 180, "°");
        pitch.Value = theme.Slider("Elevation", pitch.Value, 0, 75, "°");
        GUILayout.Label(mouseControls.Value ? "Wheel: distance  ·  Hold right mouse: orbit  ·  View settings remembered" : "Mouse controls disabled in mod settings.", theme.Muted); theme.EndCard();
        if (zoomEnabled.Value)
        {
            theme.BeginCard("ZOOM"); var wantZoom = theme.Switch("Extended zoom", zoomOverride);
            if (wantZoom != zoomOverride)
            {
                if (wantZoom) { if (Capture()) zoomOverride = true; }
                else if (target) { zoomOverride = false; camera.fieldOfView = savedFov; fovTarget.SetValue(desk, savedTargetFov); }
                else Restore();
            }
            if (zoomOverride) chosenFov = theme.Slider("Field of view", chosenFov, minFov.Value, maxFov.Value, "°");
            GUILayout.Label(zoomOverride ? "Lower values zoom in. Wheel changes FOV when not following." : "Enable to use the extended field-of-view slider.", theme.Muted); theme.EndCard();
        }
        GUILayout.Space(6); GUILayout.Label("AIRCRAFT", theme.Section);
        if (!chaseEnabled.Value) GUILayout.Label("Aircraft following is disabled in mod settings.", theme.Muted);
        search = GUILayout.TextField(search);
        var matches = aircraft.Where(p => Valid(p) && Name(p).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        GUILayout.Label(matches.Length + " aircraft  ·  Search by callsign", theme.Muted);
        scroll = GUILayout.BeginScrollView(scroll);
        if (chaseEnabled.Value) foreach (var plane in matches)
        {
            var selected = ReferenceEquals(plane, target);
            if (GUILayout.Button(Name(plane) + (selected ? "   ·   Following" : "   ·   Follow"), selected ? theme.Selected : theme.Row)) Follow(plane);
        }
        if (matches.Length == 0) GUILayout.Label(aircraft.Length == 0 ? "No active aircraft available. Load an airport session to begin." : "No aircraft match your search.", theme.Muted);
        GUILayout.EndScrollView(); GUILayout.Space(6);
        var actions = InputServices.Current?.Actions;
        var panelKey = actions?.FirstOrDefault(a => a.Definition.Id == "camera.panel")?.Binding.ToString() ?? "Ctrl+Shift+C";
        var followKey = actions?.FirstOrDefault(a => a.Definition.Id == "camera.follow_selected")?.Binding.ToString() ?? "Ctrl+Shift+F";
        GUILayout.Label(panelKey + "  Panel   ·   " + followKey + "  Follow selected", theme.Muted);
        GUILayout.Label("Escape restores your view. Hiding the panel keeps following.", theme.Muted);
        GUI.DragWindow(new Rect(0, 0, bounds.width - 90, 65));
    }
}


