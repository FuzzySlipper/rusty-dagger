# Companion repo reuse survey

This document tracks which sibling repos were surveyed and which ideas are
useful in Rusty Dagger. Siblings are design evidence, not product dependencies:
do not add `path`/`git` dependencies on sibling games. Prefer reimplementation
inside Dagger's ownership boundaries. If substantive source is actually copied,
retain ordinary license/attribution information; do not build per-rule or
per-value provenance machinery around design inspiration.

Current Engine exception: the canonical `rusty-engine` provider is consumed
only through its single Rust facade dependency. Historical notes below that
name renderer npm packages or browser renderer surfaces are survey evidence,
not current integration guidance; task 6707 removed those downstream paths.

## 2026-08-02 baseline (task 6519)

One section per sibling repo at that date: what existed, what looked reusable,
what to avoid, and the integration shape. This baseline is historical. The
2026-08-09 gameplay-lab guidance below supersedes it where they disagree.

### rusty-engine-demo (loading-bay)

**What exists**

- `rust/crates/loading-bay-game` — the full first-person game runtime:
  - `player.rs` — `PlayerControllerConfig/State/Component`,
    `ResolvedPlayerAction::{Move,Look}`, `PlayerControllerService::apply*` over
    `engine_spatial::KinematicMotionSystem` + `VoxelCollisionScene` (swept
    collision, blocked/moved facts). ~320 lines, clean deps (core-ids,
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
  project doc followed at the time (voxel gameplayProxy = truth, mesh =
  visible content). Retired by 6522 (see Decisions §4): the collision
  authority is now the dungeon static mesh's trimesh.

**Consume**: the controller and collision behavior as a reference for the
portable Daggerfall runtime. The crate is product-flavored (Loading Bay
semantics in `combat`, `extraction_beacon`), so its gameplay services are not a
valid downstream dependency here.
**Avoid**: the studio-adapter/browser-host binaries themselves (loading-bay
project semantics baked in), `enemy_combat`, `progression`, `extraction_beacon`.
**Integration**: reference only for controller semantics. The controller and
real-project walk-through now live in `crates/dagger-runtime`, consuming the
generic Engine crates directly. No `path` or git dependency on
`rusty-engine-demo` is permitted. Doors and other Daggerfall systems should be
added to Daggerfall-owned crates as their semantics become real.

### rusty-roguelike

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

### rusty-engine-ui

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
**Integration**: historical proposal only; the current product uses
`@rusty-engine/application-host` with Dagger-owned Angular UI.

### rusty-d20

**What exists**: d20 rules vocabulary/mechanics on the engine (stats, effects,
damage, restoration — per its README/Den description).

**Consume**: nothing now. If game-feel prototyping drifts toward stats/effects
(user's adjusted scope keeps this door open), reassess then — Daggerfall's own
attribute system (CLASS*.CFG exists in local/arena2) is a different vocabulary
and d20 is not a drop-in for it.
**Avoid**: pulling it in early "because RPG" — vocabulary mismatch risk.
**Integration**: revisit after the walk-through.

### rusty-view / rusty-roleplay

**What exists**: chat client kit + lore/memory service.

**Consume/avoid**: not relevant to this project. (Noted for completeness only.)

### rusty-engine (provider, not a companion — recorded for clarity)

Already consumed at the time of this historical survey: `rusty-asset-import`
CLI (content pipeline), render-model/asset-catalog semantics via artifacts,
and the Studio adapter protocol.

### Decisions (for task 6563 implementation)

1. FP controller: use the loading-bay implementation as a behavioral reference
   only. `dagger-runtime` owns the Daggerfall controller and calls
   `engine_spatial::KinematicMotionSystem` directly, avoiding Loading Bay
   session/damage/progression coupling.
2. UI: **rusty-engine-ui** for compass/minimap/theme when the browser app
   arrives (task 6528 automap is the first consumer).
3. Project/content machinery: **hand-rolled** (scripts/generate-project.py) —
   already landed and adapter-verified; no change.
4. Collision doctrine: **superseded by task 6522.** This decision originally
   adopted loading-bay's voxel gameplayProxy-as-truth + mesh-visual model
   (applied in the 6518 project doc). Task 6522 retired the gameplayProxy
   stopgap: the collision authority is now the dungeon static mesh itself
   (`collision: "trimesh"` → `StaticMeshColliderAsset` →
   `replace_static_mesh_colliders`); the scene carries no `voxelEnvironment`,
   and `voxelEnvironment` survives only as an optional *additive* authority
   for adversarial probes. The loading-bay doctrine remains the reference for
   the *controller* (point 1), not for collision authority.

---

## 2026-08-09 gameplay-lab guidance (program 6682)

The earlier 6683 survey chose an empty Rust crate, strict JSON tables, and a
horizontal sequence ending in UI/integration. That plan is superseded. The
current center of gravity is a real edit -> apply -> play -> explain -> adjust
loop in Privateer's Hold.

### What to borrow

#### asha-rpg

- Immutable, data-only TypeScript builders are a good way to author the blurry
  line between content and rules without creating a second authority.
- A compact language-neutral document can cross into Rust, where operation
  meaning, formula evaluation, legality, mutation, and semantic output live.
- New operation meaning starts in Rust; TS sugar and Angular editors expose the
  closed vocabulary.

Do **not** inherit asha-rpg's scale: broad artifact contracts, version/fingerprint
machinery, checkpoints, replay/certification, package governance, or a language
roadmap detached from current gameplay. Dagger's internal TS/Rust document
evolves in lockstep until independent consumers prove otherwise.

#### Ruleweaver

- Its simple named-variable arithmetic formula shape is appropriate for the
  spreadsheet-like checks designers commonly need.
- Its structured attack result/combat-log shape is more useful than its generic
  tick/category/message rule trace. Dagger traces should record named rolls,
  bonuses, defenses, effects, and before/after state in designer language.

Do **not** port Ruleweaver's mutable handler/service architecture or use its
generic execution trace as the product explanation.

#### rusty-engine-demo, rusty-engine-ui, and rusty-roguelike

- Reuse presentational patterns for compass, status, inventory, combat log,
  responsive Angular layout, and first-person controls where they remain
  dependency-clean.
- Reuse gameplay service shapes only as references. Dagger Rust owns Daggerfall
  semantics and connected product/runtime orchestration.
- Never import or mount sibling/Engine renderer implementation from Dagger TS.
  Current Engine is consumed through the public Rust facade.

#### rusty-d20

- Keep the useful authority rule: authored data may name and assemble supported
  operations, while Rust defines and executes their meaning.
- Do not adopt the d20 vocabulary, package compiler, canonical fingerprints,
  provenance pipeline, or generated-contract governance. Dagger needs a much
  smaller internal authoring seam driven by gameplay experiments.

### Current authoring and tooling shape

- TS/JSON defaults and Angular edits produce the same immutable experiment
  document.
- `dagger-rpg` currently admits movement plus named player/Rat/Skeletal Warrior
  stat, melee, and bounded encounter-behavior inputs, evaluates fixed Rust-owned
  resource/hit/damage formulas, and emits semantic calculation and combat
  records. Later vocabulary grows only with playable experiments.
- `dagger-runtime` installs a complete admitted experiment, owns live state,
  target/range/collision admission, rolls, health/death mutation, and explicit
  reset/retry. It also owns committed-navgrid patrol, detect/chase/attack modes,
  attack cooldowns, Engine line of sight, player damage, and bounded concise
  decision records. Player melee timing and stamina costs similarly remain
  Rust session state, with concise accepted/rejected attempt records rather
  than a generic input log. There is no per-field revision or replay protocol.
- The Angular Dagger Lab shares one browser product and one Rust session with
  the Engine-owned gameplay surface. The first surface has
  movement/resource/melee plus named encounter editors, live
  position/controller/resource readback, a Rust-backed formula worksheet,
  bounded calculation/combat/encounter histories, named profiles, and
  experiment-driven content browsing.
- `arena2` remains read-only format/reference knowledge. In particular,
  `arena2::mobile` owns classic mobile identity, sprite, animation, and facing
  reference semantics; `dagger-rpg` owns authored gameplay stats keyed by those
  same mobile ids.
- `data/` may hold committed JSON defaults where that is ergonomic. TS authoring
  modules may be used when builders make formulas/content clearer. `content/` is
  committed generated output and is regenerated with `scripts/regenerate.sh`.

### Guardrails

- Every gameplay task ends in a named Privateer's Hold product interaction.
- Headless examples and validation support play; they do not become the main
  deliverable.
- Add vocabulary and editor surface only for a current experiment.
- Keep validation focused on useful author errors: unknown fields, unsupported
  schema values, non-finite values, unusable ranges, and invalid derived
  results. Add expression/reference checks only if those concepts actually
  enter a playable document.
- No provenance graph, per-value donor lineage, artifact fingerprinting,
  deterministic random tapes, replay/checkpoint certification, revision DAG,
  compatibility matrix, package dependency solver, or exhaustive proof corpus.

---

## 2026-08-07 historical campaign survey (superseded)

The inventory below records what the first 6683 attempt examined. Its
empty-crate delivery plan, strict schema/version posture, copy-provenance rule,
replay suggestions, and horizontal 6684..6690 ordering are not current design.

**Goal:** survey `../rusty-engine-demo` (FPS controller + `ui-game-panels`/compass +
`project-content` pipeline), `../rusty-d20` and `../rusty-roguelike` (two
different data-driven RPG approaches) for **patterns only**, decide what to
**copy out** vs reimplement, and define the data-driven content shape for the
Privateer's Hold gameplay loop (combat, inventory, leveling, RPG formulas — no
quests/world travel/dialogue). This section plus `docs/design.md` system map is
the source of truth that later tasks (6684..6690) follow.

### Inventory — exact file lists surveyed

#### rusty-engine-demo

- `rust/crates/loading-bay-game/src/` — `player.rs` (PlayerControllerConfig/State,
  ResolvedPlayerAction Move/Look, apply over KinematicMotionSystem +
  VoxelCollisionScene), `door.rs` (SwitchComponent/InteractionService), `interaction.rs`,
  `combat.rs` (WeaponConfig, ResolvedAttackAction, CombatFact/Fact, cooldowns),
  `inventory.rs` (InventoryService, ItemDefinitionId), `pickup.rs`,
  `encounter.rs`, `hazard.rs`, `navigation.rs`, `weapon_authoring.rs`,
  `vitality.rs`, `progression.rs`, `session.rs` (GameSession), `runtime.rs`,
  `project_admission.rs`/`project_codec.rs`/`project_store.rs`, `bin/browser-host.rs`,
  `bin/studio-adapter.rs` + `studio_adapter/` (protocol-14 projection).
- `ts/packages/project-content/src/` — `content-artifacts.ts`,
  `synchronizeGeneratedProjects()`, and `schema.ts` (schemaVersion 24 project
  document schema). `canonicalProject()` is test-only; this package has no
  `generated.ts`.
- `libs/` — `ui-game-panels`, `ui-compass` (bearing strip), `ui-combat-log`,
  and `theme`. `ui-minimap` belongs to rusty-engine-ui, not the demo.
- `content/` — `assets/actor-kit`, `assets/brush-kit`, `assets/prop-kit` (mesh.json +
  source GLBs), `doom-e1m1/` textures, `projects/loading-bay.project.json` (canonical).

#### rusty-d20

- `rules/packages/d20-authoring/src/generated.ts` — **checked contract** emitted by
  Rust `rusty-d20-rules-contract` binary (`cargo run -p rusty-d20 --bin rusty-d20-rules-contract`).
  TypeScript is composition surface only; Rust owns vocabulary, validation, compilation.
- `rules/packages/starter-ruleset/` — multi-file TypeScript authoring modules compiled by
  Rust into canonical artifacts via `node scripts/generate-artifacts.mjs --write` (checked
  via `--check` in CI). Six canonical packages: `starter-core`, `steel-guard`, `ember-ward`,
  `wardens-gate`, `embers-wake`, `catalog-probe`.
- `rust/crates/rusty-d20/src/` — `candidate.rs` (D20RulesCandidate schemaVersion 6),
  `compiler/` (strict validation: quotas, duplicates, provenance), `component.rs`
  (AbilityScoresComponent, ActionResourcesComponent, ScheduledEffectsComponent),
  `game/` (action preview → resolve implement → ability modifier → defense eval via
  StatService → roll/damage), `session/` (live EntityState), `adventure.rs`.
- `docs/d20-rules-kernel.md` — boundary: no callbacks/expression trees in candidate;
  `gameplay-rules` + `gameplay-mechanics` + `svc-rng` supply mechanics.
- `libs/domain/src/` — `projectRuntimeReadout()`/`projectGameSnapshot()` translators.

#### rusty-roguelike

- `rust/crates/rusty-roguelike/src/` — `lib.rs`, `bootstrap.rs` (floor admission),
  `floor/` (procgen → VoxelWorld), `rules/` (starter.json → RoguelikeRulesCandidate →
  RoguelikeRuleset via `gameplay-rules`), `world/` (collapsed party square), `session/`
  (GameSession with initiative, one-activation economy, per-enemy round-robin target cursor,
  saves schema 4 with replay validation).
- `rust/content/rules/starter.json` — inert authored policy (single JSON file), strictly
  decoded/validated/compiled by Rust; TS declarations generated from Rust schema.
- `libs/renderer/src/dungeon-frame.ts` — renderer `dungeon-frame` component (camera pose +
  yawDegrees) mounting `@rusty-engine/renderer-host` RendererSurface; `view-composition.ts`.
- `libs/feature-game/src/` — `party-sheet.ts`, `loadout-panel.ts`, `minimap.ts` (grid domain).

#### rusty-engine (provider)

- `core-ids`, `core-math`, `core-space`, `engine-spatial` (KinematicMotionSystem),
  `entity-state` (EntityState/EntityView), `svc-collision` (StaticMeshColliderAsset,
  replace_static_mesh_colliders), `gameplay-mechanics`/`gameplay-rules` (mechanics catalog,
  admitted packages), `svc-rng`, `svc-volume`, `svc-pathfinding`. The adjacent
  `../rusty-engine` checkout supplies these namespaces through the single
  facade; exact commits are review evidence, not a source-dependency protocol.

### What to copy, what to avoid — per repo

#### rusty-engine-demo — copy the controller **pattern**, not the game

- **Copy:** `player.rs` FP controller shape — `PlayerControllerConfig` bounds
  (`MAX_PLAYER_SPEED_UNITS_PER_SECOND 1000`, `MAX_PLAYER_LOOK_DEGREES_PER_UNIT 180`,
  `MAX_INPUT_CONTROL_LENGTH 64`), `is_valid()` checks (finite, >0, ≤ max, pitch ±89,
  unique bindings), `ResolvedPlayerAction::{Move,Look}` + `PlayerControlFact::{Moved,Blocked,LookChanged}`
  + `PlayerControlReceipt` split, `KinematicMotionSystem` sweep + `failure-atomic` step-up
  pattern, `fallSpeedUnitsPerSecond`/`stepUpUnits` opt-ins. Also `door.rs`/`interaction.rs`
  fact pattern (SwitchComponent → GameEvent) and `inventory.rs` Engine-component shape
  (typed facts, not guessed). Copy with provenance comment like
  `// Adapted from rusty-engine-demo rust/crates/loading-bay-game/src/player.rs @<rev>`.
- **Avoid:** `combat.rs` Loading Bay specifics (enemy_combat, extraction_beacon,
  weapon ammo/penetration), `progression.rs`/`enemy_drop.rs` product progression,
  `session.rs` GameSession coupling (it pulls combat/inventory/enemy_combat together),
  `bin/browser-host.rs` + `studio_adapter/` binaries (project semantics baked in),
  `ts/packages/project-content` generator (we already own `scripts/generate-project.py`),
  `libs/ui-*` feature compositions that assume Loading Bay domain.

#### rusty-d20 — copy the **vocabulary-ownership** pattern, not the vocabulary

- **Copy:** the *boundary* that makes data-driven safe — TypeScript may compose candidate
  data, but **Rust defines the accepted vocabulary, validates, compiles immutable
  definitions, owns live state, and executes actions**. No callbacks/expression trees in
  data. The checked contract (`generated.ts` emitted by Rust) makes the authoring SDK
  a typed surface over Rust-owned schema. Also the `compile → fingerprint → provenance`
  pipeline (canonical bytes, sorted module/definition order, stable subject IDs like
  `ability:might`). For dagger, this means: `data/*.json` → Rust `Deserialize` structs →
  `dagger-rpg` validation → immutable `RpgRuleset` → pure `fn` formulas, never JS math.
- **Avoid:** the entire d20 vocabulary (`abilities`, `defenses`, `implement definitions`,
  `starter-core`/`steel-guard` packages) — Daggerfall's CLASS*.CFG/MONSTER.BSA attributes
  are a different domain; importing `rules/packages/d20-authoring` would be a category error.
  Also `libs/domain` translators (they are d20-cast specific).
- **File list to reference:** `rules/packages/d20-authoring/src/generated.ts`,
  `rust/crates/rusty-d20/src/candidate.rs`, `compiler/`, `docs/d20-rules-kernel.md`,
  `scripts/generate-artifacts.mjs`.

#### rusty-roguelike — copy the **single-JSON + strict compile** shape

- **Copy:** the minimal alternative — one inert `starter.json` strictly decoded by Rust into
  `RoguelikeRulesCandidate` → `admit_roguelike_candidate` → `RoguelikeRuleset`, with TS
  declarations generated from Rust schema. Plus the session invariants that Rust enforces:
  collapsed-party ownership, one-activation economy, save schema 4 with **replay validation**
  (re-derive receipts from fresh authored session and require exact match). The key lesson:
  a small hand-authorable file can still be *strictly* validated and liftable.
- **Avoid:** grid-locked assumptions (party sheet, procgen floor admission, VoxelWorld
  nav), initiative/round-robin specifics — they don't map to Daggerfall's free-move hold.
  Also `libs/renderer/dungeon-frame.ts` grid coupling.
- **File list to reference:** `rust/content/rules/starter.json`,
  `rust/crates/rusty-roguelike/src/rules/` (candidate + compile), `session/` (replay),
  `docs/design.md` turn model.

#### rusty-engine-ui / rusty-d20 libs/ui-* — copy **presentational** widgets only

- **Copy:** `ui-compass` (bearing strip), `ui-minimap` (marker view model), `ui-hotbar`,
  `theme` — they are dependency-clean, input-only. When a browser app exists, copy their
  view-model shape, not `feature-game-hud` composition.
- **Avoid:** `feature-*` compositions, `domain`/`protocol`/`store` game loopback.

### Historical copy rule (superseded by the 2026-08-09 guidance)

> **No `path` or `git` dependency on any sibling repo.** If a pattern from
> `../rusty-engine-demo`, `../rusty-d20`, or `../rusty-roguelike` is worth
> reusing, copy the minimal snippet into a dagger-owned crate (`dagger-rpg`,
> `dagger-runtime`, future `dagger-world`) and leave a provenance comment with
> the donor file + rev. The engine (`../rusty-engine`, `branch = "main"`)
> remains the only cross-repo provider (file upstream tasks instead of local
> workarounds). This keeps each crate liftable to the successor project without
> dragging a product's session, progression, or UI domain along.

### Superseded data-driven content shape from the first 6683 attempt

**Crate / module boundary**

- `arena2` — stays read-only BSA/MAPS/RDB/ARCH3D/TEXTURE/PAL/PAK parsing and
  classic reference semantics. No gameplay mutation or authored experiment meaning
  beyond "what the bytes say". Tests gate against `/home/research/daggerfall-files`.
- **`dagger-rpg`** (new, landed as empty crate in this task) — owns **all**
  Daggerfall-fidelity tables + pure formulas for the loop: attributes
  (STR/INT/WIL/AGI/END/PER/SPD/LUC), derived stats (health/stamina/magicka/encumbrance),
  combat (to-hit, damage, armor), item/weapon/armor defs, monster defs for the 8 hold
  mobiles, and leveling (XP table, health-per-level, attribute gains). Each table is
  a `#[derive(Deserialize)]` struct; each formula is a `pub fn` with **no inline
  numbers at call sites** (those live in `data/*.json`). Liftable: it only depends
  on `serde`/`serde_json`, not on `dagger-runtime` or engine services. Follows the
  d20 boundary (Rust owns vocabulary) and the roguelike minimal-file shape (one JSON
  per domain, strictly validated).
- `dagger-runtime` — project admission (already `from_project_json`), player controller
  (already `PlayerControllerConfig` + KinematicMotionSystem + trimesh), and the
  runtime session that **uses** `dagger-rpg` tables to construct player/monster entities,
  apply `attack_roll`/`damage_roll`, manage `Inventory` + encumbrance, and project
  leveling. It will depend on `dagger-rpg` (local path) and on generic engine crates
  (`entity-state`, `engine-spatial`, `svc-collision`). It never owns numbers.
- `dagger-studio-adapter` — stays read-only protocol-14 projection; later consumes
  `dagger-runtime` session facts for HUD/inventory visibility. No gameplay math.
- Planned: `dagger-world` — only when doors/water/enemy session state needs a shared
  home (the 6529 trigger: two crates need the same block/session state). Not created
  in Phase 0.
- `dagger-import` — stays offline CLI (`--format glb|mesh-json --texture-dir`);
  still emits `collision:"trimesh"` + scene sidecars. No RPG tables here.

**File format and place**

- **Committed `data/` vs generated `content/`** — `data/*.json` is committed,
  hand-authored, and reviewed. `content/` is generated output from
  `scripts/regenerate.sh` (GLB, mesh-json, texture publication, navgrid,
  sprites). `data/README.md` documents the convention.
- **JSON, not RON or Rust DSL** at Phase 0 — matches the engine's existing
  `content/projects/*.project.json` + `*.navgrid.json` + `*.scene.json` shape,
  avoids a new dep (`ron`), keeps tooling trivial (`serde_json`, `include_str!`).
  Each file has `schemaVersion` + typed arrays: `data/stats.json`,
  `data/weapons.json`, `data/armor.json`, `data/monsters.json`,
  `data/leveling.json`, etc. RON/JSONC with comments may be revisited if tables
  grow unwieldy, but JSON keeps the door open to the d20 generated-contract
  pattern later without committing to it now.
- **Formulas stay in one place** — pure fns in `dagger-rpg` (e.g.
  `pub fn max_health(endurance: u8, level: u8, class: &ClassDef) -> u32`) are
  tested vs DFU-known values; `dagger-runtime` call sites pass tables in, never
  hard-code. This satisfies "easy to tweak and eventually port out" — tweak the
  JSON + re-run `cargo test`, not scattered constants.
- **Provenance:** DFU semantics are donor evidence for numbers; original
  formulas are tested, not arm-waved. Keep `docs/source-provenance.md` current
  when donor revs change.

**Superseded horizontal delivery split**

- **Now (6683):** this document + `docs/design.md` system map sketch + empty
  `crates/dagger-rpg` with `data/README.md` and `hello_world` gate (2 tests).
  No gameplay logic.
- **Next (6684..6690):** 6684 populates `EntityStats`/`DerivedStats` + `data/stats.json`;
  6685 adds `attack_roll`/`damage_roll` + `data/weapons.json`/`armor.json` and the
  runtime attack authority; 6686 inventory model + RDB treasure flats; 6687 enemy
  roster (+ AI stub); 6688 leveling; 6689 HUD shell; 6690 controller wiring.

### Historical decisions from the first 6683 attempt (superseded)

1. **FP controller:** no change — still reference-only from demo `player.rs`; the
   Daggerfall controller lives in `dagger-runtime` and calls `engine_spatial` directly.
   No sibling dep added.
2. **Data ownership:** new decision — `dagger-rpg` owns tables+formulas, not
   `dagger-runtime` inline, not `arena2`, not demo/d20/roguelike imports. This was
   the open question in 6519's `Planned: dagger-content/dagger-world` — now resolved
   for the RPG half.
3. **Project/content machinery:** still hand-rolled `scripts/generate-project.py`; its
   scene generation stays out of `dagger-rpg` (visual vs RPG separation).
4. **UI:** superseded. The current Angular product mounts through Engine's
   public application-host contract.
5. **Copy rule:** tightened — every copied snippet must carry a `// Adapted from`
   provenance line with donor path + rev, and no sibling `path` dep may be added to
   `Cargo.toml`/`pnpm-workspace.yaml` without a `docs/companion-reuse.md` entry.
