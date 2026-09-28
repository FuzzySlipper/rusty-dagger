# Dagger task 8722 — focused attack-recovery follow-up

## Result

The corrected one-shot completion is verified for two successive ordinary saved-key attacks. Both were performed while looking away from the nearest hostile actors, so both produced fresh in-game no-target outcomes. The first event was sequence `2` at observed step `15562`; after a bounded recovery wait, the second event was sequence `4` at observed step `15619`. The second observation also reported the new attack animation recovery window. These sequence/step changes establish fresh results rather than a stale HUD string.

- Configured model identity: **GPT-5.6 Luna max**.
- Profile: `dagger-adaptive`; session `7e04e5b2-6dd4-46a9-b157-c7ad7ed8da17`.
- URL: `http://127.0.0.1:4473/`.
- Browser wall duration: approximately **5m 59.7s**, from `08:38:37.439Z` to browser release `08:44:37.123Z`.
- Gameplay duration: approximately **2m 11.6s**, from `Playing` observation `08:42:25.490Z` to release; within the 3-minute gameplay bound.
- Final player state: `Playing`, alive, health `31/31`, simulation step `15633`, position `(28.375, 39.301, -12.400001)`, yaw approximately `0`, grounded.
- No enemy damage, kill, loot, or movement was required for this focused recovery check.

## Neutral visible evidence

`initial-entry.png` shows a dark title screen with the Daggerfall cover centered, a visible Begin button near the lower center, and Menu/Esc at the upper right. Begin led to a cinematic video. Realtime was retained through the entry flow; a visible center click on the cinematic progressed to the first-person game. `final-after-two-attacks.png` shows a stone chamber with timber ceiling beams, blue and green wall panels, a flying creature ahead, the blade at right, crosshair, HUD, and the visible historical text `No target in melee reach ...`.

## Action chronology and receipts

1. Started the fresh browser session and captured the original title screen: `start-status.json`, `initial-capture.json`, `initial-entry.png`, `initial-inspect.json`.
2. Clicked the visible Begin control and preserved the cinematic state: `click-begin.json`, `after-begin-capture.json`, `cinematic-wait-1.json`, `cinematic-inspect-1.json`, `observe-cinematic-1.json`, `after-wait-20.json`.
3. Used ordinary realtime input/clicks only during the cinematic. `click-cinematic-center.json` produced the transition; `observe-after-cinematic-click.json` reported `mode: Playing`, step `15083`, and the live player pose.
4. Switched to action-driven time only after `Playing`: `time-action-driven.json`, `observe-playing-entry.json`. The persisted binding was live as `controls.attack: ["KeyQ"]`.
5. Turned away from loaded hostiles with `look-away-first.json`. Entry target data in `targets-entry.json` placed the nearest hostile rat about `9.90` away and imp about `13.36` away; no target was in melee reach.
6. Sent ordinary saved-key Q: `attack1-raw-keyq.json`, then advanced `250 ms` with `attack1-advance.json`. `observe-after-attack1.json` reported `AttackRejected:NoTargetInReach`, sequence `2`, observed step `15562`, and `attack-animation-active` with ready step `15607`.
7. Waited `700 ms`: `wait-after-attack1.json`; `observe-recovery-window.json` reported step `15618`, ready step `15607`, `attackUnavailableReason: null`, and retained event sequence `2`/step `15562`.
8. Sent ordinary saved-key Q again: `attack2-raw-keyq.json`, advanced `250 ms` with `attack2-advance.json`. `observe-after-attack2.json` reported a fresh `AttackRejected:NoTargetInReach`, sequence `4`, observed step `15619`, and a new `attack-animation-active` window with ready step `15664`.
9. Captured the final original frame and stopped only the owned browser: `final-capture.json`, `final-after-two-attacks.png`, `stop-receipt.json`, `post-stop-status.json`. The stop receipt reports `browser_closed: true` and `released: true`; post-stop status reports `phase: stopped`.

## Focused verdict

- **Saved KeyQ binding:** Pass. The fresh run’s live observation reported `controls.attack: ["KeyQ"]` before the attacks.
- **First ordinary attack:** Pass for delivery and fresh product outcome. Event sequence `2` / observed step `15562`; refusal was `AttackRejected:NoTargetInReach`.
- **Recovery inspection:** Pass. After the bounded wait, the attack became available (`attackUnavailableReason: null`) once ready step `15607` was passed.
- **Second ordinary attack:** Pass for repeated delivery and fresh product outcome. Event sequence advanced to `4` / observed step `15619`, with a new recovery window ready at step `15664`.
- **Transport/pointer-lock failure:** Not observed. Raw keyboard receipts completed; there were no page errors or transport refusal. This run did not require pointer-lock targeting.
- **Combat hit/kill:** Not tested in this focused check; both deliberate attacks were aimed away from enemies.

## Friction and suggestions

1. The cinematic remained visible while semantic observation still reported `Title`; a center click was needed to reach `Playing`. A clear skip/continue affordance would reduce entry ambiguity.
2. HUD no-target text is historical. The authoritative sequence and observed-step fields made it possible to distinguish the second refusal from the first; expose this fresh-event distinction directly in the ordinary input receipt.
3. Keep the corrected attack completion behavior, but report the recovery state and next-ready step alongside the result so callers can wait without guessing.
4. Keep target distance/hostility and combat-event sequence together in one compact observation for no-target checks.

## Assistance and constraints

Used the installed playtest CLI, original screenshot capture, browser inspection, visible Begin/cinematic UI, ordinary realtime click/keyboard input, and read-only `assist observe`, `targets`, `time`, `look`, and `advance`. No source inspection, custom game JSON, teleport, debug damage, state mutation, alternate harness, or product/configuration repair was used. The native host was left to the parent; only this browser session was stopped.

All evidence is retained under `/tmp/dagger-8722-luna-recovery/`.
