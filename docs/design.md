# Rusty Dagger design

Status: current model. Task state lives in Den (`rusty-dagger` project); this
document owns durable intent. When reality and this document disagree, fix the
document or the code, not neither.

## What this is

Rusty Dagger ports the **Privateer's Hold experience** — Daggerfall's starting
dungeon — and every system needed to support it into Rusty Engine, using the
original game's data files as the content source. It is the first stage of a
longer arc toward an original Daggerfall-ish game built on Rusty Engine.

It is **not** a port of Daggerfall. The classic game is a legible
starting profile and a content source, fitted into a rusty-engine-shaped
gameplay. Where the classic and the engine disagree, the engine's
shape wins. Where an experiment produces a better idea, the experiment wins.

## Interactive gameplay is the center

Rusty Dagger exists to make gameplay ideas cheap to try against a large body of
ready-made content. Its central loop is:

> edit -> apply -> play -> explain -> adjust

The construction-kit and rules-workbench surfaces serve that loop. They are not
separate proof products or comprehensive editors built ahead of use. A rule,
content field, inspector, editor, or abstraction earns its place by supporting
a named experiment in the connected Privateer's Hold product.

This changes how work is sliced. Crate and authority boundaries remain strict,
but tasks are vertical: authored values, Rust authority, live state,
presentation, Angular tooling, and a real interaction land together in the
smallest useful experiment. Headless examples and product/browser checks support
the experiment; they never substitute for playing it.

The classic rules and content are useful defaults, not a fidelity campaign.
There is no requirement for per-value donor lineage, deterministic replay,
artifact fingerprints, revision graphs, exhaustive validation matrices, or
long-term compatibility contracts. Formula checks should feel like ordinary
game-design spreadsheet work. Semantic traces should explain designer-facing
inputs, rolls, modifiers, intermediate values, results, and state changes.

## The long arc and the successor pattern

The endpoint is an original game, not this repo. The working method to get
there is the **successor pattern**: instead of endlessly refactoring a project
that has accreted the wrong shape, a successor project is started fresh when
the time comes, carrying forward the hard-won lessons that already live in
working code.

Consequences for how this repo is built:

1. **Systems, not features.** Every Daggerfall system (formats, textures,
   dungeon assembly, doors, lights, billboards, water, automap, enemies, …)
   lives behind a crate boundary that can be lifted into a successor project
   without dragging the whole demo along. Crates stay small enough that their
   public surface fits in one paragraph.
2. **Playable vertical experiments.** Clean systems are proven by using them in
   the connected game, not by postponing integration. Modularity means stable
   ownership and dependency direction, not a queue of headless models followed
   by UI and play at the end.
3. **Lessons in code.** Parser edge cases, format gotchas, scale constants,
   and conversion rules are recorded in tests and docs/daggerfall-formats.md,
   not in conversation. The successor project inherits confidence, not
   archaeology.
4. **Deliberate, not enterprise-hardened.** Rust authority and the TS/Rust
   boundary are durable. The authored vocabulary and its lockstep internal
   document are expected to evolve rapidly as experiments reveal what is
   useful. Do not freeze them behind compatibility, replay, provenance, or
   certification machinery without a concrete product need.

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
- A Dagger-owned gameplay lab for authoring supported content/rule values,
  applying them through Rust, resetting a named experiment, and inspecting
  authoritative state and semantic resolutions while playing.
- Vertical experiments for combat/encounters, loot/inventory, and progression.

Explicitly out of scope for this repo: the exterior world and fast travel.
Those belong to the successor (or to companion repos when they already exist
there).

## Upstream posture

Rusty Engine is the provider; this repo is a consumer, same as
rusty-engine-demo. Work that belongs upstream is filed upstream rather than
patched locally — the demo doubles as a needs-discovery surface for the
engine.

## Companion reuse

Don't rebuild what sibling repos already own. Current inventory (details in
task 6519 → docs/companion-reuse.md):

- **rusty-engine-demo** — loading-bay product: playerController implementation,
  ui-game-panels / ui-compass / ui-combat-log / theme libs, and the
  @rusty-engine-demo/project-content pipeline that generates studio-openable
  project documents.
- **rusty-roguelike** — first-person reference game on the engine (grid-based;
  assess controller/camera reuse).
- **rusty-engine-ui** — UI kit repo.
- **rusty-d20** — rules vocabulary; minimal expected use here.
- **asha-rpg** — useful immutable TypeScript authoring -> compact IR -> Rust
  semantic authority pattern. Its broad versioning, replay, checkpoint,
  fingerprint, and governance machinery is explicitly not a Dagger target.
- **Ruleweaver** — useful predecessor evidence for simple named-variable
  formulas and structured combat-result explanations; not an authority model
  to port.
- **rusty-view / rusty-roleplay** — chat/lore; not relevant.

### Product renderer and UI composition

Rich product UI follows Engine's downstream application-host contract:

`index.html -> main.ts -> mountRustyApplication -> mount Angular UI`

The downstream package may depend on `@rusty-engine/application-host`, but not
on renderer-host, renderer-three, render-projection, private webview code, or a
second canvas bootstrap. Rust remains authoritative for project admission,
gameplay, presentation meaning, and the resource manifest/bytes supplied to
Engine. TypeScript adapts transport and mounts Angular into the supplied UI
root; it may classify original host events through the application interaction
port before forwarding semantic input to Rust.

The application host must admit the real content-addressed resource-backed
frame. An empty, untextured, inline-only, or proof-specific frame is not a
substitute for the playable product.
Engine Studio remains a separate Engine-hosted tool and reaches this repository
only through `.rusty-studio.json` and the Rust adapter.

## Provenance and licensing

- Daggerfall game data is copyrighted Bethesda material. It is read locally
  from /home/research/daggerfall-files (or --arena2) and **never committed**.  
- Format semantics are ported from Daggerfall Unity (MIT, dfworkshop.net) as
  a reference; docs/daggerfall-formats.md cites the source files per claim.
- Original code in this repo: match rusty-engine's posture (see its LICENSE).

## Working agreements

- Task truth lives in Den (`rusty-dagger` project). Durable intent lives in
  docs/. Code and tests own everything else.
