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
- Planned: `dagger-content` (decoded materials/meshes with provenance),
  `dagger-world` (dungeon session runtime: blocks, doors, lights, water
  state), each arriving only when the code that needs a home exists.
- `render-check/` — headless verification harness (three.js GLTFLoader +
  playwright) reusing rusty-engine's installed packages; screenshots are
  durable artifacts.

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
