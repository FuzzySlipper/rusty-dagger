# Rusty Dagger

Rusty Dagger is the reference repository and proving product for WorldRpg.

WorldRpg is an opinionated construction kit and host for world-centric,
real-time, first-person systemic RPGs. The world is the durable center of
gravity. Story, quests, progression, combat, and characters are mechanisms for
inhabiting and unfolding the world, not a mandatory linear product spine.

Daggerfall is the first compiled ruleset, compatibility corpus, content source,
and game-bundle family. It is not the implicit WorldRpg architecture.

The working formula is: **Engine guarantees. Kit shapes. Ruleset decides.
Bundle assembles. Host launches.**

Ownership:

- Rusty Engine guarantees reusable infrastructure and admitted update services.
- WorldRpg.Kit defines the reusable world-RPG composition grammar and only
  mechanisms proven across compositions.
- WorldRpg.Host owns the product lifecycle, built-in ruleset registry, shipped
  bundles, launcher, defaults, and session selection.
- WorldRpg.Rulesets.Daggerfall owns all Daggerfall-specific semantics, formulas,
  identities, policies, presentation meaning, and content interpretation.
- Content packs own authored definitions, assets, worlds, placements, quests,
  encounters, and scenario state.
- Daggerfall.Import owns Arena2 and Daggerfall Unity source knowledge.
- RustyDagger.NativeProduct is the thin NativeAOT and Engine boundary; its ABI
  and service output are generated.

Code-bearing rulesets are compiled into the product. Content packs, validated
typed tuning profiles, and game bundles are loaded at runtime. Do not introduce
dynamic managed plug-in loading, reflection discovery, runtime C# compilation,
generic command buses, service locators, or a replacement gameplay DSL.

Code begins in the narrowest truthful owner.

A generic name is not evidence that code belongs in WorldRpg.Kit. Existing code
moves into the Daggerfall ruleset unless a second composition demonstrates that
the mechanism is genuinely reusable.

Daggerfall-specific assumptions are forbidden in WorldRpg.Kit. Concrete
ruleset references are permitted in WorldRpg.Host only at the explicit built-in
composition root.

Adjustable ruleset values belong in discoverable validated typed tuning handles;
authored actor, item, and world values belong in content packs; algorithmic
invariants stay beside their algorithms; source quirks belong in Daggerfall.Import;
and default bundle selection belongs in WorldRpg.Host.

There is one Rusty Engine-admitted update. WorldRpg does not create a parallel
loop, clock, timer, browser authority, or renderer.

For every task, identify:

- owning layer;
- new assumptions introduced;
- whether Daggerfall vocabulary is permitted;
- whether the change is code, tuning, content, import, or infrastructure;
- dependency changes;
- Daggerfall and boundary-canary proof.

## Ownership

> The product decides. The Engine guarantees.

C# owns gameplay state, entities, services, orchestration, content meaning, and
product policy. Engine supplies reusable infrastructure through generated,
direct, named service APIs.

Engine owns rendering resources, retained projection, frame construction,
backend realization, and canvas lifecycle. C# publishes product facts; it does
not build another renderer. TypeScript owns DOM UI only and must not acquire
authoritative gameplay state or render non-UI game elements.

Rust admits product updates. Dagger may order ordinary C# services with the
optional `Rusty.Engine.Application` pipeline or implement
`IEngineProduct.Update` directly. Dagger does not start a second loop, timer,
thread, or clock.

If a required capability is absent from the safe Engine API, the correct result
is a narrow upstream Engine request and an honest stop. Do not recreate Engine
machinery in C#, TypeScript, downstream Rust, a fake proof path, or a parallel
host merely to finish a task.

## Run the current product

The active build and launch command is:

```bash
src/scripts/run-product.sh
```

It builds the DOM UI, publishes `RustyDagger.NativeProduct` as a NativeAOT shared
library, and launches it through the adjacent Rusty Engine C# product host. The
current scripts assume the sibling Engine checkout at `/home/dev/rusty-engine`
and intentionally track that checkout forward rather than pinning an early SDK
shape.

Run the product only when the current task needs live behavior. Focused C#
compilation or NativeAOT publication is normally enough for organization and
boundary work.

## Donor material

The TypeScript under `gameplay/` remains valuable evidence for catalogs,
formulas, authored values, naming, and intended behavior. Translate that meaning
into clear C# product modules and services; do not port its package,
materialization, evaluator, or authority topology.

The root Rust crates, Angular application, HTTP server, Studio adapter, and old
verification graph are inactive donor material. They are not a parallel product
path or default acceptance gate for current work.

Daggerfall/Arena2 data remains operator-supplied. Preserve existing attribution
and provenance when adapting content.

## Guidance

Repository-specific instructions are in `AGENTS.md`. The shared practical brief
for downstream C# products lives in Den at the stable handle
`rusty-engine/downstream-csharp-agent-brief`. Its text is intentionally updated
in place while the Engine path matures.
