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
  - `--format mesh-json`: rusty-engine authored mesh source (untextured; one
    material per texture with its average colour — the engine format has no UVs).
- `content/` — generated assets (privateers-hold.glb, privateers-hold.mesh.json,
  imported/ engine artifacts)
- `render-check/` — headless render verification (three.js GLTFLoader + playwright,
  reusing the rusty-engine installed packages; writes screenshots)

## Usage

```sh
cargo run -p dagger-import -- [--arena2 DIR] [--region N] [--location NAME] \
    [--format glb|mesh-json] [--untextured] [--out FILE]
# defaults: --arena2 /home/research/daggerfall-files --region 17 \
#           --location "Privateer's Hold" --out content/privateers-hold.glb

cargo test                      # arena2 parser tests against the real data files
node render-check/check.mjs [--cam overview|top|interior] [--out shot.png]
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
  `mesh/privateers-hold`; `content/imported/` holds the published
  catalog (82 material entries) + static-mesh artifact (18,811 verts,
  27,789 indices, matching bounds).

## Data provenance & conventions

Geometry conventions (from Daggerfall Unity, MIT): GlobalScale 0.025 raw->m,
mesh coords 1/256 sub-units, UVs 1/16 texel sub-units, rotations 1/2048-turn
negated (T*Rz*Rx*Ry), RDB block side 2048 raw (51.2 m), Daggerfall Y-down ->
(x,-y,z). Handedness: DFU(Unity) is left-handed Y-up; glTF/rusty-engine is
right-handed Y-up, so Z is negated and fan winding reversed on export.
Textures: plane texture bitfield -> archive/record; dungeon table default
{119,120,122,123,124,168} (identity); door archive 74 -> 74+climateBase
(Privateer's Hold is Woodlands climate -> Temperate -> TEXTURE.374);
TEXTURE.000/.001 are virtual solid-colour archives (32x32 palette fills).

## Next steps / known gaps

Tracked as Den tasks in the `rusty-dagger` project (dependencies wired):

- **6518** Studio project wiring: generate a studio-openable
  `content/projects/privateers-hold.project.json` from the imported artifacts
  (studio opens via `?root=<dir>&project=<file>`).
- **6519** Companion-reuse survey (FP controller, UI, content pipeline) →
  docs/companion-reuse.md.
- **6520** First-person walk-through: spawn at start marker, traverse the hold
  (depends on 6518 + 6519).
- **6521 / 6522** Consume upstream **rusty-engine 6515** (static-mesh UVs) and
  **6516** (trimesh collision) when they land.
- **6523** Billboards (RDB flats), **6524** lights, **6525** action doors,
  **6526** water planes, **6528** automap, **6527** classic randomized
  per-location texture table (DFRandom port).
- **6529** Modularity gate: split crates per system as features land so systems
  port cleanly to the successor project.
