# Companion repo reuse survey (task 6519)

Date: 2026-08-02. One section per sibling repo: what exists, what to consume,
what to avoid, and the integration shape. "Consume" here means depend on as a
cargo crate / pnpm package / generated artifact, not copy-paste, unless
"vendoring" is called out.

## rusty-engine-demo (loading-bay)

**What exists**

- `rust/crates/loading-bay-game` — the full first-person game runtime:
  - `player.rs` — `PlayerControllerConfig/State/Component`,
    `ResolvedPlayerAction::{Move,Look}`, `PlayerControllerService::apply*` over
    `engine_spatial::KinematicMotionSystem` + `VoxelCollisionScene` (swept
    collision, blocked/moved facts). This is the FP controller, already Rust,
    already engine-shaped. ~320 lines, clean dependency surface (core-ids,
    core-math, engine-spatial, entity-state).
  - `door.rs`, `interaction.rs`, `hazard.rs`, `navigation.rs`, `combat.rs`,
    `inventory.rs`, `pickup.rs`, `encounter.rs` — game systems as engine
    components with typed facts.
  - `bin/browser-host.rs` — TCP session host projecting game state to the
    browser renderer (the live game loop + presentation).
  - `bin/studio-adapter.rs` + `project_store.rs` + `project_codec.rs` — the
    studio host and project admission used by scripts/check-adapter.py.
- `ts/packages/project-content` — TS generator of the demo's project docs
  (we chose to hand-roll ours instead: scripts/generate-project.py).
- `libs/ui-*` (demo): ui-game-panels, ui-compass, ui-combat-log, theme —
  mostly superseded by rusty-engine-ui (below).
- `docs/visual-content-pipeline.md` — the collision-model doctrine our
  project doc follows (voxel gameplayProxy = truth, mesh = visible content).

**Consume**: `loading-bay-game` crate for the FP controller and door system
(the exact code 6520/6525 need). The crate is product-flavored (Loading Bay
semantics in `combat`, `extraction_beacon`), so the boundary to keep: player
controller, doors, interaction, navigation, hazard, pickups — not
encounter/extraction/progression, which are loading-bay game design.
**Avoid**: the studio-adapter/browser-host binaries themselves (loading-bay
project semantics baked in), `enemy_combat`, `progression`, `extraction_beacon`.
**Integration**: cargo `path = "../rusty-engine-demo/rust/crates/loading-bay-game"`
(or git dep on FuzzySlipper/rusty-engine-demo pinned rev) for controller+doors
in the walk-through crate. The task-6520 implementation should verify whether
its public surface is usable without pulling loading-bay game semantics — if
not, extract player.rs/door.rs patterns into a rusty-dagger crate rather than
inherit the dependency.

## rusty-roguelike

**What exists**

- `libs/renderer` (Angular) — first-person dungeon-frame renderer component
  (camera pose with yawDegrees, dungeon-frame.ts), mounts a shared
  `@rusty-engine/renderer-host` RendererSurface; `dungeon-frame.spec.ts`
  proofs. Grid-locked to procgen floors.
- `libs/feature-game` — party sheet, loadout, minimap (grid roguelike domain).
- `libs/platform`, `protocol`, `store`, `transport` — generic shell plumbing.

**Consume**: the renderer component pattern (how an Angular app drives a
RendererSurface with an FP camera) as a **reference**, not a dependency —
it's grid-locked and roguelike-scoped (party, loadout). When rusty-dagger has
a browser app, copy the *shape* (component -> surface -> camera pose from
session state), not the code.
**Avoid**: everything party/combat/procgen — different game.
**Integration**: reference only.

## rusty-engine-ui

**What exists** (this is the UI kit the user meant)

- `libs/ui-*` presentational Angular widgets, each self-contained with local
  view models (no game types): ui-compass (bearing strip), ui-minimap
  (positioned markers), ui-hotbar, ui-character-status, ui-equipment,
  ui-inventory, ui-combat-log.
- `libs/feature-game-hud` (HUD composition), `feature-inventory`,
  `feature-main-menu`.
- `libs/theme`, `components`, `platform`, `renderer`, `shell`, `store`,
  `transport`, `protocol`, `domain` — app-shell plumbing.
- `apps/app` + `app-e2e` — a demo app showing the kit assembled.

**Consume**: `ui-compass` (automap-adjacent bearing display), `ui-minimap`
(automap task 6528 — its marker view model maps directly to block/door
markers), `theme`. These are dependency-clean (presentational, input-only).
**Avoid**: feature-* compositions that assume an RPG domain (character-status,
equipment, inventory, main-menu) until the successor project has those
concepts; `domain`, `protocol`, `store`, `transport` (their game loopback).
**Integration**: pnpm workspace deps `@rusty-engine/ui-*` when a rusty-dagger
browser app exists; until then, nothing to wire (render-check covers visuals).

## rusty-d20

**What exists**: d20 rules vocabulary/mechanics on the engine (stats, effects,
damage, restoration — per its README/Den description).

**Consume**: nothing now. If game-feel prototyping drifts toward stats/effects
(user's adjusted scope keeps this door open), reassess then — Daggerfall's own
attribute system (CLASS*.CFG exists in local/arena2) is a different vocabulary
and d20 is not a drop-in for it.
**Avoid**: pulling it in early "because RPG" — vocabulary mismatch risk.
**Integration**: revisit after the walk-through.

## rusty-view / rusty-roleplay

**What exists**: chat client kit + lore/memory service.

**Consume/avoid**: not relevant to this project. (Noted for completeness only.)

## rusty-engine (provider, not a companion — recorded for clarity)

Already consumed: `rusty-asset-import` CLI (content pipeline),
render-model/asset-catalog semantics via artifacts, studio adapter protocol,
renderer-three (via render-check). Upstream needs tracked as engine tasks
6515/6516 with local consume tasks 6521/6522.

## Decisions (for task 6520 implementation)

1. FP controller: **loading-bay-game's player controller** (`player.rs`) is
   the only real Rust FP controller in the fleet and is engine-shaped.
   Implementation starts by checking its surface against rusty-dagger's needs
   (Move/Look over VoxelCollisionScene); extract to a rusty-dagger crate if
   the loading-bay game-session coupling is heavier than vendoring is worth.
2. UI: **rusty-engine-ui** for compass/minimap/theme when the browser app
   arrives (task 6528 automap is the first consumer).
3. Project/content machinery: **hand-rolled** (scripts/generate-project.py) —
   already landed and adapter-verified; no change.
4. Collision doctrine: **loading-bay's** (voxel gameplayProxy truth + mesh
   visual), already applied in the 6518 project doc.
