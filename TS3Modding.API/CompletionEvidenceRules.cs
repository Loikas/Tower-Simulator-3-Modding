namespace TS3Modding.API
{
    public static class CompletionEvidenceRules
    {
        public static bool IsDeparture(FlightCompletionKind kind)=>kind==FlightCompletionKind.DepartureContactAccepted||kind==FlightCompletionKind.DepartureAutomaticHandoff;
        public static bool AutomaticDepartureHandoff(string action,string state,bool active,bool deleted,float altitude,string ownerTarget,bool nativeForgotBranch,bool flyAwayDeparture)=>
            nativeForgotBranch&&flyAwayDeparture&&AcceptedDepartureContact(action,state,active,deleted,altitude,ownerTarget,true);
        public static bool FinalApproachReport(string action, bool active, bool deleted, int messageKind, string message) =>
            active && !deleted && action == "ACTION_INCOMING" && messageKind == 4 && message != null &&
            System.Text.RegularExpressions.Regex.IsMatch(message, @"\bon final(?:\s|[.,]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
            message.IndexOf("not on final", System.StringComparison.OrdinalIgnoreCase) < 0 &&
            message.IndexOf("going around", System.StringComparison.OrdinalIgnoreCase) < 0;
        public static bool ParkedArrival(string action, string state, bool active, bool deleted, bool atTerminal, bool showStrip, bool hasTerminal, float speed) =>
            active && !deleted && action == "ACTION_INCOMING" && state == "STATE_WAIT_FOR_RESPAWN" && atTerminal && !showStrip && hasTerminal && speed == 0;
        public static bool AcceptedDepartureContact(string action, string state, bool active, bool deleted, float altitude, string ownerTarget, bool acceptedBranch) =>
            acceptedBranch && active && !deleted && (action == "ACTION_OUTGOING" || action == "ACTION_TERMINALSTART") &&
            (state == "STATE_TAKEOFFUP" || state == "STATE_FLYAWAY") && altitude > 0 &&
            string.Equals(ownerTarget, "departure", System.StringComparison.OrdinalIgnoreCase);
    }
}
