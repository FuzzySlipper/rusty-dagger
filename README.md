# Rusty Dagger — C# gameplay trial

This branch tests Rusty Dagger as an ordinary C# application/game on Rusty
Engine's experimental NativeAOT API.

- Dagger worktree: `/home/dev/worktrees/rusty-dagger-csharp-runtime`
- Dagger branch: `codex/csharp-product-runtime`
- Engine worktree: `/home/dev/worktrees/rusty-engine-csharp-runtime`
- Engine branch: `codex/csharp-nativeaot-trial`

Neither branch is stable main.

## Active layout

```text
src/
  Dagger.Product/   C# application, gameplay, and native composition root
  ui/               thin TypeScript DOM UI only
  scripts/          candidate build and launch plumbing

gameplay/           pre-pivot TypeScript semantic donor; not an active runtime
content/            imported Privateer's Hold product inputs
```

The root Rust crates, Angular application, HTTP product server, Studio adapter,
and their verification scripts are historical donor material on this branch.
Do not extend them as a parallel product path.

## Direction

> The product decides. The Engine guarantees.

C# owns gameplay state, services, orchestration, and product meaning. The paired
Engine branch supplies generated direct APIs for durable mechanisms. The
NativeAOT boundary is trusted and uses direct function tables rather than JSON
method/result traffic.

Engine owns rendering infrastructure. C# publishes product facts through named
Engine APIs. TypeScript owns DOM UI only and must never create game-world
rendering, a second canvas, or parallel retained state.

If a required Engine function is missing, stop and request it upstream. That is
preferred to recreating Engine infrastructure downstream.

## Current preparation state

The existing C# prototype predates the generated #7289 callback tables. Moving
it under `src/` establishes the source organization but does not certify it as
the next gameplay foundation. The first implementation task must replace its
old native export/output-buffer glue with the paired Engine's generated
bindings before extending gameplay. Do not hide that integration gap with a
compatibility shim or JSON bridge.

The candidate launch command lives at:

```bash
src/scripts/run-product.sh
```

It resolves `/home/dev/worktrees/rusty-engine-csharp-runtime` explicitly. Run it
only when the current task calls for product execution; this preparation task
does not require it to pass.

## Donor material

The valuable pre-pivot TypeScript under `gameplay/` remains available for
catalogs, formulas, authored content meaning, and naming. Translate those ideas
into clear C# domain modules and services. Do not port the package/evaluator/AST
topology merely because it already exists.

Daggerfall/Arena2 game data remains operator-supplied. Preserve existing source
attribution and donor provenance in code/content records while this temporary
README stays intentionally small.

## Documentation

The old architecture prose was intentionally removed/replaced on this branch.
If the C# direction is selected, a later focused documentation task will recover
useful durable concepts from stable history and write a new coherent set from
the demonstrated implementation.
