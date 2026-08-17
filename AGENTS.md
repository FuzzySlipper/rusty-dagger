# Rusty Dagger agent guidance

## Repository role

Rusty Dagger is the Daggerfall (Arena2) data-file import pipeline for Rusty
Engine, and a home for extracted content. It currently extracts Privateer's
Hold from the original game data into engine-consumable mesh assets, and owns
the Daggerfall-side runtime boundary and Studio adapter for the committed
project.

It is not a general Daggerfall remake and not the place to generalize
speculative Engine APIs. Rusty Engine owns reusable host-neutral mechanisms;
this repository owns Daggerfall format knowledge, extraction, and the
Daggerfall-owned runtime/adapter surfaces.

## Den Guidance Bootstrap

- Project ID: `rusty-dagger`
- Resolve live guidance with the Den MCP `get_agent_guidance` tool before
  substantial work.
- Treat the resolved Den guidance packet and its referenced Den documents as
  the source of truth.
- If Den is unreachable, stop and tell the user which Den tool or command
  failed and what you were about to do. Do not reconstruct Den state from
  local files.

## Source-of-truth posture

- [docs/design.md](docs/design.md) owns durable design intent;
  [docs/daggerfall-formats.md](docs/daggerfall-formats.md) owns the format
  reference. Keep them current when behavior or ownership changes.
- Current task state lives in the Den `rusty-dagger` project; next steps and
  known gaps are tracked as Den tasks, not in ad hoc local files.
- [docs/source-provenance.md](docs/source-provenance.md) owns donor and asset
  provenance. Update it when donor semantics or dependencies change.
- Daggerfall Unity (MIT) semantics are donor evidence for the parsers; the
  geometry/texture conventions in the README are authoritative for the
  extraction math. Verify against the real data files, not against memory of
  the donor.

## Daggerfall Unity donor consultation

Treat Daggerfall Unity consultation as an early design step whenever work
touches Daggerfall formats, formulas, gameplay, animation, orientation, AI,
world assembly, or other classic semantics. Do this before proposing an
original model, not only after the local approach runs into trouble.

- Use the `consult-donor-code` skill when available.
- The frozen donor source is `/home/research/daggerfall-unity`; its Codebase
  Memory project is `daggerfall-unity`. The declared donor revision is recorded
  in [docs/source-provenance.md](docs/source-provenance.md). The checkout has no
  `.git` metadata, so do not infer revision identity from the index.
- Start from [docs/donor-code-map.md](docs/donor-code-map.md), then query the
  indexed project explicitly. Search using DFU names as well as Rusty terms.
- Inspect exact source plus meaningful callers/callees. A single search hit or
  graph summary is not enough to establish the donor model.
- Classify substantial use as `adopted`, `adapted`, `rejected`, or `not found`,
  and record donor files/symbols plus deliberate deviations in the Den handoff
  or review packet.
- Preserve semantics where they are sound, but adapt them to this repository's
  Rust authority and Engine boundary. Do not copy Unity ownership, runtime
  topology, or incidental implementation constraints.
- If Codebase Memory is unavailable, consult the frozen source directly with
  `rg` and file reads. If the donor source itself is unavailable, report the
  missing evidence instead of designing from memory.

This requirement is proportional: unrelated Engine-facade or build-plumbing
work does not need donor ceremony. New Daggerfall behavior does.

## Architecture boundaries

- `crates/arena2` is read-only parsing of the classic data files. It must not
  acquire import policy, engine vocabulary, or write paths.
- `crates/dagger-import` owns offline extraction and emission (GLB, mesh-json,
  texture publication). Keep it an offline CLI; no runtime or browser seams.
- `crates/dagger-runtime` owns the Daggerfall-side runtime boundary (project
  admission, first-person controller, collision walkthrough).
- `gameplay/` is the normal home for Dagger gameplay authoring: a standalone
  TypeScript workspace (`authoring/` grammar, `catalogs/` content,
  `packages/` envelopes) that materializes the deterministic package in
  `data/gameplay/`. TypeScript authors but never evaluates; `crates/dagger-rpg`
  admits the package, owns its meaning, and is the only evaluator. See
  `gameplay/README.md` and `docs/gameplay-resolution.md`.
- `crates/dagger-studio-adapter` owns the protocol-14 Studio adapter.
  Unsupported mutations fail closed until a Dagger authority exists; do not
  add speculative write paths.
- Do not copy Engine implementations into this repository. When Dagger work
  exposes a seam that looks upstream-shaped, promote it through the two-beat
  process in "Upstream promotion (two-beat)" below, not as a deferred
  follow-up. Consume the adjacent
  `../rusty-engine` checkout through the unconditional facade as it stands and
  fix forward when upstream drift breaks something. Downstream does not fetch,
  mutate, pin, or enforce freshness for that checkout; operator update policy
  belongs outside this repository.
- `content/` is generated output that doubles as a living content tree.
  Classic regeneration (`scripts/regenerate.sh`) is the default source and
  overwrites generated files, but hand edits are legitimate and expected:
  sprite pivots, sizes, fps/loop, and playback sequences are editable in the
  Dagger Lab sprite tab (writes go through the lab bridge, which pretty-writes
  the manifest and restamps project docs), and an entry carrying
  `"edited": true` keeps those tunable fields across regeneration.
  `DAGGER_CLOBBER_SPRITES=1 scripts/regenerate.sh` rewrites everything from
  classic defaults; clearing a marker in the UI restores classic values on
  the next regeneration. Derived pixel layout (frame UVs) always follows the
  fresh pack. Hash drift between manifests and on-disk bytes is surfaced as a
  loud warning — generation stamps the actual bytes' identity and runtime
  serving warns and publishes actual content — never a silent drop or a hard
  stop.

## Upstream promotion (two-beat)

"Promote a seam upstream when reuse is proven" has repeatedly failed because
promotion is not an observable outcome of a downstream task and never gets a
forcing function. Replace it with a two-beat effort whenever Dagger work exposes
something that looks like it belongs in rusty-engine:

- **Beat 1 — co-develop.** One effort owns both the candidate rusty-engine seam
  (crate or API) and its first Dagger consumer. Write the upstream/downstream
  line before work starts — what is generic mechanism vs. what is Dagger meaning
  — and hold it while iterating. Moving the line is a recorded decision with a
  reason, never a slow drift.
- **Beat 2 — de-overfit.** Immediately port the candidate to a second,
  mechanically different consumer: not a second game of the same shape, but a
  different resolution shape (for a combat/RPG-flavored seam, the doom demo in
  `../rusty-engine-demo` is the right kind of test). Write an overfitting report
  listing what moved across the line because the second consumer needed it. The
  report is the completion evidence; a second consumer that forces changes is
  the success case, not a failure.

Do not promote the candidate to the stable `../rusty-engine` checkout until
beat 2's report exists. The two beats are one unit of work, not two backlogs.

## Code style and language authority

> Dagger Rust owns Daggerfall/gameplay logic, presentation meaning, and product
> orchestration. Engine owns the sensitive Rust-to-webview renderer boundary
> behind its public Rust facade. Downstream JS/TS never imports or mounts the
> renderer implementation.

### Rust is the authority

All Daggerfall semantics live in Rust: format reading (`arena2`), extraction
and emission (`dagger-import`), runtime authority (`dagger-runtime`), and the
Studio adapter boundary (`dagger-studio-adapter`). This includes animation
timing, directional orientation math, nav grid derivation, collision, and
controller logic.

A Rust service or function that exists only in tests but is not called from
any production path is a defect. If the native diagnostic, a headless check,
or another consumer needs a result, it must consume the Rust authority.

### Renderer implementation stays upstream

Downstream Rust depends unconditionally on the `rusty-engine` facade and uses
namespaced imports such as `rusty_engine::engine_spatial`. The runnable product
diagnostic is `dagger-native-host`; it submits Dagger-owned retained facts to
`rusty_engine::renderer_webview_host`. It must not expose the private webview,
TypeScript, Three, HTML, canvas, or object-URL implementation to Dagger code.

JS/MJS remains acceptable for the bounded Engine Studio HTTP bridge, browser
integration checks, and other test/build plumbing. It must not import
`@rusty-engine/render-*` or `@rusty-engine/renderer-*` packages, own gameplay
or presentation state, or become a second application bootstrap.

### Native diagnostics are first-class

`dagger-native-host` is durable diagnostic infrastructure, not a synthetic
renderer smoke. It must use committed project resources, real Dagger runtime
authority, physical input/readback, meaningful pick routes, and explicit
mount/failure/disposal proof. `engine-render-check/` is only a migration
pointer and must not acquire application code again.

### Content and config stay in TS/JSON

Project documents (`content/projects/*.project.json`), texture manifests, and
`scripts/generate-project.py` are content configuration — they describe what
goes into the scene, not how it behaves. Behavioral authority (timing,
movement, animation) stays in Rust.

## Work and verification

Treat a dirty worktree as shared state. Preserve unrelated changes.

### Proportionality

Fail hard only where a wrong result would lose work, corrupt shared state, or
violate a boundary: path containment, unsupported Studio protocol mutations,
the Engine boundary audit, and explicit CI check modes keep their hard stops.
Everything else — content drift, freshness mismatches, quality heuristics —
surfaces as a loud warning with operator choice, not a hard stop. When adding
a new hard failure, state in one sentence what loss it prevents; if you
can't, make it a warning.

Run the narrowest check first, then the gate that owns the changed surface.
The automatic gate (`scripts/verify.sh`, run by CI) is deliberately slim,
deterministic, and Playwright-free:

```bash
cargo test                        # arena2 parser tests against the real data files
scripts/regenerate.sh             # extraction -> engine import -> studio project doc
cargo run -p dagger-runtime --bin dagger-walkthrough
cargo run -p dagger-runtime --bin dagger-navgrid -- --check  # nav grid proof + artifact freshness
cargo run -p dagger-runtime --bin dagger-gameplay-check  # authored package resolution proof
pnpm gameplay:check               # gameplay package build + drift check
scripts/verify-native-host.sh     # Engine facade/native renderer/input/pick/lifecycle proof
python3 scripts/check-adapter.py  # local adapter; env override is diagnostic-only
```

Extraction claims require a real native render proof, not only structural
validation. The proof must reach Engine presentation only through the public
Rust facade and certify exact checked resources plus authoritative Dagger
effects. Studio-visible changes additionally require the host gates
while the Engine-owned Studio host is running:

```bash
python3 scripts/check-adapter.py       # focused adapter protocol check
```

`scripts/check-dagger-lab-browser.sh` is a manual opt-in Playwright
diagnostic, not an automatic gate: heavyweight browser choreography proved
too slow and brittle for CI. Run it by hand when a change touches the
browser product surface (renderer mounting, input arbitration, Lab UI), and
say so in the task packet. Gameplay semantics are proven by deterministic
Rust tests, not by browser choreography.

Report exactly which commands ran and which relevant live checks were skipped.
