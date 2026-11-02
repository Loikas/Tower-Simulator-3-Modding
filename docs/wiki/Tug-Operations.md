# Tug Operations

Tow aircraft between compatible stands, approve requests from parked aircraft, or collect a breakdown aircraft when Rejected Takeoffs is installed. Open the status/approval panel with **F9**.

Tow destinations use existing strips and destination displays. The aircraft label can show the stand in blue. Selected-aircraft ADIRS previews help choose a route. Airport stand aliases make names such as cargo gates easier to say.

## Addressed instructions

Use the aircraft's full callsign. Replace bracketed values with an actual stand, taxiway or runway.

```text
<callsign> tow to stand <stand>
<callsign> tow to stand <stand> via taxiway <X Y Z>
<callsign> hold tow
<callsign> continue tow
<callsign> cancel tow
<callsign> hold short of taxiway <X>
<callsign> cross runway <number>
<callsign> tow to stand <stand> cross runway <number>
<callsign> tow back to stand
```

The plain `<callsign> via X Y Z` shortcut is not a tow instruction. Use the explicit tow-to-stand form when changing a tow route.

Runway crossing clearance is separate from continuing a held tow. A route change while crossing applies to the part after that crossing. Repeated encounters with a runway need their own clearance. Native route validation still checks geometry and protection boundaries; a preview is not clearance.

## Settings and recovery

Automatic requests use a random minimum/maximum interval. Values cannot be negative and minimum cannot exceed maximum; equal bounds give a fixed interval, maximum zero disables automatic requests. A separate limit controls concurrent jobs.

Tow-only aircraft refuse taxi instructions with a tow-specific response. Normal pushback aircraft do not become tow aircraft merely because a tow instruction is issued. A requested destination must match the approved stand.

Save/load support preserves supported tow requests and job states. Moving jobs resume movement; deliberate holds remain holds. Engine audio is muted while the aircraft is being towed. Post-incident maintenance belongs to Rejected Takeoffs, rather than every ordinary tow.


## Shared command bridge (0.13.0)

Requires Framework 0.8.21. All tow voice and addressed-radio commands now use the framework command provider, including stand/via/hold-short arguments, crossing clearance and cancellation. Tow parsing, stand aliases, callsign recovery and existing phrase scope remain owned by Tug Operations. The old independent tow recognition/grammar hooks have been removed. Pilot replies use the shared radio service with abbreviated display text and the native spoken callsign.

For in-game validation, test a parked tow request and a rejected-takeoff recovery with tow-to-stand, a valid via route, hold/continue/cancel, taxiway holds and crossing clearance, including a combined tow/cross command. Repeat in Flexible AI, Fast and strict and Fast and strict Legacy. Confirm ordinary aircraft still accept native taxi/crossing commands and only one response plays per tow command. Existing saves use the same tow data format.


### Taxi/tow recognition recovery (0.13.1)

If speech recognition substitutes taxi for tow, a full callsign matching an aircraft already requesting or receiving a tow can recover `taxi to stand/gate/parking` as a tow instruction. Via taxiways, hold-short constraints and combined crossing clearance are retained and pass the existing tow validation. Hold/continue/resume/cancel taxi can also recover for those aircraft. Tow remains the intended spoken phrase.

Normal aircraft, missing/unknown/ambiguous callsigns and taxi-to-runway instructions remain native. Ownership is checked again before recovered speech dispatch. The same scoped recovery handles addressed instructions delivered directly by strict recognition. This does not change base-game pronunciation or grammar.


### Failed initial route checks (0.13.2)

A failed or over-complex initial route calculation retains the tow request, requested stand and strip so a revised clearance can be issued. The request gets a fresh five-minute simulation-time retry window without extending the underlying scheduled departure. Planner time limits remain in place to avoid long stalls. Explicit cancellation and normal request expiry still retire the request.

The initial native route watchdog allows a live worker up to five seconds total, shared across retries. Completed results are validated even if the next game update is delayed. A watchdog expiry reports a timeout rather than asserting that the route is too complex. One diagnostic record at timeout captures the requested source/vias and native search counters for investigation.


### Completion points (0.13.3)

A successfully parked transfer, breakdown recovery tow or original-stand return earns the game's current positive-arrival reward amount in native score points. Session/all-time totals update through the game's score API. This does not count as an arrival or departure; their normal native rewards remain unchanged. Cancelled, incomplete or failed route checks earn nothing. Points are added at physical completion, once per operation, without radio chatter about scoring. Existing active-tow saves retain the same format and receive their reward when parking finishes after load.


### Native route search across runways (0.13.4)

The read-only native planner receives the connected route's intermediate runway roads as internal routing hints as well as the taxiways requested by the controller. This avoids native rules excluding a crossing from an otherwise valid topology route. These hints grant no movement permission: tow runway holds and explicit crossing clearances still apply. User-visible route/readbacks contain the requested taxiways, and the aircraft's native via list remains unchanged during the check. The five-second background-worker limit remains in place.


### Strict recognition isolation (0.13.5)

Tow recognition adds a compact set of basic phrases, using the native VIA, hold-short and crossing clauses instead of enumerating every combined wording. Airborne aircraft are outside tow dispatch. A stray tow word or incomplete tow fragment does not claim instructions for unrelated aircraft. Raw speech must uniquely address an eligible ground aircraft; existing requested/active tow recovery stays scoped to its owner. After updating, restart recognition by launching the game again and retest contact departure plus tow route/hold/cross commands in both strict modes.


### Compound taxiways and short crossing curves (0.13.6)

Via and hold-short speech uses the airport's actual compound taxiway names. For example, Charlie Yankee or C Y resolves to CY when CY is defined; a missing CY is not invented by joining unrelated taxiways. Explicit compact identifiers remain supported.

A short native steering arc may briefly align with the runway while crossing. The route check allows up to 40 units of centreline alignment (or the previous wider-runway limit) only when a bounded encounter enters/exits opposite sides and has limited longitudinal displacement. Sustained runway travel and same-side incursions remain refused. Full tow protection and crossing permissions are unchanged. Route failures retain the request for revision.


### Route-guided native calculation (0.13.7)

Tow route checks use the connected topology path to constrain a private native calculation to its exact physical crossing sequence. Native turn generation still builds the route geometry, which then passes the usual parking, via and runway safety checks. Unrelated/repeated branches are removed from this private search; normal aircraft routing and the live road network are not changed. A 4,096-step limit and the five-second deadline bound unproductive work. A connected topology preview alone is still not permission to move or proof that every turn is drivable.


### Native scheduler correction (0.13.8)

Corrects a 0.13.7 regression in the private route worker. The native algorithm advances route nodes and starts queued branches in separate methods; both phases now run in native order. The prior worker could stall while waiting on branches it never started. Route guidance, native geometry validation and safety checks remain in place.

### 0.13.9 native stand exits

The private route checker aligns its corridor with the initial exits offered by the native planner. If the first preview exit is unavailable, it recalculates using eligible native exits while retaining requested taxiways. Native geometry and runway protection still validate the result. Failure details stay in the log rather than pilot radio. Live verification remains pending.

### 0.13.10 native route worker

Tow route checks run the game's complete native taxi RouteCalculator. The experimental custom corridor scheduler has been removed. Calculations use a separate instruction list, including an explicitly empty list when no taxiways are requested, without changing the aircraft's live taxi instructions. Runway safety checks and controller crossing permissions remain separate. Airport route acceptance still needs live verification.

### 0.13.11 runway routing and compact identifiers

If a native result uses a runway as a longitudinal shortcut, the checker makes one native retry with the connected taxiway sequence. Runway protection and crossing clearance remain required; the original user instruction stays unchanged. Pilot route text keeps compound taxiways together, such as CY and BB.

### 0.13.12 local runway hints

The native retry uses only the taxiways at runway entry and exit, in addition to the controller's requested route. It no longer adds the whole preview itinerary. This single retry has its own five-second calculation budget, with a ten-second total cap; ordinary checks retain their five-second limit.

### 0.13.13 native taxi inputs

The native planner receives only the controller's requested taxiways. The mod no longer inserts a runway as a mandatory route waypoint when a taxiway crosses it. Geometric runway protection and crossing clearance still apply to the returned route. The experimental runway-hint retries have been removed.

### 0.13.14 long-route headroom

Automatic requests retain a6000-unit topology-preview cap. Checked native routes allow up to12000units so native turns and instructed taxiways can extend a preview near that limit. Existing geometry and runway checks remain active.
