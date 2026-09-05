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
- WorldRpg.Kit defines the reusable world-RPG composition grammar and the
  ordinary mechanisms needed to construct a world RPG.
- WorldRpg.Host owns the product lifecycle, built-in ruleset registry, shipped
  bundles, launcher, defaults, and session selection.
- WorldRpg.Rulesets.Daggerfall owns all Daggerfall-specific semantics, formulas,
  identities, policies, presentation meaning, and content interpretation.
- Content packs own authored definitions, assets, worlds, placements, quests,
  and scenario state.
- Daggerfall.Import owns Arena2 and Daggerfall Unity source knowledge.
- `WorldRpg.Host` is the ordinary product entry. The packaged SDK generates
  CoreCLR and NativeAOT composition beneath ignored `obj` output.

Code-bearing rulesets are compiled into the product. Content packs, validated
typed tuning profiles, and game bundles are loaded at runtime. Do not introduce
dynamic managed plug-in loading, reflection discovery, runtime C# compilation,
generic command buses, service locators, or a replacement gameplay DSL.

Reusable mechanisms, and mechanisms whose placement is genuinely uncertain,
begin in WorldRpg.Kit. Daggerfall retains only its identities, formulas, attack
and reward policy, content interpretation, presentation meaning, and source
quirks. The canary validates this seam later; it is not a promotion gate.

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
- focused proof for the owning mechanism and ruleset policy.

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

## Develop and verify the current product

The checked product consumes immutable `Rusty.Engine` package
`0.1.0-dev.de226048b4d8` from the installed `.runtime/sdk-feed` and the
matched `.runtime/runtime-pack`. Start a clean checkout with the pinned,
noninteractive pair install; it validates the release checksum, payloads, ABI,
package version, and Engine source revision before atomically replacing the
whole ignored pair.

```bash
./scripts/install-engine-pair.sh
```

This follows the operational runbook
`rusty-engine/downstream-csharp-sdk-runbook`. The repository neither builds
the pair nor copies an Engine checkout into product sources.

Ordinary edit-run development is CoreCLR through the packaged host:

```bash
npm ci
./.runtime/runtime-pack/bin/rusty dev \
  --project ./src/WorldRpg.Host/WorldRpg.Host.csproj \
  --runtime ./.runtime/runtime-pack
```

The Host declares the semantic `attack=digital` intent and Engine-owned held
WASD mappings for `move.*`; the DOM attack control only claims that declared
intent and renders the `dagger.hud` projection. The SDK compiles the
product-owned DOM UI and atomically stages the loose Product bundle. The
runtime pack owns the host, browser shell, renderer, and browser transport.

NativeAOT is a separate fidelity/release check, not the edit-run loop:

```bash
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:VerifyRustyEngineAot
```

Engine contributors may use `rusty dev --engine-source /absolute/rusty-engine`.
That explicit opt-in supplies source references and a source runtime pack;
normal downstream builds never discover an adjacent checkout.

`WorldRpg.SpriteWorkbench` remains a package-backed product tool. Its launcher
stages its operator-selected publication into ignored workbench content, then
runs the same `rusty dev` workflow; it no longer assembles a browser host or
calls Cargo directly.

## Content and migration boundary

The product is C#-only. Useful donor semantics have been translated into loaded
content, typed tuning, `Daggerfall.Import`, and compiled Daggerfall ruleset
policy; the former Rust workspace, TypeScript gameplay evaluator, and encounter
demonstration topology are not present as fallback paths.

Daggerfall/Arena2 source data remains operator-supplied. Preserve the checked
in imported/authored assets, attribution, and provenance when adapting content.

## Guidance and proof

Repository-specific instructions are in `AGENTS.md`. For setup, compilation,
and running, use the stable Den runbook
`rusty-engine/downstream-csharp-sdk-runbook`; the older
`rusty-engine/downstream-csharp-agent-brief` remains the ownership reference.

Run `./scripts/verify.sh` after installation for pair verification, pinned UI
dependency installation, focused package restore/build, architecture, CoreCLR
staging, and explicit NativeAOT fidelity proof. Hosted CI is not
declared until immutable Engine artifacts are published for clean runners; do
not replace it with a cloned Engine checkout or downstream provider build.
