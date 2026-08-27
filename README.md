# Rusty Dagger

Rusty Dagger is an evolving C# game built on Rusty Engine's active NativeAOT
downstream path.

This is no longer an architecture trial. The C# implementation is the product's
mainline implementation and new work is fixed forward there. The path is still
fresh and raw: Engine service coverage, safe API ergonomics, build tooling, and
Dagger's internal organization will continue changing as real gameplay is moved
over and missing upstream capabilities are exposed.

- Dagger checkout: `/home/dev/rusty-dagger`
- Engine checkout: `/home/dev/rusty-engine`
- Evolving agent brief: `rusty-engine/downstream-csharp-agent-brief`

## Active layout

```text
src/
  Dagger.Game/          safe C# application and gameplay
  Dagger.NativeProduct/ thin generated NativeAOT composition boundary
  ui/                   DOM UI TypeScript only
  browser-bundle/       assembled Engine host and UI output
  scripts/              current build and launch path

gameplay/               pre-pivot TypeScript semantic donor
content/                imported Privateer's Hold product inputs
```

`Dagger.Game` references the safe `Rusty.Engine` contracts with unsafe code
disabled. It owns product state, content interpretation, gameplay services,
orchestration, and renderer-neutral product facts.

`Dagger.NativeProduct` contains one handwritten product selection. Rusty
Engine's source generator supplies its internal ABI layouts, safe-service
implementations, lifecycle adaptation, handle ownership, and native exports.
Ordinary gameplay does not handle pointers, native statuses, callback tables,
or `GCHandle` lifetimes.

The current `Dagger.Game` files are a compact first landing, not a frozen
application architecture. Ongoing Den work is reorganizing them around explicit
composition, product state, entities, named services, and realtime-neutral
update ordering before broader gameplay migration continues.

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

It builds the DOM UI, publishes `Dagger.NativeProduct` as a NativeAOT shared
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
