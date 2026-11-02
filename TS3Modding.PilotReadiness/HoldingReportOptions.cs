namespace TS3Modding.PilotReadiness
{
    internal enum HoldingReportKind { AssignedRunway, CrossingRunway, Taxiway }
    internal sealed class HoldingReportOptions
    {
        public HoldingReportOptions(bool assignedRunway, bool crossingRunway, bool taxiway)
        { AssignedRunway=assignedRunway; CrossingRunway=crossingRunway; Taxiway=taxiway; }
        public bool AssignedRunway { get; }
        public bool CrossingRunway { get; }
        public bool Taxiway { get; }
        public bool Any => AssignedRunway || CrossingRunway || Taxiway;
        public bool Enabled(HoldingReportKind kind) => kind == HoldingReportKind.AssignedRunway ? AssignedRunway : kind == HoldingReportKind.CrossingRunway ? CrossingRunway : Taxiway;
        public static HoldingReportKind Classify(bool runway, bool departure, bool assignedGate, bool assignedRunwayOnDepartureRoute) =>
            !runway ? HoldingReportKind.Taxiway : departure && (assignedGate || assignedRunwayOnDepartureRoute) ? HoldingReportKind.AssignedRunway : HoldingReportKind.CrossingRunway;
    }
}

