using System;
using System.Linq;
using TS3Modding.API;
using TS3Modding.UI;
using UnityEngine;

namespace TS3Modding.Tugs
{
    public sealed class TugPanel:MonoBehaviour
    {
        internal NativeTugModule Module;
        private bool visible;
        private Rect bounds=new Rect(30,100,760,620);
        private Vector2 scroll;
        private int windowId;
        private IDisposable shortcut;
        private OverlayTheme theme;
        private TowPlanningSession planning;private int planningTug;private string previewText;private Vector2 destinationScroll;
        private bool planningView,requestPlanning,requestBack;
        private int requestedTug;
        private TowDestination selectedDestination,crossingDestination;private bool requestCrossingPreview;private bool requestCheck,requestTransfer;
        private bool requestDormant,acceptDormant;private string viaInput="",holdInput="";
        private Component selectedDormant;private Action queuedTowAction;
        private void Awake()
        {
            windowId=GetInstanceID();
            if(InputServices.Current!=null)shortcut=InputServices.Current.Register(new InputActionDefinition("tugs.panel","Toggle tug panel","Tug Operations",new KeyChord("F9")),()=>visible=!visible);
        }
        private void Update()
        {
            var action=queuedTowAction;queuedTowAction=null;action?.Invoke();
            if(requestDormant){requestDormant=false;Module.RequestDormantTow();}
            if(acceptDormant){acceptDormant=false;Module.AcceptDormantTow(selectedDormant);}
            if(InputServices.Current==null&&shortcut!=null){shortcut.Dispose();shortcut=null;}
            if(shortcut==null&&Input.GetKeyDown(KeyCode.F9))visible=!visible;
            // Process view changes outside IMGUI so Layout and input events keep the same control tree.
            if(requestBack){requestBack=false;planningView=false;planning=null;previewText=null;scroll=Vector2.zero;}
            if(requestPlanning){requestPlanning=false;OpenPlanning(requestedTug);}
            if(requestCrossingPreview){requestCrossingPreview=false;try{previewText=Module.PreviewCrossings(planningTug,planning,crossingDestination);}catch(Exception error){previewText="Crossing preview unavailable: "+error.Message;}}
            if(requestCheck){requestCheck=false;try{Module.CheckTransfer(planningTug,planning,selectedDestination);}catch(Exception error){previewText=error.Message;}}
            if(requestTransfer){requestTransfer=false;try{var instruction=TowVoiceIntent.Parse("tow to stand "+selectedDestination.Alias+(string.IsNullOrWhiteSpace(viaInput)?"":" via taxiway "+viaInput)+(string.IsNullOrWhiteSpace(holdInput)?"":" hold short of taxiway "+holdInput));
                if(instruction==null)throw new InvalidOperationException("Enter valid taxiway identifiers, for example A B C or Alfa Bravo Charlie.");
                Module.RequestTransfer(planningTug,planning,selectedDestination,instruction.Via,instruction.HoldShort);}catch(Exception error){previewText=error.Message;}}
        }
        private void OnDestroy(){shortcut?.Dispose();theme?.Dispose();}
        private void OnGUI()
        {
            if(!visible||Module==null)return;
            var oldSkin=GUI.skin;var oldColor=GUI.color;var oldBackground=GUI.backgroundColor;var oldContent=GUI.contentColor;
            try
            {
                if(theme==null)theme=new OverlayTheme(oldSkin);GUI.skin=theme.Skin;GUI.color=GUI.backgroundColor=GUI.contentColor=Color.white;
                bounds.width=Mathf.Min(780,Screen.width-24);bounds.height=Mathf.Min(650,Screen.height-24);
                bounds.x=Mathf.Clamp(bounds.x,12,Mathf.Max(12,Screen.width-bounds.width-12));bounds.y=Mathf.Clamp(bounds.y,12,Mathf.Max(12,Screen.height-bounds.height-12));
                bounds=GUILayout.Window(windowId,bounds,Draw,GUIContent.none,theme.Window);
            }
            finally{GUI.skin=oldSkin;GUI.color=oldColor;GUI.backgroundColor=oldBackground;GUI.contentColor=oldContent;}
        }
        private void Draw(int id)
        {
            theme.Heading("Tug operations","GROUND OPERATIONS  /  LIVE STAND STATUS",()=>visible=false);
            var rows=Module.Rows;
            if(planning!=null&&!Module.TransferBusy&&!rows.Any(r=>r.TugId==planningTug&&r.Callsign==planning.Callsign&&r.AtStand))
            {planning=null;previewText="The aircraft is no longer parked at its stand. Return to the tug list and choose a parked aircraft.";}
            GUILayout.BeginHorizontal();Metric(rows.Length.ToString(),"TUGS");Metric(rows.Count(r=>r.Linked&&!r.Home).ToString(),"COUPLED");Metric(rows.Count(r=>r.ReturnActive).ToString(),"ACTIVE RETURNS");GUILayout.EndHorizontal();
            theme.BeginCard();GUILayout.Label(Module.OperationStatus,theme.Body);theme.EndCard();
            theme.BeginCard("STAND TRANSFER");GUILayout.Label(Module.TransferStatus,theme.Body);
            foreach(var aircraft in Module.TowingAircraft)
            {
                var currentAircraft=aircraft;GUILayout.Label(Module.TowCallsign(aircraft)+" → "+Module.TowDestinationLabel(aircraft),theme.Body);
                var runwayStatus=Module.TowRunwayStatus(aircraft);if(runwayStatus!=null)GUILayout.Label(runwayStatus,theme.Body);
                var runway=Module.WaitingRunway(aircraft);if(runway!=null&&GUILayout.Button("Clear to cross runway "+runway,theme.Primary))queuedTowAction=()=>Module.ClearTowRunway(currentAircraft,runway);
                if(Module.NeedsTowRecovery(aircraft)&&GUILayout.Button("Validate and recover tow",theme.Primary))queuedTowAction=()=>Module.RecoverTow(currentAircraft);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Hold tow",theme.Secondary))queuedTowAction=()=>Module.HoldTransfer(currentAircraft,true);
                if(GUILayout.Button("Continue tow",theme.Secondary))queuedTowAction=()=>Module.HoldTransfer(currentAircraft,false);
                if(GUILayout.Button("Cancel tow and hold",theme.Danger))queuedTowAction=()=>Module.CancelTransfer(currentAircraft);
                GUILayout.EndHorizontal();
            }
            theme.EndCard();
            GUILayout.Label("Selected-aircraft ADIRS: amber dashes = proposed route; cyan dashes = native checked. Moving tows use the native route display.",theme.Muted);
            GUILayout.Label(Module.ParkedInventoryStatus,theme.Muted);
            if(Module.DormantRequestsEnabled){theme.BeginCard("AIRCRAFT TOW REQUESTS");GUILayout.Label(Module.DormantRequestStatus,theme.Body);
            if(Module.HasDormantRequest)GUILayout.Label("Saved transfers resume their prior movement and clearance state after loading. Deliberate holds remain held.",theme.Muted);
            foreach(var aircraft in Module.DormantAircraft.Where(p=>!Module.IsTowing(p))){
                var currentAircraft=aircraft;GUILayout.Label(Module.TowCallsign(aircraft)+" → "+Module.TowDestinationLabel(aircraft),theme.Body);
                if(GUILayout.Button("Approve requested tow",theme.Primary)){selectedDormant=aircraft;acceptDormant=true;}
                if(GUILayout.Button("Dismiss request",theme.Secondary))queuedTowAction=()=>Module.CancelTransfer(currentAircraft);
            }
            if(Module.CanGenerateParkedRequest&&GUILayout.Button("Request a parked aircraft tow",theme.Secondary))requestDormant=true;
            theme.EndCard();
            }
            scroll=GUILayout.BeginScrollView(scroll);
            if(planningView)DrawPlanning();
            else
            {
            var shown=0;
            foreach(var row in rows)
            {
                shown++;theme.BeginCard();
                var stand=row.Gate!=null&&row.Gate.StartsWith("gate_",StringComparison.OrdinalIgnoreCase)?"Stand "+row.Gate.Substring(5):row.Gate??"Unassigned stand";
                GUILayout.BeginHorizontal();GUILayout.BeginVertical();GUILayout.Label(stand,theme.Title);GUILayout.Label(string.IsNullOrEmpty(row.Callsign)||row.Callsign=="-"?"No aircraft assigned":row.Callsign,theme.Body);GUILayout.EndVertical();
                GUILayout.FlexibleSpace();theme.Badge(row.ReturnActive?"RETURN IN PROGRESS":row.Phase.ToUpperInvariant(),row.ReturnActive?"info":row.WaitingForTug?"warn":row.Linked&&!row.Home?"good":row.AircraftId==0?"muted":"info");GUILayout.EndHorizontal();
                if(row.AircraftId!=0)
                {
                    GUILayout.Space(6);var state=(row.AircraftState??"").Replace("STATE_","").Replace('_',' ').ToLowerInvariant();
                    GUILayout.Label(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(state)+(string.IsNullOrEmpty(row.OutgoingCallsign)||row.OutgoingCallsign=="-"?"":"   ·   Next flight "+row.OutgoingCallsign),theme.Muted);
                    if(row.StandAssociation)GUILayout.Label("Aircraft at stand. Tug released.",theme.Muted);
                    if(row.WaitingForTug)GUILayout.Label("Waiting for tug coupling",theme.Body);
                }
                if(row.ReturnActive)
                {GUILayout.Label(row.ReturnReason??"Returning to stand",theme.Body);if(GUILayout.Button("Cancel return and hold aircraft",theme.Danger))Module.CancelReturn();}
                else if(row.CanReturn)
                {GUILayout.Space(6);if(GUILayout.Button("Return "+row.ReturnCallsign+" to "+row.Gate,theme.Primary))Module.RequestReturn(row.TugId);}
                else if(row.AircraftId!=0)GUILayout.Label(row.ReturnReason??"Return unavailable.",theme.Muted);
                if(row.AtStand&&row.AircraftId!=0&&!row.ReturnActive&&GUILayout.Button("Plan stand / parking transfer…",theme.Secondary))QueuePlanning(row.TugId);
                theme.EndCard();
            }
            if(shown==0)GUILayout.Label(Module.Status,theme.Muted);
            }
            GUILayout.EndScrollView();GUILayout.Space(6);
            var chord=InputServices.Current?.Actions.FirstOrDefault(a=>a.Definition.Id=="tugs.panel")?.Binding.ToString()??"F9";
            GUILayout.Label(chord+"  ·  Toggle panel     Drag the header to move",theme.Muted);
            GUILayout.Label("Saving or a new aircraft instruction cancels an active return.",theme.Muted);
            GUI.DragWindow(new Rect(0,0,bounds.width-90,65));
        }
        private void OpenPlanning(int tugId)
        {
            planningView=true;planningTug=tugId;scroll=Vector2.zero;destinationScroll=Vector2.zero;selectedDestination=null;crossingDestination=null;
            try{planning=Module.TransferDestinations(tugId);planningTug=tugId;previewText="Select a destination to inspect its tow route.";}
            catch(Exception error){planning=null;previewText=error.Message;}
        }
        private void QueuePlanning(int tugId){requestedTug=tugId;requestPlanning=true;}
        private void DrawPlanning()
        {
            theme.BeginCard("STAND / PARKING TRANSFER");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("← Back to tugs",theme.Secondary))requestBack=true;
            if(GUILayout.Button("Refresh destinations",theme.Secondary))QueuePlanning(planningTug);
            GUILayout.EndHorizontal();
            if(planning==null)
            {
                GUILayout.Label("Planning unavailable",theme.Title);
                GUILayout.Label(previewText??"Choose a parked aircraft from the tug list.",theme.Body);
                theme.EndCard();return;
            }
            GUILayout.Label(planning.Callsign+"  ·  From "+planning.Source,theme.Body);
            GUILayout.Label("Start tow checks a fresh aircraft-aligned native route before reserving both stands and coupling. Single player, native stands. Each runway crossing requires separate clearance after the tow stops short.",theme.Muted);
            if(planning!=null)
            {
                GUILayout.Label(planning.Destinations.Length+" stand / parking destinations",theme.Muted);
                if(planning.Destinations.Length==0)GUILayout.Label("No supported parking endpoints were found at this airport. The airport may represent its stands differently; see the tug planning log.",theme.Body);
                destinationScroll=GUILayout.BeginScrollView(destinationScroll,GUILayout.Height(180));
                foreach(var destination in planning.Destinations)
                {
                    var enabled=GUI.enabled;GUI.enabled=enabled&&!destination.Occupied&&!destination.Reserved;
                    if(GUILayout.Button(destination.Alias+"  ·  "+destination.Name+"  ·  "+destination.Kind+"  ·  "+destination.Availability,theme.Row))
                    {
                        crossingDestination=destination;
                        var result=Module.PreviewTransfer(planning,destination);
                        selectedDestination=result.Found?destination:null;
                        previewText=result.Message+(result.Found?"\nApprox. "+result.Length.ToString("0")+" world units  ·  "+string.Join(" → ",result.Roads):"");
                    }
                    GUI.enabled=enabled;
                }
                GUILayout.EndScrollView();GUILayout.Label(previewText??"",theme.Muted);
                if(crossingDestination!=null&&GUILayout.Button("Preview runway crossings (read-only)",theme.Secondary))requestCrossingPreview=true;
                if(selectedDestination!=null)
                {
                    var enabled=GUI.enabled;GUI.enabled=enabled&&!Module.RouteCheckBusy&&!Module.TransferBusy;
                    if(GUILayout.Button("Check native route to "+selectedDestination.Alias,theme.Primary))requestCheck=true;
                    GUILayout.Label("Via taxiways (optional, in order)",theme.Muted);viaInput=GUILayout.TextField(viaInput,80);
                    GUILayout.Label("Hold short of taxiway (optional)",theme.Muted);holdInput=GUILayout.TextField(holdInput,32);
                    if(GUILayout.Button("Start tow to "+selectedDestination.Alias,theme.Primary))requestTransfer=true;
                    GUI.enabled=enabled;
                }
                if(Module.RouteCheckBusy&&GUILayout.Button("Cancel native route check",theme.Secondary))Module.CancelRouteCheck();
                GUILayout.Label(Module.RouteCheckStatus,theme.Body);
            }
            theme.EndCard();
        }
        private void Metric(string value,string label)
        {GUILayout.BeginVertical(theme.Card);GUILayout.Label(value,theme.Metric);GUILayout.Label(label,theme.Section);GUILayout.EndVertical();}
    }
}


