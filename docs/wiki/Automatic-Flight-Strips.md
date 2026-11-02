# Automatic Flight Strips

Move existing strips according to the matched aircraft's runway, flight progress and recorded completion. The mod follows each board's configured links; board identity and exact aircraft matching prevent unrelated moves.

## Normal behavior

- Arrival runway routing waits for the actual final report. Changing the final runway updates routing.
- Ground and airborne departure phases advance to configured bays.
- Recorded parking moves completed arrivals to the bin.
- Accepted departure handoffs move departure strips to the bin, including supported altitude-conditioned contact instructions.
- Framework 0.8.19 also records the native missed-departure automatic handoff. Forgetting contact departure can therefore retire the strip when the native game hands the aircraft over. The game's penalty remains unchanged.

A missing aircraft alone is not completion evidence. Callsign, network ID, scene, board and observation lifetime checks still apply. Old orphan strips from before an observed completion are not binned merely because no aircraft is found.

## If a move fails

An exception in one strip's operation holds that strip individually instead of disabling all later strips. Other aircraft continue to be processed. Inspect the warning and board state before manually correcting a held strip; an uncertain native mutation is not retried blindly.

Use the settings to enable automatic movement, runway assignment routing and completed-flight binning, and to choose ground/departure headers. Heavy preview/status CSV exports require [Developer mode](Performance-and-Diagnostics.md).

The latest missed-handoff and isolation changes have automated/native-profile coverage. In-game validation should include a missed handoff followed by a normal departure handoff.
