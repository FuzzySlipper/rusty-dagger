# Rusty Dagger / WorldRpg code and migration map

**Status:** living ownership inventory; the #7436 project graph, #7323 normalized Daggerfall content packs, #7324's first accepted Daggerfall melee/consequences slice, and #7325's Dagger-owned browser wiring are implemented. Campaign #7322 remains open pending final browser evidence after upstream Rusty Engine packaging issue #7510 is fixed.

**Snapshot:** 2026-08-28, WorldRpg foundation task #7436.

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
tuning, and the boundary canary are implemented. `Daggerfall.Import` remains
the owner of source conversion/provenance work; do not imply it landed merely
because the normalized runtime seam now exists.

The current #7441 Daggerfall integration consumes Engine `ProductUpdate.Facts` directly:
only running realtime batches with a finite positive `FixedDeltaSeconds` advance
this realtime Daggerfall product. The one input slice is interpreted on the
first admitted step and the remaining `AdmittedStepCount` steps reuse resulting
held state without replaying one-shot actions. Dagger does not derive a local
clock or reinterpret protocol numbers. Its safe direct service boundary is
Mechanics (stats/tracks), Look, Spatial, Appearance, Random, and UI; current
constructors and receipts match the generated contracts at Engine HEAD.

## Ownership model

| Layer | Owns | Does not own |
| --- | --- | --- |
| Rusty Engine | Host lifecycle/admitted updates, input, rendering/resources, spatial mechanisms, and published service families. | Daggerfall policy or product state. |
| `WorldRpg.Kit` | Compiled-ruleset/session contract, typed composition IDs, and reusable or placement-uncertain world-RPG mechanisms. Bundle/content-pack/tuning resolution remains #7438. | Generic-RPG universality or Daggerfall vocabulary. |
| `WorldRpg.Host` | Product lifecycle, explicit built-in ruleset/default selection, and session construction. Bundle/launcher policy remains later work. | Daggerfall formulas, actor meaning, Arena2 files, or Privateer's Hold IDs. |
| `WorldRpg.Rulesets.Daggerfall` | Daggerfall identities, rules, formulas, policies, current content interpretation, presentation, and mutable session state. | Engine machinery and importer source formats. |
| Content packs (target) | Authored actors, items, world/location/encounter/quest data, assets, placements, and scenario state. | Arbitrary executable C# behavior. |
| `Daggerfall.Import` (target) | Arena2/DFUnity formats, source paths/records, conversion quirks, provenance, and differential validation. | Runtime session or Host composition. |
| `RustyDagger.NativeProduct` | Handwritten product-type selection plus generated NativeAOT ABI/lifecycle/service/export output. | Product logic. |
| `src/ui` | DOM projection and semantic action presentation. | Gameplay state or world rendering. |

Rulesets are **compiled**; content packs, tuning profiles, and bundles are
**loaded**. Reusable or placement-uncertain mechanisms move to Kit by default;
the later canary validates the seam rather than authorizing promotion.
Daggerfall assumptions are legal only in the Daggerfall ruleset, content packs,
presentation, and importer lanes.

## Current Engine boundary

The product decides; Engine guarantees. Current active C# source uses the safe
generated **Mechanics** stats/tracks substrate for catalog creation, definitions,
entity binding, reads, and guarded track mutation. It also uses safe Look,
Spatial, Appearance, Random, and UI families. Mechanics already publishes a safe
item/inventory/equipment substrate, including catalog, inventory lifecycle, and
equip operations. Kit now provides a thin revision-guarded inventory coordinator;
Daggerfall defines its item identities and reward policy. Future Daggerfall work
must continue to use the safe substrate where it fits while retaining its
definitions, formulas, and policy in the ruleset.
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

## Active #7436 source map

| Active source family/file | Current owner and role |
| --- | --- |
| `src/WorldRpg.Kit/**` | Safe Kit project with typed composition, Mechanics-backed actor lifetime, configured controls/input frames, Engine-default spatial scene stepping, progression, bounded facts, structured UI values, and revision-guarded inventory/equipment coordination. Its equipment view joins Engine assignments to Engine unique-item inventory and its guarded mutations use observed Engine revisions. It references Engine only and contains no bundle resolver or Daggerfall vocabulary. |
| `src/WorldRpg.Rulesets.Daggerfall/WorldRpg.Rulesets.Daggerfall.csproj` | Safe Daggerfall ruleset project referencing Kit and Engine. |
| `DaggerfallRuleset.cs` | Compiled `daggerfall` ruleset implementation creating the current session. |
| `DaggerfallSession.cs`, `DaggerfallState.cs`, `DaggerfallTuning.cs`, `DaggerfallRewardReactions.cs` | Single mutable Daggerfall session and ordered composition, typed Daggerfall tuning, attack/reward policy, and direct use of Kit mechanisms. Initial unique items are Engine item entities staged into the player before the atomic initial equipment assignment; gold is an Engine fungible stack. Monster health is a keyed authored-range roll. `ProductUpdateState` is a Kit input frame derived from Engine input by the session. |
| `Content/DaggerfallDefinitions.cs`, `Content/DaggerfallBaseContent.cs` | Daggerfall-owned immutable typed actor/item/HUD/loot/attack definitions plus item kinds, equipment classifications, and authored slot policy, with bounded schema/version/ruleset/duplicate/reference diagnostics from `daggerfall.base`. No global catalog or source-name selection remains. |
| `Content/PrivateersHoldContent.cs`, `Presentation/PrivateersHoldAppearance.cs` | Explicit typed Privateer's Hold start state, entity-backed unique loadout/equip assignments, placements, authored encounter groupings, sprite meaning, and pack-authored world/nav/collision references. Existing navgrid/static-mesh shape reading is isolated as a temporary importer adapter; its coordinate conversion, source format, and provenance destination is `Daggerfall.Import`. |
| `Facts/*`, `Modules/Combat/*`, `Presentation/*` | Daggerfall combat facts/policy and Daggerfall presentation meaning. #7324 adopts direct exact-id player/rat/skeletal melee through Engine Mechanics: equipment truth is read from Engine, stamina spend and health damage are guarded Engine mutations, and damage/death/rewards are fact-ordered. A miss latches cadence; a hit latches it only after a valid accepted damage receipt. If damage application fails, no optimistic hit fact/cooldown is invented, while a previously accepted Engine stamina spend remains authoritative. It adapts the donor struck-body table to current scalar armor (the roll is retained but does not select body armor), selected hit formula omits donor monster +40/optional modifiers, and the local XP/500 progression experiment is not classic kill XP. It rejects target acquisition, range/LOS, encounter activation, senses, nearest-actor selection, and autonomous enemy/AI loops pending named owners. |
| `AssemblyInfo.cs` | Exact friend access for `WorldRpg.Rulesets.Daggerfall.Tests`. |
| `src/WorldRpg.Host/WorldRpg.Host.csproj`, `WorldRpgProduct.cs` | Safe Host project. It gates lifecycle and one Engine-admitted update, resolves the explicit built-in default ruleset, and delegates through `IGameSession`; it does not construct `DaggerfallSession`. |
| `src/RustyDagger.NativeProduct/RustyDagger.NativeProduct.csproj`, `NativeProduct.cs` | NativeAOT composition project. Its handwritten file has only the Engine product attribute selecting `WorldRpgProduct`; generated output remains under ignored `obj/`. |
| `tests/WorldRpg.Kit.Tests/*`, `tests/WorldRpg.Rulesets.Daggerfall.Tests/*` | Focused Kit mechanism and Daggerfall policy suites, including Host lifecycle/update/disposal coverage. |
| `src/ui/*`, `src/scripts/*`, `scripts/verify.sh` | DOM UI and build/launch/verification paths updated to the new NativeProduct project; UI renders the Daggerfall semantic projection plus the exact resolved generic composition identity and only claims declared semantic input. `run-product.sh` declares `attack=digital` through the Engine host; no gameplay authority or browser bridge is added. |
| `src/browser-bundle/**`, `src/**/bin/**`, `src/**/obj/**`, `tests/**/bin/**`, `tests/**/obj/**` | Generated output; never authority or handwritten source. |
| `content/worldrpg/payloads/daggerfall.base.json` | Normalized immutable Daggerfall base definitions: player, rat, skeletal warrior, items, attacks/loot, and HUD resources. |
| `content/worldrpg/payloads/daggerfall.privateers-hold.json` | Normalized Privateer's Hold start/loadout/look, explicit actor placements, scenario groupings, appearance refs, and stable spatial artifact refs. |
| `content/projects/privateers-hold.navgrid.json`, `content/imported/privateers-hold.static-mesh.json` | Admitted spatial artifacts currently read behind the Daggerfall ruleset adapter. Their source artifact shape and coordinate conversion must move to `Daggerfall.Import` producing a purpose-neutral normalized spatial output. |
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
| `src/Dagger.Game/Modules/Encounters/EncounterReaction.cs` | Daggerfall encounter consequence handling. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Encounters/EncounterState.cs` | Encounter state. | Daggerfall ruleset. |
| `src/Dagger.Game/Modules/Encounters/EncounterSystem.cs` | Current proximity encounter policy. | Daggerfall ruleset. |
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
| Actor, item, encounter, world, asset, or placement value | Content pack. |
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
loadout/gold/look, placements, appearances, and world refs are authored pack
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
- **#7325:** Dagger-owned NativeAOT/browser wiring publishes the resolved bundle,
  compiled ruleset, ordered packs, tuning, and existing composition/content/tuning
  fingerprints through Daggerfall's HUD; normal semantic attack honestly projects
  `NoTargetInReach`. Final clean-checkout browser certification remains blocked by
  upstream Rusty Engine packaging issue #7510 (stale committed browser artifacts).

Campaign #7322 is **not closed**: it awaits that final browser evidence. Follow-up
planning inventory includes import normalization, a real launcher, persistence
identity, authoring tooling, targeting/senses, and broader gameplay mechanisms.

No runtime code, empty project structure, or speculative architecture is
created by this documentation task.
