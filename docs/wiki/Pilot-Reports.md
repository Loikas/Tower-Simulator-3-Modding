# Pilot Reports

Optional holding reports and ADIRS route display improvements. Categories cover runway/crossing and taxiway holds. Combined runway labels use spoken separators rather than saying a slash.

The ADIRS route can be dashed beyond the next hold. It follows native taxi routing, including updates after continuing from a taxiway hold. Added-window support uses the matching external components supplied with the build.

Tow-owned aircraft cooperate with Tug Operations to avoid duplicate holding reports. Tug holding calls depend on Pilot Reports being installed, enabled and the relevant crossing-report preference being enabled. Removing the report mod does not make Tug Operations invent those reports.

Push/start readiness blocking and startup speech buffering were removed. Pilot Reports should not add a new “not ready for push and start” requirement to normal aircraft.

Detailed ADIRS projection logging requires Developer mode. Rendering and functional data exchange with added displays remain available during normal play.
