# Rusty Dagger C# product guidance

## Current direction

Rusty Dagger is an ordinary evolving C# game built on Rusty Engine's active
NativeAOT downstream path. This is not a trial or disposable proof branch. New
product work belongs in the C# path and is fixed forward as the Engine SDK and
Dagger architecture mature.

The path is still fresh and raw. Public C# shapes, build ergonomics, product
organization, and available Engine service families may change. Do not mistake
that expected evolution for permission to build compatibility layers, preserve
walking-spike organization, or recreate missing Engine mechanisms downstream.

- Dagger checkout: `/home/dev/rusty-dagger`
- Paired Engine checkout: `/home/dev/rusty-engine`
- Dagger Den project: `rusty-dagger`
- Evolving cross-repo brief: `[doc: rusty-engine/downstream-csharp-agent-brief]`

Before substantial work, resolve the current Den task and project guidance,
then read the cross-repo brief when the task touches C# organization or the
Engine boundary. The current user request and owning task override the brief.
If Den is unreachable, stop and report the failed operation rather than
reconstructing current direction from old source or Git history.

## Active source shape

- `src/Dagger.Game/` is the safe ordinary C# application/game project. It owns
  product state, entities and domain records, catalogs, content interpretation,
  gameplay services, orchestration, and renderer-neutral presentation facts.
  It has unsafe code disabled.
- `src/Dagger.NativeProduct/` is the thin NativeAOT composition project. Its
  handwritten source selects the Dagger product type; Rusty Engine's source
  generator injects raw ABI layouts, service implementations, lifecycle
  adaptation, handle ownership, and native exports.
- `src/ui/` owns thin DOM UI TypeScript. It may present Engine-delivered UI
  projections and submit semantic actions. It must not own gameplay state or
  render game-world elements.
- `src/browser-bundle/` is generated/assembled host output, not a second product
  runtime or gameplay implementation lane.
- `src/scripts/` contains the current C# product build and launch path.
- `content/` contains product inputs.
- `gameplay/` is semantic donor material from the earlier TypeScript design.
  Consult it for formulas, catalogs, authored meaning, and behavior, but do not
  execute or extend its runtime/evaluator/package architecture.

The root Rust crates, Angular application, HTTP product server, Studio adapter,
Cargo workspace, package graph, and old scripts are inactive donor material.
Do not extend them, revive their authority posture, or run their gates as proof
for current C# work unless the current task explicitly asks for donor analysis.

## Product application shape

> The product decides. The Engine guarantees.

Dagger C# owns application and game logic, authoritative product state,
orchestration, content meaning, and product policy. Prefer ordinary C#:

- one explicit composition root;
- one clear persistent product-state owner;
- ordinary entities and domain records;
- named services with narrow responsibilities;
- constructor-supplied dependencies;
- direct use of safe named Engine service interfaces;
- explicit, realtime-neutral ordering inside each Engine-admitted update.

`Rusty.Engine.Application` is an optional paved road for update phases. Use it
only when it makes Dagger simpler. Dagger may customize phases or implement
`IEngineProduct.Update` directly. The helper is not application authority and
must not become a loop, clock, ECS, bus, service locator, delayed scheduler, or
mandatory module framework.

Do not create architecture merely to demonstrate a pattern. State helpers are
for genuine top-level modes; services and folders should follow real product
responsibilities. Current Den architecture tasks own the next organization
step, so do not treat today's flat file placement as frozen.

## Engine boundary

Engine Rust owns durable reusable infrastructure: host lifecycle and update
admission, input delivery, rendering/presentation mechanisms, resources,
spatial mechanisms, and other named service families as they are published.

- Do not write new downstream Rust or move product logic into Rust.
- Ordinary `Dagger.Game` code must not use `unsafe`, pointers, `Native*`,
  `GCHandle`, raw statuses, or handwritten native declarations.
- Generated sources stay under ignored `obj/` output and are never edited or
  committed.
- NativeAOT product code is trusted first-party code. Use direct typed calls;
  do not add JSON invocation, method-name dispatch, reflection registries,
  generic command buses, permission systems, compatibility negotiation, or
  adversarial boundary ceremony.
- Engine owns renderer resources, retained handles, frame construction,
  backend realization, and canvas lifecycle. C# publishes product facts through
  named Engine APIs. TypeScript remains DOM UI only.

## Missing capability stopping rule

Missing Engine capabilities are expected while this path evolves. If Dagger
cannot express required behavior through the safe generated API:

1. name the blocked product behavior;
2. identify the existing or missing Engine owner;
3. confirm the safe API does not already publish it;
4. file one narrow, purpose-neutral Engine request;
5. stop the downstream work at that boundary.

Do not fill the gap with downstream Rust, a C# reimplementation of Engine
machinery, browser rendering, fake proof behavior, a parallel host, or a
Dagger-shaped callback list. An upstream request and honest stop are valid task
outcomes.

## Work and evidence

- Preserve unrelated work and donor sources. Follow the current task's branch
  and promotion instructions.
- At meaningful milestones report the goal advanced, necessary surfaces,
  current product behavior, proof scaffolding, drift/unsupported boundaries,
  and upstream requests.
- Product behavior and maintainable code are the deliverable. Tests and review
  findings do not silently expand the task.
- Use only the focused generation, safe compilation, NativeAOT publish, or
  direct exercise that answers the current task.
- Do not run legacy Rust, Angular, broad browser, packaging, conformance,
  security, or smoke suites unless the current task explicitly requires them.
- If review feedback conflicts with the task or an owning Engine contract,
  record the disagreement and seek resolution instead of treating it as new
  marching orders.

## Documentation posture

Keep this file and the README factual and compact. Shared downstream C# guidance
lives at the stable Den handle
`rusty-engine/downstream-csharp-agent-brief` and is expected to evolve in place.
Do not recreate the deleted pre-pivot documentation corpus or write speculative
SDK promises ahead of demonstrated behavior.
