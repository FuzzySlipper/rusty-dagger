# Rusty Dagger / WorldRpg code and migration map

**Status:** living ownership inventory; the #7436 project graph, #7323 normalized Daggerfall content packs, #7324's first accepted Daggerfall melee/consequences slice, #7325's Dagger-owned browser wiring, and #7524's cohesive managed Engine SDK adoption are implemented. Engine #7545/#7546 have landed, #7325 has current packaged-product browser evidence, and campaign #7322 is closed.

**Snapshot:** 2026-08-30, #7322 closed; #7533 is active, #7534's donor ledger
is complete, #7535 has established the safe offline C# Arena2 decoder project,
and #7536 has completed normalized publication plus active C# runtime adoption. This snapshot
continues to account for the complete tracked legacy source graph before later
slices delete it.

## Authority and reading order

The current user request and owning Den task decide work. Board post #139 and
campaign #7322 define the WorldRpg structure and the ordered work:

```text
#7435 → #7441 → #7436 → #7437 → #7438 → #7323 → #7324 → #7325
```

The shared `[doc: rusty-engine/downstream-csharp-agent-brief]` supplies the
evolving safe C# / Engine boundary. Current source proves implemented behavior;
this map distinguishes it from the target graph. Den task descriptions and
threads own live sequence and review state, but older task language is not
architecture authority.

The compact rule is:

> **Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

`WorldRpg.Kit`, `WorldRpg.Host`, `WorldRpg.Rulesets.Daggerfall`, and
`RustyDagger.NativeProduct` are active projects. Loaded bundles, content packs,
tuning, the boundary canary, and the cohesive managed Engine SDK migration are
implemented. `Daggerfall.Import` now contains the #7535 checked Arena2 source
decoder and differential-fixture foundation. #7536 adds
Engine-free normalized dungeon/media contracts, canonical hashing/provenance,
atomic publication tooling, and deterministic real-data proof. Its spatial
closure now publishes an Engine-compatible static mesh, purpose-neutral
collision/navigation facts, and exact resource metadata with generated hashes;
its media closure publishes canonical material textures, billboard/enemy/weapon/
effect atlases, audio, UI, inventory, font-atlas, and authored-UI artifacts with
typed profiles and source facts. The active runtime resolves those artifacts by
manifest hash through Engine Content, admits collision/navigation atomically
through Engine Spatial, and uses Engine static-mesh, material, and sprite-atlas
owners without parsing Engine artifact documents.

The current #7441 Daggerfall integration consumes Engine `ProductUpdate.Facts` directly:
only running realtime batches with a finite positive `FixedDeltaSeconds` advance
this realtime Daggerfall product. The one input slice is interpreted on the
first admitted step and the remaining `AdmittedStepCount` steps reuse resulting
held state without replaying one-shot actions. Dagger does not derive a local
clock or reinterpret protocol numbers. Its safe direct service boundary is the
managed Engine SDK, currently including Mechanics
(stats/tracks/items/inventory/equipment), Look, Spatial, Appearance, Random, and
UI. Product-side Kit coordinators shape those capabilities; Daggerfall supplies
definitions and policy.

## Ownership model

| Layer | Owns | Does not own |
| --- | --- | --- |
| Rusty Engine | Host lifecycle/admitted updates, input, rendering/resources, spatial mechanisms, and published service families. | Daggerfall policy or product state. |
| `WorldRpg.Kit` | Compiled-ruleset/session contract, typed composition IDs, loaded bundle/content/tuning resolution, and reusable or placement-uncertain world-RPG mechanisms. | Generic-RPG universality or Daggerfall vocabulary. |
| `WorldRpg.Host` | Product lifecycle, explicit built-in ruleset/default selection, and session construction. Explicit multi-bundle selection and durable save identity are #7542. | Daggerfall formulas, actor meaning, Arena2 files, or Privateer's Hold IDs. |
| `WorldRpg.Rulesets.Daggerfall` | Daggerfall identities, rules, formulas, policies, current content interpretation, presentation, and mutable session state. | Engine machinery and importer source formats. |
| Content packs (target) | Authored actors, items, world/location/quest data, assets, placements, and non-encounter scenario state. | Arbitrary executable C# behavior or encounter grouping/activation metadata. |
| `Daggerfall.Import` | Offline Arena2/DFUnity formats, source records, conversion quirks, provenance, and differential validation. #7535 supplies checked decoders; #7536 adds normalized output publication. | Runtime session, Engine host composition, or gameplay policy. |
| `RustyDagger.NativeProduct` | Handwritten product-type selection plus generated NativeAOT ABI/lifecycle/service/export output. | Product logic. |
| `src/ui` | DOM projection and semantic action presentation. | Gameplay state or world rendering. |

Rulesets are **compiled**; content packs, tuning profiles, and bundles are
**loaded**. Reusable or placement-uncertain mechanisms move to Kit by default;
the later canary validates the seam rather than authorizing promotion.
Daggerfall assumptions are legal only in the Daggerfall ruleset, content packs,
presentation, and importer lanes.

## Current Engine boundary

The product decides; Engine guarantees. #7524 moved active C# source onto the
cohesive managed Engine SDK. This #7534 snapshot was reconciled against Engine
`384dd2b`; later campaign slices must refresh that safe surface before use.
Current code uses managed **Mechanics**
stats/tracks/items/inventory/equipment for definitions, entity binding, reads,
guarded mutation, lifecycle, and equip operations, plus managed Look, Spatial,
Appearance, Random, and UI families. Kit provides product-shaped actor,
inventory, and equipment coordination over that SDK; Daggerfall defines its
identities, authored data, formulas, and reward/equip policy. These are verified
current C# surfaces, not a promise that every Engine capability needed by the
new campaign already has a safe managed contract.

Other verified safe families include Content/ContentStore, Persistence,
Animation, Audio, CameraView, and managed Mechanics exact/continuous types.
There is no separate generated Rules/StandardExact/StandardContinuous service
at this Engine revision. Treat this as routing, not a mandate or API catalog:
reverify each safe contract when used and retain product semantics/policy in
the Daggerfall ruleset.

If a required safe C# capability is missing, name the blocked behavior and Engine
owner, confirm the wrapper is absent, file one narrow purpose-neutral
`rusty-engine` request, and stop. Never fill the gap with downstream Rust, C#
Engine reimplementation, browser authority, a fake proof, or a parallel host.

## Active #7436 source map

| Active source family/file | Current owner and role |
| --- | --- |
| `src/WorldRpg.Kit/**` | Safe Kit project with typed composition, Mechanics-backed actor lifetime, configured controls/input frames, Engine-default spatial scene stepping, progression, bounded facts, structured UI values, and revision-guarded inventory/equipment coordination. Its equipment view joins Engine assignments to Engine unique-item inventory and its guarded mutations use observed Engine revisions. It references Engine only and contains no bundle resolver or Daggerfall vocabulary. |
| `src/WorldRpg.Rulesets.Daggerfall/WorldRpg.Rulesets.Daggerfall.csproj` | Safe Daggerfall ruleset project referencing Kit and Engine. |
| `DaggerfallRuleset.cs` | Compiled `daggerfall` ruleset implementation creating the current session. |
| `DaggerfallSession.cs`, `DaggerfallState.cs`, `DaggerfallTuning.cs`, `DaggerfallRewardReactions.cs` | Single mutable Daggerfall session and ordered composition, typed Daggerfall tuning, attack/reward policy, and direct use of Kit mechanisms. Initial unique items are Engine item entities staged into the player before the atomic initial equipment assignment; gold is an Engine fungible stack. Monster health is a keyed authored-range roll. `ProductUpdateState` is a Kit input frame derived from Engine input by the session. |
| `Content/DaggerfallDefinitions.cs`, `Content/DaggerfallBaseContent.cs` | Daggerfall-owned immutable typed actor/item/HUD/loot/attack definitions plus item kinds, equipment classifications, and authored slot policy, with bounded schema/version/ruleset/duplicate/reference diagnostics from `daggerfall.base`. No global catalog or source-name selection remains. |
| `Content/PrivateersHoldContent.cs`, `Presentation/PrivateersHoldAppearance.cs` | Explicit typed Privateer's Hold start position/look, placements, and interpretation of normalized Import manifests/media sidecars. The canonical player loadout and equipment assignments belong solely to `daggerfall.base`. The ruleset verifies artifact identities, then delegates opaque collision/navigation and static-mesh documents plus texture resources to Engine Content, Spatial, and Appearance. Material slots come from the same offline mesh assembly that writes the static mesh; sprites use Engine atlases. No encounter schema, authored UV topology, or runtime spatial parser remains. |
| `Facts/*`, `Modules/Combat/*`, `Presentation/*` | Daggerfall combat facts/policy and Daggerfall presentation meaning. #7324 adopts direct exact-id player/rat/skeletal melee through Engine Mechanics: equipment truth is read from Engine, stamina spend and health damage are guarded Engine mutations, and damage/death/rewards are fact-ordered. A miss latches cadence; a hit latches it only after a valid accepted damage receipt. If damage application fails, no optimistic hit fact/cooldown is invented, while a previously accepted Engine stamina spend remains authoritative. It adapts the donor struck-body table to current scalar armor (the roll is retained but does not select body armor), selected hit formula omits donor monster +40/optional modifiers, and the local XP/500 progression experiment is not classic kill XP. It rejects target acquisition, range/LOS, encounter activation, senses, nearest-actor selection, and autonomous enemy/AI loops pending named owners. |
| `AssemblyInfo.cs` | Exact friend access for `WorldRpg.Rulesets.Daggerfall.Tests`. |
| `src/WorldRpg.Host/WorldRpg.Host.csproj`, `WorldRpgProduct.cs` | Safe Host project. It gates lifecycle and one Engine-admitted update, resolves the explicit built-in default ruleset, and delegates through `IGameSession`; it does not construct `DaggerfallSession`. |
| `src/RustyDagger.NativeProduct/RustyDagger.NativeProduct.csproj`, `NativeProduct.cs` | NativeAOT composition project. Its handwritten file has only the Engine product attribute selecting `WorldRpgProduct`; generated output remains under ignored `obj/`. |
| `src/Daggerfall.Import/**`, `src/Daggerfall.Import.Tool/**` | Standalone safe `net10.0` offline library and narrow operator CLI with no Engine/product/package dependencies. #7535 ports bounded Arena2 decoders and source transforms. #7536 adds purpose-neutral normalized world/resource contracts, deterministic PNG/atlas/media helpers, content hashes, portable source paths and byte lengths, canonical manifests, and atomic publication. The spatial closure contains normalized world facts, an exact resource-metadata catalog, Engine-compatible static-mesh JSON, collision/navigation facts, and the import manifest. It preserves per-door visual mesh identities while excluding doors from collision and derives bounded signed/multilevel walkable supports offline. The completed media closure adds canonical material textures, billboard/enemy/weapon/effect atlases, audio, UI, inventory, font-atlas, authored-UI artifacts, typed display profiles, source animation/damage-beat facts, exact mesh material-slot bindings, and cross-projection validation. The CLI consumes explicit operator-supplied Arena2 and authored-UI paths and can plan, write, and repeat-verify Privateer's Hold without publishing raw source. The checked-in closure verifies deterministically at 182 publication artifacts from 59 portable sources. It owns no runtime entities, renderer, playback, encounters, or gameplay randomness. |
| `tests/Daggerfall.Import.Tests/**` | Focused generated-byte differential, malformed-input, normalized-contract, media, publication, and real-format compatibility fixtures. Local copyrighted Arena2 files remain optional developer evidence and are never required or committed; the local operator corpus has separately proven deterministic Privateer's Hold publication. |
| `tests/WorldRpg.Kit.Tests/*`, `tests/WorldRpg.Rulesets.Daggerfall.Tests/*` | Focused Kit mechanism and Daggerfall policy suites, including Host lifecycle/update/disposal coverage. |
| `src/ui/*`, `src/scripts/*`, `scripts/verify.sh` | DOM UI and build/launch/verification paths updated to the new NativeProduct project; UI renders the Daggerfall semantic projection plus the exact resolved generic composition identity and only claims declared semantic input. `run-product.sh` declares `attack=digital` through the Engine host; no gameplay authority or browser bridge is added. |
| `src/browser-bundle/**`, `src/**/bin/**`, `src/**/obj/**`, `tests/**/bin/**`, `tests/**/obj/**` | Generated output; never authority or handwritten source. |
| `content/worldrpg/payloads/daggerfall.base.json` | Versioned immutable Daggerfall catalog definitions: full attribute/skill/track vocabulary, player and thief, 42 monsters with mobile 39 explicitly absent, items/materials, equipment slots/loadout, actions, loot tables/deferred pools, donor errata, and HUD resources. |
| `content/worldrpg/payloads/daggerfall.privateers-hold.json`, `content/worldrpg/imports/privateers-hold/**` | Normalized Privateer's Hold start position/look and explicit actor placements select the checked-in Import publication root. The generated closure carries manifests, hashes, provenance, opaque Engine spatial/static-mesh artifacts, material textures, and actor atlases. It contains no encounter/grouping/activation metadata. |
| `content/projects/privateers-hold.navgrid.json`, `content/imported/privateers-hold.static-mesh.json` | Inactive donor artifacts retained only until later cleanup slices prove no remaining consumer; the active C# product no longer reads them. |
| `gameplay/**`, root Rust/Angular/HTTP/Studio surfaces | Donor or retired-runtime evidence, not active implementation. |

## Pre-#7436 ownership snapshot (historical)

Status terms in this retained snapshot describe the pre-split checkout;
**generated** still means derived output, never authority.

| Current source family/file | Current role | Future owner / disposition |
| --- | --- | --- |
| `src/Dagger.Game/Dagger.Game.csproj` | Safe ordinary product project; unsafe disabled. | **Target Host + Daggerfall ruleset + Kit** project graph; split only in #7436. |
| `src/Dagger.Game/DaggerGame.cs` | Product lifecycle entry forwarding Engine-admitted updates to the selected product composition. | **Target `WorldRpg.Host/WorldRpgProduct.cs`**. |
| `src/Dagger.Game/AssemblyInfo.cs` | Test access to internals. | Host/ruleset test seam as projects split. |
| `src/Dagger.Game/Daggerfall/DaggerfallComposition.cs` | Current concrete composition, Daggerfall realtime-admission interpretation, state/update order, Engine service use, and disposal. | **Target `WorldRpg.Rulesets.Daggerfall/DaggerfallSession.cs`**. |
| `src/Dagger.Game/Daggerfall/DaggerfallState.cs` | Current aggregate over domain-owned mutable state. | Daggerfall ruleset. |
| `src/Dagger.Game/Daggerfall/DaggerfallTuning.cs` | Typed current tuning aggregate. | Daggerfall ruleset typed tuning/profile loader; bundle loading comes later. |
| `src/Dagger.Game/Daggerfall/DaggerfallRewardReactions.cs` | Daggerfall reward/loot/XP policy. | Daggerfall ruleset. |
| `src/Dagger.Game/Daggerfall/Content/DaggerfallDefinitions.cs` | Historical Daggerfall stat/track IDs, actor/item/HUD defaults, and prefix-based authored-name selection. | Superseded by #7323's typed loaded definitions/placements; source conversion and provenance remain `Daggerfall.Import` work. |
| `src/Dagger.Game/Daggerfall/Content/DaggerfallMechanicsCatalog.cs` | Daggerfall definitions admitted through safe Mechanics stats/tracks. | Daggerfall ruleset consuming Engine Mechanics. |
| `src/Dagger.Game/Daggerfall/Content/PrivateersHoldContent.cs` | Current exact-file selection/parsing from Engine-admitted `ProductContent`, with source-shaped project/entity/sprite interpretation. | Transitional Daggerfall ruleset code; source-path/format quirks move to `Daggerfall.Import`, normalized result to packs. |
| `src/Dagger.Game/Daggerfall/Presentation/DaggerfallHudProjection.cs` | Daggerfall resource labels/order through Engine UI/Mechanics. | Daggerfall presentation. |
| `src/Dagger.Game/Daggerfall/Presentation/DaggerfallOutcomePresentation.cs` | Daggerfall outcome wording. | Daggerfall presentation. |
| `src/Dagger.Game/Daggerfall/Presentation/PrivateersHoldAppearance.cs` | Privateer's Hold appearance choices through Engine Appearance. | Daggerfall presentation/content-pack interpretation. |
| `src/Dagger.Game/Facts/ProductFacts.cs` | Product-local accepted-transition contracts/buffer. | Move the bounded buffering mechanism to Kit; keep concrete Daggerfall fact identities and reactions in the ruleset. |
| `src/Dagger.Game/Modules/Actors/ActorsState.cs` | Mechanics-backed actor lifetime/defeat state. | Daggerfall ruleset; current generic folder is not Kit proof. |
| `src/Dagger.Game/Modules/Combat/CombatDefinitions.cs` | Combat definitions and direct formula vocabulary. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Combat/CombatModule.cs` | Daggerfall melee/cooldown/RNG/track mutation. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Encounters/{EncounterReaction,EncounterState,EncounterSystem}.cs` | Removed proximity/grouping test scaffold. | **Reject; do not port.** It was neither Daggerfall gameplay authority nor a target-selection design. |
| `src/Dagger.Game/Modules/Equipment/EquipmentState.cs` | Historical local right-hand equipment state. | Superseded by Kit's Engine Mechanics equipment view/mutations; Daggerfall owns authored slots and equip decisions, not assignment truth. |
| `src/Dagger.Game/Modules/Inventory/InventoryState.cs` | Historical local carried-item state and add mutation. | Superseded by Kit's Engine Mechanics inventory coordination; Daggerfall owns item definitions, loadout, and reward policy, not inventory truth. |
| `src/Dagger.Game/Modules/PlayerControl/PlayerControlState.cs` | Player control state/tuning. | Daggerfall ruleset unless canary proves shared semantics. |
| `src/Dagger.Game/Modules/PlayerControl/PlayerInputSystem.cs` | Input interpretation and Engine Look use. | Daggerfall ruleset unless canary proves shared semantics. |
| `src/Dagger.Game/Modules/PlayerControl/SpatialMovementSystem.cs` | Movement policy and Engine Spatial use. | Daggerfall ruleset unless canary proves shared semantics. |
| `src/Dagger.Game/Modules/Presentation/PresentationState.cs` | Presentation state/value ownership. | Daggerfall ruleset unless canary proves shared semantics. |
| `src/Dagger.Game/Modules/Presentation/UiValueBuilder.cs` | UI value construction. | Daggerfall presentation unless canary proves shared semantics. |
| `src/Dagger.Game/Modules/Progression/ProgressionState.cs` | XP state/mutation. | Daggerfall ruleset. |
| `src/Dagger.NativeProduct/Dagger.NativeProduct.csproj` | Active NativeAOT project/source-generator references. | **Target `RustyDagger.NativeProduct`**. |
| `src/Dagger.NativeProduct/NativeProduct.cs` | Assembly attribute selects current product type. | **Target `RustyDagger.NativeProduct/NativeProduct.cs`**, selecting `WorldRpg.Host.WorldRpgProduct`. |
| `tests/Dagger.Game.Tests/Dagger.Game.Tests.csproj` | Focused active C# behavior test project. | Split/rename only with its target owners; no move in #7435. |
| `tests/Dagger.Game.Tests/DaggerfallCompositionTests.cs` | Daggerfall composition/mechanics/combat evidence. | Daggerfall ruleset tests. |
| `tests/Dagger.Game.Tests/ProductFactsTests.cs` | Fact ordering/deferred delivery evidence. | Daggerfall tests unless a canary proves Kit semantics. |
| `tests/Dagger.Game.Tests/RecordingMechanics.cs` | Test mechanics double. | Daggerfall/Kit test support after #7436/#7437 evidence decides scope. |
| `src/ui/main.ts`, `src/ui/main.test.ts`, `src/ui/styles.css` | DOM HUD/action presentation and focused UI proof. | UI; Daggerfall presentation may shape its projections, never gameplay authority. |
| `src/scripts/build-ui.sh` | UI compilation/assembly. | Host build path. |
| `src/scripts/generate-browser-bundle.mjs` | Selects Engine browser host and UI contract. | Host assembly/build path; not gameplay. |
| `src/scripts/run-product.sh` | Current UI build, NativeAOT publish, Engine launch. | Host launch path; rename only after target projects exist. |
| `src/playtest.json` | Broker/headless diagnostic configuration. | Product diagnostic config; stale labels are not architecture authority. |
| `src/.gitignore` | Ignores current and retired build/bundle output names. | Active housekeeping; generated output remains non-authoritative. |
| `src/browser-bundle/**` | Browser host and compiled UI output. | **Generated** assembled host output; never a second runtime. |
| `src/**/obj/**`, `src/**/bin/**`, `tests/**/obj/**` | Generated C#/NativeAOT/build output. | **Generated**; regenerate, never edit/commit. |
| `content/imported/**`, project/mesh/scene/navgrid files | Current admitted Daggerfall/Privateer's Hold inputs. | Daggerfall content packs after normalization; source conversion provenance belongs to importer. |
| `content/textures/**`, `content/audio/**`, `content/ui/**`, validation artifacts | Authored/imported Daggerfall assets and validation evidence. | Daggerfall content packs; importer retains source-format/provenance knowledge. |
| `gameplay/src/**`, `gameplay/scripts/**` | Earlier TypeScript formulas, catalogs, behavior, package/evaluator topology. | **Donor**: translate meaning into named C# owners; never execute/extend its runtime architecture. |
| Root Rust crates, Angular, HTTP product server, Studio adapter, Cargo/package graphs, old scripts | Earlier implementation and proof carriers. | **Donor/retired runtime**: retain useful semantics only; do not revive as an active path. |

## Tuning and content placement

| Value kind | Legal home |
| --- | --- |
| Adjustable ruleset value | Discoverable, validated typed tuning handle/profile in the owning ruleset. |
| Actor, item, world, asset, placement, quest, or scenario value | Content pack. Named encounter groupings are explicitly rejected. |
| Algorithmic invariant | Beside the owning algorithm. |
| Arena2/DFUnity conversion quirk | `Daggerfall.Import`. |
| Default shipped ruleset/bundle | Host. |

Avoid magic numbers at call sites and const sprawl. A small local structural
constant is honest; an authored or adjustable value needs a typed owner instead.

## Transitional current-source assumptions

`WorldRpgProduct` selects the explicit built-in `daggerfall` default and creates
its session through `IGameRuleset`; it does not construct `DaggerfallSession`.
The session receives its ordered `daggerfall.base` and
`daggerfall.privateers-hold` definitions from the resolved composition. Starting
loadout/gold are `daggerfall.base` actor facts; start look, placements, appearances,
and world refs are scenario-pack
data, not Host/Kit defaults. The Daggerfall UI title and projection-contract
selection remain Daggerfall presentation, not Host/Kit policy.

## Superseded task and architecture audit

The previous #7310-era generic `Modules` framing is superseded as target
authority: it usefully established explicit composition, domain-owned mutation,
direct ordered updates, local resolutions, and buffered facts. Reusable and
placement-uncertain mechanisms now live in Kit; concrete Daggerfall policy
remains in the ruleset.

Older Rust/Angular/HTTP/Studio/browser-runtime task language is likewise
superseded as implementation ownership. Its useful intended behavior remains
donor evidence: content admission, world and actor meaning, combat, progression,
equipment/inventory/loot semantics, presentation, and focused proof vectors.
It must be re-triaged into the WorldRpg campaign rather than executed under old
owners. Retired Rust/Angular tasks are not actionable default work.

## #7534 donor ledger and deletion gate

This is a disposition ledger, not a port plan. It accounts for **63 tracked
`*.rs` files** (including fifteen command/example entry points and the Rust
resolution test) and **19 tracked `gameplay/**` files** (including its two
scripts and `tsconfig`). A family may contain several files only where all
members have the same useful semantic boundary and disposition. “Reject”
means delete after the named replacement/evidence is recorded; it never means
quietly retain a product behavior to keep a legacy test or tool passing.

| Donor files (coverage) | Useful semantic evidence | Disposition and truthful destination | Dependency / deletion proof |
| --- | --- | --- | --- |
| `Cargo.toml`, `Cargo.lock`, `crates/{arena2,dagger-import,dagger-rpg,dagger-runtime,dagger-studio-adapter}/Cargo.toml` (7 Cargo graph files) | Workspace membership, legacy dependency topology, command names, and only the source-family boundaries below. | **Reject** the Rust workspace/package topology. C# projects and the Engine-managed SDK are the active product; no Cargo analogue is created. | #7544 deletes these only after every row below is adopted, adapted, deferred with retained content/provenance, or rejected. `Cargo.lock` has no semantic destination. |
| `crates/arena2/src/{arch3d,bsa,cif,dfrandom,fnt,img,lib,maps,mobile,pak,palette,rdb,snd,test_fixtures,texture,texture_table}.rs`, `crates/arena2/examples/dump-texture.rs` (17) | Read-only Arena2 container/record decoding; palette, image, font, sound, dungeon/mobile/texture transforms; deterministic random and differential fixtures. `mobile.rs` supplies source archive/frame-layout meaning plus runtime behavior that is deliberately split out. | **Adapted by #7535** into safe offline C# **`Daggerfall.Import`** checked decoders, source transforms, source-only mobile metadata, and 51 focused fixtures. Runtime orientation/playback/attack selection remains rejected here and routes to later ruleset/presentation work. Keep source-format names, byte checks, and provenance in Import, never Kit/Host/runtime. | #7535 replacement proof is the standalone dependency graph plus focused generated-byte/malformed fixtures; no raw Arena2 input is committed. #7536 must consume these decoders for normalized outputs before the Rust decoder files and example CLI are deleted in #7544. |
| `crates/dagger-import/src/{combat_assets,dungeon,glb,main,meshjson,png,ui_assets}.rs`, `crates/dagger-import/src/bin/dagger-validate-sprites.rs` (8) | Conversion of source geometry, decoded images, sprite/UI/combat manifests, GLB/normalized mesh output, hashes, and sprite validation. | **Adapt** normalized asset/spatial import and provenance to **`Daggerfall.Import`**; retain normalized content packs/assets, not Rust executables or source-shaped runtime readers. | #7536 depends on #7535’s decoders. Its output must identify source/hash/converter and drive current content through Engine Content/Appearance rather than a local renderer. The validation binary becomes importer differential evidence or is deleted. |
| `crates/dagger-rpg/src/lib.rs`, `crates/dagger-rpg/src/resolution/{compile,composed,eval,loot,mechanics,mod,model,policy,progression}.rs`, `crates/dagger-rpg/tests/resolution.rs` (11) | Daggerfall catalog validation, authored IDs, formulas, action/loot/progression policy, and acceptance vectors. It also embodies a Rust compiler/evaluator/program grammar and named encounter grouping compilation. | **Adapt** individually useful Daggerfall formulas, catalogs, and acceptance examples into loaded content, typed tuning, and compiled **`WorldRpg.Rulesets.Daggerfall`** policy over Engine Mechanics. **Reject** the evaluator, structural-program runtime, generic command shape, and all encounter compilation/grouping. | #7537 owns content/catalog admission; #7538 owns formula/policy translation. Retain only semantic fixture cases that still express a chosen rule; replace C#/Engine integration proof; delete topology tests and encounter cases. |
| `crates/dagger-runtime/src/{animation,combat_assets,directional,lib,navgrid,patrol,player,project,runtime}.rs`, `crates/dagger-runtime/src/bin/{dagger-derive-route,dagger-gameplay-check,dagger-navgrid,dagger-sprite-frames,dagger-walkthrough}.rs` (14) | Project reading, directional/sprite frame meaning, authored combat asset validation, navigation evidence, player/control semantics, patrol experiments, and command-line diagnostic vectors. Animation is a useful *semantic* frame-layout/timing donor, not a replacement renderer or clock. | **Adapt** reusable controls/actor/spatial coordination only into **WorldRpg.Kit** where ownership is uncertain; put Daggerfall timing/asset meaning in the ruleset/content/tuning. Use Engine Look, Spatial, Appearance, Animation, Audio, Content and admitted update. **Reject** the Rust aggregate runtime, local loop/clock, renderer diffs, patrol/encounter runtime, and command topology. | #7539 establishes Kit/Engine routing; #7540 is future target acquisition only after verified Engine queries; #7541 ports behavior/presentation semantics. Each binary is deleted after its useful vector is a C# importer/ruleset fixture or explicitly obsolete. |
| `crates/dagger-studio-adapter/src/{lib,presentation,project_access,protocol,readout}.rs`, `crates/dagger-studio-adapter/src/bin/dagger-studio-adapter.rs`, `crates/dagger-studio-adapter/src/bin/dagger-product-server/{connected_application,developer_commands,diagnostics,live_presentation,main,melee_presentation,product_server}.rs` (13) | Content-addressed resource/provenance checks, developer readouts, sprite/atlas inspection, diagnostic overlays, semantic product presentation, and the old Studio/product-server protocol. | **Adapt** supported import/authoring diagnostics and the useful sprite-editor workflows to #7543. **Reject** the Rust server/adapter protocol, retained-frame renderer, local sprite animator, and browser/product authority. | #7543 must use Engine Content/Appearance/Animation/resource operations for the same product and editor truth. Any required safe C# sprite display/animation control absent upstream becomes a narrow Engine request; downstream must stop that slice rather than emulate it. |
| `gameplay/src/authoring/{definitions,envelope,expressions,mod,programs}.ts`, `gameplay/scripts/{materialize,verify-expressions}.mjs`, `gameplay/tsconfig.json` (8) | Authored IDs/schema/value validation, expression examples, package envelope, and materialization checks. | **Adapt** content schema/validation and chosen formula fixtures to versioned packs, `Daggerfall.Import`, typed tuning, and Daggerfall ruleset code. **Reject** the TS build, package evaluator, embedded expression/program grammar, and scripts. | #7537/#7538 decide each adopted rule. The old generated envelope/program executor is deletion-only after current C# validation and fixtures prove selected semantics. |
| `gameplay/src/catalogs/{actions,actors,derived,equipment,items,loot,monsters,rules,stats}.ts` (9) | Daggerfall actor/item/stat tables, weapon/armor/loot values, derived/combat formulas, and source notes. | **Adapt** authored records to Daggerfall content packs and selected formulas to compiled ruleset policy; Engine Mechanics remains stat/track/inventory/equipment truth. Adjustable values gain typed tuning handles; authored values stay packs. | #7537 ports records and #7538 formula policy. Do not transfer TS constants mechanically or preserve them merely as a legacy-test oracle. |
| `gameplay/src/catalogs/encounters.ts`, and only the encounter declarations/fields/imports inside `authoring/{definitions,envelope,mod}.ts` and `packages/dagger-core.ts` (the remaining 2 gameplay files are `packages/dagger-core.ts` and the encounter catalog) | The intentionally deleted named “rat introduction”/“skeletal guardroom” grouping, route-code scheduler, and prior combat demonstration harness. Non-encounter package composition in `dagger-core.ts` remains donor evidence covered by the preceding authoring/catalog rows. | **Reject the encounter catalog and encounter portions in full; do not port or replace them.** They are not Daggerfall’s world/AI/targeting design and do not become content-pack vocabulary, scenario grouping, activation, scheduler, or test fixtures. | #7537 removes encounter schema/import/data while preserving independently useful non-encounter donor semantics. #7544 proves no live source or generated package imports the encounter model. Future combat/targeting proof gets a separately designed task after this campaign. |
| Residual encounter surfaces: `content/worldrpg/payloads/daggerfall.privateers-hold.json`; `data/encounters/{privateers-hold,encounter-gallery}.json`; `content/projects/encounter-gallery.{project,navgrid}.json`; encounter parsing/model code in `PrivateersHoldContent.cs`; encounter UI/types in `apps/dagger-product`; encounter generation/launch/browser assertions in `scripts/{generate-project.py,serve-dagger-product.sh,check-dagger-product-browser.mjs}` | Current migration debris from the rejected grouping/demo system, including two named groups and their gallery/routes. Sprite assets or frame-layout evidence embedded in the gallery are useful only when rehomed under the sprite viewer/importer without encounter identity. | **Remove, do not migrate.** #7537 deletes the active normalized schema/data/reader; #7543 removes stale UI/tooling and rehomes any independently useful sprite inspection; #7544 deletes generators, gallery projects, launch paths, and obsolete assertions. | Deletion proof searches active C#, normalized payloads, generated content, UI, scripts, and tests—not only Rust/`gameplay/`. No empty compatibility field or gallery survives merely to satisfy legacy proof. |
| `apps/dagger-product/{proxy.conf.json,tsconfig.app.json}` and `apps/dagger-product/src/{main,app.component,developer-command,lab-tools-api.service,product-api.service,product-contract,product-runtime,sprite-contract,sprites-panel.component}.{ts,html}`, `apps/dagger-product/src/{index.html,styles.css}` (15 tracked app files) | The stale Angular product shell, semantic actions/readouts, developer tools, and the **sprite viewer/editor**: asset index, atlas/frame inspection, manifests, and save workflow. | **Reject** Angular application/runtime, proxy, API bridge, UI-owned render frame/sprite playback, and Studio transport. **Preserve and port** the sprite viewer/editor’s user workflows under **#7543** as a thin UI over the exact Engine-backed product content/appearance/animation/resource operations. | #7543 inventories each view/edit operation and maps it to a shared C# product/Engine operation. It may render an Engine-delivered projection and issue semantic edits, but must not calculate frames, animate, atlas-pack, or create a second sprite/render truth. Missing safe control is an upstream request, not local UI recreation. |
| Root `package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml`, `angular.json`, root `tsconfig.json`, root `tsconfig.spec.json`; retired root scripts/HTTP/browser configuration and `src/ui/main.test.ts` | Legacy installation/build/test topology and a narrow DOM proof; not product authority. | **Reject** after #7543/#7544, except non-generated authored content explicitly retained by the importer/content-pack rows. Replace only a still-valuable semantic projection test against the current UI contract. | #7544 removes after the C#/NativeAOT build and #7543 sprite/editor migration have their own focused proof. No feature survives merely because a retired Angular/Node test expected it. |

### Tests and evidence disposition

Legacy tests are donor evidence, not requirements. The table below is the
required classification before a deletion. It applies both to the Rust
`#[cfg(test)]` modules and the older TypeScript/Angular tests named above.

| Class | Current examples | Handling |
| --- | --- | --- |
| **Retain as semantic fixture** | Arena2 byte/decoder fixtures (`bsa`, `cif`, `img`, `maps`, `mobile`, `rdb`, `texture*`); selected Daggerfall formula, RNG, catalog, and sprite-layout examples. | Move the smallest source/input/expected-output vectors into importer or ruleset tests with provenance. They do not require the old harness. |
| **Replace** | Useful `dagger-rpg/tests/resolution.rs` policy examples; old sprite-manifest/editor save assertions; current C# tests that cover active Engine/Kit/ruleset contracts. | Re-express only still-chosen semantics in focused C# tests using safe Engine doubles/contracts or direct importer fixtures. |
| **Obsolete topology** | Cargo command tests, Rust runtime/server/adapter protocol tests, Angular bootstrap/proxy tests, renderer-frame-diff tests, expression-evaluator tests, and every encounter grouping/scheduler/route-code test. | Delete. Do not keep production paths, hidden compatibility layers, or magic values just to satisfy them. |

### Sprite viewer/editor preservation contract (#7543)

The legacy Angular implementation is evidence for an important tool, not its
authority. `apps/dagger-product/src/sprites-panel.component.{ts,html}`,
`sprite-contract.ts`, and `lab-tools-api.service.ts` demonstrate the required
workflow: list/index sprites, inspect an atlas and its frames, inspect/edit a
manifest, and save validated changes. `crates/dagger-import` plus
`crates/dagger-runtime/src/{animation,combat_assets,directional}.rs` supply
the importer and Daggerfall frame-layout provenance needed to interpret it.

#7543 preserves that workflow in a thin UI, but every preview, current-frame
readout, replacement, and persisted resource must use the same Engine
Content/AuthoredContent/ContentStore/Appearance resource path selected from the
verified safe contracts and used by the running product. It must not introduce
a local manifest persistence authority. Engine **#7564** is complete. The live
safe managed Appearance contract now exposes atlas creation, atlas-backed sprite
creation, frame selection, and sprite readback, including bounded per-frame
presentation data. Runtime and preview code must use that common Engine-owned
frame truth. Semantic timing and direction selection remain product/ruleset
policy admitted through the one Engine update; the UI must not become a playback
authority.
The UI receives projections and emits semantic actions; it does not own an
animation timer, derive directional frames, pack atlases, serve resources, or
apply renderer diffs or substitute animated-mesh APIs. #7543 stops any affected
portion at a newly verified missing safe boundary rather than adding DOM/C# playback. This is
deliberately stricter than the old Angular/Rust server split. It is a safe
managed-SDK integration task, not permission to treat generated output as an
API catalogue.

## Post-#7325 complete-source migration campaign

Campaign #7533 is active after #7325 and #7524. Its closure condition is complete
semantic disposition of all 63 tracked Rust files and all 19 files under
`gameplay/`, followed by removal of those source graphs **and every residual
encounter surface enumerated above**. This is not a line-for-line port: behavior
and authored meaning move to their truthful owners; obsolete runtime, evaluator,
package, transport, editor, and encounter-demo topology is recorded as rejected
and removed.

| Donor family | Planned disposition | Task |
| --- | --- | --- |
| Entire Rust/`gameplay/` corpus and coupled metadata | Establish the file/concept ledger, replacement evidence, deliberate deviations, and deletion proof before changing behavior. | #7534 |
| `crates/arena2` | Checked source decoders, transforms, quirks, source-only mobile metadata, and differential fixtures are implemented in safe offline C# `Daggerfall.Import`; #7536 now consumes them into its in-progress normalized-publication foundation. | #7535 |
| `crates/dagger-import` and source-shaped spatial/resource adapters | Produce normalized assets, spatial artifacts, manifests, hashes, and provenance for runtime packs; retire runtime parsing of legacy project schemas. | #7536 |
| `gameplay` stats, actors, monsters, items, equipment, loot, and presentation references | Load authored values from versioned content packs, put adjustable policy in typed tuning, and keep Daggerfall identities/interpretation in the ruleset. The separate named encounter catalog is rejected rather than migrated. | #7537 |
| `gameplay` expressions/programs and `dagger-rpg` formulas/resolution | Adapt useful formulas, actions, progression, and loot to named compiled C# policy over managed Engine services; reject the evaluator, structural-program runtime, and replacement DSL. | #7538 |
| `dagger-runtime` project/player/navigation behavior | Keep reusable world admission, controls, actor lifetime, and spatial coordination in Kit; use Engine lifecycle, Look, Spatial (including its navigation operations), and Content rather than porting the aggregate runtime. | #7539 |
| Rust targeting/raycast/sensing evidence | Implement Daggerfall target choice and melee admission over verified Engine perception/spatial/collision queries; do not revive the deleted encounter-targeting scaffold. | #7540 |
| Rust enemy behavior, directional/animation, audio, and combat-asset evidence | Split reusable coordination to Kit, Daggerfall behavior/timing to ruleset/content/tuning, and pathing/animation/appearance/audio/camera/time mechanisms to Engine. No encounter system, grouping, activation, or combat test harness is recreated. | #7541 |
| Old server selection/readout/save identity | Add explicit Host bundle selection and durable bundle/ruleset/pack/tuning/save identity over Engine Persistence and Content. | #7542 |
| `dagger-studio-adapter`, HTTP server, stale Angular app, and gameplay package authoring | Preserve only useful diagnostics, provenance, thin DOM presentation, supported C# import/authoring behavior, and the sprite viewer/editor (atlas/frame inspection and manifest editing). Reject and retire the old topology and UI-side render/animation authority. | #7543 |
| Remaining `.rs`, `gameplay/`, Cargo, retired scripts/config/apps | Delete only after replacements and provenance are proven; certify the clean C#/NativeAOT product. Authored/imported content remains. | #7544 |

The dependency shape keeps the importer, catalogs/rules, and Kit/Engine world
lanes independently reviewable, then converges through targeting/behavior,
Host state, retired-surface removal, and final cleanup. Each implementation
slice must consult its exact donor and finish with two Luna-max drift audits:
missed upstream Engine reuse, then Daggerfall leakage/hardcoding/tuning
placement. A missing safe Engine capability produces one narrow upstream task
and stops only the affected slice.

## Campaign handoff

- **#7435:** charter/map/task authority is complete.
- **#7441:** Engine contract reconciliation is complete.
- **#7436:** landed the compiled project graph, Kit-default mechanism split, and
  Host/ruleset/session seam while preserving focused proof.
- **#7437:** use an intentionally incompatible canary to exercise the boundary
  and dependency laws.
- **#7438:** loaded bundles, content packs, typed tuning, and resolution are complete.
- **#7323:** normalized Daggerfall first-contact definitions/scenario packs are
  complete, retaining one explicit `Daggerfall.Import` spatial artifact adapter.
- **#7324:** accepted first-contact melee/consequences: deterministic keyed health,
  hit/body/damage/loot rolls (seed 0; player `dagger.combat.v1`, enemy
  `dagger.combat.ai.v1`), direct safe Engine damage receipts, and deferred
  exactly-once consequences. The deterministic keys are deliberately rebased
  from donor runtime randomness to Engine generation/simulation-step plus exact
  attacker/target/salt identity. No live target selector or AI was restored.
- **#7524:** the product and focused tests use the cohesive managed Engine SDK;
  handwritten interop-era service contracts have been retired.
- **#7325:** Dagger-owned NativeAOT/browser wiring publishes the resolved bundle,
  compiled ruleset, ordered packs, tuning, and existing composition/content/tuning
  fingerprints through Daggerfall's HUD; normal semantic attack honestly projects
  `NoTargetInReach`. Engine #7545/#7546 are landed. Current live evidence exercises
  the ordinary packaged product with one Engine canvas, the resolved composition,
  pre-pointer-lock Attack, Engine-arbitrated pointer lock, and visible W movement
  driven by the Dagger-owned C# player pose and Engine camera.
- **#7533:** active complete-source migration campaign. **#7534 is complete**
  and inventories every donor. **#7535 has implemented** the standalone safe
  Arena2 decoder/test foundation. **#7536 is implemented** with normalized
  contracts, deterministic publication/provenance tooling, a checked-in
  operator-corpus Privateer's Hold closure, and active runtime adoption through
  Engine Content, Spatial, and Appearance. Engine #7577 supplies atomic
  content-backed collision/navigation admission; no downstream array adapter remains.
  #7537-#7543 migrate or reject the
  remaining coherent semantic families; #7544 removes the fully dispositioned
  Rust/`gameplay/` graph and certifies the C#-only product. Named encounter
  scaffolding is a campaign-wide rejection, while #7543 preserves the sprite
  viewer/editor over shared Engine mechanisms.

Campaign #7322 is **closed**. Follow-up work is dependency-ordered under #7533
rather than left as an unowned inventory.

## #7537 catalog donor ledger

`gameplay/src/catalogs/{stats,actors,monsters,items,equipment,actions,loot,derived}.ts`
and `gameplay/src/authoring/definitions.ts` were consulted as semantic donors.
Stats, actors, items, slots, actions, material armor values, loot matrix, and
the documented mobile-39 / Chain2 / bow-hand errata are **adapted** into the
versioned `daggerfall.base` content payload and typed Daggerfall interpretation.
The TypeScript evaluator, package materializer, and encounter grouping are
**rejected**: no runtime TypeScript, evaluator, package metadata, or encounter
topology is loaded by the NativeAOT product. Loot category pools are carried as
explicitly deferred authored references until a later ruleset-owned generator
has normalized item pools; this catalog slice does not fabricate them.

The active C# ruleset loads and validates this catalog directly. No TypeScript
or Rust evaluator/runtime, encounter topology, empty project structure, or
speculative architecture is introduced.
