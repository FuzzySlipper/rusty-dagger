# Loading Daggerfall's Privateer's Hold into rusty-engine as an untextured mesh

Research date: 2026-08-01. Sources: `/home/research/daggerfall-unity` (DFU 0.16 source),
`/home/research/daggerfall-files` (original Arena2 data), `/home/dev/rusty-engine`.

**Status: end-to-end pipeline validated with a working proof-of-concept extractor**
(`extract_dungeon.py` in this directory → `privateers_hold.obj`: 18,811 verts / 9,263 tris,
bounds exactly 3×3 dungeon blocks = X/Z [-51.2, +102.4] m, Y [0, 51.1] m, 365 model instances, 0 missing).

---

## 1. How Daggerfall Unity loads dungeon levels from the original files

Three original data files are involved (all present in `/home/research/daggerfall-files`):

| File | Contents |
|---|---|
| `MAPS.BSA` | Per-region records `MAPNAMES.nnn`, `MAPTABLE.nnn`, `MAPPITEM.nnn`, `MAPDITEM.nnn` — which dungeon blocks make up each location's dungeon |
| `BLOCKS.BSA` | 1,295 named records: `*.RDB` dungeon blocks, `*.RMB` exterior blocks, `*.RDI` interiors |
| `ARCH3D.BSA` | 10,251 numeric records: raw 3D meshes, record id = model id (e.g. `61000`) |

DFU source files that own each step (all under `Assets/Scripts`):
- `API/BsaFile.cs` — archive reader
- `API/MapsFile.cs` — location → dungeon block list
- `API/BlocksFile.cs` + `API/DFBlock.cs` — `.RDB` block parser
- `API/Arch3dFile.cs` + `API/DFMesh.cs` + `MeshReader.cs` — mesh decode + triangulation
- `Utility/RDBLayout.cs` + `Internal/DaggerfallDungeon.cs` — block/model instancing & assembly

### 1.1 BSA archive format (BsaFile.cs, `ReadHeader`/`ReadDirectory`)

- Header: `i16 DirectoryCount`, `u16 DirectoryType` (`0x0100` = named records, `0x0200` = numeric-id records). Record data starts at offset 4.
- Directory lives at **end of file**. Named: `count × 18 bytes` (14-byte cstring name + i32 size). Numeric: `count × 8 bytes` (u32 id + i32 size).
- Records are stored contiguously from offset 4 in directory order.

### 1.2 Location → dungeon block list (MapsFile.cs)

For region index N (Daggerfall region = **17**), using records `.0NN`:

1. `MAPNAMES` — `u32 locationCount`, then `count × 32-byte` name strides. Privateer's Hold = index **179**.
2. `MAPTABLE` — `count × 17-byte` entries: `i32 MapId, u32 bitfield(lon), i32 lat, u8 dungeonType, u32 key`.
   Privateer's Hold: **MapId 187853213** (matches DFU's `IsMainStoryDungeon` special case in `DaggerfallDungeon.cs:151`).
3. `MAPPITEM` — `count × u32` offset table; record at `count*4 + offset`. After skipping doors
   (`u32 doorCount` + 6 bytes each), the `LocationRecordElementHeader` has `LocationId` (u16) at **+33**.
   Privateer's Hold exterior `LocationId = 50049`.
4. `MAPDITEM` — `u32 dungeonCount`, then `count × 8-byte` entries `{u32 offset, u16 isDungeon, u16 exteriorLocationId}`.
   Match on `exteriorLocationId`; dungeon record at `4 + count*8 + offset`. After its own
   `LocationRecordElement` (doors + 112-byte header) comes the `DungeonHeader`
   (`u16, u32, u32, u16 blockCount, 5 bytes` = 17 bytes), then `blockCount × 4-byte` block entries:
   `i8 X, i8 Z, u16 bitfield` where `blockNumber = bitfield & 0x3FF`, `isStart = bitfield & 0x400`,
   `blockIndex = bitfield >> 11` → letter from `{"N","W","L","S","B","M"}`; name = `{letter}{blockNumber:0000000}.RDB`.

**Privateer's Hold dungeon layout (validated against the binary data):**

| Block | Grid X | Grid Z | Start |
|---|---|---|---|
| `S0000999.RDB` | 0 | 0 | ✔ |
| `B0000009.RDB` | -1 | 0 | |
| `B0000006.RDB` | 0 | -1 | |
| `B0000003.RDB` | +1 | 0 | |
| `B0000012.RDB` | 0 | +1 | |

(One start block + 4 border blocks — the classic smallest dungeon cross. None of DFU's `FixRdbData`
block patches touch these five blocks, so no fixups are needed.)

### 1.3 RDB dungeon block format (BlocksFile.cs `ReadRdb*`, DFBlock.cs)

```
Header (20 bytes):        u32 unk, u32 width, u32 height, u32 objectRootOffset, u32 unk   (width=height=8 for S0000999)
ModelReferenceList:       750 × (5-byte cstring ModelId + 3-byte cstring Description)      ("61203","C00" etc.)
ModelDataList:            750 × u32 (unknown, skippable)
ObjectSectionHeader:      512 bytes (u32 unknownOffset, ..., "DAGR" magic at +56)          (skippable for geometry)
UnknownLinkedList:        at unknownOffset (skippable)
ObjectRootList:           width*height × i32 offsets at objectRootOffset (-1 = empty cell)
Object linked list node:  i32 next, i32 prev, i32 XPos, i32 YPos, i32 ZPos, u8 type, u32 resourceOffset
                          type: 0x01 = Model, 0x02 = Light, 0x03 = Flat   (walk until next < 0)
Model resource (@resourceOffset): i32 XRot, i32 YRot, i32 ZRot, u16 modelIndex,
                          u32 triggerFlag, u8 soundIndex, i32 actionOffset (+ action records)
```

For untextured geometry only **type-0x01 Model objects** are needed; flats (billboards), lights,
action records, doors logic and the unknown linked list can all be skipped. (Optional later: action
doors are ordinary model objects flagged via `IsActionDoor`; including them as static geometry is fine for a first test.)

**Flat records consumed by the pipeline** (type 0x03, texture bitfield `archive = >>7`,
`record = &0x7F`): visible billboard flats become sprite entities; editor-archive (199)
records 15/16 are enemy spawn markers (routed to enemy sprites); editor-archive record 19 is
the **random-treasure marker** (DFU `RDBLayout.cs` `AddRandomTreasure`) — since task 7073's
second slice these are consumed as lootable treasure-pile containers: `dagger-import` routes
them to the scene sidecar's `treasure` list with the dungeon's loot key (the MAPS.BSA dungeon
type byte indexed through the donor's `LootTables.cs` `GenerateLoot` dungeon-type array —
Privateer's Hold is type 2, Human Stronghold, mapping to key `N`), and `generate-project.py`
emits one visible `treasure-<id>` sprite entity (id band 3000+, TEXTURE.216[0] icon) per
marker. The other editor records dropped alongside in S0000999.RDB (199/11 quest item,
199/18 quest marker) stay hidden: quest/item markers are out of scope.

### 1.4 ARCH3D mesh format (Arch3dFile.cs, MeshReader.cs, DFMesh.cs)

```
Header: 4-byte version cstring ("v2.7"), i32 pointCount, i32 planeCount, u32 radius, u64 null,
        i32 planeDataOffset, i32 objectDataOffset, i32 objectDataCount, u32 unk, u64 null,
        i32 pointListOffset (@48), i32 normalListOffset (@52), u32 unk, i32 planeListOffset (@60)
Per plane at planeListOffset:  u8 pointCount, u8 unk, u16 textureBitfield, u32 unk        (8-byte header)
Per point:                     i32 pointOffset, i16 u, i16 v                              (8 bytes)
Point coordinates:             3 × i32 at pointListOffset + pointOffset (v2.6/v2.7)
                               (for v2.5: pointListOffset + pointOffset*3)
Plane normal:                  3 × i32 per plane at normalListOffset (sequential)
```

- **Mesh coordinates are 1/256 fixed-point sub-units** — `Arch3dFile.pointDivisor = 256.0`
  (this is the easy-to-miss step: raw ÷ 256 → same units as RDB positions).
- Each plane is a convex polygon triangulated as a **fan from point 0** with DFU winding `(0, i+2, i+1)`.
- Texture bitfield (`archive = >>7`, `record = &0x7F`) only matters for texturing; ignorable here.

### 1.5 World transform chain (MeshReader.cs, RDBLayout.cs `GetModelMatrix`, DaggerfallDungeon.cs)

Constants:
- `MeshReader.GlobalScale = 0.025` (raw units → meters)
- `BlocksFile.RDBDimension = 2048` raw units per block side → `RDBLayout.RDBSide = 51.2` m
- `BlocksFile.RotationDivisor = 5.688888…` (2048 units = 360°, i.e. rotations are 1/2048-turn)

Per vertex (Daggerfall is Y-down, DFU targets Unity left-handed Y-up):
```
v = (x/256, -y/256, z/256) * 0.025
```
Per model object: `position = (XPos, -YPos, ZPos) * 0.025`,
`degrees = -raw / 5.688888…`, matrix = **T · Rz · Rx · Ry** (applied in that multiplication order).
Per block: `origin = (gridX * 51.2, 0, gridZ * 51.2)`.

### 1.6 First-person weapon CIF records (CifRciFile.cs)

Classic `WEAPON*.CIF` files have no master header. For the non-bow weapons,
record 0 is one ordinary IMG record:

```text
i16 xOffset, i16 yOffset, i16 width, i16 height,
u16 compression (0=raw, 2=RLE), u16 pixelDataLength, pixel bytes
```

The remaining records are weapon animations. Each starts with:

```text
u16 width, u16 height, u16 lastFrameWidth,
i16 xOffset, i16 lastFrameYOffset, i16 dataLength,
u16 frameOffsets[31], u16 totalSize
```

Every non-zero frame offset is relative to the animation record start. Frame
pixels use DFU's byte RLE: codes `0..127` copy `code+1` literal bytes; codes
`128..255` repeat the following palette index `code-127` times. Output is
bounded to `width*height`. Weapon images use `ART_PAL.COL`; palette index 0 is
transparent. `WEAPON02.CIF` is the classic dagger: one idle frame followed by
six five-frame strike records (down, down-left, left, right, down-right, up).

### 1.7 DAGGER.SND combat clips (SndFile.cs)

`DAGGER.SND` is a numeric BSA. Each record payload is raw unsigned 8-bit mono
PCM at 11025 Hz. DFU `SoundClips` values address BSA directory order; the BSA
numeric record ID is distinct provenance. Publication wraps selected records
in a standard 44-byte PCM WAV header without resampling or changing samples.
The clone-first dagger slice uses swing-high-pitch index 106 and hit variants
108 through 112.

## 2. rusty-engine side — what exists and what to plug into

- **Rust core**: `rust/crates/render-model` owns mesh data (`StaticMeshAsset`, `MeshPayloadDescriptor`,
  `PackedMeshResource`, `pack_mesh_resources` in `mesh_resource.rs`). `svc-mesh` is the mesh service.
- **Importer**: `rust/crates/asset-import` is **glTF/GLB-only** (`plan_import`, `plan_animated_glb_import`,
  `gltf_package.rs`) with a CLI at `src/bin/rusty_asset_import.rs`
  (`plan`/`write`/`init-sidecar`/`validate-sidecar`). No OBJ importer — and adding one is unnecessary (see plan).
- **Renderer boundary**: downstream consumers use the public Rust facade and
  Engine's Rust webview adapter; the private TypeScript/Three implementation
  is not a Dagger dependency or vocabulary. Engine Studio remains a separate
  first-party product. Coordinate convention is **right-handed Y-up**, which
  differs from Unity/DFU (left-handed):
  when emitting glTF, map DFU-space `(x, y, z) → (x, y, -z)` and reverse triangle winding
  (or accept a mirrored dungeon for a pure geometry smoke test — decision point).
- **Headless test path (ideal for "just a test")**: `rust/crates/render-presentation/tests/contract.rs`
  consumes frame fixtures (`fixtures/render/*-v1.json`) and compares against golden snapshots
  (`fixtures/render/goldens/*.snapshot`). The goldens already show untextured static meshes with flat
  RGB materials (`kind staticMesh asset mesh/room-wall materials [0.68,0.73,0.76]`). A dungeon mesh can be
  validated exactly the same way without a GPU.
- **Content locations**: authored assets live in `content/assets/`, conversion requests in
  `content/conversion/`, test data in `fixtures/`.

## 3. Implementation plan (minimal viable test)

Goal: one untextured static mesh asset `mesh/privateers-hold` renderable/validatable in rusty-engine.

1. **New Rust tool: `daggerfall-import`** (suggested location: new bin target or small crate, e.g.
   `rust/crates/asset-import/src/bin/daggerfall_import.rs` or a standalone `tools/` crate — confirm with repo conventions):
   - Implement the four parsers from §1 (BSA, MAPS, RDB, ARCH3D) — ~400–600 lines of Rust; the binary
     layouts above are complete and validated. The PoC `extract_dungeon.py` here is a direct reference.
   - Hardcode (or parameterize) the Privateer's Hold block table from §1.2 for the first test;
     full MAPS.BSA resolution can come later.
   - Emit a **single-mesh GLB** (untextured; flat default material; optionally per-block or
     per-texture-archive primitives as vertex colors/groups for later texturing work).
     GLB emission is simple: one POSITION accessor + one INDICES accessor, no materials/textures.
     Mind the handedness: DFU space is left-handed Y-up; glTF is right-handed Y-up → negate Z and flip winding.
2. **Run it through the existing pipeline**: `rusty_asset_import write privateers-hold.glb <out>` →
   packed mesh resource; register as static mesh asset `mesh/privateers-hold` (pattern after the
   `kenney-*` fixtures in `fixtures/render/assets/`).
3. **Validate headless**: add a presentation-frame fixture placing one `mesh/privateers-hold` instance
   with a flat material, extend/duplicate the `render-presentation` contract test pattern, assert
   golden snapshot (bounds/handles) — no GPU needed.
4. **Optional visual check**: load the same asset through Engine Studio or
   Dagger's Rust-native host. Downstream code does not import the private
   renderer backend.

Why GLB handoff instead of a native OBJ/RDB path: the engine's importer, validation, sidecar/metadata,
and packing already speak glTF; the Daggerfall parser stays a clean offline converter and no engine
import-format surface area is added.

### Expected effort
- BSA + RDB + ARCH3D + MAPS parsers in Rust: ~0.5–1 day (specs above are complete; PoC exists).
- GLB writer: small (positions + u32 indices; the mesh is 18.8k verts/9.3k tris, well under limits —
  verify `MAX_SOURCE_BYTES`/resource caps in asset-import).
- Import + fixture + contract test wiring: ~0.5 day.

### Known gotchas (all verified against the binary data)
- `pointDivisor = 256` on ARCH3D vertices (without it the dungeon is kilometers across).
- `DungeonHeader` is **17** bytes (2+4+4+2+5), not 15 — misalignment corrupts the block table.
- RDB object type enum: Model = **0x01** (not 0); skip Light (0x02) / Flat (0x03).
- Rotations are 1/2048-turn units, negated; matrix order T·Rz·Rx·Ry.
- Y is negated twice (vertices and object positions) — missing either one turns the dungeon inside-out.
- Plane polygons are fans; polygon points are duplicated per plane (no welding needed for a test).
- Handedness: DFU(Unity) left-handed vs rusty-engine right-handed Y-up — decide mirror vs Z-negate+rewind.
- Later, for texturing: plane texture bitfield → archive/record, plus `DungeonTextureTables.cs` climate
  remapping and `BLOCKS.BSA` `.RDI`/flats for billboards — all safely ignorable for the untextured test.

## 4. Artifacts in this directory
- `bsa_inspect.py` — generic BSA lister/extractor (documents the archive format).
- `privateers_hold.py` — resolves Privateer's Hold from MAPS.BSA (region 17 → block table).
- `extract_dungeon.py` — full PoC: RDB+ARCH3D → OBJ using the exact DFU conventions.
- `privateers_hold.obj` — the extracted dungeon (18,811 v / 9,263 f; blocks at ±51.2 m offsets).

## 5. Implementation outcome (2026-08-01)

The plan in §3 was executed in `/home/dev/rusty-dagger`:
- `crates/arena2` — Rust parsers for BSA/MAPS/RDB/ARCH3D/TEXTURE/PAL/PAK (unit-tested against the real data).
- `crates/dagger-import` — CLI producing (a) a **textured GLB** (combined dungeon node + one named node per door, embedded PNGs) and (b) the engine-native **untextured** `privateers-hold.mesh.json`.
- The real checked project is verified through `dagger-native-host`, which
  reaches Engine presentation only through the public Rust facade. The former
  downstream browser renderer harness was removed.
- mesh.json admitted by the engine's `rusty-asset-import` with zero diagnostics as `mesh/privateers-hold` (artifacts in `content/imported/`).
- TEXTURE.nnn decode (incl. RecordRle), PAL.PAL palette, dungeon texture table + climate door remap (74→374 for Privateer's Hold Woodlands climate), and TEXTURE.000/.001 solid-colour virtual archives are all implemented — details in the rusty-dagger README.
