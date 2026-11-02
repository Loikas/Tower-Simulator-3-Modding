using System;

namespace TS3Modding.PilotReadiness
{
    // One report per physical holding episode. A stopped traffic queue without
    // a verified hold target never creates an episode.
    internal sealed class HoldingReportTracker
    {
        private object target;
        private double stoppedSince = double.NaN, episodeStart;
        private bool reported;
        private string nativeMessage;
        private double nativeAt = double.NegativeInfinity;
        public string Name { get; private set; }
        public string Message { get; private set; }
        public void NativeReport(string message, double now) { nativeMessage = message; nativeAt = now; }
        public void Observe(object holdTarget, string name, string message, double now)
        {
            if (holdTarget == null || string.IsNullOrWhiteSpace(name)) return;
            if (!ReferenceEquals(target, holdTarget))
            { target = holdTarget; episodeStart = now; stoppedSince = double.NaN; reported = false; }
            Name = name; Message = message;
        }
        public bool Ready(bool holding, bool eligible, float speed, double now)
        {
            if (!holding || !eligible || float.IsNaN(speed) || float.IsInfinity(speed) || Math.Abs(speed) > 0.05f)
            {
                // A vehicle braking toward a known hold is not there yet. Only
                // leaving the hold clears the episode, not normal deceleration.
                stoppedSince = double.NaN;
                if (!holding || !eligible) { target = null; reported = false; }
                return false;
            }
            if (target == null || reported) return false;
            if (nativeAt >= episodeStart - 1 && NamesTarget(nativeMessage, Name)) { reported = true; return false; }
            if (double.IsNaN(stoppedSince)) stoppedSince = now;
            return now - stoppedSince >= 0.75;
        }
        public void Sent() { reported = true; }
        public void Cancel() { target = null; reported = false; stoppedSince = double.NaN; }
        private static bool NamesTarget(string message, string name)
        {
            if(message==null||name==null) return false;
            foreach(var end in name.Split('/'))
            {
                var pattern=System.Text.RegularExpressions.Regex.Escape(end);
                if(System.Text.RegularExpressions.Regex.IsMatch(end,@"^0[1-9]$")) pattern="0?"+end.Substring(1);
                if(System.Text.RegularExpressions.Regex.IsMatch(message,@"(?<![A-Za-z0-9])"+pattern+@"(?![A-Za-z0-9])",System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }
    }
}

