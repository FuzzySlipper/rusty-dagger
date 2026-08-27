# Rusty Dagger code and C# migration map

**Status:** living migration inventory

**Snapshot:** 2026-08-26, checkout `663bc356914c480edaf05082c62b2339c4e14f50`

**Direction:** ordinary C# product code on Rusty Engine's generated NativeAOT path

This document answers two questions during the conversion:

1. What code and product meaning exists in this repository now?
2. Where does each concern belong as Rusty Dagger moves forward in C#?

Update this map when a migration changes ownership, retires a donor path, adds a
safe Engine dependency, or materially ports product behavior. It is an
inventory and status record, not a second SDK contract or a replacement for Den
task state.

## Authority and reading rules

- The current user request and owning Den task decide the work.
- `AGENTS.md` owns repository-specific direction.
- `[doc: rusty-engine/downstream-csharp-agent-brief]` owns the evolving shared
  downstream C# guidance.
- Den tasks and task threads own live sequence, scope, and review state.
- Current source owns implemented behavior.
- This document summarizes those authorities at the snapshot above. Recheck
  them before relying on a status that may have moved.

The central rule is:

> The product decides. The Engine guarantees.

`Dagger.Game` owns application/gameplay state, entities, catalogs, content
meaning, policy, orchestration, and renderer-neutral presentation facts. Rusty
Engine owns host update admission and reusable input, look, spatial, resource,
appearance, UI-stream, rendering, and other published mechanisms.
`Dagger.NativeProduct` is only the generated NativeAOT composition boundary.
TypeScript under `src/ui/` is DOM UI only.

Existing Rust, Angular, and gameplay TypeScript are donor evidence. Preserve
useful formulas, authored meaning, content facts, and user-visible behavior, but
do not preserve their runtime, evaluator, package, HTTP, Studio, or authority
topology merely because it exists.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| Active | Part of the current C# product build/run path. |
| Ported | The relevant behavior has a current C# owner. This does not imply every donor feature is present. |
| Partial | A useful vertical subset exists in C#, with named donor behavior still absent or simplified. |
| Donor | Read-only semantic or behavioral evidence for future C# work. |
| Tool donor | Offline extraction, generation, validation, or inspection code; not the active product runtime. |
| Generated | Derived output. Regenerate from its owning source instead of editing it as authority. |
| Retire | Obsolete runtime or authority topology that should disappear once no still-needed evidence or generation dependency remains. |
| Upstream | A reusable mechanism belongs in Rusty Engine, not in downstream C# or TypeScript. |
| Re-triage | A Den item or proof still describes the old Rust/Angular architecture and must be reconciled before execution. |

## Current conversion position

The first NativeAOT split is complete: safe product code lives in
`src/Dagger.Game/`, while `src/Dagger.NativeProduct/` selects the product and
receives generated ABI/service/lifecycle code under ignored `obj/` output.
The product builds and runs through `src/scripts/run-product.sh` and the sibling
Engine checkout.

The C# implementation is real but still compact. It currently covers lifecycle,
one persistent state owner, admitted content interpretation, input/look,
Engine-owned spatial movement, a small combat/loot slice, retained appearance
facts, and one HUD projection. It is not yet a complete port of the Rust and
TypeScript products.

The next priority architecture task is Den #7310. Its dependencies, Dagger
#7308 and Engine #7309, are done. #7310 preserves current behavior while
reorganizing the compact C# landing into explicit composition, entities, one
product-state owner, named services, and realtime-neutral Rust-admitted update
ordering. It explicitly precedes renewed gameplay-semantic porting.

Several older planned or in-progress tasks still name Rust, Angular, the HTTP
product service, or browser rendering as their intended owners. Those task
descriptions are migration evidence, not current implementation authority; they
need re-triage against the C# direction before work begins.

## Active product flow

```text
content/ product inputs
        |
        v
Rusty Engine C# product host admits files + ProductUpdate
        |
        v
Dagger.NativeProduct (generated ABI/lifecycle composition)
        |
        v
Dagger.Game
  content interpretation -> persistent state -> input/look -> spatial
  -> player combat -> enemy combat -> update sequence -> UI/appearance facts
        |                                           |
        v                                           v
Engine mechanisms and renderer                 dagger.hud UI projection
                                                     |
                                                     v
                                             src/ui DOM presentation
```

There is one host-admitted update. Dagger does not own another clock, loop,
timer, thread, or browser animation authority. The current explicit order in
`DaggerGame.Update` is:

1. derive a bounded delta from the admitted update;
2. advance cooldown state;
3. gather input and integrate look through Engine;
4. propose spatial movement through Engine;
5. resolve requested player melee;
6. resolve the active enemy attack;
7. advance the update counter;
8. publish HUD and appearance facts through Engine.

## Active C# source map

There are 11 current tracked C# source/project files. `Dagger.Game` is safe
ordinary product code; `Dagger.NativeProduct` contains only composition plus
generated boundary output.

| File | Concepts and concerns | Migration status |
| --- | --- | --- |
| `src/Dagger.Game/Dagger.Game.csproj` | `net10.0`, nullable/implicit usings, unsafe disabled, direct safe Engine project reference. | Active safe product project. |
| `src/Dagger.Game/DaggerGame.cs` | Product lifecycle, composition, admitted-update decoding/order, delta derivation, and single persistent `State` property. | Active vertical slice; #7310 will separate composition/update owners without changing behavior. |
| `src/Dagger.Game/GameState.cs` | World points; player/actor state; cooldowns, vitals, XP; motion/look; inventory/equipment/item records. | Active persistent state, but domain breadth and folder/service organization are partial. |
| `src/Dagger.Game/Catalogs.cs` | Records for stats, actors, weapons, encounters, enemy attacks, loot; player/rat/skeleton/longsword/two encounters. | Active narrow catalog slice. |
| `src/Dagger.Game/PrivateersHoldContent.cs` | Allowlisted admitted-file copy; project/entity/sprite interpretation; nav-cell conversion; collision decoding. | Active product content interpretation. Partial and coupled to current generated JSON inputs. |
| `src/Dagger.Game/Gameplay.cs` | Input/look, update intent, proximity encounters, player/enemy melee, loot award, keyed formulas and outcomes. | Active narrow gameplay slice; several service responsibilities remain co-located. |
| `src/Dagger.Game/SpatialGameplayService.cs` | Owns/disposes one Engine spatial session; installs collision/navigation; proposes controller steps and continues accepted state. | Active correct Engine-mechanism boundary. |
| `src/Dagger.Game/DaggerPresentation.cs` | Opens HUD stream, creates world/sprite appearances, publishes UI structured values and alive appearance facts. | Active renderer-neutral product facts; animation/corpses/effects/audio absent. |
| `src/Dagger.Game/UiValueBuilder.cs` | Safe owned construction of null/number/string/object `UiValue` arenas. | Active local helper; keep narrow rather than growing a schema/transport framework. |
| `src/Dagger.NativeProduct/Dagger.NativeProduct.csproj` | NativeAOT shared library; unsafe enabled only for generated code; references game, Engine, and source generator. | Active thin composition project. |
| `src/Dagger.NativeProduct/NativeProduct.cs` | One assembly attribute selecting `DaggerGame`. | Active and appropriately minimal. |

Ignored `src/Dagger.Product/obj/**` C# files are stale output from the retired
single-project spike, not another source lane. Current generated raw layouts,
service implementations, lifecycle exports, copying, handles, and native
status adaptation live under `Dagger.NativeProduct/obj/**`; never edit or
commit them.

The currently generated safe Engine context exposes Look, Spatial, Appearance,
Random, and UI families. Dagger uses them directly. It does not use JSON method
dispatch, reflection, a service locator, handwritten ABI declarations,
`GCHandle`, pointers, or raw status values.

Current C# coverage is intentionally narrow:

| Area | Current state |
| --- | --- |
| Lifecycle/composition | Ported vertical slice: create/start/update/pause/resume/shutdown/dispose with generated boundary. |
| State/time | One explicit owner and cooldown/update progression; no persistence, modes, or level progression. |
| Input/look | Keys, pointer delta/button, clear, movement and attack intent; other device/payload kinds absent. |
| Spatial | Player collision/navigation/controller step ported; actor navigation, patrol, chase, LOS, and targeting absent. |
| Content | Three exact files and selected scene/entity/sprite/collision fields only. |
| Catalog/combat | Player, rat, skeleton, longsword, two encounters, simple melee and one loot entry. |
| Appearance | Static mesh and alive enemy sprites; no camera publication, directional animation, corpses, treasure, effects, lights, or material breadth. |
| UI | Vitals, XP, encounter, outcome, Attack action only. |

The admitted project contains more authored content than the C# slice uses:
296 entities include 43 enemies, 43 corpse entities, and eight treasure
entities, while C# recognizes 28 enemies by name prefix (25 rats and three
skeletons). Presentation currently creates sprite resources for all authored
enemy entries but publishes only recognized live actors, leaving 15 created
resources unused and hiding dead actors instead of selecting authored corpses.

Conditional upstream boundaries are first-person camera/view publication,
audio, animation/mutable sprite frames, and persistence. Broader catalogs,
equipment, loot, AI, and content interpretation are Dagger product work, not
proven Engine blockers.

## Current TypeScript UI and host assembly

This path is active and deliberately small. It has no authored HTML file and no
world-rendering code. Engine's browser host owns the sole canvas; Dagger mounts
DOM UI in the separate downstream UI container.

| File | Concepts and concerns | Status |
| --- | --- | --- |
| `src/ui/main.ts` | Declares the local HUD projection shape, mounts HUD DOM, accepts only `dagger.ui.snapshot.v1`, writes vitals/encounter/outcome text, and emits the `attack` digital intent. | Active. Real projection/action wiring; no authoritative gameplay state. |
| `src/ui/styles.css` | Fixed overlay layout, vitals, outcome text, and interactive Attack button. The outer HUD ignores pointer events while the button opts in. | Active DOM presentation. Current layout is intentionally small and has not adopted presentation aspect bounds. |
| `src/scripts/build-ui.sh` | Strictly compiles `main.ts`, invokes bundle generation, and copies CSS. | Active build entrypoint. |
| `src/scripts/generate-browser-bundle.mjs` | Selects Engine's product browser host, realtime lifecycle, browser advance owner, runtime HTTP adapter, and exact UI stream/contract. | Active host assembly. Engine source owns the generated canvas/transport implementation. |
| `src/scripts/run-product.sh` | Builds the UI, NativeAOT-publishes the product library, and launches Engine's C# runtime with bundle/content/port. | Active product launch. |
| `src/playtest.json` | Broker-owned launch and headless browser diagnostic configuration. | Active diagnostic; stale `rusty-dagger-csharp-trial` label. |
| `src/.gitignore` | Ignores both current .NET/bundle/native outputs and old `Dagger.Product` output names. | Active housekeeping with one harmless legacy entry. |

The generated `src/browser-bundle/` currently contains `index.html`,
`main.js`, `bridge.js`, `runtime-adapter.js`, Engine's
`product-browser-host.js`, and compiled UI JS/CSS. It is ignored and must never
be reviewed as a second authored product runtime. Engine runtime assembly
injects the renderer preload descriptor when it serves the bundle.

The semantic action round trip is:

```text
Attack DOM click -> Engine host intent claim -> runtime /input adapter
-> ProductInput(kind 8, label "attack") -> GameplayInput
-> C# combat/state -> dagger.hud projection -> Engine subscriber -> DOM
```

The current HUD needs no new Engine capability. Nested projection validation is
lightweight and the TypeScript interface is locally declared rather than
generated, so contract evolution should remain narrow and coordinated.

## Gameplay TypeScript semantic donor

This tree is the densest compact catalog of authored gameplay meaning. It is
read-only donor material. The expression/program/envelope/package topology is
obsolete; formulas and catalogs should be ported into direct, named C# owners.

| File | Concepts and concerns | C# migration status |
| --- | --- | --- |
| `gameplay/src/authoring/definitions.ts` | Types/builders for stats, actors, tracks, behavior, actions, items, equipment, rules, loot, encounters. | Partial records in `Catalogs.cs`; most fields absent. Retire generic builder topology. |
| `gameplay/src/authoring/expressions.ts` | Closed exact expression adapter plus Dagger equipment/armor leaves. | Do not port tree/evaluator; only a direct formula subset exists in `CombatMath`. |
| `gameplay/src/authoring/programs.ts` | Sequence/conditional/spend/damage/intent program grammar. | Direct C# control flow partially covers behavior; retire grammar/runtime. |
| `gameplay/src/authoring/envelope.ts` | Schema-2 binary64 package, provenance, canonical JSON, fingerprint. | Retire runtime/package topology. |
| `gameplay/src/authoring/mod.ts` | Barrel for old authoring DSL. | Retire. |
| `gameplay/src/catalogs/stats.ts` | 9 attributes, 35 skills, 3 tracks, 7 armor parts, XP/level. | Six-field `StatBlock` and three player tracks are a small subset. |
| `gameplay/src/catalogs/actions.ts` | Five actions, hit formula/evidence, weapon/armor leaves, stamina costs, cooldowns. | Rat/skeleton/player melee partially ported; thief/power attack and evidence breadth absent. |
| `gameplay/src/catalogs/actors.ts` | Player and thief, starting loadout, behavior, XP, loot. | Player partial; thief absent. |
| `gameplay/src/catalogs/monsters.ts` | 42 classic monsters, stats/skills/health/armor/attacks/XP/team/metal gates/loot. | Only rat and skeletal warrior records exist in C#. |
| `gameplay/src/catalogs/derived.ts` | 20 named formulas and 35 skill advancement multipliers. | Small combat/initial-value subset embedded directly; named rule catalog absent. |
| `gameplay/src/catalogs/items.ts` | 31 items: weapons, armor, shields, gold, arrows; materials, hands, weights, prices. | Only longsword damage/skill is typed; other initial inventory entries are strings. |
| `gameplay/src/catalogs/equipment.ts` | 25 paper-doll slots, capacity, hand exclusivity, armor classification. | Not ported; C# only has right-hand weapon. |
| `gameplay/src/catalogs/loot.ts` | 22 classic tables, gold ranges, category probabilities, repeat/level semantics. | One simplified skeleton gold award only. |
| `gameplay/src/catalogs/encounters.ts` | Two current encounters, member IDs, names/objectives, route codes. | Names/objectives/member IDs ported; route and multi-member shape absent. |
| `gameplay/src/catalogs/rules.ts` | Empty catalog and one available rejection grammar. | No active semantics to port. |
| `gameplay/src/packages/dagger-core.ts` | Composes all catalogs into package `dagger/core`. | Not consumed by C#; retire composition topology. |
| `gameplay/scripts/materialize.mjs` | Converts compiled package modules into canonical checked-in JSON. | Generation-only donor tool. |
| `gameplay/scripts/verify-expressions.mjs` | Rejects invalid/retired expression leaves and malformed payloads. | Evaluator-adapter proof only; retire with topology. |
| `gameplay/tsconfig.json` | Independent TS compilation config. | Donor build config. |
| `data/gameplay/dagger-core.package.json` | 113,948-byte materialized package with provenance. | Generated donor output; no C# consumer. |

The materialized package contains 44 actors (player, thief, 42 monsters), five
actions, 31 items, two encounters, 20 derived rules, 25 equipment slots, 22
loot tables, and no active rule definitions. Actor IDs/mobile IDs and internal
loot/action/item references are structurally consistent in the committed
artifact.

Key semantic inventory:

- combat hit admission clamps a d100 threshold to 3–97 and combines skill,
  struck armor, luck/agility differentials, dodging, and player evidence;
- damage uses weapon or attack ranges plus strength-derived modifiers;
- authored derived rules cover damage/to-hit, recovery, encumbrance, breath,
  fatigue, spell points, hand-to-hand, backstab, level/XP, HP gain, reflex
  scaling, and skill-use advancement;
- monsters include multi-attack data, material-to-hit gates, loot references,
  and far broader stats than current C#;
- equipment and loot are real product models, not merely UI decoration.

Important known divergences in the current slice:

- the rat's C# hand-to-hand skill is `35` (matching the donor), but C# starts
  at maximum rather than rolling the 9–16 health range;
- the skeleton's authored action is long-blade-shaped, while C# resolves enemy
  hit chance with `HandToHand`; C# also starts at maximum rather than rolling
  17–66 health;
- skeleton table `H` is reduced to 2–10 gold, omitting other categories;
- player maxima align, but most attributes/skills and item metadata are absent;
- the donor `xp-level` tree is `floor(xp / 500)` while its comment expects a
  caller-owned base level; preserve that distinction during a direct port.

These are migration facts, not authority to broaden #7310. Semantic porting is
explicitly sequenced after the architecture pass.

## Angular product donor

This is a polished but inactive Angular + Rust HTTP product. It contains
substantial user-visible behavior worth preserving selectively, but its server,
polling, JSON DTO, sampled-input, retained-render-diff, and Angular authority
topology must retire.

| File | Concepts and concerns | Migration disposition |
| --- | --- | --- |
| `apps/dagger-product/proxy.conf.json` | Proxies `/api` to the old server on port 4274. | Retire with server. |
| `apps/dagger-product/tsconfig.app.json` | Angular application compilation config. | Retire when Angular donor is no longer needed. |
| `apps/dagger-product/src/index.html` | Angular document shell and application mount. | Replaced by generated current host shell. |
| `apps/dagger-product/src/main.ts` | Fetches bootstrap, mounts Engine application-host and Angular UI, enables developer commands, configures one renderer and 4:3–16:9 bounds. | Donor behavior/config; old composition path is retired. |
| `apps/dagger-product/src/app.component.html` | Full HUD; inventory/equipment/carried grid; loot; character sheet; Lab explorer, sprites, content, definitions, combat, encounters, progression, and tooling panels. | Major UI/behavior donor. None of the old HTTP authority should migrate. |
| `apps/dagger-product/src/app.component.ts` | Polling, modal/focus/Escape keys, inventory/loot commands, notices, content navigation, stale-poll fencing, and error UI. | Preserve useful interaction/accessibility behavior selectively; retire polling and direct renderer access. |
| `apps/dagger-product/src/product-api.service.ts` | HTTP readout/reset/equip/grid/loot command facade. | Retire transport; C# owns product actions/state. |
| `apps/dagger-product/src/lab-tools-api.service.ts` | HTTP content jump, item grant, sprite index, and manifest save. | Retire transport; re-scope product-useful tooling separately. |
| `apps/dagger-product/src/product-contract.ts` | Large browser DTO model for definitions, actors, combat, encounters, content, inventory, loot, progression, and notices. | Semantic/read-model donor; do not port as a JSON protocol. |
| `apps/dagger-product/src/product-runtime.ts` | Bootstrap/resource load, sampled physical input, state polling, retained renderer updates, pointer lock, product-local keys, audio resume, disposal. | Retire runtime/renderer authority. Preserve only useful input/focus behavior under current Engine host contracts. |
| `apps/dagger-product/src/developer-command.ts` | Engine command client plus Dagger scenario schemas. | No current equivalent; re-scope after a safe C# developer-command contract exists. |
| `apps/dagger-product/src/sprite-contract.ts` | Normalizes sprite/weapon/effect/texture manifests, UVs, sizes, animations, orientations, and paths. | Strong authored-content inspection donor. |
| `apps/dagger-product/src/sprites-panel.component.ts` | Sprite filters/playback/orientation review and manifest field editing/save/discard. | Product-useful Lab/tool donor; old whole-manifest HTTP save path retires. |
| `apps/dagger-product/src/sprites-panel.component.html` | Sprite review, playback, frame/UV inspector, orientation controls, editing UI. | UI/tool donor. |
| `apps/dagger-product/src/styles.css` | Classic HUD/window styling, modal/accessibility layout, fixed inventory geometry, Lab, sprite blitting, responsive rules. | Visual/interaction donor; prune unused historical selectors during an owning UI port. |

Legacy user-visible behaviors worth deliberate preservation include modal
`inert`/`aria-hidden`, live regions, focus return to gameplay, Escape repeat
protection and close-first handling, stable inventory geometry, authored/live
distinction, sprite sequence/orientation inspection, and bounded 4:3–16:9
presentation. The current UI has only vitals, one encounter summary, one outcome
string, and Attack.

Current counterparts are partial: content/project interpretation, basic player
and actor state, movement/look, proximity encounters, small combat/loot,
world/sprite appearance, and HUD projection exist in C#. Inventory/equipment
actions, loot UI, character/progression surfaces, notices, content browser,
sprite Lab/editor, developer scenarios, and rich diagnostics do not.

The Angular graph is already stale against its Engine dependency: focused
typecheck/build currently fail because `RustyApplicationUiContext` no longer
exposes the renderer property used by the legacy component/bootstrap. This is
additional retirement evidence, not a reason to repair the donor path.

Related stale carriers are `angular.json`, the `product:*` package scripts,
Angular dependencies/lock entries, `scripts/serve-dagger-product.sh`, the manual
browser diagnostic, `.den-serve.json`, and `.den-playwright.json`. Gameplay TS
packages share the root lockfile, so retiring Angular does not imply deleting
all Node dependencies without a separate dependency audit.

## Rust donor and tool map

All root Rust crates are inactive product code under the current direction.
Some offline import behavior and test vectors remain valuable evidence, but no
Rust package below is a parallel production lane.

### `crates/arena2`

This crate is a format-decoding toolkit for operator-supplied Arena2 data. It is
not a C# runtime dependency. Generic decoding may remain offline or move to an
explicit Engine-owned source-import capability; Daggerfall-specific mappings
remain product/content meaning.

| File | Concepts and concerns | Migration disposition |
| --- | --- | --- |
| `Cargo.toml` | Dependency-free Arena2 parser package. | Offline tool donor. |
| `src/lib.rs` | Coordinate/UV/rotation conventions and checked little-endian cursor/range helpers. | Format-tool foundation; do not copy into runtime C#. |
| `src/test_fixtures.rs` | Synthetic BSA/PAK fixtures. | Importer-test donor only. |
| `examples/dump-texture.rs` | Texture inspection/export CLI example. | Offline donor. |
| `src/bsa.rs` | Named/numeric BSA parsing and checked record lookup. | Reusable source-format decoder; offline/upstream import concern. |
| `src/pak.rs` | Climate/politics PAK RLE and climate mapping. | Decoder is tooling; climate meaning is Dagger content. |
| `src/palette.rs` | PAL parsing and indexed-to-RGBA conversion. | Offline decoder; transparency interpretation is Dagger-specific. |
| `src/fnt.rs` | Fixed 240-glyph 16x16 font parsing. | Offline decoder; emitted font/art is UI content. |
| `src/img.rs` | IMG parsing including headerless 320x200 UI form. | Offline decoder and Dagger UI provenance. |
| `src/snd.rs` | BSA sound records and unsigned 8-bit/11025 Hz WAV conversion. | Offline decoder; clip selection is product content. |
| `src/dfrandom.rs` | Classic deterministic LCG and inclusive ranges. | Semantic donor; do not create a duplicate runtime RNG owner. |
| `src/texture_table.rs` | Climate/location texture remaps and seeded location tables. | Daggerfall content semantics. |
| `src/maps.rs` | MAPS names/tables/dungeons/blocks/location IDs/map pixels. | Binary decoder plus Dagger location meaning. |
| `src/arch3d.rs` | ARCH3D geometry, packed UVs, face matrices, version offsets. | Offline geometry decoder. |
| `src/cif.rs` | Weapon CIF image/animation and RLE records. | Offline decoder; action meaning comes from product catalogs. |
| `src/texture.rs` | TEXTURE archives, virtual textures, frames, and RLE variants. | Offline decoder; Engine owns retained texture resources. |
| `src/mobile.rs` | Partial mobile catalog, corpses, attacks, orientations, animation, sizes, bearing sectors. | Strong product/presentation donor; explicitly incomplete. |
| `src/rdb.rs` | RDB model/light/flat/action/resource parsing plus door/enemy/treasure interpretation. | Parser is tooling; classifications and exceptions are Dagger content meaning. |

### `crates/dagger-import`

| File | Concepts and concerns | Migration disposition |
| --- | --- | --- |
| `Cargo.toml` | Offline extractor dependencies. | Tool donor. |
| `src/main.rs` | CLI for GLB, mesh JSON, scene sidecars, textures, billboard/enemy atlases, combat/UI assets, manifest edit preservation, and generated cleanup. | Offline orchestration only. |
| `src/dungeon.rs` | Central MAPS/BLOCKS/ARCH3D/TEXTURE conversion; transforms, flat classification, doors, lights, collision, enemies, treasure, and scene metadata. | Product-specific import/content donor. Keep classifications with Dagger content, not Engine infrastructure. |
| `src/glb.rs` | Minimal GLB writer and door nodes. | Generic-ish offline writer; unsafe byte view must not enter safe `Dagger.Game`. |
| `src/meshjson.rs` | Authored mesh/collision/material source for Engine asset import. | Engine-boundary artifact producer; confirm the supported upstream import contract before replacing it. |
| `src/png.rs` | Minimal RGBA PNG encoder. | Generic offline utility. |
| `src/combat_assets.rs` | Weapon/effect/audio record identities, atlas packing, action alignment, and edit preservation. | Strong combat/presentation catalog donor. |
| `src/ui_assets.rs` | Classic HUD/window/image/font extraction plus authored UI PNGs and provenance. | Strong UI content donor. |
| `src/bin/dagger-validate-sprites.rs` | Manifest/PNG/ground-truth validation and JSON/HTML reports. | QA/proof tooling; not runtime authority. |

The import flow is currently:

```text
Arena2 source -> arena2 decoders -> dagger-import
-> GLB / mesh JSON / sidecars / atlases / manifests
-> Engine asset import + Python project generation
-> committed content -> current C# content admission
```

Current C# does not parse BSA, PAK, MAPS, RDB, ARCH3D, CIF, TEXTURE,
IMG, FNT, or SND. It consumes already generated project, navgrid, collision,
and referenced texture content. That separation is sound. Replacement tooling
must preserve operator-edited pivots, sizes, FPS, loops, sequences, and
alignment fields rather than treating all generated manifests as disposable.

### `crates/dagger-rpg`

This crate is the legacy package compiler, generic evaluator, resolution policy,
Engine mechanics bridge, equipment/inventory binder, loot generator, and
progression implementation. Its 60 integration tests and seven unit tests are a
valuable semantic checklist. Its IR/compiler/evaluator/transaction topology is
not the target C# architecture.

| File | Concepts and concerns | Migration disposition |
| --- | --- | --- |
| `Cargo.toml` | Rust gameplay package over Engine, serde, and JSON. | Retire active package. |
| `src/lib.rs` | Forbids unsafe, documents Rust package/evaluation/mutation authority, exports resolution. | Authority prose/topology retired; semantic code remains donor evidence. |
| `src/resolution/mod.rs` | Aggregates compile, composed leaves, eval, loot, mechanics, model, policy, progression. | Retire module topology. |
| `src/resolution/model.rs` | Authored/compiled schema, closed programs/leaves, product and Engine state/readouts/events/faults, armor hit table, material ranks. | Port needed domain records and behavior explicitly; do not recreate the giant carrier model. |
| `src/resolution/compile.rs` | Package identity/schema/quotas, ID/reference/vocabulary/equipment/loot validation, expression compilation and provenance. | Retire compiler/admission boundary. Preserve product validation rules with the C# catalogs that need them. |
| `src/resolution/composed.rs` | Dagger exact leaves for equipped skill/dice and struck armor over generic Engine expression machinery. | Retire adapter/evaluator machinery; implement named C# formula inputs directly. |
| `src/resolution/mechanics.rs` | Stat/track bounds, synthetic maxima, equipment slots/capacity/exclusivity, armor/shield contributions, impact kind. | Strong equipment/stat semantic donor; Engine mechanisms remain direct safe services. |
| `src/resolution/eval.rs` | Derived/action evaluator, evidence/roll materialization, weapon/unarmed selection, track helpers, actor spawn, inventory/equipment binding, candidate operations. | Port product rules/state transitions cluster by cluster; retire generic evaluator. |
| `src/resolution/policy.rs` | Admit/gather/check/plan/commit action lifecycle, rules, spend, damage, metal gates, revision checks, readouts. | Behavioral donor for a named C# action/combat owner, not a mandatory policy framework. Reach/cooldown remain orchestration concerns. |
| `src/resolution/loot.rs` | Deterministic classic loot adaptation, category slots/chances/scaling, evidence, containers and item binding. | Strong direct C# loot-service donor; do not request an Engine loot runtime. |
| `src/resolution/progression.rs` | Player kill XP, level threshold, bounded HP rolls, health update, reset/restore. | Direct C# progression donor. Current C# only accumulates XP. |
| `tests/resolution.rs` | 60 integration tests covering catalogs/admission, combat/evidence, equipment, loot, and progression. | Donor acceptance inventory; not a current C# gate. |

The old flow was:

```text
materialized TS package -> Engine package admission -> Rust compiler
-> composed expression IR -> Dagger resolution policy/evaluator
-> Engine mechanics/entity mutation -> loot/progression/readouts
```

The right migration unit is a semantic cluster, not this pipeline. Typed C#
catalogs, formula services, state, action admission/ordering, equipment, loot,
progression, and presentation can be ported as ordinary product code as their
tasks become current. No Engine request is justified for a Dagger package
compiler, generic evaluator, loot runtime, or product vocabulary.

The legacy tests expose useful behavior such as shared player/AI action
semantics, stamina spend on miss, bounded roll evidence, body-part armor,
material gates, dual wield/two-hand conflicts, capacity, 22 loot tables,
deterministic category generation, player-only XP, and per-level HP rolls.
Transfer focused vectors only when the corresponding C# behavior is in scope.

Donor caveats to resolve rather than blindly reproduce include evidence bounds
that can select the wrong subject, progression stats omitted from live-stat
projection, different zero-fill behavior between derived/live evaluation,
late loot/material reference failures, multi-step flows without one outer
transaction, declared-but-unconsumed loot evidence, and negative damage being
clamped rather than rejected.

### `crates/dagger-runtime`

| File | Concepts and concerns | Migration disposition |
| --- | --- | --- |
| `Cargo.toml` | Runtime package over `arena2`, `dagger-rpg`, and Rusty Engine; declares five tool/proof binaries. | Retire as active product package. |
| `src/lib.rs` | Module surface plus broad behavioral test corpus. | Tests are a semantic acceptance inventory; retire Rust implementation/proofs after equivalent behavior has an owning C# test. |
| `src/runtime.rs` | Central session/product state, encounters, combat, patrol, progression, inventory/equipment, loot, notices, reset/jump, readouts, and Engine calls. | Primary product donor. Port concerns into ordinary C# state/services; retire the runtime topology wholesale. |
| `src/project.rs` | Strict project/scene admission, collision authority, player config, enemy/treasure extraction. | Partially represented by `PrivateersHoldContent`; strict admission and treasure breadth remain donor semantics. |
| `src/player.rs` | Input validation, look, camera-relative movement, fixed substeps, receipts/facts. | Partially represented by `GameplayInput` and `SpatialGameplayService`; keep behavior evidence, not the Rust owner. |
| `src/patrol.rs` | Deterministic nav-aware patrol/chase/attack modes and attack intents. | Port to a named C# gameplay service; current C# has only proximity encounter selection. |
| `src/navgrid.rs` | Static-mesh/raycast nav derivation, slope/headroom filtering, grounding. | Current C# consumes the precomputed grid. Runtime derivation would need a safe Engine spatial query rather than downstream machinery. |
| `src/animation.rs` | Deterministic environment/enemy directional animation, attack/hurt one-shots, frame diffs. | Port animation state/timing facts when needed; Engine owns frame/atlas realization. |
| `src/directional.rs` | Eight-sector heading and glTF/DFU orientation conversion. | Presentation-semantic donor; Engine owns billboarding/rendering. |
| `src/combat_assets.rs` | Validated weapon/effect/audio catalog and content identities. | Port catalog/content meaning; resource and audio loading stay upstream. |
| `src/bin/dagger-walkthrough.rs` | Collision, grounding, traversal, wall blocking/sliding, and look proof. | Retire binary; preserve only focused current-path behavior checks. |
| `src/bin/dagger-navgrid.rs` | Offline navgrid derivation/validation and committed artifact writer. | Tool donor while the committed navgrid is consumed; not a runtime path. |
| `src/bin/dagger-derive-route.rs` | Bounded BFS/route artifact generator. | Retire from runtime; re-home only if future authoring work proves it useful. |
| `src/bin/dagger-sprite-frames.rs` | Legacy TCP sprite/patrol/animation endpoint. | Retire process and JSON endpoint; transfer semantic facts only. |
| `src/bin/dagger-gameplay-check.rs` | Offline combat/inventory/loot/progression proof. | Retire binary; port useful vectors after matching C# behavior exists. |

### `crates/dagger-studio-adapter`

| File | Concepts and concerns | Migration disposition |
| --- | --- | --- |
| `Cargo.toml` | Studio adapter and old product-server package/dependencies. | Retire active package. |
| `src/lib.rs` | Adapter exports for stdio protocol and render bundles. | Retire shell. |
| `src/project_access.rs` | Root containment, symlink/regular-file checks, and content identity. | Security/tooling donor only; do not preserve the adapter boundary. |
| `src/readout.rs` | Studio hierarchy, asset/entity/scene inspection, and placeholder sections. | Preserve useful authored-content inspection concepts only if a future owning Studio surface needs them. |
| `src/presentation.rs` | Content-addressed texture/audio loading and JSON render-diff bundle construction. | Retire JSON renderer adapter; current C# publishes appearance facts directly. |
| `src/protocol.rs` | Line-delimited protocol v14 for describe/open/read/close; rejects mutation. | Retire protocol and wire vocabulary. |
| `src/bin/dagger-studio-adapter.rs` | Stdio adapter entrypoint. | Retire. |
| `src/bin/dagger-product-server/main.rs` | Old Rust HTTP product entrypoint and flags. | Retire. |
| `src/bin/dagger-product-server/product_server.rs` | HTTP routes, JSON command queue, asset serving, manifest writes. | Retire server/queue/transport topology; semantic actions belong in direct C# and Engine host surfaces. |
| `src/bin/dagger-product-server/connected_application.rs` | Old composition root and 50 ms loop; maps commands into runtime and coalesces retained updates. | Retire host loop/diff queue; one Engine-admitted update owns current ordering. |
| `src/bin/dagger-product-server/developer_commands.rs` | Engine inspect/mechanics/admin commands plus Dagger scenarios. | Port product-useful scenarios only after a safe published C# developer-command surface exists. |
| `src/bin/dagger-product-server/diagnostics.rs` | Debug overlays, live/nav anchors, debug projection. | Express useful facts through safe Engine APIs when available; retire Rust composition. |
| `src/bin/dagger-product-server/live_presentation.rs` | Enemy atlases, patrol transforms, animation, corpse visibility, retained updates. | Port presentation facts; Engine owns retained resources and realization. |
| `src/bin/dagger-product-server/melee_presentation.rs` | Viewmodel/effect phases, swing/hit audio, impact sprites. | Port action-phase facts; requires named Engine presentation/audio capabilities. |

The retired flow joined its own loop, HTTP commands, `DaggerRuntime`, retained
diff queues, and browser presentation. None of those carriers should survive.
The valuable product semantics are patrol/AI, richer combat/progression,
inventory/equipment/loot, animation and corpse transitions, melee presentation,
audio timing, admission rules, and inspection scenarios.

Conditional upstream needs exposed by this donor are typed appearance animation
or frame updates, audio/melee presentation services, collision raycast/ground
queries for runtime derivation, and a safe developer-command contract. They are
not all blockers for the current slice; request each only when an owning Dagger
behavior actually needs it.

## Build, launch, generation, and proof surfaces

| Surface | Current role | Migration status |
| --- | --- | --- |
| `src/scripts/build-ui.sh` | Compiles `src/ui/main.ts`, assembles the Engine browser host bundle, and copies UI CSS. | Active. |
| `src/scripts/generate-browser-bundle.mjs` | Calls Engine's product-browser-host artifact and configures realtime browser advancement plus the `dagger.hud` projection contract. | Active host assembly; generated output only. |
| `src/scripts/run-product.sh` | Builds UI, NativeAOT-publishes `Dagger.NativeProduct`, and launches Engine's `csharp-product-runtime` with content. | Active run path. |
| `src/browser-bundle/` | Assembled Engine host, runtime adapter, and UI output. | Generated and ignored. |
| `src/Dagger.Game/{bin,obj}/`, `src/Dagger.NativeProduct/{bin,obj}/` | .NET build and generated-source output. | Generated and ignored. |
| `.gitignore`, `src/.gitignore` | Exclude root Rust/Node/web/local outputs and current/stale .NET/bundle/native outputs. | Active repository housekeeping; ignored generated files remain non-authoritative. |
| `Cargo.toml`, `Cargo.lock` | Root workspace for the inactive Rust product/tool graph. | Donor/tool topology; not a current C# product manifest. |
| `package.json`, `angular.json`, `apps/dagger-product/**` | Angular, gameplay-authoring, and old web-product graph. | Donor topology; root scripts are not current C# gates. |
| `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `tsconfig.json` | Workspace/build resolution for Angular and shared gameplay-authoring dependencies. | Mixed donor metadata; audit dependencies before retiring Angular because gameplay tooling shares the lockfile. |
| `scripts/regenerate.sh` | Rebuilds the legacy Rust import/project/nav/validation chain. | Tool donor; still explains committed content provenance, not a product runtime gate. |
| `scripts/generate-project.py` | Materializes Studio-era project JSON and asset catalogs from imported artifacts. | Tool donor and current content provenance; eventual owner must be decided per content-admission work. |
| `scripts/audit-engine-boundary.sh` | Checks the old adjacent Rust facade, forbidden selective Engine dependencies, and retired native/Studio host references. | Focused legacy-topology audit invoked by the old aggregate gate; not proof of the active C# boundary. |
| `scripts/verify.sh` | Runs the broad pre-pivot Rust, Angular, gameplay TS, Studio-adapter verification graph. | Legacy gate; prohibited as proof for ordinary C# work unless explicitly requested. |
| `.github/workflows/ci.yml` | Provisions Rust/Node dependencies and invokes the legacy aggregate gate. | CI topology is stale relative to the active C# direction and needs a separately scoped migration. |
| `.rusty-crew-review.json` | Requires the legacy `ci` check and names `scripts/verify.sh` as local verification. | Managed-review routing metadata is stale relative to the C# path; reconcile under an owning CI/review task. |
| `src/playtest.json` | Launches the current C# path for broker-owned browser playtesting. | Active diagnostic config; its project label still says `rusty-dagger-csharp-trial` and should be corrected in an owning task. |
| `product-playtest.scenario.json` | Describes a directional-sprite gallery mission served by the old product. | Donor diagnostic; not evidence for current C# behavior. |
| `.rusty-studio.json`, `scripts/check-adapter.py` | Launch and test the old Studio adapter protocol. | Donor/retire topology. |
| `scripts/serve-dagger-product.sh`, browser check scripts | Build/serve and exercise the Angular + Rust HTTP product. | Donor/retire topology. |

## Content and generated-data map

| Surface | Concern | Authority and migration treatment |
| --- | --- | --- |
| `content/projects/privateers-hold.project.json` | Authored scene, entities, sprite assets, and player start consumed by current C#. | Active admitted product input, although produced by the older generator chain. |
| `content/projects/privateers-hold.navgrid.json` | Planar navigation cells consumed by current C#. | Active admitted product input; generation remains legacy-tool-owned. |
| `content/projects/encounter-gallery.project.json`, `content/projects/encounter-gallery.navgrid.json` | Generated bounded gallery scene, sprite assets, and walkable cells used by the old directional-sprite product diagnostic. | Generated donor/diagnostic inputs; current C# launch selects Privateer's Hold instead. |
| `content/imported/privateers-hold.static-mesh.json` | Collision vertices/triangles consumed by current C#. | Active admitted product input; generated. |
| `content/privateers-hold.glb`, `content/privateers-hold.mesh.json`, scene JSON | Intermediate/import and scene facts from Arena2 extraction. | Generated/tool-donor inputs; not read directly by current C# runtime. |
| `content/imported/*.json` | Imported asset catalog, provenance, and static-mesh payload. | Generated; only the static mesh is directly read by current C#. |
| `content/textures/**` and manifests | World, billboard, enemy, and combat image assets plus generated metadata. | Product assets and donor provenance. Current C# reaches referenced sprite textures through Engine appearance/resource APIs. |
| `content/ui/**` | HUD, inventory, character-sheet, fonts, and UI art. | Mostly legacy Angular product assets; preserve for later DOM UI migration where useful. |
| `content/audio/**` | Combat audio assets. | Product donor inputs; no current C# audio owner/API use. |
| `content/validation/**` | Sprite review HTML/JSON. | Generated proof artifacts, not runtime authority. |
| `data/gameplay/dagger-core.package.json` | Materialized TypeScript gameplay package. | Generated donor output; never edit as gameplay authority. |
| `data/encounters/**` | Encounter-gallery and Privateer's Hold authored encounter lists. | Donor product data; current C# hard-codes only two encounters instead of loading these files. |
| `data/sprite-names.json` | Friendly billboard naming metadata. | Import/tool donor data. |
| `data/ui-authored-assets.json`, `data/ui-original/**` | Authored UI asset registry and source art. | UI donor/source assets; preserve provenance. |
| `artifacts/**` | Built web outputs and manual evidence. | Generated/ignored; never source authority. |

## Concept migration matrix

| Product concern | Donor evidence | Current owner/status | Next boundary |
| --- | --- | --- | --- |
| Lifecycle and update admission | Rust product server/runtime loops | Engine admits lifecycle and updates; `DaggerGame` implements `IEngineProduct`. **Ported.** | #7310 reorganizes update phases without adding a loop. |
| Persistent product state | Rust runtime/session models and TS package state | `DaggerGameState`, `PlayerState`, and `ActorState`. **Partial.** | Make one state owner explicit; retain current outputs. |
| Composition and service ownership | Concentrated spike code plus older adapters | `DaggerGame` constructs input, spatial, and presentation owners. **Partial.** | #7310 separates explicit composition and named services. |
| Time/update sequence | Rust turn/runtime counters | C# derives delta from admitted updates and tracks `Updates`. **Partial; terminology is stale.** | Rename the misleading turn-shaped deterministic keys/update counter under #7310 while preserving behavior. |
| Input and look | Rust/browser input paths | `GameplayInput` plus Engine `Look`. **Partial.** | Preserve current movement/attack mappings; future semantic actions stay typed and direct. |
| Spatial movement/collision/navigation | `dagger-runtime` navigation/controller code and generated navgrid | Engine `SpatialSession`, collision/navigation replacement, and character step; Dagger owns intent/config/state continuation. **Ported vertical slice.** | Missing gameplay breadth should request purpose-neutral Engine services rather than add downstream spatial machinery. |
| Scene/content admission | Importer, project generator, Studio project schema | C# copies three admitted files and interprets project actors, sprite facts, nav cells, and collision. **Partial.** | Define future content/resource admission through Engine when the safe family evolves; do not revive Studio/HTTP topology. |
| Catalogs | `gameplay/src/catalogs/**`, materialized package, Rust resolution models | Small hard-coded C# player, rat, skeleton, longsword, attacks, loot, encounters. **Partial.** | Port product semantics incrementally after #7310; do not port AST/evaluator/package machinery. |
| Combat formulas | TS expressions/catalogs and `dagger-rpg` mechanics/eval | C# hit chance, keyed hit/damage rolls, cooldown/stamina, enemy melee. **Partial.** | Reconcile formula fidelity and missing action/spell/effect breadth in focused semantic tasks. |
| Loot and inventory | TS/Rust catalogs and Angular inventory behavior | C# awards one skeleton loot entry and stores a flat inventory with right-hand weapon. **Early partial.** | Inventory/equipment actions, stack semantics, UI projection, and broader tables remain to port. |
| Encounters and AI | Authored encounter data, Rust patrol/runtime | C# selects two proximity encounters and executes stationary cooldown melee. **Early partial.** | Patrol, navigation intent, spell casting, and broader actor behavior remain donor semantics. |
| Appearance/world publication | Rust presentation/server and Angular host | C# publishes one static mesh and alive sprite facts through Engine appearance. **Ported vertical slice.** | Animation/directional frames/material breadth require named Engine capabilities as published. |
| HUD/UI projection | Angular product contract and templates | C# publishes health/stamina/magicka/XP, active encounter, and last outcome; `src/ui` renders the DOM. **Early partial.** | Port useful inventory, loot, character, menu, notices, accessibility, and focus behavior without old HTTP/Angular authority. |
| Audio | Importer combat manifests and audio files | No current C# publication/service use. **Not ported.** | Stop at missing Engine audio family; do not play audio directly in downstream browser code. |
| Persistence/settings | Old planned Rust/Angular task descriptions | No current C# persistence/settings owner. **Not ported; old tasks need re-triage.** | Use the safe Engine persistence family when published; product schema/policy stays C#. |
| Studio/Lab/developer commands | Rust Studio adapter/server and Angular Lab | No current-path equivalent beyond donor files. **Not ported / topology retired.** | Re-scope product-useful inspection and commands against C# and named Engine APIs. |
| Arena2 extraction | `arena2` and `dagger-import` | Offline donor/tool chain produces committed content. **Not active runtime.** | Keep only as explicit import tooling when authorized; reusable admission/asset mechanisms belong upstream. |

## Known migration gaps and cautions

- The active C# HUD is much smaller than the legacy Angular experience.
- The active catalog slice is hard-coded and represents only a few actors,
  encounters, one weapon, one enemy loot entry, and a small stat subset.
- Current content parsing deliberately admits three known JSON files directly.
  This is working product interpretation, not a promise that the current raw
  JSON shape is the final Engine content API.
- `DaggerPresentation` creates Engine appearances and publishes facts, which is
  the correct ownership direction. Animation, audio, materials, richer UI, and
  other missing families must stop at the safe Engine boundary when absent.
- `UiValueBuilder` is downstream safe-value construction around an Engine
  contract. Do not let it grow into a generic schema/transport framework.
- Current deterministic combat keys contain `turn:` even though updates are
  realtime-neutral. #7310 explicitly owns the terminology/sequence cleanup.
- The broad root CI and verification scripts still prove the retired Rust and
  Angular topology. Their green state would not certify the active C# product.
- Content is currently consumed by the C# runtime but still generated by legacy
  Rust/Python/Studio-shaped tools. Runtime migration and content-pipeline
  migration are separate decisions.
- No migration status in this document authorizes deleting donor code or
  generated assets. Delete only under an owning task after confirming all
  useful semantics and provenance have durable replacements.

## Update checklist

When changing this document:

1. record the checkout/SHA and live Den task that motivated the update;
2. classify each touched file as active, donor, generated, retire, or upstream;
3. name the product behavior, not merely the source language;
4. point to the new C# owner for anything marked ported;
5. keep still-missing donor semantics explicit;
6. distinguish runtime migration from content/tool/proof migration;
7. name missing Engine capabilities and the owning request instead of a local
   workaround;
8. never infer task completion or review approval from this inventory.
