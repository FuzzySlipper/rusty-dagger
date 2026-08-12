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
starting profile and a content source, fitted into a rusty-engine-shaped
gameplay laboratory. Where the classic and the engine disagree, the engine's
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
smallest useful experiment. Headless examples and native/browser checks support
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
- **asha-rpg** — useful immutable TypeScript authoring -> compact IR -> Rust
  semantic authority pattern. Its broad versioning, replay, checkpoint,
  fingerprint, and governance machinery is explicitly not a Dagger target.
- **Ruleweaver** — useful predecessor evidence for simple named-variable
  formulas and structured combat-result explanations; not an authority model
  to port.
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
- `dagger-rpg` — the Dagger-owned Rust semantic kernel for authored gameplay.
  It admits the compact experiment document, owns the closed operation
  vocabulary and formula evaluation, compiles admitted values into private
  structures, and emits designer-facing calculation/resolution records. It has
  no Engine, Angular, host, storage, replay, or certification dependencies.
  Daggerfall-inspired defaults may be assembled from TS/JSON, but Rust alone
  decides what they mean.
- `dagger-runtime` — Daggerfall-owned project admission, player controller,
  real-project collision walkthrough, experiment application/reset, and live
  gameplay state. It also admits the small supported enemy-reference catalog
  used by the Lab, installs the committed navgrid, and owns grounded patrol,
  detection, chase, attack cooldowns, line-of-sight admission, enemy attacks,
  and jump-to-content commands. It consumes
  only the public `rusty_engine` Rust facade and
  local `dagger-rpg`; it does not import sibling game products. It owns
  authoritative session mutation and exposes readback plus bounded semantic
  resolution history to Dagger-owned presentation/tooling.
- `dagger-studio-adapter` — Rust-owned protocol-14 read-only admission and
  render projection for the committed Privateer's Hold project, plus the
  exact-resource bundle consumed by Dagger product and diagnostic hosts. The
  adapter reuses `dagger-runtime`; it rejects mutations until a Dagger-owned
  authority exists. `dagger-native-host` remains a first-class native
  diagnostic: it ticks the runtime authority, presents live transforms and encounter
  decisions, visualizes the committed navgrid through bounded retained frames,
  and proves physical input, pick, resize, resource admission, lifecycle, and
  disposal through Engine's public Rust facade. Engine privately owns the
  webview renderer.
- `.rusty-studio.json` — trusted root-local registration for the Engine-hosted
  Studio product. Dagger supplies project data and its Rust adapter; Engine
  owns the service, browser application, renderer, host-file transport, and
  user-settings machinery. See `docs/studio-host.md` for the runnable
  boundary.
- `apps/dagger-lab` — the first Dagger Lab Angular surface: whole-document
  authoring, live readback, the player resource worksheet, and selectable
  recent calculation inspection. A browser-local profile shelf stores named
  copies of the complete lockstep document for quick A/B experiments; storage
  is an authoring convenience, not runtime authority or a package system.
  The target product composition mounts this Angular surface only into the UI
  root supplied by Engine's `@rusty-engine/application-host`. That host owns
  the sole gameplay canvas, UI root, render cadence/lifecycle, and input
  arbitration. A Dagger-owned Rust transport supplies admitted frames, exact
  content-addressed resources, camera updates, live state, and semantic
  intents; Angular does not own or start gameplay authority. Opening the Lab
  changes the host interaction mode rather than mounting a second renderer or
  runtime, and returning to play restores gameplay focus through the bounded
  host port. The browser product is a fixed application window: gameplay uses
  the stable renderer viewport, while the Lab is a distinct opaque workspace
  with its own bounded scroll container. Lab content never participates in
  document, application-host, or renderer sizing. This matches the layout
  contract expected from a later native wrapper without making the wrapper the
  owner of product composition.

  The connected product's periodic state is also a Rust-projected retained
  frame, not camera-only polling. A shared Dagger live-presentation component
  consumes authoritative encounter positions plus `AnimationService` timing
  and emits absolute enemy transforms, directional sprite frames, and
  environmental-flat frames through Engine's application renderer facade.
  Native diagnostics compose their overlays around that same production
  component; they are not a separate animation implementation. Pointer deltas
  remain raw in the browser bridge and acquire Dagger's FPS yaw/pitch meaning
  once in the Rust product host. That adapter maps mouse-right and mouse-up to
  positive canonical Engine yaw and pitch respectively; the retained camera
  readout and character-controller heading share that same yaw basis so WASD
  movement remains camera-relative.

  First-person melee presentation follows the same boundary and is clone-first.
  `dagger-runtime` owns attack direction, contact, recovery, cooldown, stamina,
  outcome, target health, and death. An attack input starts the Rust-owned
  swing whenever cooldown permits; fatigue cost saturates at zero and never
  suppresses the classic weapon action. Aimed target selection and
  damage occur only when that action reaches its contact frame, so empty space
  still produces the same complete weapon motion. Cooldown rejection records
  the input without replacing or restarting an active swing. The generated
  combat catalog maps semantic weapon/action/effect/audio IDs to classic
  Arena2 resources. The weapon is intentionally camera-relative through Engine's public `viewmodel`
  layer, while blood/sparkle belongs at the world impact point through Engine's
  world presentation facade. Fullscreen impact flashes and oversized
  target-health overlays are not the default clone treatment. TypeScript only
  transports diagnostic readback used by browser gates; it does not time or
  reinterpret the action. Reset clears the action and restores encounter
  visibility from the same Rust session authority.

  `dagger-native-host --browser-product` owns the connected Rust session and
  serves the Angular product, its complete Rust-projected frame, exact
  content-addressed resource bytes, authoritative camera/state, semantic input
  endpoint, and Lab API. The ordinary `dagger-native-host` mode remains an X11
  renderer diagnostic rather than a second product surface. Closing and
  reopening the browser product reconnects to the same Rust session.
  Loopback is the default bind and an explicit LAN bind remains trusted,
  unauthenticated development mode. Worksheet evaluation is side-effect-free;
  Reset & Play restores the named start and focuses the Engine-owned gameplay surface. The app
  calls Rust for every evaluation or mutation and never imports or mounts
  Engine renderer implementation.
  The connected content browser searches the 43 committed enemy identities,
  keeps decoded Arena2 reference fields distinct from authored player/Rat rules
  and live patrol/resource/AI state, and asks Rust to choose a navigable grounded
  approach before returning focus to the Engine-owned gameplay surface. The
  encounter editor exposes only
  the supported per-archetype detection, patrol/chase speed, attack range,
  cooldown, and damage terms; concise state-change and attack records explain
  the current play session. Rat gameplay keys to Arena2 mobile ID 0 and the
  Skeletal Warrior profile to mobile ID 15; `dagger-rpg` does not own a
  duplicate classic identity roster. It is
  deliberately not a generic Arena2 editor or a raw-coordinate teleport
  surface.
- `data/` — optional committed, hand-authored JSON defaults for the experiment
  document. TS authoring modules may be preferable when builders materially
  improve readability. `content/` is committed generated output from
  `scripts/regenerate.sh`; never hand-edit it. See `data/README.md`.
- Planned only when demanded by code: `dagger-content` (decoded reusable
  material/mesh meaning) and `dagger-world` (shared dungeon session state for
  blocks, doors, lights, water, encounters). Neither is a prerequisite for the
  gameplay lab.
- `engine-render-check/` — migration pointer. The durable renderer diagnostic
  is `dagger-native-host`: Dagger Rust owns project meaning and product
  orchestration, the public `rusty_engine` facade owns the contract, and
  Engine privately owns the Rust-to-webview/Three boundary. Downstream source
  has no renderer TypeScript, HTML canvas bootstrap, or renderer package
  imports.

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

The application host must admit the same content-addressed resource-backed
frame used by the native diagnostic. An empty, untextured, inline-only, or
proof-specific browser frame is not a substitute for the playable product.
Engine Studio remains a separate Engine-hosted tool and reaches this repository
only through `.rusty-studio.json` and the Rust adapter.

### Gameplay authoring shape (program 6682, tasks 6683, 6689, 6684, 6685, 6687, and 6690)

- **TypeScript/Angular authors; Rust means and acts.** Immutable TS builders,
  simple JSON defaults, and Angular forms may assemble supported values. They
  all produce one compact internal experiment document. The first document
  exposes movement speed, named player and Rat attribute/resource inputs, and
  the bounded melee inputs needed for player-versus-Rat play. Rust owns fixed
  resource, hit, and damage formula shapes. `dagger-rpg`
  admits and evaluates them; `dagger-runtime` applies player values and
  constructs per-entity Rat resources for admitted mobile ID 0 entities. It
  derives the focused target, checks live planar reach and Engine collision
  line of sight, mutates health/death, and retains semantic attack records.
  Player melee cooldown and stamina cost are authored terms, but the timer,
  resource admission/mutation, physical input edge, and accepted/rejected
  attempt records stay in `dagger-runtime`. Cooldown advances only through the
  native play-session tick; physical `R` and the Lab reset command both call
  the same runtime reset that restores resources and timing state.
  The same admitted document supplies bounded behavior terms for named Rat and
  Skeletal Warrior encounters. `dagger-runtime` owns their nav-aware
  patrol/detect/chase/attack loop, cooldowns, Engine collision line of sight,
  player-health mutation, and concise decision history; the native host only
  ticks and presents those effects.
  There is no TS evaluator, expression AST, callback escape hatch, or replay
  contract.
- **Small vocabulary, grown by play.** Begin with the first movement/derived
  value experiment. Add reads, arithmetic, rolls, conditions, effects, or
  content fields only for a named interactive slice. Do not create a general
  rules VM or enumerate a future game's whole domain up front.
- **Whole-document apply.** The lab submits a complete candidate. Rust either
  returns readable author errors or installs it and exposes authoritative
  readback. Reset/retry is explicit. Per-field revisions, merges, package
  dependency resolution, compatibility migrations, and schema governance are
  out of scope.
- **Named profiles are local drafts.** Save As, duplicate, rename, and delete
  operate on complete documents in Dagger Lab browser storage. Selecting a
  profile only loads its draft; the UI calls it active only after Rust admits
  the complete document and returns matching authoritative readback. Profiles
  have no revisions, lineage, merge behavior, or compatibility promise.
- **Useful validation only.** Reject unknown fields, unsupported schema values,
  non-finite values, clearly unusable ranges, and invalid derived results.
  Focused formula examples and regressions are enough; there is no exhaustive
  proof or deterministic replay program.
- **Semantic explanations, not execution logs.** Records are shaped around
  gameplay resolutions: named inputs, rolls/modifiers, intermediate values,
  result, and before/after authoritative state. The current max-health records
  expose only the fields they genuinely possess; actor/action/target filtering
  arrives with interactive resolutions that carry those identities. Store a
  bounded recent-session history and add copy/export only when it serves play.
  Combat attempts add only designer-facing timing/resource facts: accepted or
  rejected outcome, cooldown before/after, and stamina before/cost/after.
- **Daggerfall is a preset.** Arena2 and classic/DFU knowledge help populate
  useful defaults. The lab does not track per-value provenance or require exact
  fidelity before an experiment can proceed.
- **Successor lift:** `dagger-rpg` remains host-neutral Rust authority and the
  authored document remains data-only. A successor may reuse the useful pieces
  without the Dagger native host, Angular product, or classic content.

### Modularity gate evaluation (tasks 6529 and 6708)

The 2026-08-03 check found no useful split. Task 6707 then unified Studio and
native rendering behind one projection owner, making the real boundary clearer
but growing neighboring responsibilities. Task 6708 therefore splits the
adapter internally by purpose: protocol transport, project readout,
presentation/resource admission, and native application/proof/view/diagnostic
orchestration. This is a module split, not another crate or abstraction layer;
the public library surface remains `run_stdio` and typed `build_render_bundle`,
and the native binary remains a thin composition root.

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
- A second consumer (beyond `dagger-import`) needs shared decoded
  material/mesh meaning -> that data is the seed of `dagger-content`.

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
- `dagger-runtime` translates Dagger-owned bindings, speed, spawn, and the
  admitted `fallSpeedUnitsPerSecond` / `stepUpUnits` policy into Engine's
  canonical `CharacterControllerConfig`. Engine's `CharacterControllerService`
  and `CharacterMotionComponent` are the sole movement, gravity, grounding,
  step, slope, ledge, and wall-slide authority. The authored 0.1s Dagger action
  cadence is deterministically subdivided into bounded Engine fixed steps. A
  deliberate 1m / 60m-per-second bounded recovery override admits the existing
  cube-era spawn markers into the canonical 1.8m capsule without moving spawn
  selection policy out of Dagger.
- Camera look is integrated by Engine's `FirstPersonLookService`; Dagger owns
  pointer-event translation and converts Engine's canonical basis signs to the
  renderer/Daggerfall camera-degree convention before publishing the stable
  camera and aim readout. `ResolvedPlayerAction::Look` therefore follows the
  Engine delta sign, while `PlayerControllerState` remains the compatible
  renderer/Daggerfall degree readout. No Dagger solver or look fallback remains.

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
- The studio adapter emits `defineSpriteAtlas` + `createSprite` per enemy
  (`billboard: cylindrical`; the renderer honors billboard modes,
  rusty-engine 6630).
- The per-camera-tick directional authority is consumer-side by engine design
  ("projection-driven, never renderer wall-clock") and lives in
  `dagger-runtime::directional` (`evaluate_directional`, arena2::mobile
  semantics): camera pose + the Rust patrol heading -> per-enemy orientation
  frame. A moving actor snaps to its actual displacement heading because the
  current movement model is not turn-rate constrained. Consumers apply the
  frames (`updateSprite` ops) and never re-implement the math — the
  Rust tests and `dagger-sprite-frames` consume them without reimplementing
  the sector math. The native and connected-product presentation loops submit
  those updates through the same public retained-frame facade. Camera-facing
  stays Engine presentation behavior; Dagger never calls the private renderer.
- Classic Rat and Imp records contain direction-dependent scale metadata that
  is unsuitable for live geometry. Import retains those source sizes as
  provenance, but crops each decoded frame to visible pixels, normalizes it to
  one per-enemy height and one median width per direction with nearest-neighbor
  sampling, and bottom-centers it in a uniform atlas cell. The project publishes one fixed world size and pivot
  for every frame; frame changes only select UVs. The generated encounter
  gallery exercises real Rat, Imp, and Skeletal Warrior atlases, Rust
  patrol/heading and animation authority, a collidable trimesh floor, and the
  same Engine application host without requiring dungeon navigation.

## Sprite animation service (task 6640)

Animated sprites — environment flats (torch flames cycle classic TEXTURE.nnn
multi-frame records) and directional enemy sprites — are driven by a
consolidated per-tick animation authority in `dagger-runtime::animation`.

**Design principle**: one evaluation pass per tick over all animated sprites
produces a consolidated frame diff (only changed entries), not per-entity
polling. This is deliberate: once naive per-entity polling becomes the
pattern, future work inherits it. The service shape stays the same when
offscreen-sprite throttling arrives (engine 6632 visibility readouts exist):
freeze an entry's `last_frame` or filter the diff — no API change.

Ownership split:

- `arena2::mobile` owns the DFU animation timing constants: `MOVE_ANIM_SPEED`
  (6fps), `FLY_ANIM_SPEED` (10fps), `IDLE_ANIM_SPEED` (4fps), and
  `ENV_BILLBOARD_FPS` (5fps, DFU DaggerfallBillboard default). Enemy idle
  records are 1-frame (static orientation); move records carry 4-8 frames.
  Classic env textures carry 4-5 frames per animated record.
- `dagger-import` packs multi-frame billboard records into horizontal strip
  atlas PNGs (one per animated (archive, record) pair) and emits per-frame
  UV rects + frameCount + fps in the billboard manifest. Single-frame records
  stay backward-compatible (plain PNG, no frameCount key). The importer
  decodes ALL frames via `arena2::texture::frame_pixels(record, frame)`.
- `dagger-runtime::animation` owns the Daggerfall-side per-tick authority:
  `AnimationService` tracks elapsed time and per-sprite state; one
  `evaluate(dt, camera)` call per tick walks all entries and emits only
  changed frames as a `Vec<FrameUpdate>`. Two `SpriteKind`s:
  - `Env { frame_count, fps }`: time-cycled, frame = `(elapsed * fps) % frame_count`.
  - `Enemy { position, heading, mobile_id }`: actor-relative camera-driven
    orientation via `evaluate_directional`; move-state cycling uses
    `orientation × anim_frame`.
- `AnimationService` remains the sole per-tick authority and is covered in
  Rust. `dagger-native-host` composes its consolidated updates with patrol
  transforms in one bounded Rust tick and submits them through the facade.
  There is no browser-side clock or per-sprite polling.

`dagger-sprite-frames --serve` remains a headless inspection tool for per-step
camera poses. The native diagnostic calls `AnimationService::evaluate` directly;
the service's elapsed-time state never crosses the renderer boundary.

**Fidelity**: classic torches/flames carry 4-5 frames at 5fps (DFU
DaggerfallBillboard default). Enemy idle records are 1-frame for all
Privateer's Hold enemies (the orientation evaluation IS the idle animation).
Move-state cycling at 6fps (ground) / 10fps (flying) comes with 6641.

## Navigation grid (task 6639)

Enemy spawn Y is an authored spawn point, not floor support — all 43 measured
spawns float (0.5–1.8m). The nav grid is how runtime grounding and patrol
(6641) get support answers. Ownership split:

- Upstream `svc-pathfinding` is voxel-only: `build_nav_projection` requires a
  `VoxelWorld`, `NavProjection` has no host-derived constructor, and
  `find_path` is planar-same-Y only (no stairs). Filed upstream:
  rusty-engine 6642 (projection from host-derived walkable cells) and 6643
  (step-aware vertical neighbors). Grid-style navigation fits the known
  consumer; maintained pure-Rust navmesh crates (`rerecast`, `landmass`,
  `pathfinding`) were named in the filing in case scope grows.
- `dagger-runtime::navgrid` owns the Dagger-side derivation — projection
  *construction*, not a pathfinder: a bounded sweep over the dungeon mesh AABB
  casts one downward ray per 0.5m column into the admitted collision scene
  (backface-culled trimesh raycast, so only up-facing surfaces register),
  re-casting below each hit so multi-level columns (ledge over main floor)
  record every standable level. A surface is walkable when it faces up
  (normal.y ≥ 0.7), has 2m headroom, and is enclosed (a down-facing surface
  within 64m overhead rejects open-sky rooftops; sized to the full mesh so the
  start room's ~30m shaft stays walkable). 0.25m level quantization matches
  the derive-route convention.
- `dagger-navgrid --write|--check` is the headless proof: known spots
  (start room floor y=32, spawn ledge y=38.4, multi-level column, ≥4 RDB
  blocks, rock columns unwalkable) plus ground-support answers for all 43
  enemy spawns (every one lands on a walkable cell within 12m). Writes the
  committed `content/projects/privateers-hold.navgrid.json`; regenerate.sh
  keeps it fresh.
- The committed navgrid and grounding behavior are certified headlessly in
  Rust. The native and connected-product `N` overlay selects at most 512
  nearby same-level cells
  from this artifact and submits retained debug cubes through the facade. The
  `G` overlay similarly shows authored spawns, live grounded patrol positions,
  sprite bounds, and Rust-owned headings. Both controls are opt-in and their
  off/on lifecycle destroys and replaces retired handles. The connected game
  ports the same Rust diagnostic owner and keys rather than rebuilding overlay
  meaning in TypeScript. Adjacency (walls
  between columns) is deliberately not modeled here — path connectivity is
  the upstream seam's job (6642/6643).

## Native diagnostic update lifecycle (task 6710)

`dagger-native-host` is the campaign's advanced diagnostic, replacing the
retired browser flycam without restoring downstream renderer access.

- One `NativeDiagnostics` owner admits the real project and committed navgrid,
  constructs `AnimationService` and `PatrolService`, and emits one validated
  `RenderFrameDiff` per bounded tick. Patrol translations update the same
  sprite handles created by the shared presentation projection; animation
  frames and overlays are combined in that frame.
- At most one diagnostic frame may be in flight. The next tick waits for the
  Engine frame receipt, preventing presentation work from starving physical
  input and keeping update pressure bounded.
- `G` toggles authored-spawn/live-patrol diagnostics; `N` toggles nearby
  navgrid cells. Retained overlay projection owns create/update/destroy and
  allocates a fresh handle when an overlay returns after removal, so stale
  handles never regain authority.
- The Linux proof performs real X11 `G`/`N` on-off-on cycles, waits for applied
  Engine receipts, observes live patrol movement and animation advancement,
  destroys retained diagnostics, and only then disposes the Engine renderer.


- Every format claim is backed by a test against the real data files
  (arena2 unit tests run against /home/research/daggerfall-files).
- The runnable visible result is backed by the native Engine host proof;
  Studio has a separate real-browser integration gate.
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
