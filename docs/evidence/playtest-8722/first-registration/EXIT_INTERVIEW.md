# Dagger task 8722 — Luna first exit interview

## Outcome

- **Primary outcome:** `infrastructure_error` / blocked before gameplay. The ordinary entry UI worked, but live adaptive discovery was unavailable, so the requested control and gameplay mission could not be exercised honestly.
- **Profile/session:** `dagger-adaptive`, `efd1f82d-bee0-43c5-b944-5d3a4c254f7d`, slot-1.
- **Host:** `http://127.0.0.1:4473/`, owned by the parent and left running.
- **Browser duration:** approximately two minutes from browser creation to cleanup; the 12-minute gameplay ceiling was not reached because the integration blocker appeared first.
- **Cleanup:** browser stop succeeded with `browser_closed: true`, `released: true`; post-stop status is `stopped`.

## What was observed

1. The original entry screenshot showed a mostly black title screen with a broken-image icon/text reading `Rusty Dagger`, a visible `Begin` button, and `Menu · Esc`.
2. The visible `Begin` control was activated through an ordinary absolute pointer click at its inspected screen position. This advanced the product into the intro sequence.
3. Ordinary Space input advanced the intro through book and cinematic frames. The last retained frame shows two torch-lit characters; no gameplay HUD or controllable world was reached before the blocker stop.
4. `playtest assist ... {"op":"discover"}` returned:

   `playtest: page.evaluate: Error: playtest.help is not registered on a live module.`

   This is the observed integration failure. The parent supplied a reviewer diagnosis that the host declares `IEngineProduct` but is missing `IDebugCommandModuleSource`; that diagnosis was not independently inspected here.
5. Because live help/binding discovery was unavailable, I did not guess current gameplay bindings, select action-driven time, attempt door/encounter/loot actions, or exercise the requested Attack=KeyQ rebind. The parent then instructed me to pause actions and stop the browser while the profile is refreshed.

## Per-feature verdicts

- **Entry UI:** pass; `Begin` was visible and responded to ordinary pointer input.
- **Intro progression:** pass for the exercised Space input; the cinematic advanced and produced new original frames.
- **Live assist discovery:** blocked; `playtest.help` was not registered.
- **Current controls/action-driven mode:** unverified because discovery failed before a live product module was available.
- **Door/encounter/loot/combat:** untested by design after the blocker; no gameplay state was mutated.
- **Attack rebind to KeyQ:** untested; no safe authoritative control surface was available.

## Friction and suggestions

### Gameplay

1. Expose a clear transition from the intro cinematic to the playable scene, including an observable ready state and current bindings.
2. Keep the entry/title art fallback visibly intentional when the expected logo asset is unavailable.
3. Surface a recoverable in-product message when the runtime adapter is not ready for gameplay inspection.

### Tools

1. Make `discover` return a structured capability-unavailable receipt with the missing module/interface and next safe action.
2. Keep the browser console error, assist operation, and rejected runtime request correlated in one evidence receipt.
3. Allow read-only control discovery independently of the debug-module registration when the product advertises ordinary bindings.

### Telemetry

1. Publish registration/readiness facts for `playtest.help` and the live adapter before action-driven testing begins.
2. Include the operation name and response body for rejected debug requests; the current error is only a `page.evaluate` string.
3. Distinguish title/intro/cinematic state from playable-world state in readiness telemetry.

## Evidence

All files are under `/tmp/dagger-8722-luna-first/`:

- `initial-entry.png`, `initial-entry.json`: original title scene.
- `entry-inspect.json`: inspected visible `Begin` target and controls.
- `entry-click-begin.json`: ordinary pointer activation receipt.
- `after-begin.png`, `after-begin.json`: first intro/book frame.
- `intro-space-1.png`, `intro-space-2.json`, `intro-space-3.json`, `intro-space-4.json`: ordinary Space progression evidence.
- `intro-cinematic-blocker.png`, `intro-cinematic-blocker.json`: last retained cinematic frame before stopping.
- `discover-initial.json`: failed live discovery receipt (`playtest.help` unregistered).
- `stop-receipt.json`, `post-stop-status.json`: browser cleanup receipts.

The original browser artifacts remain at:

`/home/agent/.local/state/crew-playtest-local/browser/efd1f82d-bee0-43c5-b944-5d3a4c254f7d/`

## Cleanup

Only the owned browser session was stopped. The native host on port 4473 remains running for the parent’s refreshed profile.
