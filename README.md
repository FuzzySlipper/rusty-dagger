# Rusty Dagger

Rusty Dagger is a playable, inspectable Privateer's Hold experiment built from
operator-supplied Daggerfall/Arena2 data. Dagger owns its semantic Rust Product
Kernel, authored rules, content meaning, offline import pipeline, and
read-only Studio adapter. Rusty Engine owns generated application hosting, the
sole renderer/canvas, normalized physical input, and Product Model assembly.

The product lives in the standard Product Layout:

- `rusty.toml` — product identity, lifecycle, UI projection contract, content
  root, and optional desktop packaging policy.
- `kernel/` — the Dagger-owned Product Kernel and its nested semantic crates.
- `rules/` — admitted Runtime Composition declarations; never a live gameplay
  evaluator.
- `ui/` — framework-free rich DOM presentation, subscribed only to immutable
  Rust projection envelopes and claiming declared intents.
- `content/` — declared, content-addressed material required by the product.
- `authoring-content/` — importer, Studio, validation, and other offline
  artifacts; it is intentionally outside the admitted runtime closure.

The retained offline and authoring tools are intentionally separate from the
running product:

- `crates/arena2` reads classic binary source formats.
- `crates/dagger-import` extracts and publishes derived content.
- `crates/dagger-studio-adapter` provides Dagger's read-only Studio protocol
  adapter and its stdio authoring binary.

## Product workflow

The public `rusty` CLI is the only product build, host, package, and browser
workflow. It must be available from the adjacent Rusty Engine checkout or
installed on `PATH`.

```bash
rusty check --path .
rusty build --path .
rusty test --path .
rusty package --path . --wrapper desktop
```

`rusty test` is the browser-owned evidence path. `rusty package` verifies the
desktop wrapper policy and exact package closure; it does not itself certify a
headed Tauri installation.

For the full local gate, run:

```bash
scripts/verify.sh
```

## Offline content work

Provide Arena2 through `local/arena2`, `ARENA2_DIR`, or an explicit importer
argument. Classic source files are not committed. Regenerate Dagger-derived
content with:

```bash
scripts/regenerate.sh
```

## Studio

Engine-hosted Studio invokes the retained `dagger-studio-adapter` stdio binary
for Dagger-specific, read-only project admission and projection. It is not the
playable product host and does not start an HTTP server.

## Source and attribution

Daggerfall/Arena2 data is copyrighted Bethesda material and is supplied by the
operator. Generated content carries the relationship to those local bytes;
this repository does not grant redistribution rights to the original game
assets.

Daggerfall Unity is MIT-licensed donor evidence for format and classic
semantic interpretation. The frozen consulted source is
`/home/research/daggerfall-unity`, declared revision
`81e89e90c27bc3c1a7a61871e545fad129174dec`. See the Den project documents for
current product and provenance policy.
