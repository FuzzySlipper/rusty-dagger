# Rusty Dagger / WorldRpg code and migration map

**Status:** #7544 C#-only product cutover. The legacy Rust workspace, TypeScript
gameplay evaluator, generated gameplay package, encounter demo data, Studio
adapter, Angular shell, and their retired launch/proof paths have been removed.
They are not supported fallback runtimes.

## Authority

The current user request and owning Den task decide work. The durable product
shape is:

> **Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

The shared `[doc: rusty-engine/downstream-csharp-agent-brief]` governs the
safe C# / Engine ownership boundary. The current
`[doc: rusty-engine/downstream-csharp-sdk-runbook]` governs acquiring,
verifying, compiling, and running the exact published SDK/runtime pair.
Rulesets are compiled into the SDK-generated product composition;
content packs, tuning profiles, and bundles are loaded. A missing safe Engine
contract is an upstream request and an honest stop, never a downstream Rust,
browser, or local-Engine substitute.

## Active product graph

| Owner | Active responsibility |
| --- | --- |
| Rusty Engine | Admitted host lifecycle and update, input, rendering/resources, spatial mechanisms, and published service families. |
| `WorldRpg.Kit` | Typed composition and reusable or placement-uncertain world-RPG mechanisms, including Engine-backed controls, actor lifetime, spatial stepping, bounded facts, progression, UI values, and inventory/equipment coordination. |
| `WorldRpg.Host` | Ordinary product entry, lifecycle, explicit built-in ruleset/default selection, and session construction. The immutable SDK generates CoreCLR and NativeAOT composition beneath ignored `obj` output. |
| `WorldRpg.Rulesets.Daggerfall` | Daggerfall identities, formulas, attack/reward policy, content interpretation, presentation meaning, save behavior, and session composition. |
| `Daggerfall.Import` and `.Tool` | Offline Arena2/DFUnity decoding, normalized import/publication, provenance, and differential validation. It has no runtime entity, renderer, scheduler, or encounter authority. |
| `src/ui` and `src/sprite-ui` | Thin DOM rendering of Engine-delivered projections and semantic actions. The sprite workbench uses Engine content, appearance, and playback rather than a UI renderer or timer. |
| `content/worldrpg/**` | Versioned Daggerfall packs, tuning, bundle selection, normalized Privateer's Hold publication, hashes, and source provenance. |

`WorldRpg.Kit` never names Daggerfall, Arena2, Privateer's Hold, or DFUnity.
The Host may select a built-in Daggerfall bundle only at its explicit
composition seam. Daggerfall-specific values stay in the ruleset or content;
adjustable policy is exposed through typed tuning and authored values stay in
content packs.

## Retained evidence and content

`content/worldrpg/**` is the active loaded content boundary. The adjacent
imported/authored asset corpus and source-hash/provenance records remain
preserved as content evidence; they do not restore the removed Rust or
TypeScript runtime topology. Local copyrighted Arena2 inputs are optional
operator material and are not committed.

`src/playtest.json`, the package-backed CoreCLR launcher, focused C# test
projects, and the SDK-hosted sprite workbench remain the current proof and
operator surfaces. `scripts/install-engine-pair.sh` atomically installs the
pinned complete SDK/runtime pair beneath `.runtime`; the installed SDK
feed/runtime pack and generated
`bin/**`/`obj/**` output are not handwritten authority; no Engine browser bundle,
NativeProduct bridge, or Cargo host is retained in this repository.

## Legacy disposition ledger

| Former family | Disposition | Current evidence / owner |
| --- | --- | --- |
| Arena2 and Daggerfall Rust decoders/importers | **Adapted, then deleted.** | `Daggerfall.Import` owns checked offline decoders, source transforms, normalized publication, byte/hash provenance, and focused differential fixtures. |
| Rust Daggerfall catalog/formula/runtime graph | **Adapted or rejected, then deleted.** | Chosen catalogs/formulas live in Daggerfall content and compiled ruleset policy; generic mechanisms live in Kit over Engine. The local runtime loop, renderer, clock, targeting helpers, patrol, route tools, and command binaries were rejected. |
| TypeScript `gameplay/**` catalogs and authoring | **Adapted or rejected, then deleted.** | Selected authored records are loaded WorldRpg content and selected rules are compiled Daggerfall policy. The package envelope, evaluator, expression/program grammar, materializer, and TypeScript build were rejected. |
| Generated `dagger-core` package | **Rejected and deleted.** | It was output for the retired evaluator, not a runtime content contract. |
| Encounter catalog, schemas, gallery, grouping, scheduler, and data | **Rejected and deleted.** | They were a former combat demonstration scaffold, not Daggerfall world, AI, targeting, or content-pack vocabulary. Future combat proof requires its own design task. |
| Studio adapter, HTTP server, Angular product shell, old browser assertions, and source-coupled Engine browser assembly | **Adapted then retired.** | Import provenance tooling and `WorldRpg.SpriteWorkbench` preserve the useful diagnostics and sprite inspection/animation/save workflow through Engine services. The packaged runtime pack owns browser shell, renderer, and transport. |
| Cargo workspace, lockfile, crate manifests, checked NativeProduct bridge, and root package topology | **Rejected and deleted.** | The C# project graph plus immutable Engine SDK is the sole product implementation path. CoreCLR is ordinary development; NativeAOT is an explicit fidelity/release target. |

### Removed source accounting

| Former path family | Source count | Final disposition |
| --- | ---: | --- |
| `crates/arena2/**` | 17 Rust files | Adapted as offline `Daggerfall.Import` decoder and source-transform semantics, then deleted. |
| `crates/dagger-import/**` | 8 Rust files | Adapted as normalized import, publication, and provenance semantics, then deleted. |
| `crates/dagger-rpg/**` | 11 Rust files | Chosen catalogs/formulas adapted to content and Daggerfall policy; evaluator and encounter machinery rejected; files deleted. |
| `crates/dagger-runtime/**` | 14 Rust files | Kit/Engine/ruleset semantics adapted selectively; local runtime, renderer, scheduler, and tools rejected; files deleted. |
| `gameplay/**` | 19 TypeScript/script/config files | Chosen content and policy semantics adapted; evaluator, materializer, package, and encounter topology rejected; files deleted. |

`data/sprite-names.json` is retained as historical/authored naming evidence with
no active consumer or runtime authority. Its removal is deferred only to a
future content-specific cleanup if desired; it is not an incomplete donor
classification.

## Focused verification

Use only proof that exercises the current C# product boundary: package-backed
build/test projects, SDK-owned CoreCLR staging, the explicit NativeAOT target,
and a direct browser exercise when the owning task requires visible behavior.
Do not run deleted Cargo, Angular, TypeScript gameplay, Studio, HTTP, or legacy
browser/package assembly gates.
