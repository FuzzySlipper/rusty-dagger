# Rusty Dagger — C# gameplay

Rusty Dagger is moving forward as an ordinary C# application/game on Rusty
Engine's NativeAOT API.

- Dagger checkout: `/home/dev/rusty-dagger`
- Engine checkout: `/home/dev/rusty-engine`

## Active layout

```text
src/
  Dagger.Game/          safe C# application and gameplay
  Dagger.NativeProduct/ generated NativeAOT composition boundary
  ui/                   thin TypeScript DOM UI only
  scripts/              candidate build and launch plumbing

gameplay/           pre-pivot TypeScript semantic donor; not an active runtime
content/            imported Privateer's Hold product inputs
```

The root Rust crates, Angular application, HTTP product server, Studio adapter,
and their verification scripts are historical donor material.
Do not extend them as a parallel product path.

## Direction

> The product decides. The Engine guarantees.

C# owns gameplay state, services, orchestration, and product meaning. Engine
supplies generated direct APIs for durable mechanisms. The
NativeAOT boundary is trusted and uses direct function tables rather than JSON
method/result traffic.

Engine owns rendering infrastructure. C# publishes product facts through named
Engine APIs. TypeScript owns DOM UI only and must never create game-world
rendering, a second canvas, or parallel retained state.

If a required Engine function is missing, stop and request it upstream. That is
preferred to recreating Engine infrastructure downstream.

## Current implementation state

`Dagger.Game` owns ordinary safe C# gameplay, state, content interpretation,
and Engine service use. It references only the safe `Rusty.Engine` contracts.
`Dagger.NativeProduct` selects that product type and receives its internal ABI,
service implementations, lifecycle adaptation, and exports from the Engine
source generator.

The candidate launch command lives at:

```bash
src/scripts/run-product.sh
```

It resolves the stable sibling Engine checkout. Run it only when the current
task calls for product execution.

## Donor material

The valuable pre-pivot TypeScript under `gameplay/` remains available for
catalogs, formulas, authored content meaning, and naming. Translate those ideas
into clear C# domain modules and services. Do not port the package/evaluator/AST
topology merely because it already exists.

Daggerfall/Arena2 game data remains operator-supplied. Preserve existing source
attribution and donor provenance in code/content records while this temporary
README stays intentionally small.

## Documentation

The old architecture prose was intentionally removed. A later focused
documentation task will recover useful durable concepts from Git history and
write a new coherent set from demonstrated implementation.
