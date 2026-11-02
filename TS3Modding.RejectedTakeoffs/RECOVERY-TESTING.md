# Rejected Takeoffs 0.3.0: tug recovery preview

Update 0.3.1 / Tugs 0.9.1: the unladen recovery tug approaches along the checked route in reverse, then hands the last 20 world units to native nose-wheel coupling. Other runways encountered during approach require explicit clearance addressed to the aircraft, through voice or the tug panel. The already occupied pickup runway is authorized by recovery approval. Hold/continue also controls the approaching tug. Ordinary stand-transfer tugs retain their native approach.

Origin crossing state now releases after actual aircraft/tug physical clearance, without requiring a stopped recovery to traverse the larger admission margin. Reroutes can retry a nearby connected native road at junctions; invalid routes retain the previous checked route and hold. Full native diagnostics are logged; radio refusals remain short. Only recovery's redundant tug approval readback is removed; ordinary towing readbacks remain.

Taxi-back outcomes now show the native green stand marker on the selected aircraft's main ADIRS, plus added windows with External ADIRS 0.1.5.

Requires framework 0.8.11 and enabled Tug Operations 0.9.0 with stand transfers enabled. Without that provider, the tug outcome setting is hidden and no tug breakdowns are generated.

After a rejection and pilot checks, the tug outcome reserves a free stand using the game's aircraft/airline/terminal compatibility rules. The pilot requests a tow using the same stand alias shown on the strip, aircraft label and ADIRS. Taxi, takeoff and lineup commands cannot restart the broken aircraft under its own power.

Approve with `CALLSIGN tow to stand B12`, optionally `via taxiway A B`, or use **Approve requested tow** in the F9 panel. The existing speech bridges handle the same command in Flexible AI and strict/legacy modes. Wrong destinations are refused. The native tug approaches the front wheel and must actually couple before movement begins. The checked route uses a forward native runway exit followed by the native stand route. Later runway crossings still need their own clearance. Hold/continue and route changes use the existing tow commands.

Cancel before approval withdraws the clearance but keeps the broken aircraft and its destination reservation. During movement it holds the tow; use continue tow to resume. Completion parks the aircraft at the reserved stand, preserves its callsign and schedule, and restores push/start readiness.

For a focused test, set taxi-back to 0% and tug recovery to 100%. These percentages share a 100% budget; the remaining percentage means retry departure. Developer mode's forced rejection affects only triggering, not the outcome. Restore preferred probabilities after testing.

Check:
1. Before approval, no movement; destination visible and another aircraft cannot claim it.
2. Taxi/takeoff refused; wrong stand refused; panel and voice approval work.
3. Actual tug coupling, runway vacating, holding and resuming, arrival parking and renewed push/start.
4. A later crossing needs fresh clearance; an unrelated aircraft can use the origin runway after the recovery has fully vacated.

First-version limits: forward connected runway exits only; no reverse recovery path or replacement stand when its reservation becomes invalid. Saving is blocked while any incident recovery request/tow is active, because rejected-takeoff incident save/load is not implemented yet. Normal stand-transfer save/load remains supported. Native approach/coupling and the selected-aircraft destination marker need live validation.
