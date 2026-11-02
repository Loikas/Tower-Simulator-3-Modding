using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.API
{
    [Flags] public enum KeyModifiers { None=0,Control=1,Shift=2,Alt=4 }
    public sealed class KeyChord
    {
        public string Key { get; }
        public KeyModifiers Modifiers { get; }
        public KeyChord(string key,KeyModifiers modifiers=KeyModifiers.None)
        { if(string.IsNullOrWhiteSpace(key)||!char.IsLetter(key[0])||key.Any(c=>!char.IsLetterOrDigit(c))||((int)modifiers&~7)!=0)throw new ArgumentException("Use one named key and separate modifier flags.");Key=string.Equals(key,"None",StringComparison.OrdinalIgnoreCase)?"None":key;Modifiers=modifiers; }
        public override string ToString()=> (Modifiers.HasFlag(KeyModifiers.Control)?"Ctrl+":"")+(Modifiers.HasFlag(KeyModifiers.Shift)?"Shift+":"")+(Modifiers.HasFlag(KeyModifiers.Alt)?"Alt+":"")+Key;
        public bool Same(KeyChord other)=>other!=null&&Modifiers==other.Modifiers&&string.Equals(Key,other.Key,StringComparison.OrdinalIgnoreCase);
    }
    public sealed class InputActionDefinition
    {
        public string Id { get; } public string Label { get; } public string Group { get; } public KeyChord Default { get; }
        public bool Native { get; }
        public InputActionDefinition(string id,string label,string group,KeyChord defaultBinding,bool native=false)
        { if(string.IsNullOrWhiteSpace(id)||id.Any(c=>!char.IsLetterOrDigit(c)&&c!='.'&&c!='-'&&c!='_'))throw new ArgumentException("Action IDs may contain letters, numbers, dots, hyphens and underscores.");
          Id=id;Label=label;Group=group;Default=defaultBinding??throw new ArgumentNullException(nameof(defaultBinding));Native=native; }
    }
    public sealed class InputActionState
    {
        public InputActionDefinition Definition { get; internal set; }
        public KeyChord Binding { get; internal set; }
        public bool Overridden { get; internal set; }
    }
    public interface IInputBindings
    {
        IDisposable Register(InputActionDefinition definition,Action pressed);
        IReadOnlyList<InputActionState> Actions { get; }
        // null restores the default; KeyChord("None") unbinds an action.
        void SetBinding(string actionId,KeyChord binding);
        string[] Conflicts(string actionId,KeyChord binding);
    }
    public static class InputServices
    {
        public static IInputBindings Current { get; private set; }
        public static void Attach(IInputBindings service){if(Current!=null)throw new InvalidOperationException("An input provider is already attached.");Current=service??throw new ArgumentNullException(nameof(service));}
        public static void Detach(IInputBindings service){if(ReferenceEquals(Current,service))Current=null;}
    }
    // Main-thread registry; Unity sampling and persistence are supplied by the input plugin.
    public sealed class InputBindingRegistry : IInputBindings
    {
        private sealed class Entry
        {
            public InputActionDefinition Definition;public KeyChord Override;public Action Pressed;
            public bool Held,Down,Up,Blocked;
        }
        private readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        private readonly Func<string,KeyChord> read;private readonly Action<string,KeyChord> write;private readonly Action<Exception> error;
        private readonly Action<KeyChord> validate;
        private int frame=int.MinValue;
        public InputBindingRegistry(Func<string,KeyChord> read,Action<string,KeyChord> write,Action<Exception> error,Action<KeyChord> validate=null)
        {this.read=read;this.write=write;this.error=error;this.validate=validate;}
        public IReadOnlyList<InputActionState> Actions=>entries.Values.Select(e=>new InputActionState{Definition=e.Definition,Binding=e.Override??e.Definition.Default,Overridden=e.Override!=null}).ToArray();
        public IDisposable Register(InputActionDefinition definition,Action pressed)
        {
            if(entries.ContainsKey(definition.Id))throw new InvalidOperationException("Duplicate input action: "+definition.Id);
            validate?.Invoke(definition.Default);
            var entry=new Entry{Definition=definition,Override=read(definition.Id),Pressed=pressed};entries.Add(definition.Id,entry);
            return new Registration(()=>{Entry live;if(entries.TryGetValue(definition.Id,out live)&&ReferenceEquals(live,entry))entries.Remove(definition.Id);});
        }
        private sealed class Registration:IDisposable{private Action release;public Registration(Action release){this.release=release;}public void Dispose(){var action=release;release=null;action?.Invoke();}}
        public string[] Conflicts(string id,KeyChord binding)=>binding==null||binding.Key=="None"?new string[0]:entries.Where(p=>p.Key!=id&&binding.Same(p.Value.Override??p.Value.Definition.Default)).Select(p=>p.Value.Definition.Label).ToArray();
        public void SetBinding(string id,KeyChord binding)
        {
            var entry=entries[id];if(binding!=null&&Conflicts(id,binding).Length>0)throw new InvalidOperationException("Shortcut already used by: "+string.Join(", ",Conflicts(id,binding)));
            write(id,binding);entry.Override=binding;entry.Held=entry.Down=entry.Up=false;entry.Blocked=true;
        }
        public bool Overridden(string id)=>entries[id].Override!=null;
        public bool ClaimsPrimaryKey(string exceptId,string key)
        {
            foreach(var pair in entries)
            {
                var entry=pair.Value;
                if(pair.Key!=exceptId&&(entry.Override!=null||!entry.Definition.Native)&&entry.Held&&
                    string.Equals((entry.Override??entry.Definition.Default).Key,key,StringComparison.OrdinalIgnoreCase))return true;
            }
            return false;
        }
        public void UpdateDefault(string id,KeyChord binding)
        {
            var entry=entries[id];var d=entry.Definition;
            entry.Definition=new InputActionDefinition(d.Id,d.Label,d.Group,binding,d.Native);
        }
        public void Sample(int currentFrame,Func<KeyChord,bool> held,bool focused)
        {
            if(frame==currentFrame)return;frame=currentFrame;
            foreach(var pair in entries.ToArray())
            {
                Entry live;if(!entries.TryGetValue(pair.Key,out live)||!ReferenceEquals(live,pair.Value))continue;
                var entry=pair.Value;var binding=entry.Override??entry.Definition.Default;
                bool physical=binding.Key!="None"&&held(binding);
                if(!focused&&physical)entry.Blocked=true;
                if(!physical)entry.Blocked=false;
                var now=focused&&physical&&!entry.Blocked;
                entry.Down=now&&!entry.Held;entry.Up=!now&&entry.Held;entry.Held=now;
                if(entry.Down&&entry.Pressed!=null)try{entry.Pressed();}catch(Exception exception){error(exception);}
            }
        }
        public bool Query(string id,int kind){var entry=entries[id];return kind==0?entry.Held:kind==1?entry.Down:entry.Up;}
    }
}
