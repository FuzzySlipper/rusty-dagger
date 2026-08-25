# Rusty Dagger agent guidance

Rusty Dagger imports Daggerfall/Arena2 content into Rusty Engine and owns the
Daggerfall-side Product Kernel, gameplay policy, rules, content meaning, and
read-only Studio adapter. It is an exploratory game project centered on
Privateer's Hold, not a general Daggerfall remake or a place for speculative
Engine APIs.

## Begin with Den

- Project ID: `rusty-dagger`.
- Resolve `get_agent_guidance` before substantial work. Follow the returned
  guidance and its referenced Den documents.
- If Den is unreachable, stop and report the failed tool or command. Do not
  reconstruct project decisions from old commits or local prose.
- Active work, acceptance, known gaps, and review evidence live in Den tasks.
  Board posts are historical records, not standing instructions.

Permanent project concepts:

- [Project charter](den://documents/rusty-dagger/project-charter)
- [Architecture and ownership](den://documents/rusty-dagger/architecture-and-ownership)
- [Gameplay authoring and runtime](den://documents/rusty-dagger/gameplay-authoring-and-runtime)
- [Content import and provenance](den://documents/rusty-dagger/content-import-and-provenance)
- [Verification and certification](den://documents/rusty-dagger/verification-and-certification)
- [Known limitations](den://documents/rusty-dagger/known-limitations)

These documents own durable intent; production code, schemas, and tests own
exact implemented behavior. Do not create another repository design note.

## Working boundaries

- Preserve unrelated changes in the shared worktree.
- Dagger Rust owns Daggerfall/gameplay meaning and product orchestration in
  `kernel/`; `kernel/dagger-runtime` and `kernel/dagger-rpg` are the only live
  semantic crates. TypeScript in `rules/` authors admitted composition only;
  framework-free TypeScript in `ui/` presents immutable projection envelopes
  and claims declared intents. Neither evaluates gameplay nor mounts an Engine
  renderer implementation.
- Consume only the adjacent public `rusty-engine` facade. Do not fetch, pin,
  reset, update, or enforce freshness for the sibling checkout.
- `rusty.toml` is the product composition root. The public `rusty` CLI owns
  admission, generated browser host, bounded browser evidence, and package
  closure. Engine owns the sole renderer/canvas; Dagger has no Angular
  workspace, HTTP product service, polling client, or second input/renderer
  loop.
- Desktop wrapper policy is manifest-declared and verified by the Product
  Model package flow; a native wrapper is not an ordinary feature seam.
- Unsupported Studio mutations fail closed. Ordinary content drift and quality
  heuristics warn unless a hard stop prevents concrete loss or boundary
  violation.

## Donor consultation

For new Daggerfall formats, formulas, gameplay, animation, orientation, AI, or
world assembly, consult the frozen Daggerfall Unity source before designing an
alternative. Use the `consult-donor-code` skill when available and follow
[Content import and provenance](den://documents/rusty-dagger/content-import-and-provenance).
Inspect exact source and meaningful callers/callees; classify substantial use
as adopted, adapted, rejected, or not found in the task/review evidence.

This is proportional. Engine-facade and build-plumbing work does not need donor
ceremony. If the donor index is unavailable, read
`/home/research/daggerfall-unity` directly. If the source is unavailable,
report the missing evidence rather than designing from memory.

## Verification

Run the narrowest check first, then the gate that owns the changed surface.
`scripts/verify.sh` retains offline import and Studio-adapter checks, then
uses the public `rusty` CLI for `check`, `build`, browser-owned `test`, and
`package`. Do not recreate an Angular build, a Dagger HTTP server, polling
browser scripts, or an alternate browser harness. Gameplay semantics belong in
focused Product Kernel tests; visible interaction and composition require the
Product Model browser evidence path when changed.

Report exactly what ran and which relevant live checks were skipped. Add or
retain proofs only while they serve a current product feature or risky
boundary; see
[Verification and certification](den://documents/rusty-dagger/verification-and-certification).
