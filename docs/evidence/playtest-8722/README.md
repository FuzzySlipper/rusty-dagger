# Dagger #8722 playtest evidence

## Early trials

These reports are original tester observations; later diagnosis is recorded here.
The playtester role uses GPT-5.6 Luna at max reasoning. Each tester owned its
browser; the parent owned a separate native host and persistence directory.

- `first-registration`: initial host omitted `IDebugCommandModuleSource`; discovery
  failed. Fixed and covered by the Host registration/restart regression test.
- `second-demand-step`: live commands discovered; the tester entered gameplay,
  turned from the entry wall into the chamber and submitted ordinary inputs.
  Movement, recovery and rebind effects stayed frozen. Engine manual advancement
  incorrectly delivered Demand mode to a Realtime product. Fixed upstream in
  `58f6316dad11e3071f220d5d34b2cf985a6f19d0`, with a runtime callback regression.
  The apparent stale readout was the actual product state: Engine counters
  advanced while Dagger correctly ignored those demand updates.
- `final-interrupted`: a second tester was interrupted by the parent during entry
  after the above Engine bug was found. Its Title state is not evidence of a
  separate confirmed entry failure. A corrected-runtime trial follows.

Original temporary receipts remain under `/tmp/dagger-8722-luna-*`; selected
original captures and useful receipts are retained here. Reports distinguish
transport submission from observed gameplay effects and do not claim a clear.

## Source verification and review

- Focused playtest/combat suite: 46 passed; covers live saved bindings and speed,
  resource refusal without spending, inspection state preservation and gesture rebasing.
- Host suite: 44 passed, including registration and callbacks following restart.
- Kit: 140 passed; workbench: 24 passed; import: 606 passed; architecture: 7 passed.
- Ruleset final run: 1,249 passed with one known baseline assertion excluded.
  The complete initial suite failed only
  `Selected_RDB_door_visuals_share_their_runtime_pose_and_keep_the_appearance_snapshot_unique`
  (expected 65 meshes, actual 66). A clean archive of the original checkout using
  its original SDK pin reproduced that failure. It is outside this change.
- CoreCLR release staging passed. The three persistent reviewer lanes (Engine
  reuse, product reuse and runtime trust) passed after the recorded fixes.
- Engine manual-time regression and full runtime library suite: 43 passed.
  Engine reuse review also passed the upstream mode correction.

Engine pointer-metadata commit `e14db30ba217ee05942a79b140baca7dc79f9daa`
has passing C# and docs GitHub jobs. Den gate 3736 is failed because the initial
request incorrectly required an unscheduled render job; changing the required
list did not reopen that terminal gate. This is not reported as a passed gate.
The actual runtime correction `58f6316d` has its own gate 3738.

No clean warning delta is claimed: original browser receipts include existing
inventory-art and GPU readback warnings, without a compatible baseline run.

## Corrected runtime adoption

Published and adopted pair `0.1.0-dev.58f6316dad11` through the ordinary immutable
pair publisher and `scripts/update-engine-pin.sh`. CoreCLR release staging and
all three focused playtest adapter tests pass against this pair.

Gate 3738 was read from Den after completion: **passed**, with Rust, C# and docs
checks successful for exact SHA `58f6316dad11e3071f220d5d34b2cf985a6f19d0`.
The authoritative receipt is retained in `engine-gate-3738.json`.
## Corrected runtime trials

Both independent GPT-5.6 Luna testers used ordinary installed tools in separate
native worlds on ports 4473 and 4474. Original interviews and selected receipts
and screenshots are retained in `corrected-2` and `corrected-3`.

- Trial 2 moved 2.05 units in its first 500ms forward action, approached rat 2008
  from 9.90 to 1.46 units, and confirmed visibility and attack selection. Its first
  attack produced a fresh `NoTargetInReach` refusal. Subsequent attacks remained
  blocked by animation recovery. It died with zero kills; no door or loot result.
  Wallclock was 12m44s, exceeding its 12-minute limit by 44s.
- Trial 3 moved 2.88 units in its first 700ms action and approached the rat and imp.
  It saved Attack=KeyQ through ordinary settings, confirmed visually and in live
  controls. It stopped alive at 20/31 health, with no confirmed kill, door or loot.
- **Correction to trial 3's interview:** its claimed new Q no-target result is
  unsupported. Before/after observations retain combat sequence 2 and observed
  step 13891. The HUD was historical. Binding persistence and physical input
  transport are established; a fresh Q gameplay outcome was inconclusive.
- **Correction to both diagnoses:** `attackReadyAtStep` is a cooldown threshold,
  not a continuously changing readiness value. Passing it did not clear a separate
  presentation guard. The live stall exposed a pre-existing Dagger one-shot
  completion bug, rather than a failure of Engine time admission.

## Attack recovery fix

Engine returns an advancing completed receipt once, followed by completed
receipts that no longer advance. Dagger previously required another advancing
receipt before returning the weapon to idle, permanently blocking later attacks.
The fix records the first advancing completion and returns to idle on the next
completed receipt. It preserves the final animation frame for one outer update
and uses the existing Engine playback contract. Preflight now distinguishes
`attack-animation-active` from `attack-cooldown`.

The exact two-receipt regression and 62 related weapon/playtest tests pass;
CoreCLR release staging passes. All three persistent review lanes passed this
narrow follow-up. The prior guard against an initial non-advancing completion
remains tested.

## Follow-up priorities

1. Connect shared spatial probes/clearance to Dagger's full controller environment
   before claiming traversable routes. Loaded target coordinates helped approach
   enemies, but did not establish door access.
2. Make cinematic continuation easier to discover and budget separately from
   gameplay; enforce tester wallclock deadlines more strictly.
3. Keep fresh combat receipts beside screenshots. Historical HUD text must not
   be interpreted as a new action result; saved bindings alone do not prove hits.

These trials establish useful adaptive exploration and expose gameplay defects.
They do not establish a level clear, enemy defeat, door opening or looting.

## Short recovery regression trial

A final GPT-5.6 Luna trial on the recovery fix used the persisted KeyQ binding,
ordinary keyboard input and bounded action-driven advancement while looking away
from enemies. The first swing produced combat sequence 2 at observed step 15562
(`NoTargetInReach`). After a bounded recovery window the animation refusal cleared.
The second swing produced a **new** sequence 4 at observed step 15619 with the same
expected no-target outcome. This verifies repeated saved-key attack delivery and
recovery without relying on historical HUD text. No hit or kill is claimed.
Selected original receipts and the final interview are retained in `recovery`.

The short trial used about six minutes wallclock and 2m12s after Playing. All
three tester browser sessions were stopped and released. The parent stopped its
isolated native hosts and removed only the two temporary profiles via live
reload. Existing Dagger/Doom hosts and unrelated repository edits were preserved.
