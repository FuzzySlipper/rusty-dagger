# rusty-dagger

Daggerfall (Arena2) data-file import pipeline for rusty-engine, and a home for
extracted content. Currently extracts **Privateer's Hold** (the classic starting
dungeon) from the original game data into engine-consumable mesh assets.

## Layout

- `crates/arena2` — read-only parsers for classic Daggerfall data files, ported
  from Daggerfall Unity semantics (see `/home/research/reports/daggerfall-mesh-research/REPORT.md`):
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

- Studio project wiring: studio opens external projects via
  `?root=<dir>&project=<file>` (see rusty-engine-demo
  apps/loading-bay-studio/src/studio-startup.ts), but a schema-24
  `*.project.json` needs catalog/hash machinery that is normally produced by
  the consumer's content pipeline (@rusty-engine-demo/project-content). The
  imported artifacts here (catalog + static-mesh) are the inputs that pipeline
  would consume.
- **Textured static meshes in engine**: render-model already has
  `MeshAttributeName::Uv` + `PackedStreamsLeV2` plumbing, but the authored
  `.mesh.json` source format (`asset-import/src/source.rs`, deny_unknown_fields)
  and the renderer-three static-mesh path carry no UVs. Upstream task filed:
  rusty-engine **task 6515** (UV through static mesh pipeline). The recent
  voxel-texture work is voxel-surface-only (voxel-convert/material.rs) — it did
  not add static-mesh UVs.
- **Collision**: rusty-engine static meshes resolve to VisualOnly/AabbFallback/
  Proxy (render-model `MeshCollisionPolicy`); svc-collision projects parry3d
  from voxel authority only. Daggerfall Unity collides dungeons with
  `MeshCollider` (sharedMesh = the render mesh; convex for props; box colliders
  on sliding doors) — i.e. straight triangle-mesh collision. Upstream task
  filed: rusty-engine **task 6516** (trimesh collision policy in svc-collision).
- Flats (billboards), lights, action-door animation, water, and the
  automap/start-marker metadata are parsed-skipped (models only).
- Texturing is classic-default; the per-location randomized texture table
  (DFRandom over MapId) is not replicated.
