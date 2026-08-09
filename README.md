# rusty-dagger

Daggerfall (Arena2) data-file import pipeline for rusty-engine, and a home for
extracted content. Currently extracts **Privateer's Hold** (the classic starting
dungeon) from the original game data into engine-consumable mesh assets.

**Intent and tracking**: durable design in [docs/design.md](docs/design.md)
(format reference: [docs/daggerfall-formats.md](docs/daggerfall-formats.md));
current task state in the Den `rusty-dagger` project.

## Layout

- `crates/arena2` — read-only parsers for classic Daggerfall data files, ported
  from Daggerfall Unity semantics (see [docs/daggerfall-formats.md](docs/daggerfall-formats.md)):
  - `bsa.rs` — BSA archives (MAPS/BLOCKS/ARCH3D)
  - `maps.rs` — MAPS.BSA location -> dungeon block layout resolution
  - `rdb.rs` — RDB dungeon block objects (models, positions, rotations)
  - `arch3d.rs` — ARCH3D.BSA mesh records (planes, points, UVs, texture ids)
  - `texture.rs` — TEXTURE.nnn archives (uncompressed + RecordRle/ImageRle, solid-color virtual archives)
  - `palette.rs` — PAL.PAL 256-colour palette
  - `pak.rs` — CLIMATE.PAK/POLITIC.PAK (world climate -> door texture base)
- `crates/dagger-import` — CLI that assembles a dungeon from the classic files:
  MAPS layout -> RDB objects -> ARCH3D meshes -> world-space triangles grouped
  by texture, emitted as:
  - `--format glb` (default): one GLB, one primitive per (archive,record) texture,
    embedded PNG textures (NEAREST, REPEAT), computed flat normals.
  - `--format mesh-json`: rusty-engine authored mesh source **with real UVs and
    per-material texture references** (upstream 6515 consumed: `uvs` stream +
    `materials[].texture` -> `texture/<slug>` catalog entries). `--texture-dir
    content/textures` publishes the decoded PNGs + a sha256 manifest that
    generate-project.py stamps into the catalog so the studio host can serve
    them as exact content-addressed render resources. `--untextured` keeps the
    legacy average-color fallback for A/B mood comparison.
- `crates/dagger-runtime` — Daggerfall-owned Rust runtime boundary. It admits
  the committed Privateer's Hold project, owns the first-person controller,
  applies admitted experiments, owns reset/readback and bounded calculation
  history, and provides the real-project collision walkthrough without
  importing the loading-bay game.
- `crates/dagger-rpg` — host-neutral Rust authority for the compact gameplay
  experiment document, validation, derived values, and designer-facing
  calculation records.
- `apps/dagger-lab` — Dagger-owned Angular authoring/readback surface. It is
  served by `dagger-native-host`, submits whole documents and side-effect-free
  formula worksheets to Rust, exposes live player state plus selectable recent
  calculations, and keeps a small browser-local shelf of named complete
  experiment profiles. Profile activation still goes through Rust admission;
  the app has no gameplay evaluator or Engine renderer dependency.
- `crates/dagger-studio-adapter` — Dagger-owned presentation boundary shared
  by the read-only protocol-14 Studio adapter and `dagger-native-host`. It
  strictly decodes Dagger projection into Engine's public retained-frame
  types; unsupported Studio mutations fail closed until Dagger owns them.
- `scripts/studio-host.mjs` / `scripts/serve-studio.sh` — bounded local HTTP
  bridge for the exact Engine Studio static app and the local adapter.
- `content/` — generated assets (privateers-hold.glb, privateers-hold.mesh.json,
  imported/ engine artifacts)
- `engine-render-check/` — migration pointer only. Dagger no longer owns
  renderer TypeScript, HTML, canvas bootstrap, or renderer package imports;
  Engine privately owns that boundary behind the Rust facade.

## Usage

```sh
cargo run -p dagger-import --bin dagger-import -- [--arena2 DIR] [--region N] [--location NAME] \
    [--format glb|mesh-json] [--texture-dir DIR] [--untextured] [--out FILE]
# defaults: --arena2 local/arena2 --region 17 \
#           --location "Privateer's Hold" --out content/privateers-hold.glb

# Regenerate everything (extract -> engine import -> studio project doc):
scripts/regenerate.sh

cargo test --workspace --locked
cargo run -p dagger-runtime --bin dagger-walkthrough
./scripts/verify-native-host.sh # real Engine host, X11 input, pick, resources, lifecycle
pnpm install
pnpm lab:build
cargo run -p dagger-studio-adapter --bin dagger-native-host
# Press L in the native product to open its connected Dagger Lab. Play with
# W/A/S/D, use G for patrol diagnostics, and N for navgrid diagnostics.
# The Lab is also directly reachable at http://127.0.0.1:4274 while this
# native session is running; closing its browser tab does not stop play.
./scripts/check-dagger-lab-browser.sh # profiles/preview/apply/explain/A-B play + responsive proof
./scripts/check-engine-freshness.py # fail loudly when Engine main has moved
python3 scripts/check-adapter.py   # local adapter; env override is diagnostic-only
# Human-visible Studio host (uses a local Rusty Engine Studio build; set
# RUSTY_ENGINE_STUDIO_STATIC_ROOT to override the conventional sibling path):
scripts/serve-studio.sh
# Focused HTTP/adapter check while the host is running:
node scripts/check-studio-host.mjs
# Real Chromium desktop+narrow render proof while the host is running:
scripts/check-studio-browser.sh
```

## Verification status (2026-08-08)

- Extraction: 5 RDB blocks (S0000999 start + 4 border), 365 model instances,
  17,651 verts / 8,683 tris, 81 unique textures — matches the checked importer
  proof-of-concept exactly (bounds X[-51.2,102.4] Y[0,51.1] Z[-102.4,51.2] m,
  glTF right-handed space).
- GLB: structurally validated (JSON/accessor/bufferView/PNG decoding) and
  rendered headless; the original three.js GLTFLoader harness
  (`render-check/`) has since been removed in favor of the real
  rusty-engine renderer proof below.
- Engine-native: `content/privateers-hold.mesh.json` is admitted by the
  engine's `rusty-asset-import` CLI with zero diagnostics as
  `mesh/privateers-hold`; `content/imported/` holds the published catalog
  (82 material + 81 texture entries) + static-mesh artifact (17,651 verts,
  26,049 indices, position/normal/uv layout, matching bounds). The shared
  adapter projects the textured frame (defineTexture + textured materials +
  textureResources manifest); host serves exact content-addressed PNG bytes
  with hash verification.
- Native render proof: `dagger-native-host` mounts Engine's private webview
  adapter through `rusty_engine`, submits the real retained Privateer's Hold
  frame and 121 exact texture resources, configures views/camera/resize,
  reads state and renders, routes physical input and picks into authoritative
  Dagger player state, proves a miss is a no-op, rejects corrupt resources
  transactionally, and disposes. Engine Studio retains its separate browser
  integration proof; neither path exposes renderer implementation packages to
  Dagger source.
- Native advanced diagnostics: Rust batches directional/environment animation,
  authoritative patrol transforms, and bounded retained overlays. `G` toggles
  authored/live sprite and heading facts; `N` toggles nearby committed navgrid
  cells. The X11 proof covers on/off replacement and disposal.
- Gameplay lab: the Angular surface edits the same schema-1 document as
  `data/experiments/privateers-hold-starter.json`. Rust atomically admits the
  complete candidate, installs movement speed plus player/Rat stats, calculates
  and explains fixed health, stamina, and magicka rules, retains the latest 16
  Apply calculations, resets the playable run to the committed start, and
  exposes live authoritative position/resource/controller readback. Rat
  gameplay values key to Arena2 mobile ID 0 without duplicating classic
  identity data in `dagger-rpg`. Invalid candidates leave the active experiment
  untouched. Its live content browser exposes the
  committed enemy catalog through Rust: decoded mobile ID/name/archive and
  authored spawn remain separate from live patrol position and active player
  experiment values. Jump-to-play names an admitted entity; `dagger-runtime`
  chooses a grounded navigable approach and focuses the native product rather
  than accepting browser-authored coordinates. The native window advertises
  and handles `L` through Engine physical-input readback to open the Lab for
  that session. Closing and reopening the companion tab reattaches to the same
  Rust runtime rather than creating a second gameplay authority.

## Data provenance & conventions

Geometry conventions (from Daggerfall Unity, MIT): GlobalScale 0.025 raw->m,
mesh coords 1/256 sub-units, UVs 1/16 texel sub-units, rotations 1/2048-turn
negated (T*Rz*Rx*Ry), RDB block side 2048 raw (51.2 m), Daggerfall Y-down ->
(x,-y,z). Handedness: DFU(Unity) is left-handed Y-up; glTF/rusty-engine is
right-handed Y-up, so Z is negated and fan winding reversed on export.
Textures: plane texture bitfield -> archive/record; the dungeon texture table
defaults to the classic per-location randomized table (DFRandom seeded by the
dungeon's LocationId; Privateer's Hold -> {23,22,19,22,20,368}), with
`--texture-table default` selecting the identity table
{119,120,122,123,124,168}; door archive 74 -> 74+climateBase
(Privateer's Hold is Woodlands climate -> Temperate -> TEXTURE.374);
TEXTURE.000/.001 are virtual solid-colour archives (32x32 palette fills).

## Next steps / known gaps

Current active feature work remains in Den: gameplay-laboratory program `6682`
with child campaigns `6719` (lab/construction-kit foundation), `6720`
(combat/encounters), and `6721` (growth/loot/inventory), plus water `6526` and
automap `6528`. Task `6707` changes only the Engine integration and repository
health posture; it does not absorb those owners. The numbered items below are
historical landmarks and design triggers, not an inferred active queue.

Tracked as Den tasks in the `rusty-dagger` project (dependencies wired):

- **6518** Studio project wiring: generate a studio-openable
  `content/projects/privateers-hold.project.json` from the imported artifacts
  (studio opens via `?root=<dir>&project=<file>`).
- **6519** Companion-reuse survey (FP controller, UI, content pipeline) →
  docs/companion-reuse.md.
- **6563** Self-contained downstream runtime: local controller/admission and
  real-project `dagger-walkthrough` now live here.
- **6564** Standalone Studio adapter/browser host: local Rust admission,
  protocol-14 readout, and HTTP bridge are now in this repository. The host
  serves the Engine Studio build selected by
  `RUSTY_ENGINE_STUDIO_STATIC_ROOT` (or the conventional sibling build path).
- **6521** Textured engine-native chain (landed): mesh-json carries real UVs
  and per-material texture references (upstream 6515); the studio adapter
  projects `defineTexture` ops + a protocol-14 `textureResources` manifest so
  studio renders the dungeon textured, matching the GLB. The average-color
  fallback remains behind `--untextured`.
- **6522** Consume upstream **rusty-engine 6516** (trimesh collision) to
  retire the gameplayProxy stopgap.
- **6523** Billboards (RDB flats), **6524** lights, **6525** action doors,
  **6526** water planes, **6528** automap, **6527** classic randomized
  per-location texture table (DFRandom port).
- **6529** Modularity gate: split crates per system as features land so systems
  port cleanly to the successor project.
