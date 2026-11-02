# Performance and diagnostics

Normal play should not require Developer mode. Use the launcher checkbox when testing a developer option or collecting diagnostics. The preference is under `%LOCALAPPDATA%/TS3Modding/preferences/developer-mode.txt`; cached readers can take up to two seconds to notice changes.

## What Developer mode enables

- Framework performance sampling and 30-second `performance-latest.txt` summaries.
- Periodic observation/status diagnostic exports.
- Strip preview/status CSVs.
- Detailed tug inventory/transition/failed-route geometry traces.
- ADIRS projection diagnostics and the rejected-takeoff force-abort control.

Functional display data, help metadata, save/load, basic warnings/errors and safety-critical movement audits remain available without it. Do not gate a required gameplay bridge as if it were diagnostic logging.

The starter log defaults off and checks Developer mode before constructing messages. Its count shortcut logs once on user input and does not run a background sampler.

## Diagnose a stutter

Compare the same save, camera and traffic state. Change one feature at a time. A once-per-second pause suggests periodic work; it does not identify a particular mod by itself.

Frame intervals include native simulation, rendering, loading and focus/pause delays. They are not mod CPU time. Nested timing totals overlap; do not add parents and children. GC counts are process-wide and do not identify allocation ownership. Standalone Camera/Effects work is not individually covered by framework module timing.

The tug optimization uses one scan-scoped association index instead of repeated full-airport searches. Issued operations still validate fresh state; an observation cache is not command authority.

Avoid global scene searches, reflection/geometry rebuilds, formatting and file writes every frame. Gate expensive diagnostics before computing their data, not just before writing the log.
