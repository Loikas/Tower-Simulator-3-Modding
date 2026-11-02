using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace TS3Modding.API
{
    /// <summary>Exact, aircraft-addressed phrases. Registration and callbacks use Unity's main thread.</summary>
    public sealed class RadioCommand
    {
        public RadioCommand(string id, string[] phrases, Action<RadioCommandContext> execute)
        {
            if(string.IsNullOrWhiteSpace(id)||id.Length>128)throw new ArgumentException("A namespaced command ID is required.");
            if(phrases==null||phrases.Length==0||phrases.Length>16)throw new ArgumentException("Provide 1–16 phrases.");
            Id=id; Phrases=Array.AsReadOnly(phrases.Select(p=>{
                var text=Normalize(p);if(text.Length<3||text.Length>96||!Regex.IsMatch(text,@"^[a-z0-9]+(?: [a-z0-9]+)*$"))throw new ArgumentException("Phrases must contain words and digits, without placeholders.");return text;
            }).Distinct().ToArray()); Execute=execute??throw new ArgumentNullException(nameof(execute));
        }
        public string Id { get; }
        public System.Collections.Generic.IReadOnlyList<string> Phrases { get; }
        public Action<RadioCommandContext> Execute { get; }
        public static string Normalize(string text)=>Regex.Replace((text??"").Trim().TrimEnd('.',',','!','?').ToLowerInvariant(),@"\s+"," ");
    }
    public sealed class RadioCommandContext
    {
        private readonly Action<string> reply;
        public RadioCommandContext(string callsign,string phrase,Action<string> reply):this(null,callsign,phrase,reply){}
        public RadioCommandContext(object nativeAircraft,string callsign,string phrase,Action<string> reply){NativeAircraft=nativeAircraft;Callsign=callsign;Phrase=phrase;this.reply=reply??throw new ArgumentNullException(nameof(reply));}
        public object NativeAircraft { get; }
        public string Callsign { get; }
        public string Phrase { get; }
        /// <summary>Reply during the callback. The framework appends the native spoken callsign.</summary>
        public void Reply(string message){if(string.IsNullOrWhiteSpace(message)||message.Length>240||message.Any(char.IsControl))throw new ArgumentException("Pilot replies must be 1–240 characters.");reply(message);}
    }
    public interface IRadioCommandService { IDisposable Register(RadioCommand command); }
    public static class RadioCommands
    {
        public static IRadioCommandService Current { get; private set; }
        public static void Attach(IRadioCommandService service){if(service==null)throw new ArgumentNullException(nameof(service));if(Current!=null)throw new InvalidOperationException("Command service already attached.");Current=service;}
        public static void Detach(IRadioCommandService service){if(ReferenceEquals(Current,service))Current=null;}
    }
}
