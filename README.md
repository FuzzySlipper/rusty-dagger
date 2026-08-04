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
  and provides the real-project collision walkthrough without importing the
  loading-bay game.
- `crates/dagger-studio-adapter` — Rust-owned protocol-14 Studio adapter. It
  admits the committed project through `dagger-runtime` and publishes its
  static mesh/lights as an Engine render frame; unsupported mutations fail
  closed until a Dagger authority exists.
- `scripts/studio-host.mjs` / `scripts/serve-studio.sh` — bounded local HTTP
  bridge for the exact Engine Studio static app and the local adapter.
- `content/` — generated assets (privateers-hold.glb, privateers-hold.mesh.json,
  imported/ engine artifacts)
- `engine-render-check/` — headless render proof through the real rusty-engine
  renderer (renderer-three browser surface, consumed from rusty-engine
  `main`); the primary render verification gate
- `render-check/` — legacy debug view (ad-hoc three.js GLTFLoader + playwright);
  kept for reference, no further investment

## Usage

```sh
cargo run -p dagger-import -- [--arena2 DIR] [--region N] [--location NAME] \
    [--format glb|mesh-json] [--texture-dir DIR] [--untextured] [--out FILE]
# defaults: --arena2 local/arena2 --region 17 \
#           --location "Privateer's Hold" --out content/privateers-hold.glb

# Regenerate everything (extract -> engine import -> studio project doc):
scripts/regenerate.sh

cargo test                      # arena2 parser tests against the real data files
cargo run -p dagger-runtime --bin dagger-walkthrough
# Render proof through the real rusty-engine renderer (one-time: pnpm install
# inside engine-render-check/):
node engine-render-check/check.mjs
# Legacy ad-hoc three.js debug view:
node render-check/check.mjs [--cam overview|top|interior] [--out shot.png]
python3 scripts/check-adapter.py   # local adapter; env override is diagnostic-only
# Human-visible Studio host (uses a local Rusty Engine Studio build; set
# RUSTY_ENGINE_STUDIO_STATIC_ROOT to override the conventional sibling path):
scripts/serve-studio.sh
# Focused HTTP/adapter check while the host is running:
node scripts/check-studio-host.mjs
# Real Chromium desktop+narrow render proof while the host is running:
scripts/check-studio-browser.sh
```

## Verification status (2026-08-01)

- Extraction: 5 RDB blocks (S0000999 start + 4 border), 365 model instances,
  18,811 verts / 9,263 tris, 81 unique textures — matches the validated Python
  proof-of-concept exactly (bounds X[-51.2,102.4] Y[0,51.1] Z[-102.4,51.2] m,
  glTF right-handed space).
- GLB: structurally validated (JSON/accessor/bufferView/PNG decoding) and
  **rendered headless through three.js GLTFLoader** (the same renderer
  rusty-engine studio uses) with playwright+swiftshader; assertions on mesh
  count, triangle count, textured materials, bounds, and pixel coverage pass
  for overview/top/interior cameras. Screenshots in render-check/*.png.
- Engine-native: `content/privateers-hold.mesh.json` is admitted by the
  engine's `rusty-asset-import` CLI with zero diagnostics as
  `mesh/privateers-hold`; `content/imported/` holds the published catalog
  (82 material + 81 texture entries) + static-mesh artifact (18,811 verts,
  27,789 indices, position/normal/uv layout, matching bounds). The studio
  adapter projects the textured frame (defineTexture + textured materials +
  textureResources manifest); host serves exact content-addressed PNG bytes
  with hash verification.
- Render proof (2026-08-04): `engine-render-check/` renders the dungeon
  through the real rusty-engine renderer (renderer-three browser surface,
  consumed from rusty-engine `main`) in headless Chromium, asserting
  triangle/draw-call counts, texture-resource count, pixel gates, and zero
  console errors across overview + interior poses. This is the render
  verification gate going forward; the three.js `render-check/` remains as a
  legacy debug view.

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
