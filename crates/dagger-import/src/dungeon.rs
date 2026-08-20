//! Dungeon assembly: MAPS.BSA layout + BLOCKS.BSA RDB objects + ARCH3D.BSA meshes
//! + TEXTURE.nnn/palette -> textured, world-space triangle primitives.
//!
//! Space conversions:
//! - Daggerfall mesh raw units: /256 sub-units, Y-down; DFU emits Unity (LH, Y-up)
//!   via (x, -y, z) * 0.025, model matrix M = T * Rz * Rx * Ry with
//!   degrees = -raw / 5.688888..., block origin (bx * 51.2, 0, bz * 51.2).
//! - glTF is RH Y-up: we emit (x, y, -z) and use natural fan winding (0, i+1, i+2)
//!   (reversed from DFU's (0, i+2, i+1) to preserve facing after mirroring).

use crate::glb::{PrimitiveInput, TextureInput};
use arena2::arch3d::{Arch3dFile, Mesh};
use arena2::bsa::BsaArchive;
use arena2::maps::{self, DungeonLayout};
use arena2::mobile::mobile_type;
use arena2::pak::{climate_base_type, PakFile};
use arena2::palette::Palette;
use arena2::rdb;
use arena2::texture::TextureFile;
use arena2::texture_table::{
    apply_texture_table, random_texture_table_classic, DEFAULT_TEXTURE_TABLE,
};
use arena2::{GLOBAL_SCALE, POINT_DIVISOR, RDB_SIDE, ROTATION_DIVISOR, TEXTURE_DIVISOR};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::rc::Rc;

/// Which dungeon texture table to apply (DFU DungeonTextureTables).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TextureTableMode {
    /// Classic default identity table {119, 120, 122, 123, 124, 168}.
    Default,
    /// Per-location classic randomized table: DFRandom seeded by the
    /// dungeon's LocationId, what classic uses for main-story dungeons.
    Classic,
}

#[derive(Default)]
struct PrimitiveBuild {
    positions: Vec<[f32; 3]>,
    normals: Vec<[f32; 3]>,
    uvs: Vec<[f32; 2]>,
    indices: Vec<u32>,
}

#[derive(Debug, Default)]
pub struct BuildStats {
    pub blocks: usize,
    pub models_used: usize,
    pub models_missing: usize,
    pub verts: usize,
    pub tris: usize,
    pub textures: usize,
    pub texture_failures: Vec<String>,
    pub bounds_min: [f32; 3],
    pub bounds_max: [f32; 3],
    /// The dungeon texture table applied (DFU DungeonTextureTables).
    pub texture_table: [u16; 6],
}

pub struct BuildOutput {
    /// Combined dungeon primitives (the collision trimesh source AND the
    /// static render mesh) — hinged doors are NOT in this set.
    pub primitives: Vec<PrimitiveInput>,
    /// Per-door render primitives (named `door-N-<model_id>`, sanitize-safe
    /// for three.js/glTF consumers), emitted into the GLB so doors render as
    /// distinct nodes but kept OUT of `primitives` so the collision trimesh
    /// has open doorways for route derivation.
    pub door_primitives: Vec<PrimitiveInput>,
    pub textures: Vec<TextureInput>,
    pub stats: BuildStats,
    /// Dungeon-scene metadata: markers in glTF world space (RH Y-up, meters).
    pub scene: DungeonScene,
}

#[derive(Debug, Clone)]
pub struct BillboardFlat {
    /// glTF world-space position (meters).
    pub position: [f32; 3],
    pub texture_archive: u16,
    pub texture_record: u16,
}

#[derive(Debug, Clone)]
pub struct DoorScene {
    /// glTF world-space position (meters, block origin + model offset).
    pub position: [f32; 3],
    /// glTF world-space rotation as euler degrees (x, y, z, DFU T·Rz·Rx·Ry).
    pub rotation_deg: [f32; 3],
    pub model_id: String,
    /// Hinged action door (DFU DaggerfallActionDoor, OpenAngle=-90). True for
    /// every DOR-tagged model; the door swings open rather than sliding.
    pub hinged: bool,
    /// Optional special action record (slide axis/duration/magnitude) when the
    /// model carries one — most hinged doors do not.
    pub action: Option<DoorAction>,
}

#[derive(Debug, Clone)]
pub struct DoorAction {
    pub axis: u8,
    pub duration: u16,
    pub magnitude: u16,
}

/// A classic enemy flat (DFU AddFixedRDBEnemy): a directional billboard to be
/// emitted as its own scene node, not baked into the static mesh. View-only:
/// position + mobile type; facing is identity (DFU spawns RDB enemies
/// unrotated, facing Unity +z).
#[derive(Debug, Clone)]
pub struct EnemyScene {
    pub position: [f32; 3],
    pub mobile_id: u8,
    pub name: String,
    pub texture_archive: u16,
}

/// A random-treasure marker (DFU RDBLayout AddRandomTreasure, editor archive
/// 199 record 19): where a lootable treasure pile container is placed.
/// `loot_key` is the classic dungeon-treasure loot table key resolved from
/// the dungeon's MAPS.BSA type through the donor's dungeon-type array
/// (`dungeon_treasure_loot_key`); `None` when the dungeon type has no key.
#[derive(Debug, Clone)]
pub struct TreasureScene {
    /// glTF world-space position (meters).
    pub position: [f32; 3],
    /// Raw RDB flat flags, carried as available provenance.
    pub flags: u16,
    pub loot_key: Option<String>,
}

/// The donor's dungeon-type → dungeon-treasure loot key array (DFU
/// LootTables.cs GenerateLoot `lootTableKeys`, indexed by the classic
/// MAPS.BSA dungeon type: 0 Crypt .. 18 Cemetery; DFU RDBLayout passes
/// `(int)dungeonType` straight through). Privateer's Hold's MAPS.BSA dungeon
/// type byte is 2 (Human Stronghold) — verified against the real data files —
/// which maps to "N".
pub const DUNGEON_TREASURE_LOOT_KEYS: [(&str, &str); 19] = [
    ("Crypt", "K"),
    ("Orc Stronghold", "N"),
    ("Human Stronghold", "N"),
    ("Prison", "N"),
    ("Desecrated Temple", "K"),
    ("Mine", "M"),
    ("Natural Cave", "M"),
    ("Coven", "Q"),
    ("Vampire Haunt", "K"),
    ("Laboratory", "U"),
    ("Harpy Nest", "D"),
    ("Ruined Castle", "N"),
    ("Spider Nest", "L"),
    ("Giant Stronghold", "F"),
    ("Dragon's Den", "S"),
    ("Barbarian Stronghold", "N"),
    ("Volcanic Caves", "M"),
    ("Scorpion Nest", "L"),
    ("Cemetery", "N"),
];

/// Resolve the dungeon-treasure loot table key for a classic MAPS.BSA dungeon
/// type. Out-of-range types (including NoDungeon, 255) have no key.
pub fn dungeon_treasure_loot_key(dungeon_type: u8) -> Option<&'static str> {
    DUNGEON_TREASURE_LOOT_KEYS
        .get(usize::from(dungeon_type))
        .map(|(_, key)| *key)
}

/// The treasure pile billboard icon: DFU renders RandomTreasure containers
/// with one of 20 TEXTURE.216 records (DaggerfallLootDataTables.cs
/// `randomTreasureArchive` = 216, `randomTreasureIconIndices`). The icon has
/// no bearing on the generated loot ("Random treasure is generated only when
/// clicked on and icon has no bearing"), so we publish one deterministic
/// icon: index 0 of the donor's list = record 0.
pub const TREASURE_ICON_ARCHIVE: u16 = 216;
pub const TREASURE_ICON_RECORD: u16 = 0;

#[derive(Debug, Clone)]
pub struct DungeonScene {
    pub start_marker: Option<[f32; 3]>,
    pub enter_marker: Option<[f32; 3]>,
    pub light_count: usize,
    pub flat_count: usize,
    /// Point lights in glTF world space: (position, range in meters).
    pub lights: Vec<([f32; 3], f32)>,
    /// Visible billboard flats (RDB type 0x03, excluding editor/enemy markers).
    pub billboards: Vec<BillboardFlat>,
    /// Classic enemies (directional billboards), from RDB enemy flats.
    pub enemies: Vec<EnemyScene>,
    /// Random-treasure markers (editor archive 199 record 19), placed as
    /// lootable container sprites.
    pub treasure: Vec<TreasureScene>,
    /// Action-door models, carved out of the static mesh into separate nodes.
    pub doors: Vec<DoorScene>,
}

type Mat3 = [[f32; 3]; 3];

fn rot_x(a: f32) -> Mat3 {
    let (s, c) = a.sin_cos();
    [[1.0, 0.0, 0.0], [0.0, c, -s], [0.0, s, c]]
}
fn rot_y(a: f32) -> Mat3 {
    let (s, c) = a.sin_cos();
    [[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]]
}
fn rot_z(a: f32) -> Mat3 {
    let (s, c) = a.sin_cos();
    [[c, -s, 0.0], [s, c, 0.0], [0.0, 0.0, 1.0]]
}
fn mat_mul(a: Mat3, b: Mat3) -> Mat3 {
    let mut out = [[0.0; 3]; 3];
    for i in 0..3 {
        for j in 0..3 {
            out[i][j] = a[i][0] * b[0][j] + a[i][1] * b[1][j] + a[i][2] * b[2][j];
        }
    }
    out
}
fn mat_vec(m: Mat3, v: [f32; 3]) -> [f32; 3] {
    [
        m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
        m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
        m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2],
    ]
}

fn sub(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}
fn cross(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}
fn normalize(v: [f32; 3]) -> [f32; 3] {
    let len = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
    if len > 1e-12 {
        [v[0] / len, v[1] / len, v[2] / len]
    } else {
        [0.0, 1.0, 0.0]
    }
}

fn average_rgb(rgba: &[u8]) -> [f32; 3] {
    let (mut r, mut g, mut b, mut n) = (0u64, 0u64, 0u64, 0u64);
    for px in rgba.as_chunks::<4>().0 {
        r += px[0] as u64;
        g += px[1] as u64;
        b += px[2] as u64;
        n += 1;
    }
    let n = n.max(1) as f32;
    [
        r as f32 / n / 255.0,
        g as f32 / n / 255.0,
        b as f32 / n / 255.0,
    ]
}

pub struct Importer {
    arena2_dir: PathBuf,
    palette: Palette,
    climate_base: u16,
    texture_table: [u16; 6],
    mesh_cache: HashMap<String, Rc<Mesh>>,
    texfile_cache: HashMap<u16, Option<Rc<TextureFile>>>,
    texture_keys: HashMap<(u16, u16), usize>, // (archive, record) -> output texture index
    textures: Vec<TextureInput>,
    texture_failures: Vec<String>,
}

impl Importer {
    pub fn new(
        arena2_dir: &Path,
        layout: &DungeonLayout,
        table_mode: TextureTableMode,
    ) -> Result<Self, String> {
        let palette =
            Palette::load(&arena2_dir.join("PAL.PAL")).map_err(|e| format!("PAL.PAL: {e}"))?;
        let (px, py) = maps::lon_lat_to_map_pixel(layout.longitude, layout.latitude);
        let world_climate = match PakFile::load(&arena2_dir.join("CLIMATE.PAK")) {
            Ok(pak) => pak.get(px, py),
            Err(e) => {
                // Classic mode derives the table from climate authority:
                // fail closed rather than silently publish the identity table.
                if table_mode == TextureTableMode::Classic {
                    return Err(format!(
                        "CLIMATE.PAK required for --texture-table classic: {e}"
                    ));
                }
                None
            }
        };
        let climate_base = world_climate
            .map(|wc| climate_base_type(wc) as u16)
            .unwrap_or(300); // Temperate fallback
        let texture_table = match table_mode {
            TextureTableMode::Classic => {
                let wc = world_climate.ok_or_else(|| {
                    format!(
                        "CLIMATE.PAK has no climate at {} map pixel ({px}, {py}) \
                         required for --texture-table classic",
                        layout.location_name
                    )
                })?;
                // Classic seeds the table with the dungeon's LocationId (DFU
                // DaggerfallDungeon: Dungeon.RecordElement.Header.LocationId).
                random_texture_table_classic(layout.location_id, wc)
                    .map_err(|e| format!("{}: {e}", layout.location_name))?
            }
            TextureTableMode::Default => DEFAULT_TEXTURE_TABLE,
        };
        Ok(Importer {
            arena2_dir: arena2_dir.to_path_buf(),
            palette,
            climate_base,
            texture_table,
            mesh_cache: HashMap::new(),
            texfile_cache: HashMap::new(),
            texture_keys: HashMap::new(),
            textures: Vec::new(),
            texture_failures: Vec::new(),
        })
    }

    fn texture_file(&mut self, archive: u16) -> Option<Rc<TextureFile>> {
        if let Some(entry) = self.texfile_cache.get(&archive) {
            return entry.clone();
        }
        let path = self.arena2_dir.join(format!("TEXTURE.{archive:03}"));
        let loaded = TextureFile::load(&path).ok().map(Rc::new);
        if loaded.is_none() {
            self.texture_failures
                .push(format!("TEXTURE.{archive:03} unreadable"));
        }
        self.texfile_cache.insert(archive, loaded.clone());
        loaded
    }

    /// Resolve (archive, record) -> output texture index + pixel dims, decoding on first use.
    fn resolve_texture(&mut self, archive: u16, record: u16) -> Option<(usize, f32, f32)> {
        let key = (archive, record);
        if let Some(&idx) = self.texture_keys.get(&key) {
            let (w, h) = self.texture_dims(&key).unwrap_or((64.0, 64.0));
            return Some((idx, w, h));
        }
        let tex = self.texture_file(archive)?;
        let info = tex.record_info(record as usize)?;
        let (w, h, indexed) = tex
            .frame_pixels(record as usize, 0)
            .map_err(|e| {
                self.texture_failures
                    .push(format!("TEXTURE.{archive:03} rec {record}: {e}"));
                e
            })
            .ok()?;
        let rgba = self.palette.to_rgba(&indexed);
        let avg = average_rgb(&rgba);
        let png = crate::png::encode_rgba(w as u32, h as u32, &rgba);
        let idx = self.textures.len();
        self.textures.push(TextureInput {
            name: format!(
                "TEXTURE.{archive:03}[{record}] ({info_w}x{info_h})",
                info_w = info.width,
                info_h = info.height
            ),
            png,
            id: (archive, record),
            avg_color: avg,
        });
        self.texture_keys.insert(key, idx);
        Some((idx, info.width as f32, info.height as f32))
    }

    fn texture_dims(&self, key: &(u16, u16)) -> Option<(f32, f32)> {
        let tex = self.texfile_cache.get(&key.0)?.as_ref()?;
        let info = tex.record_info(key.1 as usize)?;
        Some((info.width as f32, info.height as f32))
    }

    fn mesh(&mut self, arch: &Arch3dFile, model_id: &str) -> Option<Rc<Mesh>> {
        if let Some(m) = self.mesh_cache.get(model_id) {
            return Some(m.clone());
        }
        let mesh = arch.mesh(model_id).ok().map(Rc::new);
        if let Some(m) = &mesh {
            self.mesh_cache.insert(model_id.to_string(), m.clone());
        }
        mesh
    }
}

/// Route one RDB flat into the scene lists. DFU precedence for the editor
/// archive: record 19 is a random-treasure marker (AddRandomTreasure) even if
/// the flat carries mobile bits, records 15/16 are enemy spawn markers, and
/// any flat with a non-zero mobile id is a fixed enemy (AddFixedRDBEnemy).
/// The remaining editor records (start/enter, plus 199/11 quest item and
/// 199/18 quest marker) are dropped: quest/item markers are out of scope.
fn route_flat(
    f: &rdb::RdbFlatObject,
    origin: [f32; 3],
    treasure_loot_key: Option<&str>,
    scene: &mut DungeonScene,
    stats: &mut BuildStats,
) {
    // Shared DFU->glTF placement: (x, -y, z) * GlobalScale + block origin,
    // then glTF (x, y, -z).
    let dfu = [
        f.x as f32 * GLOBAL_SCALE + origin[0],
        -f.y as f32 * GLOBAL_SCALE + origin[1],
        f.z as f32 * GLOBAL_SCALE + origin[2],
    ];
    let position = [dfu[0], dfu[1], -dfu[2]];
    if f.is_treasure_marker() {
        scene.treasure.push(TreasureScene {
            position,
            flags: f.flags,
            loot_key: treasure_loot_key.map(str::to_string),
        });
        return;
    }
    if f.is_enemy() {
        // DFU AddFixedRDBEnemy: enemy flats become directional billboard
        // nodes, not static billboards. Mobile id = faction_or_mobile_id &
        // 0xFF (0-42 monster, 128-146 humanoid; random markers carry the same
        // id semantics here).
        let mobile_id = (f.faction_or_mobile_id & 0xFF) as u8;
        match mobile_type(mobile_id) {
            Some(mobile) => scene.enemies.push(EnemyScene {
                position,
                mobile_id,
                name: mobile.name.to_string(),
                texture_archive: mobile.texture_archive,
            }),
            None => stats.texture_failures.push(format!(
                "enemy flat with unknown mobile id {mobile_id} \
                 (archive {} record {}), skipped",
                f.texture_archive, f.texture_record
            )),
        }
        return;
    }
    if !f.is_visible_billboard() {
        return;
    }
    // DFU AddFlat: billboard texture from TEXTURE.nnn[record].
    scene.billboards.push(BillboardFlat {
        position,
        texture_archive: f.texture_archive,
        texture_record: f.texture_record,
    });
}

pub fn build_dungeon(
    arena2_dir: &Path,
    region: usize,
    location_name: &str,
    textured: bool,
    table_mode: TextureTableMode,
) -> Result<BuildOutput, String> {
    let maps_bsa =
        BsaArchive::load(&arena2_dir.join("MAPS.BSA")).map_err(|e| format!("MAPS.BSA: {e}"))?;
    let blocks_bsa =
        BsaArchive::load(&arena2_dir.join("BLOCKS.BSA")).map_err(|e| format!("BLOCKS.BSA: {e}"))?;
    let arch =
        Arch3dFile::load(&arena2_dir.join("ARCH3D.BSA")).map_err(|e| format!("ARCH3D.BSA: {e}"))?;

    let layout = maps::resolve_dungeon(&maps_bsa, region, location_name)?;
    let mut imp = Importer::new(arena2_dir, &layout, table_mode)?;

    let mut stats = BuildStats {
        blocks: layout.blocks.len(),
        texture_table: imp.texture_table,
        ..Default::default()
    };
    stats.bounds_min = [f32::MAX; 3];
    stats.bounds_max = [f32::MIN; 3];
    let mut scene = DungeonScene {
        start_marker: None,
        enter_marker: None,
        light_count: 0,
        flat_count: 0,
        lights: Vec::new(),
        billboards: Vec::new(),
        enemies: Vec::new(),
        treasure: Vec::new(),
        doors: Vec::new(),
    };
    // The dungeon-treasure loot key comes from the MAPS.BSA dungeon type via
    // the donor's dungeon-type array (LootTables.cs GenerateLoot); every
    // treasure marker in the dungeon shares it.
    let treasure_loot_key = dungeon_treasure_loot_key(layout.dungeon_type);

    // One primitive per texture key (None = untextured/default material).
    let mut prims: HashMap<Option<(u16, u16)>, PrimitiveBuild> = HashMap::new();
    // Per-door render primitives, kept OUT of the combined dungeon primitives
    // (and therefore out of the collision trimesh). Each entry:
    // (door scene index, door texture archive, record, model id, geometry).
    let mut door_prims: Vec<(usize, u16, u16, String, PrimitiveBuild)> = Vec::new();

    for block_ref in &layout.blocks {
        let data = blocks_bsa
            .get(&block_ref.name)
            .ok_or_else(|| format!("{} not in BLOCKS.BSA", block_ref.name))?;
        let block = rdb::parse_rdb(data)?;
        let origin = [
            block_ref.x as f32 * RDB_SIDE,
            0.0,
            block_ref.z as f32 * RDB_SIDE,
        ];
        scene.light_count += block.lights.len();
        scene.flat_count += block.flats.len();
        for f in &block.flats {
            route_flat(f, origin, treasure_loot_key, &mut scene, &mut stats);
        }
        for l in &block.lights {
            // DFU AddLight: position (x, -y, z) * 0.025 + block origin; range = radius * 0.025 * 3.
            let dfu = [
                l.x as f32 * GLOBAL_SCALE + origin[0],
                -l.y as f32 * GLOBAL_SCALE + origin[1],
                l.z as f32 * GLOBAL_SCALE + origin[2],
            ];
            let range = l.radius as f32 * GLOBAL_SCALE * 3.0;
            scene.lights.push(([dfu[0], dfu[1], -dfu[2]], range));
        }
        if block_ref.is_start {
            // Marker positions are block-local raw coords (same units as models):
            // DFU space (x, -y, z) * 0.025 + block origin, then glTF (x, y, -z).
            if let Some((_f, m)) = block.start_marker() {
                let dfu = [
                    m[0] as f32 * GLOBAL_SCALE + origin[0],
                    -m[1] as f32 * GLOBAL_SCALE + origin[1],
                    m[2] as f32 * GLOBAL_SCALE + origin[2],
                ];
                scene.start_marker = Some([dfu[0], dfu[1], -dfu[2]]);
            }
            if let Some(m) = block.enter_marker() {
                let dfu = [
                    m[0] as f32 * GLOBAL_SCALE + origin[0],
                    -m[1] as f32 * GLOBAL_SCALE + origin[1],
                    m[2] as f32 * GLOBAL_SCALE + origin[2],
                ];
                scene.enter_marker = Some([dfu[0], dfu[1], -dfu[2]]);
            }
        }

        for obj in &block.models {
            let mesh = match imp.mesh(&arch, &obj.model_id) {
                Some(m) => m,
                None => {
                    stats.models_missing += 1;
                    continue;
                }
            };
            stats.models_used += 1;

            // DFU GetModelMatrix: M = T * Rz * Rx * Ry; degrees = -raw / ROTATION_DIVISOR
            let deg = |r: i32| (-r as f32 / ROTATION_DIVISOR).to_radians();
            let rot = mat_mul(
                rot_z(deg(obj.z_rot)),
                mat_mul(rot_x(deg(obj.x_rot)), rot_y(deg(obj.y_rot))),
            );
            let obj_pos = [
                obj.x as f32 * GLOBAL_SCALE,
                -obj.y as f32 * GLOBAL_SCALE,
                obj.z as f32 * GLOBAL_SCALE,
            ];

            // Hinged action doors are carved OUT of the static collision mesh:
            // their triangles are NOT merged into the combined dungeon
            // primitives (which become the collision trimesh), so the doorway
            // is open for route derivation. Instead each door's geometry is
            // emitted as its own named render primitive (door still visible,
            // correctly textured, and addressable as a distinct node the
            // runtime can swing open — DFU DaggerfallActionDoor, OpenAngle=-90).
            if obj.is_action_door() {
                let dfu = [
                    obj.x as f32 * GLOBAL_SCALE + origin[0],
                    -obj.y as f32 * GLOBAL_SCALE + origin[1],
                    obj.z as f32 * GLOBAL_SCALE + origin[2],
                ];
                let door_index = scene.doors.len();
                let mut door_prim = PrimitiveBuild::default();
                for plane in &mesh.planes {
                    if plane.points.len() < 3 {
                        continue;
                    }
                    let remapped = apply_texture_table(
                        plane.texture_archive,
                        &imp.texture_table,
                        imp.climate_base,
                    );
                    let (tex_w, tex_h) = match imp.resolve_texture(remapped, plane.texture_record) {
                        Some((_idx, w, h)) => (w, h),
                        None => (64.0, 64.0),
                    };
                    let base = door_prim.positions.len() as u32;
                    let mut world: Vec<[f32; 3]> = Vec::with_capacity(plane.points.len());
                    for p in &plane.points {
                        let local = [
                            p.x as f32 / POINT_DIVISOR * GLOBAL_SCALE,
                            -p.y as f32 / POINT_DIVISOR * GLOBAL_SCALE,
                            p.z as f32 / POINT_DIVISOR * GLOBAL_SCALE,
                        ];
                        let v = mat_vec(rot, local);
                        let wdfu = [
                            v[0] + obj_pos[0] + origin[0],
                            v[1] + obj_pos[1] + origin[1],
                            v[2] + obj_pos[2] + origin[2],
                        ];
                        let gltf = [wdfu[0], wdfu[1], -wdfu[2]];
                        world.push(gltf);
                        door_prim.uvs.push([
                            p.u as f32 / TEXTURE_DIVISOR / tex_w,
                            p.v as f32 / TEXTURE_DIVISOR / tex_h,
                        ]);
                    }
                    let n = normalize(cross(sub(world[1], world[0]), sub(world[2], world[0])));
                    for w in &world {
                        door_prim.normals.push(n);
                        door_prim.positions.push(*w);
                    }
                    for i in 0..(plane.points.len() as u32 - 2) {
                        door_prim.indices.push(base);
                        door_prim.indices.push(base + i + 1);
                        door_prim.indices.push(base + i + 2);
                    }
                }
                let tex_key = apply_texture_table(
                    mesh.planes.first().map(|p| p.texture_archive).unwrap_or(74),
                    &imp.texture_table,
                    imp.climate_base,
                );
                let tex_rec = mesh.planes.first().map(|p| p.texture_record).unwrap_or(0);
                door_prims.push((
                    door_index,
                    tex_key,
                    tex_rec,
                    obj.model_id.clone(),
                    door_prim,
                ));
                scene.doors.push(DoorScene {
                    position: [dfu[0], dfu[1], -dfu[2]],
                    rotation_deg: [deg(obj.x_rot), deg(obj.y_rot), deg(obj.z_rot)],
                    model_id: obj.model_id.clone(),
                    hinged: true,
                    action: obj.action.map(|record| DoorAction {
                        axis: record.axis,
                        duration: record.duration,
                        magnitude: record.magnitude,
                    }),
                });
                continue;
            }

            for plane in &mesh.planes {
                if plane.points.len() < 3 {
                    continue;
                }
                let remapped = apply_texture_table(
                    plane.texture_archive,
                    &imp.texture_table,
                    imp.climate_base,
                );
                let tex_key = if textured {
                    Some((remapped, plane.texture_record))
                } else {
                    None
                };

                // Resolve texture (decode on first use) to get dims for UVs
                let (tex_ok, tex_w, tex_h) = match tex_key {
                    Some((a, r)) => match imp.resolve_texture(a, r) {
                        Some((_idx, w, h)) => (true, w, h),
                        None => (false, 64.0, 64.0),
                    },
                    None => (false, 64.0, 64.0),
                };
                let prim_key = if tex_ok { tex_key } else { None };
                let prim = prims.entry(prim_key).or_default();

                // Transform plane points to world (DFU space), then to glTF space
                let base = prim.positions.len() as u32;
                let mut world: Vec<[f32; 3]> = Vec::with_capacity(plane.points.len());
                for p in &plane.points {
                    let local = [
                        p.x as f32 / POINT_DIVISOR * GLOBAL_SCALE,
                        -p.y as f32 / POINT_DIVISOR * GLOBAL_SCALE,
                        p.z as f32 / POINT_DIVISOR * GLOBAL_SCALE,
                    ];
                    let v = mat_vec(rot, local);
                    let dfu = [
                        v[0] + obj_pos[0] + origin[0],
                        v[1] + obj_pos[1] + origin[1],
                        v[2] + obj_pos[2] + origin[2],
                    ];
                    let gltf = [dfu[0], dfu[1], -dfu[2]]; // LH -> RH
                    for (k, coordinate) in gltf.iter().copied().enumerate() {
                        stats.bounds_min[k] = stats.bounds_min[k].min(coordinate);
                        stats.bounds_max[k] = stats.bounds_max[k].max(coordinate);
                    }
                    world.push(gltf);
                    prim.uvs.push([
                        p.u as f32 / TEXTURE_DIVISOR / tex_w,
                        p.v as f32 / TEXTURE_DIVISOR / tex_h,
                    ]);
                }

                // Flat normal from the first fan triangle (glTF space)
                let n = normalize(cross(sub(world[1], world[0]), sub(world[2], world[0])));
                for w in &world {
                    prim.normals.push(n);
                    prim.positions.push(*w);
                }

                // Natural fan winding (reversed from DFU to preserve facing after mirror)
                for i in 0..(plane.points.len() as u32 - 2) {
                    prim.indices.push(base);
                    prim.indices.push(base + i + 1);
                    prim.indices.push(base + i + 2);
                    stats.tris += 1;
                }
                stats.verts += plane.points.len();
            }
        }
    }

    // Finalize the combined dungeon primitives (collision trimesh source +
    // static render mesh). Hinged doors are NOT in this set.
    let mut primitives: Vec<PrimitiveInput> = Vec::new();
    let mut keys: Vec<Option<(u16, u16)>> = prims.keys().cloned().collect();
    keys.sort_by_key(|k| (k.is_none(), k.map(|(a, r)| (a, r))));
    for key in keys {
        let build = prims.remove(&key).unwrap();
        let (name, texture) = match key {
            Some((a, r)) => {
                let idx = imp.texture_keys.get(&(a, r)).copied();
                (format!("TEXTURE.{a:03}[{r}]"), idx)
            }
            None => ("default".to_string(), None),
        };
        primitives.push(PrimitiveInput {
            name,
            positions: build.positions,
            normals: build.normals,
            uvs: build.uvs,
            indices: build.indices,
            texture,
        });
    }

    // Emit each hinged door as its own named render primitive (GLB only; NOT
    // in the collision mesh), keyed to the door texture (74+climate).
    let mut door_primitives: Vec<PrimitiveInput> = Vec::new();
    for (door_index, tex_key, tex_rec, model_id, build) in door_prims {
        if build.indices.is_empty() {
            continue;
        }
        let texture = imp.texture_keys.get(&(tex_key, tex_rec)).copied();
        door_primitives.push(PrimitiveInput {
            name: format!("door-{door_index}-{model_id}"),
            positions: build.positions,
            normals: build.normals,
            uvs: build.uvs,
            indices: build.indices,
            texture,
        });
    }

    stats.textures = imp.textures.len();
    stats.texture_failures = imp.texture_failures.clone();
    Ok(BuildOutput {
        primitives,
        door_primitives,
        textures: imp.textures,
        stats,
        scene,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    struct FixtureDir(PathBuf);

    impl FixtureDir {
        fn new(tag: &str) -> Self {
            let path =
                std::env::temp_dir().join(format!("dagger-import-{tag}-{}", std::process::id()));
            let _ = std::fs::remove_dir_all(&path);
            std::fs::create_dir_all(&path).unwrap();
            std::fs::write(path.join("PAL.PAL"), vec![0u8; 768]).unwrap();
            Self(path)
        }

        fn path(&self) -> &Path {
            &self.0
        }
    }

    impl Drop for FixtureDir {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    fn fixture_layout() -> DungeonLayout {
        DungeonLayout {
            region: 0,
            location_index: 0,
            location_name: "Fixture Hold".into(),
            map_id: 42,
            location_id: 50050,
            longitude: 0,
            latitude: 0,
            dungeon_type: 2,
            blocks: Vec::new(),
        }
    }

    fn rdb_flat(archive: u16, record: u16, faction_or_mobile_id: u16) -> rdb::RdbFlatObject {
        rdb::RdbFlatObject {
            x: 100,
            y: -200,
            z: 300,
            texture_archive: archive,
            texture_record: record,
            flags: 7,
            magnitude: 0,
            sound_index: 0,
            faction_or_mobile_id,
            next_object_offset: -1,
            action: 0,
        }
    }

    #[test]
    fn flat_routing_places_treasure_markers_and_keeps_editor_markers_hidden() {
        let mut scene = DungeonScene {
            start_marker: None,
            enter_marker: None,
            light_count: 0,
            flat_count: 0,
            lights: Vec::new(),
            billboards: Vec::new(),
            enemies: Vec::new(),
            treasure: Vec::new(),
            doors: Vec::new(),
        };
        let mut stats = BuildStats::default();
        // Natural Cave (MAPS.BSA dungeon type 6) resolves to the donor's "M"
        // dungeon-treasure key.
        route_flat(
            &rdb_flat(199, 19, 0),
            [0.0; 3],
            dungeon_treasure_loot_key(6),
            &mut scene,
            &mut stats,
        );
        // Quest/item markers (199/11, 199/18) stay dropped.
        route_flat(
            &rdb_flat(199, 11, 0),
            [0.0; 3],
            None,
            &mut scene,
            &mut stats,
        );
        route_flat(
            &rdb_flat(199, 18, 0),
            [0.0; 3],
            None,
            &mut scene,
            &mut stats,
        );
        // A real billboard and a fixed enemy keep their existing routes.
        route_flat(
            &rdb_flat(210, 16, 0),
            [0.0; 3],
            None,
            &mut scene,
            &mut stats,
        );
        route_flat(
            &rdb_flat(199, 16, 138),
            [0.0; 3],
            None,
            &mut scene,
            &mut stats,
        );

        assert_eq!(scene.treasure.len(), 1);
        assert_eq!(scene.treasure[0].loot_key.as_deref(), Some("M"));
        assert_eq!(scene.treasure[0].flags, 7);
        assert_eq!(scene.billboards.len(), 1);
        assert_eq!(scene.enemies.len(), 1);
        // Donor table spot checks (LootTables.cs GenerateLoot lootTableKeys).
        assert_eq!(dungeon_treasure_loot_key(0), Some("K")); // Crypt
        assert_eq!(dungeon_treasure_loot_key(6), Some("M")); // Natural Cave
        assert_eq!(dungeon_treasure_loot_key(18), Some("N")); // Cemetery
        assert_eq!(dungeon_treasure_loot_key(255), None); // NoDungeon
    }

    #[test]
    fn start_block_random_treasure_markers_all_route_to_the_scene() {
        // Real-data proof: Privateer's Hold's start block S0000999.RDB (from
        // BLOCKS.BSA) carries 8 random-treasure markers (archive 199 record
        // 19) plus one 199/11 and one 199/18 quest/item marker; routing must
        // place exactly the 8 treasure markers and keep the quest/item
        // markers dropped.
        let arena2 = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../local/arena2");
        let blocks_path = arena2.join("BLOCKS.BSA");
        if !blocks_path.exists() {
            eprintln!(
                "skipping real Arena2 treasure-marker check: {} is absent",
                blocks_path.display()
            );
            return;
        }
        let blocks = BsaArchive::load(&blocks_path).expect("BLOCKS.BSA");
        let block = rdb::parse_rdb(blocks.get("S0000999.RDB").expect("S0000999.RDB"))
            .expect("parse start block");
        let treasure_markers = block
            .flats
            .iter()
            .filter(|flat| flat.is_treasure_marker())
            .count();
        assert_eq!(treasure_markers, 8);
        assert_eq!(
            block
                .flats
                .iter()
                .filter(|flat| flat.texture_archive == 199 && flat.texture_record == 11)
                .count(),
            1
        );
        assert_eq!(
            block
                .flats
                .iter()
                .filter(|flat| flat.texture_archive == 199 && flat.texture_record == 18)
                .count(),
            1
        );

        let mut scene = DungeonScene {
            start_marker: None,
            enter_marker: None,
            light_count: 0,
            flat_count: 0,
            lights: Vec::new(),
            billboards: Vec::new(),
            enemies: Vec::new(),
            treasure: Vec::new(),
            doors: Vec::new(),
        };
        let mut stats = BuildStats::default();
        for flat in &block.flats {
            route_flat(flat, [0.0; 3], Some("N"), &mut scene, &mut stats);
        }
        assert_eq!(scene.treasure.len(), 8, "all markers reach the sidecar");
        assert!(scene
            .treasure
            .iter()
            .all(|t| t.loot_key.as_deref() == Some("N")));
    }

    fn constant_pak(value: u8) -> Vec<u8> {
        let header_len = arena2::pak::PAK_HEIGHT * 4;
        let mut data = vec![0u8; header_len];
        for row in 0..arena2::pak::PAK_HEIGHT {
            let offset = header_len + row * 3;
            data[row * 4..row * 4 + 4].copy_from_slice(&(offset as u32).to_le_bytes());
            data.extend_from_slice(&(arena2::pak::PAK_WIDTH as u16).to_le_bytes());
            data.push(value);
        }
        data
    }

    #[test]
    fn classic_mode_fails_closed_without_climate_pak() {
        let layout = fixture_layout();
        let dir = FixtureDir::new("no-climate");
        let err = Importer::new(dir.path(), &layout, TextureTableMode::Classic)
            .err()
            .expect("classic mode must fail without CLIMATE.PAK");
        assert!(err.contains("CLIMATE.PAK"), "{err}");
        // Explicitly requested default mode keeps the identity table.
        let imp = Importer::new(dir.path(), &layout, TextureTableMode::Default).unwrap();
        assert_eq!(imp.texture_table, DEFAULT_TEXTURE_TABLE);
    }

    #[test]
    fn classic_mode_fails_closed_with_truncated_climate_pak() {
        let layout = fixture_layout();
        let dir = FixtureDir::new("truncated-climate");
        std::fs::write(dir.path().join("CLIMATE.PAK"), b"\0\0\0\0").unwrap();
        let err = Importer::new(dir.path(), &layout, TextureTableMode::Classic)
            .err()
            .expect("classic mode must fail with a truncated CLIMATE.PAK");
        assert!(err.contains("CLIMATE.PAK"), "{err}");
    }

    #[test]
    fn classic_mode_uses_hermetic_climate_authority() {
        let layout = fixture_layout();
        let dir = FixtureDir::new("climate");
        std::fs::write(dir.path().join("CLIMATE.PAK"), constant_pak(231)).unwrap();
        let imp = Importer::new(dir.path(), &layout, TextureTableMode::Classic).unwrap();
        assert_eq!(imp.texture_table, [23, 22, 19, 22, 20, 368]);
    }
}
