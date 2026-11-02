using System;
namespace TS3Modding.API
{
    // Optional provider: absence means no automatic pilot holding report.
    public static class PilotReportPreferences
    {
        private static Func<bool> crossing;
        public static bool CrossingReportsEnabled=>crossing!=null&&crossing();
        public static IDisposable RegisterCrossingReports(Func<bool> enabled)
        {
            if(enabled==null)throw new ArgumentNullException(nameof(enabled));
            if(crossing!=null)throw new InvalidOperationException("Pilot crossing-report preferences already registered.");
            crossing=enabled;return new Token(enabled);
        }
        private sealed class Token:IDisposable
        {
            private Func<bool> owner;internal Token(Func<bool> owner){this.owner=owner;}
            public void Dispose(){if(owner!=null&&ReferenceEquals(crossing,owner))crossing=null;owner=null;}
        }
    }
}
