using System;
using System.Collections.Generic;
using System.Linq;
using TS3Modding.API;

namespace TS3Modding;

internal sealed class RadioCommandRegistry
{
    readonly Dictionary<string, RadioCommand> entries = [];
    internal RadioCommand[] Snapshot { get; private set; } = [];
    internal void Add(RadioCommand command)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        if (entries.Count >= 64 || entries.ContainsKey(command.Id) || Snapshot.Any(c => c.Phrases.Any(a => command.Phrases.Any(b => a == b || a.EndsWith(" " + b, StringComparison.Ordinal) || b.EndsWith(" " + a, StringComparison.Ordinal)))))
            throw new InvalidOperationException("Command ID or overlapping phrase already registered, or command limit reached.");

        entries.Add(command.Id, command); Snapshot = [.. entries.Values];
    }

    internal void Remove(string id) { entries.Remove(id); Snapshot = [.. entries.Values]; }
    internal void Clear() { entries.Clear(); Snapshot = []; }
}
