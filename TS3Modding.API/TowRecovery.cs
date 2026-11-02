using System;
namespace TS3Modding.API
{
    public struct TowRecoveryPoint { public float X,Y,Z; public TowRecoveryPoint(float x,float y,float z){X=x;Y=y;Z=z;} }
    // All calls and callbacks run on the Unity thread. The requester owns the stand
    // reservation; the provider owns coupling/movement after controller approval.
    public sealed class TowRecoveryRequest
    {
        public object Aircraft,Runway,Stand;
        public string DestinationAlias;
        public Func<bool> IsValid;
        public Action<int,object[],Action<TowRecoveryPoint[]>,Action<string>> PlanRoute;
        public Action Completed;
    }
    public static class TowRecoveryServices
    {
        private static Func<TowRecoveryRequest,IDisposable> provider;
        public static bool Available=>provider!=null;
        public static IDisposable Register(Func<TowRecoveryRequest,IDisposable> handler)
        {
            if(handler==null)throw new ArgumentNullException(nameof(handler));
            if(provider!=null)throw new InvalidOperationException("Tow recovery provider already registered.");
            provider=handler;return new Token(()=>{if(ReferenceEquals(provider,handler))provider=null;});
        }
        public static IDisposable Request(TowRecoveryRequest request)=>provider?.Invoke(request);
        private sealed class Token:IDisposable {private Action release;internal Token(Action release){this.release=release;}public void Dispose(){var action=release;release=null;action?.Invoke();}}
    }
}
