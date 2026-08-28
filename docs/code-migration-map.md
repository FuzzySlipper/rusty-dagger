# Rusty Dagger / WorldRpg code and migration map

**Status:** living ownership inventory; target names below are not implemented
project names unless explicitly marked **target**.

**Snapshot:** 2026-08-28, WorldRpg foundation task #7435.

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

`WorldRpg.Kit`, `WorldRpg.Host`, `WorldRpg.Rulesets.Daggerfall`,
`RustyDagger.NativeProduct`, and `Daggerfall.Import` are **target** names. The
current checked-in projects remain `Dagger.Game` and `Dagger.NativeProduct`
until #7436 moves them. No documentation claim about the target graph is proof
that a project, canary, bundle resolver, or importer has already landed.

Adjacent current-source drift is explicit: `DaggerGame` and the current direct
composition still use obsolete ProductUpdate/input/clock/constructor shapes and
are not asserted to build against Engine HEAD. #7441 reconciles them with the
already-published Engine update/input/look/spatial/appearance contracts before
the project split.

## Ownership model

| Layer | Owns | Does not own |
| --- | --- | --- |
| Rusty Engine | Host lifecycle/admitted updates, input, rendering/resources, spatial mechanisms, and published service families. | Daggerfall policy or product state. |
| `WorldRpg.Kit` (target) | Small compiled-ruleset/session and loaded-composition grammar, content-pack IDs/order, validated tuning loading, diagnostics, and mechanisms proved by a second real composition. | Generic-RPG universality or Daggerfall vocabulary. |
| `WorldRpg.Host` (target) | Product lifecycle, shipped-ruleset registry, default bundle, launcher/selection, construction, and product diagnostics. | Daggerfall formulas, actor meaning, Arena2 files, or Privateer's Hold IDs. |
| `WorldRpg.Rulesets.Daggerfall` (target) | Daggerfall identities, rules, formulas, policies, content interpretation, presentation, saves, and mutable session state. | Engine machinery and importer source formats. |
| Content packs (target) | Authored actors, items, world/location/encounter/quest data, assets, placements, and scenario state. | Arbitrary executable C# behavior. |
| `Daggerfall.Import` (target) | Arena2/DFUnity formats, source paths/records, conversion quirks, provenance, and differential validation. | Runtime session or Host composition. |
| `RustyDagger.NativeProduct` (target) | Handwritten product-type selection plus generated NativeAOT ABI/lifecycle/service/export output. | Product logic. |
| `src/ui` | DOM projection and semantic action presentation. | Gameplay state or world rendering. |

Rulesets are **compiled**; content packs, tuning profiles, and bundles are
**loaded**. Existing gameplay moves to the Daggerfall ruleset by default. A
generic name or current `Modules/` placement does not prove Kit reuse; promotion
needs evidence from a second real composition/canary with shared semantics.
Daggerfall assumptions are legal only in the Daggerfall ruleset, content packs,
presentation, and importer lanes.

## Current Engine boundary

The product decides; Engine guarantees. Current active C# source uses the safe
generated **Mechanics** stats/tracks substrate for catalog creation, definitions,
entity binding, reads, and guarded track mutation. It also uses safe Look,
Spatial, Appearance, Random, and UI families. Mechanics already publishes a safe
item/inventory/equipment substrate, including catalog, inventory lifecycle, and
equip operations, although the current Dagger slice has not migrated to it:
`InventoryState` and `EquipmentState` remain local Daggerfall implementation
facts. Future Daggerfall work must consult and use the safe substrate where it
fits, while keeping Daggerfall definitions, formulas, and policy in the ruleset.
Those are verified current C# surfaces, not a promise that every Engine Rust
capability has a safe wrapper.

Other verified generated families are Content/ContentStore, Persistence, Rules
(StandardExact and StandardContinuous), Animation, Audio, and CameraView. Treat
this as routing, not a mandate or API catalog: reverify each safe contract when
used and retain product semantics/policy in the Daggerfall ruleset.

If a required safe C# capability is missing, name the blocked behavior and Engine
owner, confirm the wrapper is absent, file one narrow purpose-neutral
`rusty-engine` request, and stop. Never fill the gap with downstream Rust, C#
Engine reimplementation, browser authority, a fake proof, or a parallel host.

## Current active source → future owner map

Status terms: **active** means used in today’s C# product path; **target** means
the post-#7436 home; **generated** means derived output, never authority.

| Current source family/file | Current role | Future owner / disposition |
| --- | --- | --- |
| `src/Dagger.Game/Dagger.Game.csproj` | Safe ordinary product project; unsafe disabled. | **Target Host + Daggerfall ruleset + Kit** project graph; split only in #7436. |
| `src/Dagger.Game/DaggerGame.cs` | Product lifecycle/admitted-update entry. | **Target `WorldRpg.Host/WorldRpgProduct.cs`**. |
| `src/Dagger.Game/AssemblyInfo.cs` | Test access to internals. | Host/ruleset test seam as projects split. |
| `src/Dagger.Game/Daggerfall/DaggerfallComposition.cs` | Current concrete composition, state/update order, Engine service use, disposal. | **Target `WorldRpg.Rulesets.Daggerfall/DaggerfallSession.cs`**. |
| `src/Dagger.Game/Daggerfall/DaggerfallState.cs` | Current aggregate over domain-owned mutable state. | Daggerfall ruleset. |
| `src/Dagger.Game/Daggerfall/DaggerfallTuning.cs` | Typed current tuning aggregate. | Daggerfall ruleset typed tuning/profile loader; bundle loading comes later. |
| `src/Dagger.Game/Daggerfall/DaggerfallRewardReactions.cs` | Daggerfall reward/loot/XP policy. | Daggerfall ruleset. |
| `src/Dagger.Game/Daggerfall/Content/DaggerfallDefinitions.cs` | Daggerfall stat/track IDs, actor/item/HUD definitions, formulas/defaults; current `enemy-rat`/`enemy-skeletalwarrior` authored-name prefix heuristics. | Daggerfall ruleset; prefix heuristics are transitional importer adapters whose durable destination is `Daggerfall.Import` under #7323. |
| `src/Dagger.Game/Daggerfall/Content/DaggerfallMechanicsCatalog.cs` | Daggerfall definitions admitted through safe Mechanics stats/tracks. | Daggerfall ruleset consuming Engine Mechanics. |
| `src/Dagger.Game/Daggerfall/Content/PrivateersHoldContent.cs` | Current exact-file selection/parsing from Engine-admitted `ProductContent`, with source-shaped project/entity/sprite interpretation. | Transitional Daggerfall ruleset code; source-path/format quirks move to `Daggerfall.Import`, normalized result to packs. |
| `src/Dagger.Game/Daggerfall/Presentation/DaggerfallHudProjection.cs` | Daggerfall resource labels/order through Engine UI/Mechanics. | Daggerfall presentation. |
| `src/Dagger.Game/Daggerfall/Presentation/DaggerfallOutcomePresentation.cs` | Daggerfall outcome wording. | Daggerfall presentation. |
| `src/Dagger.Game/Daggerfall/Presentation/PrivateersHoldAppearance.cs` | Privateer's Hold appearance choices through Engine Appearance. | Daggerfall presentation/content-pack interpretation. |
| `src/Dagger.Game/Facts/ProductFacts.cs` | Product-local accepted-transition contracts/buffer. | Daggerfall ruleset unless second real composition proves a narrower Kit mechanism. |
| `src/Dagger.Game/Modules/Actors/ActorsState.cs` | Mechanics-backed actor lifetime/defeat state. | Daggerfall ruleset; current generic folder is not Kit proof. |
| `src/Dagger.Game/Modules/Combat/CombatDefinitions.cs` | Combat definitions and direct formula vocabulary. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Combat/CombatModule.cs` | Daggerfall melee/cooldown/RNG/track mutation. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Encounters/EncounterReaction.cs` | Daggerfall encounter consequence handling. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Encounters/EncounterState.cs` | Encounter state. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Encounters/EncounterSystem.cs` | Current proximity encounter policy. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Equipment/EquipmentState.cs` | Current local right-hand equipment state. | Daggerfall ruleset; future behavior consults/uses safe Engine Mechanics equipment substrate while policy stays Daggerfall-owned. |
| `src/Dagger.Game/Modules/Inventory/InventoryState.cs` | Current local carried-item state and add mutation. | Daggerfall ruleset; future behavior consults/uses safe Engine Mechanics inventory substrate while definitions/formulas/policy stay Daggerfall-owned. |
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
| Actor, item, encounter, world, asset, or placement value | Content pack. |
| Algorithmic invariant | Beside the owning algorithm. |
| Arena2/DFUnity conversion quirk | `Daggerfall.Import`. |
| Default shipped ruleset/bundle | Host. |

Avoid magic numbers at call sites and const sprawl. A small local structural
constant is honest; an authored or adjustable value needs a typed owner instead.

## Transitional current-source assumptions

The current product directly constructs `DaggerfallComposition` and defaults to
Privateer's Hold. Its starting loadout/gold, encounter, and appearance values are
current Daggerfall authored data, not Host/Kit defaults; they move into packs
through #7438/#7323. The current Daggerfall UI title and projection-contract
selection likewise remain Daggerfall presentation, not Host/Kit policy.

## Superseded task and architecture audit

The previous #7310-era generic `Modules` framing is superseded as target
authority: it usefully established explicit composition, domain-owned mutation,
direct ordered updates, local resolutions, and buffered facts, but did **not**
prove that those modules are reusable or belong in a Kit. They begin in the
Daggerfall ruleset under the current campaign.

Older Rust/Angular/HTTP/Studio/browser-runtime task language is likewise
superseded as implementation ownership. Its useful intended behavior remains
donor evidence: content admission, world and actor meaning, combat, progression,
equipment/inventory/loot semantics, presentation, and focused proof vectors.
It must be re-triaged into the WorldRpg campaign rather than executed under old
owners. Retired Rust/Angular tasks are not actionable default work.

## Campaign handoff

- **#7435 (current):** charter/map/task authority only; no runtime move.
- **#7441:** reconcile the current product with published Engine
  update/input/look/spatial/appearance contracts before the project split.
- **#7436:** establish the target compiled C# project graph and narrow
  Host/Kit/ruleset/session seam, preserving behavior.
- **#7437:** use an intentionally incompatible canary to prove the boundary and
  dependency laws; do not promote code merely to satisfy it.
- **#7438:** add loaded bundles, content packs, typed tuning, and resolution.
- **#7323 → #7325:** normalized Daggerfall first-contact content, faithful
  Daggerfall melee/consequences, then NativeAOT browser exercise.

No runtime code, empty project structure, or speculative architecture is
created by this documentation task.
