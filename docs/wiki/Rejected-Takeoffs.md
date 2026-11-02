# Rejected Takeoffs

Add occasional early rejected takeoffs. The trigger varies with aircraft takeoff speed and available stopping distance; it is not a fixed 35-knot limit.

After rejecting, the pilot stops and performs checks. The duration varies around its configured center: 75–125%, sampled once per incident. Taxi instructions are refused while checks are still in progress.

## Outcomes

- Retry departure when native runway conditions allow it.
- Taxi back under the aircraft's own power to a free compatible stand.
- Request tug recovery when Tug Operations is available.

The taxi-back destination uses the stand marker. Breakdown/tug settings are hidden when the optional tug provider is unavailable. Outcome percentages share a budget; they cannot both be set to 100%.

After returning to a stand, the aircraft shuts down for maintenance, clears its old runway assignment and can later return to native departure readiness. Defaults are 15–45 simulation minutes after taxi-back and 60–180 after breakdown recovery. These settings do not turn ordinary stand transfers into maintenance.

Save/load covers checks, taxi-back, tug approach/towing and maintenance. Preserve a save from before the incident when validating unfamiliar airports.

The force-abort testing option requires **Developer mode**. Normal incidence settings do not require it. Rejected Takeoffs is separate from future landing-incident ideas.
