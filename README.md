# rusty-dagger

Rusty Dagger reads locally supplied Daggerfall/Arena2 data and builds a
playable, inspectable Privateer's Hold product on Rusty Engine. It owns the
offline import pipeline, Dagger gameplay/runtime authority, Angular product,
and Dagger-side Studio adapter.

Permanent project documentation lives in Den:

- [Project charter](den://documents/rusty-dagger/project-charter)
- [Architecture and ownership](den://documents/rusty-dagger/architecture-and-ownership)
- [Gameplay authoring and runtime](den://documents/rusty-dagger/gameplay-authoring-and-runtime)
- [Content import, formats, and provenance](den://documents/rusty-dagger/content-import-and-provenance)
- [Verification and certification](den://documents/rusty-dagger/verification-and-certification)
- [Known limitations](den://documents/rusty-dagger/known-limitations)

Current work and review state is in the Den `rusty-dagger` project. Historical
proposals and investigations are Board records, not current instructions.

## Repository map

- `crates/arena2` — read-only classic data parsers.
- `crates/dagger-import` — offline extraction and asset publication CLI.
- `crates/dagger-rpg` — Dagger gameplay package admission and evaluation.
- `crates/dagger-runtime` — live project, controller, collision, encounter, and
  gameplay-session authority.
- `crates/dagger-studio-adapter` — Dagger projection, Studio protocol adapter,
  and `dagger-product-server`.
- `gameplay/src` — TypeScript gameplay authoring; materializes into
  `data/gameplay`.
- `apps/dagger-product` — Angular product UI mounted through Engine's public
  application host.
- `content` — committed generated product assets and project documents.

The normal feature-development product is the web application-host path. Rust
owns gameplay/runtime meaning, Angular owns Dagger UI, and Engine owns the one
renderer/canvas. Tauri is reserved for eventual publication and release
certification, not ordinary feature work or default CI.

## Quick start

Provide an Arena2 directory at `local/arena2`, through `ARENA2_DIR`, or with the
importer's `--arena2` option. The adjacent `../rusty-engine` checkout supplies
the Rust facade, asset importer, and local TypeScript packages.

```bash
# Full source extraction and project regeneration
scripts/regenerate.sh

# Build authored gameplay and the Angular product
pnpm install
pnpm gameplay:build
pnpm product:build

# Run the connected product, then open http://127.0.0.1:4274
cargo run -p dagger-studio-adapter --bin dagger-product-server

# Deterministic aggregate repository checks
scripts/verify.sh

# Manual browser diagnostic for browser-visible product changes
scripts/check-dagger-product-browser.sh
```

## Developer command console

The connected application-host product includes the Engine-owned **Dagger
developer commands** pull-down. It uses the public Engine host envelope,
generated client, standard command schemas, and shell; the product service
only queues requests to its existing runtime safe point.

- `standard.inspect.entity` and `standard.inspect.mechanics` are read-only
  Engine standard inspections. The player entity is `1` in the committed
  Privateer's Hold session.
- `standard.admin.track.set` is the visibly privileged Engine track-owner
  adapter. It is distinct from normal combat and restoration.
- `dagger.scenario.prepare`, `.melee`, and `.advance` respectively set up a
  committed target, run production first-person melee, and advance bounded
  production ticks.
- `dagger.scenario.progression` is an admin-only demonstration: it resets and
  executes the committed Orc/Giant-Bat kill sequence through real melee,
  exposing the resulting XP, level transition, receipts, events, and
  projections in the returned Dagger readout.

The console is diagnostic tooling, not a player surface or a persistence and
replay authority.

Run the importer directly when working on extraction:

```bash
cargo run -p dagger-import --bin dagger-import -- \
  --arena2 local/arena2 --region 17 --location "Privateer's Hold" \
  --format glb --out content/privateers-hold.glb
```

For Engine-hosted Studio, confirm the sibling service at
`http://127.0.0.1:4310/`, then select this repository and
`content/projects/privateers-hold.project.json`. Dagger supplies
`.rusty-studio.json`, project data, and `dagger-studio-adapter`; Engine owns the
Studio service and browser product.

## Source and attribution

Daggerfall/Arena2 game data is copyrighted Bethesda material. Source game
files are operator-supplied and are not committed. Generated assets under
`content/` derive from that local installation; this repository does not grant
rights to redistribute the original game data.

Format and classic-semantic interpretation uses the MIT-licensed Daggerfall
Unity project as donor evidence. The frozen consulted source is
`/home/research/daggerfall-unity`, declared revision
`81e89e90c27bc3c1a7a61871e545fad129174dec`. Generated manifests retain source
file/record provenance while runtime code consumes semantic asset IDs. See
[Content import, formats, and provenance](den://documents/rusty-dagger/content-import-and-provenance)
for the consultation and conversion contract.

Angular production builds emit `dist/apps/dagger-product/3rdpartylicenses.txt`;
publication packaging must retain the applicable generated dependency notices.
