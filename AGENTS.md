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

## Architecture boundaries

- `crates/arena2` is read-only parsing of the classic data files. It must not
  acquire import policy, engine vocabulary, or write paths.
- `crates/dagger-import` owns offline extraction and emission (GLB, mesh-json,
  texture publication). Keep it an offline CLI; no runtime or browser seams.
- `crates/dagger-runtime` owns the Daggerfall-side runtime boundary (project
  admission, first-person controller, collision walkthrough).
- `crates/dagger-studio-adapter` owns the protocol-14 Studio adapter.
  Unsupported mutations fail closed until a Dagger authority exists; do not
  add speculative write paths.
- Do not copy Engine implementations into this repository; promote a smaller
  Engine seam upstream only when reuse is proven. The engine moves fast:
  consume rusty-engine `main` and fix forward when upstream drift breaks
  something — do not gate work behind exact-revision provenance rituals.
- `content/` is generated output. Regenerate it with `scripts/regenerate.sh`
  rather than hand-editing artifacts.

## Code style and language authority

> Rust owns all Daggerfall/gameplay logic. JS/TS owns rendering bootstraps,
> content configuration, and headless checks. JS observes and applies Rust
> authority results — it never becomes a second authority.

### Rust is the authority

All Daggerfall semantics live in Rust: format reading (`arena2`), extraction
and emission (`dagger-import`), runtime authority (`dagger-runtime`), and the
Studio adapter boundary (`dagger-studio-adapter`). This includes animation
timing, directional orientation math, nav grid derivation, collision, and
controller logic.

A Rust service or function that exists only in tests but is not called from
any production path is a defect. If the flycam, a headless check, or any
other consumer needs a result, it must consume the Rust authority — not
reimplement the math in JS.

### JS is a minimal bootstrap

The `engine-render-check/` JS files (flycam, headless checks, dump-frame)
are thin presentation bootstraps. Their job is:

1. Mount the rusty-engine renderer surface.
2. Wire input (pointer lock, keyboard, camera movement).
3. Poll Rust authorities (the sprite-frames server, the adapter dump).
4. Apply authority results to the renderer surface (`updateSprite`, camera
   pose, gizmo ops).

They must not recompute frame indices, animation timing, directional
orientation, or any gameplay semantics. If a JS page grows beyond mounting,
input, and result application, the new logic belongs in Rust — add a Rust
bin or extend the runtime, then consume it from JS.

### Flycam and debugging are first-class

`engine-render-check/` is durable diagnostic infrastructure, not a throwaway
test page. It survives content migration (Daggerfall content → original
content): flycam, sprite diagnostics, nav grid overlays, and collision
probes are useful in their own right regardless of the content source.
Invest in keeping them clean and minimal. A flycam page that accretes
gameplay logic is a maintenance liability; a flycam page that stays a thin
bootstrap over Rust authorities is a durable tool.

### Content and config stay in TS/JSON

Project documents (`content/projects/*.project.json`), texture manifests, and
`scripts/generate-project.py` are content configuration — they describe what
goes into the scene, not how it behaves. Behavioral authority (timing,
movement, animation) stays in Rust.

## Work and verification

Treat a dirty worktree as shared state. Preserve unrelated changes.

Run the narrowest check first, then the gate that owns the changed surface:

```bash
cargo test                        # arena2 parser tests against the real data files
scripts/regenerate.sh             # extraction -> engine import -> studio project doc
cargo run -p dagger-runtime --bin dagger-walkthrough
cargo run -p dagger-runtime --bin dagger-navgrid -- --check  # nav grid proof + artifact freshness
node engine-render-check/check.mjs  # render proof via the real rusty-engine renderer
python3 scripts/check-adapter.py  # local adapter; env override is diagnostic-only
```

Extraction claims require a real render proof, not only structural validation:
headless Chromium screenshots through the actual rusty-engine renderer
(renderer-three browser surface, consumed from rusty-engine `main`) via
`engine-render-check/check.mjs`, with assertions on triangle/draw-call counts,
texture-resource count, pixel coverage, and zero console errors (one-time
setup: `pnpm install` inside `engine-render-check/`). This is the only render
verification path — the ad-hoc three.js harnesses were removed; when the
engine renderer lacks a capability, file an upstream rusty-engine task rather
than building a side renderer. Studio-visible changes require the host gates
while `scripts/serve-studio.sh` is running:

```bash
node scripts/check-studio-host.mjs     # focused HTTP/adapter check
scripts/check-studio-browser.sh        # real Chromium desktop+narrow render proof
```

Report exactly which commands ran and which relevant live checks were skipped.
