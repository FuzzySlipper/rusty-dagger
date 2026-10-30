# Rusty Dagger / WorldRpg agent guidance

Rusty Dagger is the reference repository and proving product for **WorldRpg**:
an opinionated construction kit and reference host for world-centric, real-time,
first-person systemic RPGs. The world is the center of gravity; story, quests,
progression, combat and characters inhabit it rather than forming a linear spine.

> **Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

Daggerfall is the first compiled ruleset, compatibility corpus, content source
and game-bundle family. It is not implicit WorldRpg architecture.

## Sources of truth

Den (project `rusty-dagger`) owns tasks, their status, dependencies and
evidence. This file and the repository docs describe how the product is built,
not where work stands; don't record progress, completion or "current" task
state here. Before substantial work, read the owning Den task. The user request
and that task override older wording elsewhere. If Den is unreachable, say so
and don't invent task records or infer completion from source or Git.

- Gameplay and persistence design: [docs/gameplay-design.md](docs/gameplay-design.md)
- Actors and mechanics owners: [docs/actors-and-mechanics.md](docs/actors-and-mechanics.md)
- Game UI: [docs/game-ui.md](docs/game-ui.md)
- Coverage direction: `docs/daggerfall-coverage-plan.md`; scope contract and
  decisions: `docs/daggerfall-task-preparation.md`; stable inventory IDs:
  `docs/coverage/` (`content-scope.md` names each content section's owner)
- Engine boundary brief (C# organization or Engine-boundary work):
  `[doc: rusty-engine/downstream-csharp-agent-brief]`
- Review lanes and packets: `docs/agent-review/`
- The paired Engine checkout is `rusty-engine`, beside this one.

## Projects and ownership

| Owner | Responsibility |
| --- | --- |
| `WorldRpg.Kit` | Reusable world-RPG mechanisms: typed IDs, ruleset/session contracts, bundle/content-pack/tuning resolution, controls, actor lifetime, spatial session stepping, progression, bounded facts, structured UI values, inventory/equipment, targeting, attack execution, effects, AI coordination, corpse/loot and typed notifications. Not a universal RPG framework. |
| `WorldRpg.Rulesets.Daggerfall` | Daggerfall identities, formulas, attack and reward policy, content interpretation, presentation meaning, save behavior and session composition. Domain folders and `Modules/` are organization, not boundaries. |
| `WorldRpg.Rulesets.Daggerfall.Variants` | Compiled deviations from classic rules that a bundle selects with `variants`. Each declares a manifest (ruleset, claimed seams, tuning profile, content packs) and replaces whole seam owners. Classic owners never branch on a variant and never reference this project. |
| `WorldRpg.Host` | Product lifecycle, the one product type and bundle declaration, built-in ruleset/default selection, the entry screen's bundle choice, and session construction. It may select Daggerfall; it never interprets Daggerfall rules or source files. |
| `Daggerfall.Import` | Offline Arena2 and Daggerfall Unity knowledge: source formats, conversion quirks, provenance, differential validation. Runtime code consumes normalized packs, not source-shaped data. |
| `WorldRpg.SpriteAuthoring` | Product-neutral sprite inspection document and overlay contract. No references, packages or ruleset vocabulary. |
| `WorldRpg.SpriteWorkbench` | Developer-only Engine product for sprite atlases and overlays, not part of a bundle. References only Kit and SpriteAuthoring. |
| Content packs | Authored actors, items, worlds, placements, encounters, quests, assets and scenario state that a ruleset interprets. |
| TypeScript UI | Thin DOM presentation of Engine-delivered projections and semantic actions. It owns no gameplay state and no world rendering. |

The `Rusty.Engine` SDK generates the ABI, lifecycle adaptation and both
CoreCLR and NativeAOT compositions beneath ignored `obj/`. Never edit or commit
generated `obj/` sources.

Rulesets are **compiled** into the product; content packs, typed tuning
profiles and game bundles are **loaded**. Code-bearing rule changes need a
rebuild; valid content or tuning changes don't. No runtime assembly loading,
reflection discovery, ambient `Resolve<T>()`, generic command buses, gameplay
DSL or plug-in ABI. Explicitly composed, named Kit services and typed RuleEvents
are the way to make contributions discoverable.

## Kit, Daggerfall and tuning

Reusable mechanisms start in the Kit; when placement is uncertain, prefer the
Kit and keep concrete Daggerfall policy behind ruleset-owned definitions. The
Kit never mentions Daggerfall, Arena2, Privateer's Hold or DFUnity vocabulary;
those belong to the ruleset, its content and presentation, and the importer.
Don't move Daggerfall vocabulary into the Kit by renaming it.

Give each value one home: adjustable rule values are typed, validated tuning
handles; actor, item, encounter and world values are content; algorithmic
invariants stay beside their algorithm; source quirks stay in the importer;
product defaults stay in the Host. Keep compact structural constants local.

## Gameplay composition

Compose Engine `Actor` in Kit/Dagger facades with named properties (`Stats`,
`Inventory`, `Equipment`, `Targeting`) over the attached components.
`DaggerActorFactory` assembles actors and `DaggerfallState.Kit` provides the
session services. Factories create components; wrapping an entity never does.
Keep runtime EntityId, kind/origin TypeId and durable product identity distinct.

Use direct methods for simple reads and actions, typed RuleEvents where
participants contribute, and typed notifications for completed changes.
Gameplay mutates through canonical Kit owners; it doesn't need proposals,
snapshots, receipts or rollback.

Saves use Engine `ProductStateStore` with `JsonProductStateCodec` and
source-generated `JsonTypeInfo`. Capture meaningful state at explicit save
boundaries, rebuild shared mechanics references, and restore authored stat
inputs before tracks. Never serialize native handles or derive identity from
runtime entity IDs. Only the current schema exists: breaking development saves
is fine, so add no schema versions, migrations or compatibility readers. Report
malformed data or missing definitions; don't silently drop state.

## Engine boundary

> The product decides. The Engine guarantees.

The product owns gameplay logic, authoritative state, catalogs, content
meaning, policy and ordering within each Engine-admitted update. The Engine owns
host lifecycle, input, rendering and resources, spatial mechanisms, and its
published services (Look, Spatial, Appearance, Random, UI, Content, Persistence,
Animation, Audio, CameraView, mechanics `Stat`/`Track`/`StatsComponent`,
inventory, equipment and effects facades). Check the packaged contract when you
use one.

- One Engine-admitted update. The `Rusty.Engine.Application` phases are
  optional. No second loop, clock, timer, thread, scheduler, ECS or renderer;
  product game time advances inside admitted updates.
- Safe C# only: no `unsafe`, pointers, `Native*`, `GCHandle`, raw statuses,
  handwritten native declarations or downstream Rust.
- If a needed behavior isn't in the safe API, confirm no wrapper exposes it,
  then implement it upstream if that's authorized, or file one narrow
  `rusty-engine` request and report the block. Don't rebuild Engine machinery
  downstream or fake a substitute path.
- Use the packaged SDK and runtime pack. `rusty dev` is ordinary development;
  NativeAOT is an explicit fidelity/release target; `--engine-source` is an
  explicit contributor override only.
- Move the Engine pin only with `rusty update`, in a worktree, and commit it
  with the code that needs it in the same turn. An uncommitted pin in the shared
  checkout blocks everyone's next pull.

## Game files and assets

Never commit the Arena2 directory, the music, or media extracted directly from
them. Don't commit reproducible or ephemeral output either: importer output
(the `local/*.rpak` containers built by `Daggerfall.Import.Tool import`), study
renders and other WIP. Fix generated output by changing the importer or the
authored source, never by hand. Finished art made for the game, including art
inspired by the original, is ordinary content: commit the finals. Small facts
from the game (identifiers, names, record numbers, short excerpts) are fine
wherever they're useful.

## Donors

The original game and `/home/research/daggerfall-unity` are references for
behavior and content, not for code style or architecture. Consult donor
behavior when implementing Daggerfall compatibility; independently specified
WorldRpg features don't need it. Adapt UI concepts to thin DOM presentation and
semantic actions. Don't port Unity widgets, MonoBehaviours, singletons,
Resources loading, Unity serialization or DFU GameManager topology. No MIDI
playback.

## Working

- **Tasks are complete behavior.** Include the real callers, state changes,
  content and save/UI interoperability. A demo, proof slice, hardcoded example
  or partial adapter isn't a finished task. When rescoping, move the deferred
  requirement to a concrete receiving task and point to it.
- **Reuse first.** Before creating a mechanism, check the Engine surface and
  existing owners.
- **Checks.** Run focused compilation and tests suited to the change. Window
  runs, UI tests and screenshots use at least 1920x1080, which is the minimum
  supported window.
- **Tests** record intended behavior, not authority. When a change supersedes
  an expectation, update or remove the test in the same change.
- **Failures.** A hard failure must name the loss it prevents: data loss, an
  ownership violation or a silently wrong artifact. Otherwise warn and report
  the observed value.
- **Docs** describe current design, not history or status. No commit hashes or
  pinned versions in prose; evidence and dated reports go to Den.
- **Code** should be clear and small: match surrounding style, name things for
  what they mean, and leave no dead branches, parallel paths or speculative
  abstraction.
- **Git.** Commit and push each turn's or task's work directly to `main`. This
  is a solo repository. Keep commits scoped and leave unrelated dirty files
  alone.
