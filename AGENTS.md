# Rusty Dagger C# trial guidance

## Scope and pairing

This guidance applies only to:

- Dagger worktree `/home/dev/worktrees/rusty-dagger-csharp-runtime`
- Dagger branch `codex/csharp-product-runtime`
- Engine worktree `/home/dev/worktrees/rusty-engine-csharp-runtime`
- Engine branch `codex/csharp-nativeaot-trial`

Stable Rusty Dagger and Engine `main` are not the implementation targets for
this trial. Do not redirect the experiment to `/home/dev/rusty-engine`, merge
either branch to main, or revive a stable-main compatibility path during an
ordinary task.

## Den

- Project ID: `rusty-dagger`.
- Resolve live Den guidance before substantial work. The user's current task
  and C#-trial task descriptions override older project documents that prescribe
  downstream Rust, compiled TypeScript gameplay, Angular, HTTP product servers,
  Product Model, or the previous authority posture.
- If Den is unreachable, stop and report the failed tool. Do not reconstruct
  current task state from deleted prose or old commits.

## Active source shape

- `src/Dagger.Product/` is the sole active downstream application/gameplay
  implementation lane.
- `src/ui/` is a thin DOM UI adapter. It may present projections and submit
  semantic UI actions; it must not own gameplay state or render non-UI game
  elements.
- `src/scripts/` builds the UI/product and launches the paired Engine branch.
- `content/` and imported project/nav data remain product inputs.
- `gameplay/` is semantic donor material from the pre-pivot TypeScript design.
  Consult its catalogs, formulas, and authored meaning, but do not execute it,
  extend its runtime architecture, or reproduce its AST/package machinery in
  C#.
- Existing root Rust crates, Angular application, HTTP server, Studio adapter,
  Cargo workspace, package graph, and old scripts are historical donor code.
  They are not an active product route and their tests are not acceptance gates
  for the C# trial.

## Product and Engine responsibilities

> The product decides. The Engine guarantees.

- C# owns all new downstream application/game logic, authoritative product
  state, gameplay services, orchestration, content meaning, and policy.
- Do not write new downstream Rust. Do not move gameplay back into Rust to make
  an existing type, test, or boundary easier to satisfy.
- Use the generated C# API from the paired Engine for lifecycle, input,
  rendering/presentation, spatial mechanisms, resources, persistence
  primitives, and other reusable infrastructure.
- Direct typed calls and ordinary C# organization are the green path. Do not
  create JSON method protocols, string dispatch, capability registries, generic
  command buses, schema/version negotiation, or security ceremony.
- Engine owns the renderer, resources, retained handles, frames, backend, and
  canvas. C# supplies product facts through named Engine functions. TypeScript
  supplies DOM UI only. Any TypeScript/browser rendering of non-UI game elements
  is a wrong turn.

## Stopping rule

If the product needs an Engine capability that the generated API does not
provide, stop. Report the exact upstream need and request an Engine task. Do not
recreate the mechanism in C#, TypeScript, a fake renderer, a test harness, or a
parallel host merely to mark the task complete. “This needs upstream work” is a
valid successful task outcome.

## Task execution

- Organize product code as ordinary C# modules with a thin native composition
  root. Avoid a single runtime-glue dumping ground.
- At meaningful milestones report: goal advanced, necessary surfaces, proof
  scaffolding, drift/unsupported boundary, and upstream requests.
- Product behavior is the deliverable. Tests, review paperwork, and conformance
  exercises are optional evidence and must not expand the task.
- Use only the narrow compile/path/readback checks named by the current task.
  Do not run old Rust, Angular, browser, packaging, conformance, or security
  suites unless explicitly requested.
- Preserve unrelated work and donor sources. Commit and push only the
  experimental Dagger branch.

## Documentation status

This branch intentionally has no durable architecture corpus beyond this short
guidance and the root README. If the C# spike is selected, a later focused task
will extract useful durable ideas from stable history and write new documents
from demonstrated behavior. Do not write aspirational architecture during the
port.
