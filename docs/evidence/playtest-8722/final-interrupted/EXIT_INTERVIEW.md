# Dagger task 8722 final-adapter playtest exit interview

## Outcome

- **Classification:** `infrastructure_error` / blocked before gameplay entry. The browser and profile launched, but the ordinary Begin/opening transition did not reach `Playing`.
- **Profile:** `dagger-adaptive-final`, URL `http://127.0.0.1:4474/`, service `127.0.0.1:48200`, slot-1.
- **Session:** `deed203c-8d1b-4770-9e0f-7237444637a2`.
- **Runtime pair:** `e14db30ba217` (supplied task metadata).
- **Model identity:** Luna worker; the playtest service/session metadata exposed no separate model-name field.
- **Duration:** 2026-09-28 08:00:34.914Z to 08:03:06.157Z, about 2m31s wallclock; 0 minutes of actual `Playing` gameplay.
- **Cleanup:** my browser closed and the slot was released. The parent-owned native host was not stopped or changed.

## Visible sequence

The initial original screenshot showed a dark title screen with a centered Daggerfall cover image, a visible `Begin` button, and `Menu · Esc` at top right: [initial.png](/tmp/dagger-8722-luna-final/initial.png). I inspected the visible DOM and used the ordinary `.dagger-entry-begin` button. The next original capture showed the opening book/cinematic (“The Elder Scrolls: Chapter II”): [after-begin.png](/tmp/dagger-8722-luna-final/after-begin.png).

After waiting and observing, the screen returned to the title with `Begin` still visible. I clicked the same visible Begin button once more and captured immediately; it still showed the title screen: [begin-2-immediate.png](/tmp/dagger-8722-luna-final/begin-2-immediate.png). The final original capture was also the title screen: [final.png](/tmp/dagger-8722-luna-final/final.png).

This is a neutral visual observation. I did not infer that a playable map or actor view was accepted merely because the page and query layer were live.

## Diagnostics and controls

`playtest assist discover` succeeded. It reported ordinary product actions `forward`, `back`, `left`, `right`, `attack`, `use`, `jump`, `toggle-weapon`, `inventory`, `character`, and `menu`; keyboard controls included WASD, Primary attack, `F` interact, `Z` weapon, `I` inventory, and Escape menu. It also reported `lookAdvancesTime:false`, action-driven/manual/realtime time modes, and `playtest.observe`.

I set action-driven time once. The receipt returned `advancedMs:0`, `fixedStepHz:60`, `worldHeld:true`, and simulation step `7809`. No gameplay input, free look, target approach, door use, attack, loot pickup, or KeyQ rebinding was attempted after the runtime blocker was identified.

The final live product observation returned:

- `mode: Title`
- `simulationStep: 0`
- `attackUnavailableReason: mode-Title; use ordinary UI`
- `lastCombatEvent: null`
- `lastInteractionRefusal: null`
- `lastUseStep: null`
- `lastOutcome: ""`
- `dead: false`
- equipment `long-blade`, health 31, magicka 100, stamina 5568
- player position `(28.375, 38.975002, -12.400001)`, viewpoint `(28.375, 39.725002, -12.400001)`, yaw `180.000005`, pitch `0`
- visible query targets included alive actors and doors, but every door had `focusReason: OutsideQuery`, `visibility: Unknown`, and the observation explicitly said routes are unavailable and loaded positions are not traversable routes.

The observation contains product-facing diagnostics even while the mode is Title, but these did not establish gameplay entry. No target was selected and no action effect was observed.

## Blocker assessment

The direct product blocker is the ordinary title-to-playing transition: the visible Begin click briefly produced the opening-book image, then the browser returned to Title with Begin still visible. A second ordinary click did not leave Title. The final observe explicitly refused attack because the product was still in Title mode.

The parent also reported a shared runtime issue from the parallel Dagger trial: manual advance calls `update_admitted(DEMAND_UPDATE_MODE)`, while Dagger accepts only realtime fixed-step facts. That can advance a simulation counter without advancing gameplay or input. I did not call a manual advance in this session; I retain this as parent-observed runtime context, separate from the visible Title-state evidence.

## Feature classification

| Feature | Result | Evidence |
|---|---|---|
| Profile/service launch | Pass operationally | `start.json`: HTTP ready, Chromium session, browser capabilities present. |
| Initial visual capture | Pass | Original title screenshot opened and retained. |
| Ordinary Begin UI | Uncertain / blocked | First click showed cinematic, then Title returned; second click did not reach Playing. |
| Opening cinematic | Observed | Original book/cinematic capture retained. |
| Action-driven time setup | Pass as harness operation | Returned `advancedMs:0`, 60 Hz, world held. It did not establish product gameplay. |
| Bindings/discovery | Pass | `discover-title-cinematic.json` lists actions and bindings. |
| Free look / named-target approach | Not tested | Blocked before Playing; no target route was invented from loaded positions. |
| Door interaction | Not tested | No ordinary Playing state or in-reach named door. |
| Combat | Not tested | `attackUnavailableReason` was Title mode. |
| Loot/inventory | Not tested | No gameplay UI entered. KeyQ attack rebind was not attempted. |
| Cleanup | Pass | `stop.json` reports browser closed and released. |

## Friction and suggestions

### Runtime / harness

1. Make the title-to-playing transition produce a durable state receipt after an ordinary Begin click, including cinematic phase and the next permitted control. Here the visual cinematic appeared, but the product returned to Title without an exposed reason.
2. Keep runtime admission contracts aligned: the parent-observed manual-advance `DEMAND_UPDATE_MODE` mismatch can produce a moving simulation counter without gameplay/input progress. Surface an explicit refusal instead of a successful-looking advance when the product runtime rejects the fact.
3. Keep `mode`, `simulationStep`, and action receipts correlated. In this run the time receipt reported step 7809 while the later Title observation reported step 0, which is useful diagnostic evidence but confusing without a generation/phase explanation.
4. Preserve a supported ordinary “continue opening sequence” control in the visible UI or expose its current phase through the normal product observation. Do not require an observer to guess between clicking Begin, waiting, and keyboard input.

### Gameplay / product

1. The Title screen gives a clear Begin button and the cinematic is visually present, but the transition did not expose whether the click was accepted, whether the cinematic finished, or whether a second click was required.
2. The interaction query describes doors and actors while the mode is Title, but marks them `OutsideQuery`/`Unknown` and explicitly says routes are unavailable. Keeping Title-mode targets out of gameplay guidance would reduce misleading surface area.
3. The inventory art warning appeared in the browser console (`inventory frame art is not published by this session`); this did not prevent title rendering but should be tracked separately from the runtime blocker.

## Evidence and receipts

- Profile and session: [profile.json](/tmp/dagger-8722-luna-final/profile.json), [start.json](/tmp/dagger-8722-luna-final/start.json)
- Initial/cinematic/final original captures: [initial-capture.json](/tmp/dagger-8722-luna-final/initial-capture.json), [after-begin-capture.json](/tmp/dagger-8722-luna-final/after-begin-capture.json), [final-capture.json](/tmp/dagger-8722-luna-final/final-capture.json)
- DOM and capability receipts: [inspect-title.json](/tmp/dagger-8722-luna-final/inspect-title.json), [discover-title-cinematic.json](/tmp/dagger-8722-luna-final/discover-title-cinematic.json), [time-action-driven.json](/tmp/dagger-8722-luna-final/time-action-driven.json)
- Product observation: [final-observe.json](/tmp/dagger-8722-luna-final/final-observe.json)
- Event journal: [events-final.jsonl](/tmp/dagger-8722-luna-final/events-final.jsonl)
- Cleanup: [stop.json](/tmp/dagger-8722-luna-final/stop.json)
