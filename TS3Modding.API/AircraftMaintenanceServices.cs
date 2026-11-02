using System;
namespace TS3Modding.API
{
    // Maintenance policy belongs to its provider; transport only queries it and
    // reports a parked handover. All calls run on the simulation thread.
    public static class AircraftMaintenanceServices
    {
        private static Func<object,bool> contains;private static Action<object> parked;
        public static bool Contains(object aircraft)=>contains?.Invoke(aircraft)==true;
        public static void ReturnedToStand(object aircraft){if(Contains(aircraft))parked?.Invoke(aircraft);}
        public static IDisposable Register(Func<object,bool> query,Action<object> returned)
        {
            if(query==null||returned==null)throw new ArgumentNullException();
            if(contains!=null)throw new InvalidOperationException("Maintenance provider already registered.");
            contains=query;parked=returned;return new Token(()=>{if(ReferenceEquals(contains,query)){contains=null;parked=null;}});
        }
        private sealed class Token:IDisposable{private Action release;internal Token(Action action){release=action;}public void Dispose(){var action=release;release=null;action?.Invoke();}}
    }
}
