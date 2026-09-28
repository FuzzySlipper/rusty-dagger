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
