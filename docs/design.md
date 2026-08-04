# Rusty Dagger design

Status: current model. Task state lives in Den (`rusty-dagger` project); this
document owns durable intent. When reality and this document disagree, fix the
document or the code, not neither.

## What this is

Rusty Dagger ports the **Privateer's Hold experience** — Daggerfall's starting
dungeon — and every system needed to support it into Rusty Engine, using the
original game's data files as the content source. It is the first stage of a
longer arc toward an original Daggerfall-ish game built on Rusty Engine.

It is explicitly **not** a port of Daggerfall. The classic game is a legible
design target and a content source, fitted into a rusty-engine-shaped demo.
Where the classic and the engine disagree, the engine's shape wins.

## The long arc and the successor pattern

The endpoint is an original game, not this repo. The working method to get
there is the **successor pattern**: instead of endlessly refactoring a project
that has accreted the wrong shape, a successor project is started fresh when
the time comes, carrying forward the hard-won lessons that already live in
working code. For human teams this is usually a bad trade; for agent-driven
development the economics invert — a fresh reinterpretation of the problem
space, grounded in proven components, repeatedly outperforms heroic
refactoring. This has held up across several projects in this fleet.

Consequences for how this repo is built:

1. **Systems, not features.** Every Daggerfall system (formats, textures,
   dungeon assembly, doors, lights, billboards, water, automap, enemies, …)
   lives behind a crate boundary that can be lifted into a successor project
   without dragging the whole demo along. Crates stay small enough that their
   public surface fits in one paragraph.
2. **No rush to the headline deliverable.** A fast path to "walk Privateer's
   Hold" that tangles systems together is a loss, not a win. The deliverable
   arrives when the systems are clean enough to keep.
3. **Lessons in code.** Parser edge cases, format gotchas, scale constants,
   and conversion rules are recorded in tests and docs/daggerfall-formats.md,
   not in conversation. The successor project inherits confidence, not
   archaeology.

## Why authentic Daggerfall content (and not greybox)

Mood is the thing being studied. The actual textures, geometry, lighting, and
layout of Privateer's Hold carry thirty-year-old intent about dungeon feel —
claustrophobia, texture rhythm, door placement, water. Porting the authentic
content first means mood experiments happen by tweaking real material directly
in rusty-engine, not by authoring inspired-by stand-ins and iterating twice.
When the successor project authors original content, the tweaked DF material
serves as the reference for what worked.

This is also why the pipeline preserves fidelity where it is cheap to do so:
the classic texture-table randomization, climate-based door textures, and
per-block water are all on the table rather than simplified away.

## What "the Privateer's Hold experience" covers

Spawn in the hold's flooded entrance chamber, read the dungeon by torchlight
and texture, open doors, find the way up and out. Concretely, the systems that
must exist:

- Authentic geometry and textures (done: extraction + textured GLB).
- First-person controller with triangle-accurate collision.
- Sliding action doors.
- Billboards (torches, furniture, markers) and point lights.
- Block water.
- Start marker spawn and a minimal automap.
- Studio/project integration so the whole thing is inspectable and editable.

Explicitly out of scope for this repo: the exterior world, other dungeons, and fast travel.
Those belong to the successor (or to companion repos when they already exist
there).

## Upstream posture

Rusty Engine is the provider; this repo is a consumer, same as
rusty-engine-demo. Work that belongs upstream is filed upstream rather than
patched locally — the demo doubles as a needs-discovery surface for the
engine. Both 2026-08-02 upstream needs have landed and are consumed here:

- rusty-engine task 6515 — UV vertex data through the static mesh pipeline.
  **Consumed (task 6521)**: mesh-json carries `uvs` + `materials[].texture`;
  the studio adapter projects `defineTexture` + protocol-14 `textureResources`
  so studio renders the textured dungeon matching the GLB.
- rusty-engine task 6516 — triangle-mesh collision policy for static mesh
  assets in svc-collision. **Consumed (task 6522)**: the collision authority is
  the dungeon static mesh (`collision: "trimesh"`); the gameplayProxy stopgap
  is retired (see the collision authority section below).

## Companion reuse

Don't rebuild what sibling repos already own. Current inventory (details in
task 6519 → docs/companion-reuse.md):

- **rusty-engine-demo** — loading-bay product: playerController implementation,
  ui-game-panels / ui-compass / ui-combat-log / theme libs, and the
  @rusty-engine-demo/project-content pipeline that generates studio-openable
  project documents.
- **rusty-roguelike** — first-person reference game on the engine (grid-based;
  assess controller/camera reuse).
- **rusty-engine-ui** — UI kit repo (inventory pending).
- **rusty-d20** — rules vocabulary; minimal expected use here.
- **rusty-view / rusty-roleplay** — chat/lore; not relevant.

## System map

Current crates and the direction they grow (enforced by the recurring
modularity gate, task 6529):

- `arena2` — pure, read-only readers of classic data files (BSA, MAPS, RDB,
  ARCH3D, TEXTURE, PAL, PAK). No game semantics, no rendering, no allocation
  of meaning beyond "what the bytes say". Everything learned about the formats
  is proven here by tests against the real data.
- `dagger-import` — CLI glue: drives arena2, assembles dungeon geometry and
  textures, emits GLB / engine mesh-json. Emitters (glb, png, meshjson) split
  out (→ `dagger-export`) if they outgrow the crate.
- `dagger-runtime` — Daggerfall-owned project admission, player controller,
  and real-project collision walkthrough. It consumes only generic Rusty
  Engine crates (git deps on the public repo); it does not import
  loading-bay-game.
- `dagger-studio-adapter` — Rust-owned protocol-14 read-only admission and
  render projection for the committed Privateer's Hold project. The adapter
  reuses `dagger-runtime`; it rejects mutations until a Dagger-owned authority
  exists.
- `scripts/studio-host.mjs` — bounded HTTP/static host for the Engine Studio
  app, adapter lifecycle, normalized host-file browsing, and atomic
  per-project user settings. It is transport/presentation glue, not gameplay
  authority. See `docs/studio-host.md` for the runnable contract.
- Planned: `dagger-content` (decoded materials/meshes with provenance),
  `dagger-world` (dungeon session runtime: blocks, doors, lights, water
  state), each arriving only when the code that needs a home exists.
- `engine-render-check/` — headless render proof through the real
  rusty-engine renderer (renderer-three browser surface, consumed from
  rusty-engine `main`): adapter protocol-14 readout -> vite page ->
  verified texture resources -> overview/interior/directional-enemy
  assertions + screenshots. This is the only render verification path —
  when the engine renderer lacks a capability, file an upstream task
  instead of building a side renderer.

### Modularity gate evaluation (task 6529, 2026-08-03)

Evaluated after billboards (6523) landed and the 6525 door attempt: **no
split is warranted yet.** Current sizes and public surfaces: `arena2` 1327
lines / 8 files (pure readers, one-paragraph purpose); `dagger-import` 1319
lines / 5 files (CLI glue + emitters); `dagger-runtime` 1917 lines / 6 files
(admission + controller + collision walkthrough); `dagger-studio-adapter` 961
lines / 1 file (protocol-14 boundary). Every crate's purpose still fits in one
paragraph and matches this map.

The design's "when to pull" conditions are not met: the emitters (`glb.rs`
229, `meshjson.rs` 183, `png.rs` 98) are each under the ~300-line
`dagger-export` threshold and cohesive inside `dagger-import`; no code yet
needs a `dagger-content` (decoded-materials) or `dagger-world` (session
runtime) home. Concrete triggers for the next gate, re-evaluated as features
land:

- A crate's public surface stops fitting in one paragraph (the recurring
  check), or any emitter crosses ~300 lines → split `dagger-export`.
- Doors (6525), water (6526), or enemies (6595) introduce shared block/session
  state that two or more crates need → that state is the seed of
  `dagger-world` (block layout, door/light/water/enemy session objects).
- A second consumer (beyond `dagger-import`) needs decoded material/mesh data
  with provenance → that data is the seed of `dagger-content`.

## Collision authority: the dungeon trimesh (tasks 6563, 6522)

Upstream triangle-mesh collision landed in rusty-engine (task 6516,
`MeshCollisionPolicy::Trimesh`) and is now consumed here (task 6522). The
hidden `gameplayProxy` material-voxel stopgap is **retired** — the collision
authority is the dungeon static mesh itself:

- `dagger-import --format mesh-json` emits `collision: "trimesh"` → the
  imported static-mesh artifact carries `collision.kind: "trimesh"`.
- `dagger-runtime` admission decodes the mesh's full inline triangle payload
  (floors, walls, ceilings, ramps) into a `StaticMeshColliderAsset` and
  registers one instance at identity via `replace_static_mesh_colliders`. The
  scene carries no `voxelEnvironment`; the legacy rasterizer (and its
  wall/underside limitations) is gone. `svc-collision` (parry3d) is the sole
  collision authority — the kinematic sweep blocks on real geometry with no
  controller changes.
- `voxelEnvironment` is accepted as an optional *additive* authority (used by
  the adversarial controller probes) but is not required; a project with
  neither a trimesh mesh nor any voxels fails closed.
- The Daggerfall-owned controller keeps its `fallSpeedUnitsPerSecond` /
  `stepUpUnits` opt-ins (substepped settle, failure-atomic bounded step-up).

**What the trimesh changes.** The retired proxy only kept up-facing surfaces,
so walls were incidental and the old walkthrough route silently clipped
through the start room's z=-6.81 wall. With the trimesh, walls are real. The
start room's spawn ledge (38.4m) is enclosed at its level, and its exit to
the main floor (32.0m) is a **door baked into the static mesh** — so the
full start-room → border-block route is gated on Daggerfall doors (task
6525, doors split + openable). That is a door problem, not a collision
deficiency: the start room's main floor connects freely to the descending
multi-level dungeon (probed 8m+ unobstructed, down to y≈13).

**Route derivation is runtime-driven (task 6522).** `scripts/find-route.py`
had become a second, approximate collision system next to the real one (the
compiled-language bet: Python is script plumbing, not durable logic). It is
replaced by `dagger-derive-route` (`src/bin/dagger-derive-route.rs`), which
admits the real project and flood-fills the movement graph by *actually
driving* `DaggerRuntime` — settle + Move + authoritative readback — so the
route and the collision authority are one system. Once doors open (6525) it
derives the full route against the real runtime.

`crates/dagger-runtime/src/bin/dagger-walkthrough.rs` is the headless proof,
driving the admitted project through the Daggerfall-owned controller API and
asserting on **authoritative** readback:

1. Settle — from the parsed start marker the player falls and comes to rest
   on genuine trimesh support (raycast into the world collision projection
   over the body footprint; a further idle does not move it).
2. Reachable-region traversal — from the start room's main floor the player
   walks into the descending multi-level dungeon (the region open without
   doors), with support asserted and blocked facts observable. The full
   cross-dungeon route resumes once doors open (6525).
3. Negative probes — each must change the authoritative outcome: (a) a tall
   wall (injected additive voxelEnvironment) produces `Blocked` facts and no
   horizontal drift or cumulative step-up height; (b) no support outside the
   trimesh bounds; (c) a project with the mesh stripped (no collision
   authority) fails admission instead of admitting a collision-less world;
   (d) a dangling explicit entry scene fails closed.

Boundary-contact note: svc-collision treats exactly-flush contact as
overlapping (parry intersection), so horizontal motion while exactly flush is
blocked. The substepped settle leaves a sub-0.1m hover; the walkthrough's
per-action movement assertions would catch a violation.

## Directional enemy sprites (task 6595)

Classic enemies are view-only directional billboards. Ownership split:

- `arena2::mobile` owns the Daggerfall reference data and math: the minimal
  enemy table (mobile id -> texture archive/idle semantics), the DFU 8-sector
  orientation function, the Move/Idle record+flip tables, and DFU billboard
  record sizing.
- `dagger-import` collects RDB enemy flats into scene nodes (`scene.enemies`,
  never baked into the static mesh) and packs one 8-frame orientation atlas
  PNG per mobile id (mirrored sides baked, palette index 0 transparent).
- The studio adapter emits `defineSpriteAtlas` + `createSprite` per enemy,
  parented under a group node (`directional: true`) so a live driver can
  rotate it — renderer-three does not implement billboard modes (rusty-engine
  6630) and `updateSprite` cannot patch transforms.
- The per-camera-tick directional authority is consumer-side by engine design
  ("projection-driven, never renderer wall-clock"): compute bearing ->
  orientation frame + camera-facing yaw, emit `update` + `updateSprite` ops.
  The engine-render-check harness owns the first such driver (per-pose);
  a runtime live loop (future `dagger-world`) reuses `arena2::mobile`.
- Static-size limitation: a sprite's quad size is fixed at creation (front
  record), while DFU scales per orientation record; accepted for view-only.

## Verification culture

- Every format claim is backed by a test against the real data files
  (arena2 unit tests run against /home/research/daggerfall-files).
- Every visible result is backed by a headless render assertion plus a
  screenshot artifact (engine-render-check, the real rusty-engine renderer).
- Every engine claim is checked against engine source, not memory; upstream
  gaps are filed with file/line evidence (see tasks 6515/6516).

## Provenance and licensing

- Daggerfall game data is copyrighted Bethesda material. It is read locally
  from /home/research/daggerfall-files (or --arena2) and **never committed** —
  nor are derived textures/GLBs published. The extracted assets in content/
  exist for local development and will be replaced by original content in the
  successor project.
- Format semantics are ported from Daggerfall Unity (MIT, dfworkshop.net) as
  a reference; docs/daggerfall-formats.md cites the source files per claim.
- Original code in this repo: match rusty-engine's posture (see its LICENSE).

## Working agreements

- Task truth lives in Den (`rusty-dagger` project). Durable intent lives in
  docs/. Code and tests own everything else.
- This document is updated whenever the model changes — scope, system
  boundaries, upstream posture, deliverable shape.
- The deliverable (walk Privateer's Hold) is real but unhurried: each landing
  is reviewed for whether the successor project would want to keep it.
