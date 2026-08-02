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
engine. Current upstream needs (both filed 2026-08-02):

- rusty-engine task 6515 — UV vertex data through the static mesh pipeline
  (render-model already has `MeshAttributeName::Uv` and `PackedStreamsLeV2`;
  the authored source format and renderer path don't carry them).
- rusty-engine task 6516 — triangle-mesh collision policy for static mesh
  assets in svc-collision (parry3d TriMesh; DFU equivalent: MeshCollider with
  sharedMesh = render mesh).

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
  Engine crates at the exact public pin; it does not import loading-bay-game.
- Planned: `dagger-content` (decoded materials/meshes with provenance),
  `dagger-world` (dungeon session runtime: blocks, doors, lights, water
  state), each arriving only when the code that needs a home exists.
- `render-check/` — headless verification harness (three.js GLTFLoader +
  playwright) reusing rusty-engine's installed packages; screenshots are
  durable artifacts.

## Collision stopgap and the walk-through proof (task 6563)

Until upstream triangle-mesh collision lands (rusty-engine task 6516), the
generated studio project carries a hidden `gameplayProxy` material-voxel
environment as the collision authority, rasterized from the dungeon mesh by
`scripts/generate-project.py`:

- Every up-facing triangle (`ny/|n| > 0.5`) is rasterized into 0.5m columns;
  each column's surface heights are clustered (0.3m) and every cluster
  becomes one solid voxel whose top face is the cluster height rounded to
  the nearest cell boundary. Columns keep **every** walkable level — the
  start-marker layer (38.4m) and the levels beneath it — which is what makes
  both the spawn support and the descending border route real.
- The Daggerfall-owned `dagger-runtime` controller opts into
  `fallSpeedUnitsPerSecond` / `stepUpUnits`: a constant-speed, 0.1m-substepped
  downward settle after every action (including idles), plus a bounded ledge
  climb assist. A retry starts from the action's pre-motion position so a
  partially blocked first sweep cannot be applied twice. A retry that remains
  blocked on any axis blocked before the rise restores the pre-step height
  while preserving the retry's horizontal slide; only a retry that clears all
  originally blocked axes keeps the rise. Repeated input therefore cannot
  climb a taller-than-step obstacle. The generic Engine motion system remains
  the sole collision authority.
- A present `entryScene` is authoritative and must resolve to a named scene;
  a dangling reference is rejected. An absent entry scene may select the first
  scene for legacy/generated documents.
- `scripts/find-route.py` derives the verified route
  (`content/projects/privateers-hold.route.json`) from the **proxy voxels
  themselves** (not the mesh), mirroring the controller's footprint, settle,
  and step-up rules. `scripts/regenerate.sh` runs the whole chain.

Known stopgap limitations: vertical walls contribute no voxels (wall solidity
is incidental), and raised solids are represented by their top surface only,
so their undersides are hollow. The accepted route is checked against the
proxy, so its floor collision is real regardless.

`crates/dagger-runtime/src/bin/dagger-walkthrough.rs` is the headless proof,
driving the admitted project through the Daggerfall-owned controller API and
asserting on **authoritative** readback:

1. Settle — from the parsed start marker the player falls and comes to rest
   on proxy support (occupancy probe over the body footprint finds solid
   within the fall-substep window; a further idle does not move it).
2. Traversal — the waypoint route from the start block into a border block
   ((0,-1) → (1,-1), ~25m, descending ~6.5m), with support asserted after
   every action and the end block verified from the position readback.
3. Negative probes — each must change the authoritative outcome: (a) a tall
   wall derived from the committed project produces `Blocked` facts and no
   horizontal drift or cumulative step-up height; (b) the proxy shifted 2m
   down removes the spawn support and lands the player measurably lower; (c)
   no support outside covered columns; (d) deleting a route-midpoint column's
   voxels removes its support; (e) a dangling explicit entry scene fails
   admission instead of silently selecting another scene.

Boundary-contact note: svc-collision treats exactly-flush contact as
overlapping (parry intersection), so horizontal motion while exactly flush is
blocked. The substepped settle leaves a sub-0.1m hover; because voxel tops
are 0.5-multiples and the substep (0.1) divides 0.5, a positive initial hover
is invariant along any route — the walkthrough's per-action movement
assertions would catch a violation.

## Verification culture

- Every format claim is backed by a test against the real data files
  (arena2 unit tests run against /home/research/daggerfall-files).
- Every visible result is backed by a headless render assertion plus a
  screenshot artifact (render-check).
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
