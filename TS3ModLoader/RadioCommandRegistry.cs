using System;
using System.Linq;
using System.Collections.Generic;
using TS3Modding.API;
namespace TS3Modding
{
    internal sealed class RadioCommandRegistry
    {
        readonly Dictionary<string,RadioCommand> entries=new Dictionary<string,RadioCommand>();
        internal RadioCommand[] Snapshot {get;private set;}=new RadioCommand[0];
        internal void Add(RadioCommand command)
        {
            if(command==null)throw new ArgumentNullException(nameof(command));
            if(entries.Count>=64||entries.ContainsKey(command.Id)||Snapshot.Any(c=>c.Phrases.Any(a=>command.Phrases.Any(b=>a==b||a.EndsWith(" "+b,StringComparison.Ordinal)||b.EndsWith(" "+a,StringComparison.Ordinal)))))throw new InvalidOperationException("Command ID or overlapping phrase already registered, or command limit reached.");
            entries.Add(command.Id,command);Snapshot=entries.Values.ToArray();
        }
        internal void Remove(string id){entries.Remove(id);Snapshot=entries.Values.ToArray();}
        internal void Clear(){entries.Clear();Snapshot=new RadioCommand[0];}
    }
}
