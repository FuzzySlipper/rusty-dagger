# Rusty Dagger / WorldRpg product guidance

## Direction and authority

Rusty Dagger is the reference repository and proving product for **WorldRpg**:
an opinionated construction kit and reference host for world-centric, real-time,
first-person systemic RPGs. The world is the durable center of gravity; story,
quests, progression, combat, and characters inhabit and unfold it rather than
forming a mandatory linear spine.

The durable formula is:

> **Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

Daggerfall is the first compiled ruleset, compatibility corpus, content source,
and game-bundle family. It is not implicit WorldRpg architecture. The C# path is
an evolving mainline product path, not a spike or a compatibility exercise.

- Dagger checkout: `/home/dev/rusty-dagger`
- paired Engine checkout: `/home/dev/rusty-engine`
- Dagger Den project: `rusty-dagger`
- current shared boundary brief: `[doc: rusty-engine/downstream-csharp-agent-brief]`
- current structure authority: Board post #139 and campaign #7322

Before substantial work, resolve the current Den task and project guidance, then
read the downstream brief for C# organization or Engine-boundary work. The user
request and owning task override older wording. If Den is unreachable, stop and
report the failed read rather than reconstructing direction from source or Git.

## Current state versus target graph

**Implemented today:** `src/WorldRpg.Kit/` contains the safe composition
contract and reusable world-RPG mechanisms, while `src/WorldRpg.Rulesets.Daggerfall/` owns Daggerfall policy and session composition,
`src/WorldRpg.Host/` owns product lifecycle and built-in selection, and
`src/RustyDagger.NativeProduct/` is the thin NativeAOT boundary. The ruleset's
current `Modules/` placement is a migration fact rather than an architecture boundary.

**Ordered target:** #7435 → #7441 → #7436 → #7437 → #7438 → #7323 → #7324 → #7325.

#7441 reconciled the current product with the published Engine
update/input/look/spatial/appearance contracts. #7436 establishes the project
graph; #7437 exercises Kit through an intentionally incompatible composition.

| Target owner | Responsibility |
| --- | --- |
| `WorldRpg.Kit` | Reusable and reasonably uncertain world-RPG mechanisms: typed IDs, compiled ruleset/session contracts, controls, actor lifetime, spatial session stepping, progression, bounded facts, structured UI values, and thin Engine-backed inventory coordination. Bundle/content-pack/tuning resolution is #7438 work. It is not a universal RPG framework. |
| `WorldRpg.Host` | Current reference-product lifecycle, explicit built-in ruleset/default selection, and session construction. Bundle/launcher selection and diagnostics expand later. It may select Daggerfall, never interpret Daggerfall rules or source files. |
| `WorldRpg.Rulesets.Daggerfall` | Daggerfall identities, formulas, attack and reward policy, content interpretation, presentation meaning, save behavior, and Daggerfall session composition. |
| `RustyDagger.NativeProduct` | The microscopic NativeAOT/Engine integration assembly. Handwritten code selects the Host product type; generated output supplies ABI, lifecycle adaptation, services, handles, and exports. |
| `Daggerfall.Import` | Offline Arena2 and Daggerfall Unity knowledge, source formats, conversion quirks, provenance, and differential validation. Runtime code consumes normalized packs, not source-shaped data. |
| Content packs | Authored actors, items, worlds, placements, encounters, quests, assets, and scenario state interpreted by a ruleset. |
| TypeScript UI | Thin DOM presentation of Engine-delivered projections and semantic actions. It owns neither gameplay state nor game-world rendering. |

Rulesets are **compiled** into the NativeAOT product. Content packs, typed tuning
profiles, and game bundles are **loaded**. Adding code-bearing ruleset semantics
requires a product rebuild; changing valid content/tuning does not. Do not add
runtime assembly loading, reflection discovery, `Assembly.Load`, service
locators, generic command buses, a new gameplay DSL, or a universal plug-in ABI.

## Kit, Daggerfall, and tuning rules

Reusable mechanisms begin in `WorldRpg.Kit`; when placement is genuinely
uncertain, prefer Kit and keep concrete Daggerfall policy behind ruleset-owned
definitions and configuration. The canary validates that seam later; it is not a
promotion gate. Do not make Kit universal or move Daggerfall vocabulary merely
by renaming it.

Daggerfall assumptions are legal only in the Daggerfall ruleset, Daggerfall
content packs, Daggerfall presentation, and `Daggerfall.Import`. The Host may
choose a built-in Daggerfall ruleset/bundle only at its explicit catalog/default
composition seam. The Kit must not mention Daggerfall, Arena2, Privateer's Hold,
or DFUnity vocabulary.

Give each value one honest home:

- Adjustable ruleset values use discoverable, validated, typed tuning handles.
- Actor, item, encounter, and world values belong in content packs.
- Algorithmic invariants stay beside the owning algorithm.
- Source-format quirks stay in `Daggerfall.Import`.
- Product default selection stays in the Host.

Do not solve this with magic numbers hidden in call sites or a const field for
every authored value. Keep compact structural constants local and promote a
value only when it is genuinely adjustable or authored data.

## Engine boundary

> The product decides. The Engine guarantees.

The product owns application/gameplay logic, authoritative state, entities,
catalogs, content meaning, policy, and ordering within each Engine-admitted
update. Engine owns reusable host lifecycle/admission, input, rendering and
resources, spatial mechanisms, and published service families.

Use direct safe named C# Engine APIs. The current product uses the Mechanics
stats/tracks substrate (catalogs, entity binding, reads, and guarded mutations),
plus Look, Spatial, Appearance, Random, and UI. Mechanics also already publishes
safe item, inventory, and equipment catalog/lifecycle/equip operations; use that
substrate through Kit coordination while Daggerfall retains item definitions and
policy. Daggerfall
definitions, formulas, and policy remain in the ruleset. Other capabilities are
usable only when their safe generated C# contract is verified. The generated
surface also includes Content/ContentStore, Persistence, Rules (StandardExact and
StandardContinuous), Animation, Audio, and CameraView; this is boundary routing,
not an API catalog. Reverify each contract when it is used, and keep product
semantics and policy in the ruleset.

Do not write downstream Rust or move product logic into Rust. Ordinary safe
product code must not use `unsafe`, pointers, `Native*`, `GCHandle`, raw
statuses, or handwritten native declarations. Generated `obj/` sources are
ignored output: never edit or commit them.

If a required behavior is absent from the safe API: name the behavior and Engine
owner, confirm no safe wrapper already exposes it, file one narrow
purpose-neutral `rusty-engine` request, and stop at that boundary. Do not
reimplement Engine machinery in C#, TypeScript, or downstream Rust, and do not
substitute a fake proof path or parallel host.

## Update, donors, and evidence

There is one Engine-admitted update. Host/ruleset code may use the optional
`Rusty.Engine.Application` phases when it simplifies the product or implement
`IEngineProduct.Update` directly; it must not create a second loop, clock,
timer, thread, browser authority, ECS, scheduler, service locator, or renderer.

`gameplay/` is semantic donor material for formulas, catalogs, authored meaning,
and behavior. Root Rust crates, Angular, the HTTP server, Studio adapter, old
scripts, and their gates are inactive donor material. Preserve useful semantics;
do not revive their runtime/evaluator/package/transport/authority topology.
`src/browser-bundle/` and generated NativeAOT output are assembled output, not
another product runtime or an authority source.

Use only focused generation, safe compilation, NativeAOT publication, or direct
exercise required by the current task. Do not run retired Rust/Angular/broad
browser/packaging gates unless the task explicitly calls for them. Keep this
file and the README factual and compact; the migration map records current and
target ownership in detail.
