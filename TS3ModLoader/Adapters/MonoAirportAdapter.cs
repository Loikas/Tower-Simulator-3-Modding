using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Linq;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding.Adapters
{
    internal sealed class MonoAirportAdapter : IDisposable
    {
        private readonly MonoAircraftAdapter aircraft;
        private readonly Type gateType, stripType;
        private readonly FieldInfo gateTerminal, gateName, gateState, gatePlane;
        private readonly FieldInfo stripData, stripParent, blockHeader;
        private readonly FieldInfo dataName, dataNetwork, dataBlock, dataDeparture;
        private readonly Type boardType, blockType;
        private readonly FieldInfo stripOwner, boardBin, boardItems, blockItems, blockColumn, columnBoard, columnBlocks, boardColumns, blockPrefab, boardManager, boardWait;
        private readonly MethodInfo moveItem;
        private readonly FieldInfo assignedRunway, nextBlocks, blockRunways;
        private readonly Dictionary<int, Component> liveStrips = new Dictionary<int, Component>();
        private readonly CompletionHooks completionHooks;
        public MonoAirportAdapter(Assembly assembly, string hash, Action<string> log)
        {
            aircraft = new MonoAircraftAdapter(assembly, hash);
            gateType = ProfileBindings.ComponentType(assembly, "IKGate");
            stripType = ProfileBindings.ComponentType(assembly, "StripItem");
            var terminal = ProfileBindings.ComponentType(assembly, "TerminalGate");
            var airplane = ProfileBindings.ComponentType(assembly, "Airplane");
            var block = ProfileBindings.ComponentType(assembly, "StripBlock");
            blockType = block;
            boardType = ProfileBindings.ComponentType(assembly, "Strip");
            var column = ProfileBindings.ComponentType(assembly, "StripCol");
            var data = assembly.GetType("TCP+RespStripItem", true);
            gateTerminal = ProfileBindings.Field(gateType, "gate", terminal);
            gateName = ProfileBindings.Field(terminal, "gatename", typeof(string));
            gatePlane = ProfileBindings.Field(gateType, "plane", airplane);
            gateState = ProfileBindings.Field(gateType, "state", assembly.GetType("IKGate+PBBMPCGNAPE", true));
            if (!gateState.FieldType.IsEnum) throw new NotSupportedException("Gate state is no longer an enum.");
            stripData = ProfileBindings.Field(stripType, "rsi", data);
            stripParent = ProfileBindings.Field(stripType, "parent", block);
            // SetBin controls the next-button label; actual bin membership comes from board.binblock.
            blockHeader = ProfileBindings.Field(block, "header", typeof(string));
            dataName = ProfileBindings.Field(data, "name", typeof(string));
            dataNetwork = ProfileBindings.Field(data, "netidx", typeof(int));
            dataBlock = ProfileBindings.Field(data, "blk", typeof(string));
            dataDeparture = ProfileBindings.Field(data, "dep", typeof(bool));
            assignedRunway = ProfileBindings.Field(data, "c_rw", typeof(string));
            nextBlocks = ProfileBindings.Field(block, "nexts", typeof(List<>).MakeGenericType(block));
            blockRunways = ProfileBindings.Field(block, "runway", typeof(string[]));
            stripOwner = ProfileBindings.Field(stripType, "ADOIPDPLMBE", boardType);
            boardBin = ProfileBindings.Field(boardType, "binblock", block);
            boardItems = ProfileBindings.Field(boardType, "items", typeof(List<>).MakeGenericType(stripType));
            blockItems = ProfileBindings.Field(block, "items", typeof(List<>).MakeGenericType(stripType));
            blockColumn = ProfileBindings.Field(block, "colum", column);
            columnBoard = ProfileBindings.Field(column, "strip", boardType);
            columnBlocks = ProfileBindings.Field(column, "blocks", typeof(List<>).MakeGenericType(block));
            boardColumns = ProfileBindings.Field(boardType, "colums", typeof(List<>).MakeGenericType(column));
            blockPrefab = ProfileBindings.Field(block, "prefab_item", typeof(GameObject));
            boardManager = ProfileBindings.Field(boardType, "mgr", assembly.GetType("UIDataManager", true));
            boardWait = ProfileBindings.Field(boardType, "strip_wait", typeof(float));
            moveItem = boardType.GetMethod("MoveItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { stripType, block, typeof(bool) }, null);
            if (moveItem == null || moveItem.ReturnType != typeof(void) || moveItem.IsStatic)
                throw new NotSupportedException("Strip.MoveItem signature changed.");
            try { completionHooks = new CompletionHooks(assembly, aircraft, log); }
            catch (Exception error) { log("Completion hooks unavailable; departure archival remains disabled: " + error); }
        }
        public AirportObservation Observe()
        {
            var planes = aircraft.Observe();
            var gates = new List<GateSnapshot>();
            foreach (var value in Resources.FindObjectsOfTypeAll(gateType))
            {
                var component = value as Component;
                if (!ProfileBindings.InLoadedScene(component)) continue;
                var terminal = gateTerminal.GetValue(component) as Component;
                gates.Add(new GateSnapshot(component.GetInstanceID(), component.gameObject.scene.name,
                    terminal ? (string)gateName.GetValue(terminal) : null, gateState.GetValue(component).ToString(),
                    aircraft.ObservedId(gatePlane.GetValue(component) as Component)));
            }
            var strips = new List<StripSnapshot>();
            liveStrips.Clear();
            foreach (var value in Resources.FindObjectsOfTypeAll(stripType))
            {
                var component = value as Component;
                if (!ProfileBindings.InLoadedScene(component)) continue;
                var data = stripData.GetValue(component);
                if (data == null) continue; // UI instance not initialized yet.
                var parent = stripParent.GetValue(component) as Component;
                var owner = stripOwner.GetValue(component) as Component;
                if (!parent || !owner || string.IsNullOrWhiteSpace((string)dataName.GetValue(data))) continue;
                var ownerItems = boardItems.GetValue(owner) as IList;
                if (ownerItems == null || !ownerItems.Contains(component)) continue;
                liveStrips.Add(component.GetInstanceID(), component);
                var blocks=new List<Component>();
                foreach(var col in ((boardColumns.GetValue(owner) as IList)?.Cast<object>()??Enumerable.Empty<object>()).OfType<Component>())
                    if(col && columnBoard.GetValue(col) as Component==owner)
                        blocks.AddRange(((columnBlocks.GetValue(col) as IList)?.Cast<object>()??Enumerable.Empty<object>()).OfType<Component>().Where(b=>b));
                var graph=blocks.Distinct().Select(b=>new StripRoutingBlock{Id=b.GetInstanceID(),Header=(string)blockHeader.GetValue(b),Runways=(string[])blockRunways.GetValue(b),
                    Next=((nextBlocks.GetValue(b) as IList)?.Cast<object>()??Enumerable.Empty<object>()).OfType<Component>().Where(n=>n).Select(n=>n.GetInstanceID()).ToArray()}).ToArray();
                if(!graph.Any(b=>b.Id==parent.GetInstanceID()))continue;
                var bin=boardBin.GetValue(owner) as Component;
                var routes=StripRunwayRouting.Routes(graph,parent.GetInstanceID(),!(bool)dataDeparture.GetValue(data),bin?bin.GetInstanceID():0);
                strips.Add(new StripSnapshot(component.GetInstanceID(), component.gameObject.scene.name,
                    (string)dataName.GetValue(data), (int)dataNetwork.GetValue(data),
                    parent ? (string)blockHeader.GetValue(parent) : (string)dataBlock.GetValue(data),
                    (bool)dataDeparture.GetValue(data), parent == (boardBin.GetValue(owner) as Component),
                    (string)assignedRunway.GetValue(data), routes, owner.GetInstanceID()));
            }
            return new AirportObservation(planes, gates, strips, aircraft.CompletionEvidence());
        }
        public bool TryArchiveDormantTow(StripSnapshot expected,object plane,object schedule,Action<string> audit)
        {
            if(!aircraft.MatchesRetiredTow(expected,plane,schedule))return false;
            // Use the board's actual BIN and its normal synchronized MoveItem path.
            return TryMove(expected,null,audit,true);
        }
        public bool TryArchive(StripSnapshot expected, FlightCompletion evidence, Action<string> audit)
        {
            if (expected == null || evidence == null || expected.InBin || !aircraft.CompletionEvidence().Contains(evidence) ||
                evidence.Callsign != expected.Callsign || evidence.NetworkIndex != expected.NetworkIndex || evidence.Scene != expected.Scene ||
                expected.Departure != CompletionEvidenceRules.IsDeparture(evidence.Kind)) return false;
            // A recycled callsign/network identity must not inherit another lifetime's completion.
            var current = aircraft.Observe();
            if (current.Any(a => a.Scene == evidence.Scene && a.Details.NetworkIndex == evidence.NetworkIndex && a.Details.Callsign == evidence.Callsign && !a.Id.Equals(evidence.Aircraft))) return false;
            audit("ARCHIVE EVIDENCE " + evidence.Callsign + " kind=" + evidence.Kind + " id=" + evidence.Aircraft + " confirmed=" + evidence.ConfirmedUtc.ToString("O"));
            return TryMove(expected, null, audit, true);
        }
        // Returns false for a stale record or unsupported layout; exceptions disable automation upstream.
        public bool TryMove(StripSnapshot expected, string targetHeader, Action<string> audit, bool archiving = false)
        {
            Component item;
            if (!liveStrips.TryGetValue(expected.InstanceId, out item) || !ProfileBindings.InLoadedScene(item) ||
                item.gameObject.scene.name != expected.Scene || !item.gameObject.activeInHierarchy) return false;
            var owner = stripOwner.GetValue(item) as Component;
            var source = stripParent.GetValue(item) as Component;
            var data = stripData.GetValue(item);
            if (!owner || !source || data == null || !owner.gameObject.activeInHierarchy) return false;
            if (expected.BoardInstanceId!=0 && owner.GetInstanceID()!=expected.BoardInstanceId) return false;
            var sourceColumn = blockColumn.GetValue(source) as Component;
            if (!sourceColumn || columnBoard.GetValue(sourceColumn) as Component != owner) return false;
            if ((string)dataName.GetValue(data) != expected.Callsign || (int)dataNetwork.GetValue(data) != expected.NetworkIndex ||
                (bool)dataDeparture.GetValue(data) != expected.Departure || (string)blockHeader.GetValue(source) != expected.Block ||
                source == (boardBin.GetValue(owner) as Component)) return false;
            var targets = new List<Component>();
            if (archiving)
            {
                var actualBin = boardBin.GetValue(owner) as Component;
                if (!actualBin) return false;
                targets.Add(actualBin); targetHeader = (string)blockHeader.GetValue(actualBin);
            }
            var columns = boardColumns.GetValue(owner) as IList;
            if (columns == null) return false;
            if (!archiving) foreach (var entry in columns)
            {
                var col = entry as Component;
                if (!col || columnBoard.GetValue(col) as Component != owner) continue;
                var blocks = columnBlocks.GetValue(col) as IList;
                if (blocks == null) continue;
                foreach (var block in blocks)
                {
                    var candidate = block as Component;
                    if (candidate && (string)blockHeader.GetValue(candidate) == targetHeader) targets.Add(candidate);
                }
            }
            if (targets.Count != 1) return false;
            var target = targets[0];
            if (!ProfileBindings.InLoadedScene(target) || target.gameObject.scene != item.gameObject.scene ||
                target == source || (!archiving && (target == (boardBin.GetValue(owner) as Component) || targetHeader.Equals("BIN", StringComparison.OrdinalIgnoreCase)))) return false;
            var fromItems = blockItems.GetValue(source) as IList;
            var toItems = blockItems.GetValue(target) as IList;
            var allItems = boardItems.GetValue(owner) as IList;
            var prefab = blockPrefab.GetValue(target) as GameObject;
            var colTarget = blockColumn.GetValue(target) as Component;
            if (fromItems == null || toItems == null || allItems == null || !prefab || !prefab.transform.parent ||
                (!archiving && !colTarget) || (colTarget && columnBoard.GetValue(colTarget) as Component != owner) ||
                !(boardManager.GetValue(owner) as Component) || (float)boardWait.GetValue(owner) > 0 ||
                fromItems.Cast<object>().Count(x => ReferenceEquals(x, item)) != 1 || toItems.Contains(item) ||
                allItems.Cast<object>().Count(x => ReferenceEquals(x, item)) != 1) return false;
            audit("MOVE ATTEMPT " + expected.Callsign + " id=" + expected.InstanceId + " " + expected.Block + " -> " + targetHeader);
            moveItem.Invoke(owner, new object[] { item, target, true });
            if (stripParent.GetValue(item) as Component != target || fromItems.Contains(item) ||
                toItems.Cast<object>().Count(x => ReferenceEquals(x, item)) != 1 || (string)dataBlock.GetValue(data) != targetHeader)
                throw new InvalidOperationException("Strip.MoveItem postconditions failed; automatic moves stopped.");
            audit("MOVE COMMITTED " + expected.Callsign + " -> " + targetHeader);
            return true;
        }
        public int CompletionHookCount => completionHooks == null ? 0 : 4;
        public void Dispose() { completionHooks?.Dispose(); }
    }
}
