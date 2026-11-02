using System;
namespace TS3Modding.API
{
    /// <summary>Advanced parser registration. Capture/grammar callbacks may run on recognition threads:
    /// use immutable snapshots only. ResolveSpeech and Execute run on the game thread.</summary>
    public sealed class RadioCommandProvider
    {
        public RadioCommandProvider(string id,Func<RadioCommandAddress,bool> accepts,Action<RadioCommandContext> execute,
            Func<string,string> captureSpeech=null,Func<string,string> resolveSpeech=null,
            Func<string,string> extendCommands=null,Func<string,string> extendTargets=null,
            bool requireRadioContact=true,double duplicateWindowSeconds=0)
        {
            if(string.IsNullOrWhiteSpace(id)||id.Length>128)throw new ArgumentException("A namespaced provider ID is required.");
            if((captureSpeech==null)!=(resolveSpeech==null))throw new ArgumentException("Supply both speech callbacks.");
            if(double.IsNaN(duplicateWindowSeconds)||duplicateWindowSeconds<0||duplicateWindowSeconds>5)throw new ArgumentOutOfRangeException(nameof(duplicateWindowSeconds));
            Id=id;Accepts=accepts??throw new ArgumentNullException(nameof(accepts));Execute=execute??throw new ArgumentNullException(nameof(execute));
            CaptureSpeech=captureSpeech;ResolveSpeech=resolveSpeech;ExtendCommands=extendCommands;ExtendTargets=extendTargets;
            RequireRadioContact=requireRadioContact;DuplicateWindowSeconds=duplicateWindowSeconds;
        }
        public string Id{get;} public Func<RadioCommandAddress,bool> Accepts{get;}
        public Action<RadioCommandContext> Execute{get;} public Func<string,string> CaptureSpeech{get;}
        public Func<string,string> ResolveSpeech{get;} public Func<string,string> ExtendCommands{get;} public Func<string,string> ExtendTargets{get;}
        public bool RequireRadioContact{get;} public double DuplicateWindowSeconds{get;}
    }
    public sealed class RadioCommandAddress
    {
        public RadioCommandAddress(object nativeAircraft,string instruction){NativeAircraft=nativeAircraft;Instruction=instruction;}
        // Opaque identity for reviewed adapters. Do not call Unity APIs from capture callbacks.
        public object NativeAircraft{get;} public string Instruction{get;}
    }
    public interface IRadioCommandProviderService : IRadioCommandService
    {
        IDisposable RegisterProvider(RadioCommandProvider provider);
        // Main thread only; useful for asynchronous operational reports as well as command replies.
        void Reply(object nativeAircraft,string message,string displayText=null);
    }
}
