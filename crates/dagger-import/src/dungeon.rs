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
use arena2::pak::{climate_base_type, PakFile};
use arena2::palette::Palette;
use arena2::rdb;
use arena2::texture::TextureFile;
use arena2::{GLOBAL_SCALE, POINT_DIVISOR, RDB_SIDE, ROTATION_DIVISOR, TEXTURE_DIVISOR};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::rc::Rc;

/// DFU DungeonTextureTables.ApplyTextureTable with the classic default table
/// {119, 120, 122, 123, 124, 168} (identity) and climate-based door offset.
fn apply_texture_table(archive: u16, climate_base: u16) -> u16 {
    const TABLE: [u16; 6] = [119, 120, 122, 123, 124, 168];
    match archive {
        74 => archive + climate_base,
        119 => TABLE[0],
        120 => TABLE[1],
        122 => TABLE[2],
        123 => TABLE[3],
        124 => TABLE[4],
        168 => TABLE[5],
        a => a,
    }
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
}

pub struct BuildOutput {
    pub primitives: Vec<PrimitiveInput>,
    pub textures: Vec<TextureInput>,
    pub stats: BuildStats,
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
    for px in rgba.chunks_exact(4) {
        r += px[0] as u64;
        g += px[1] as u64;
        b += px[2] as u64;
        n += 1;
    }
    let n = n.max(1) as f32;
    [r as f32 / n / 255.0, g as f32 / n / 255.0, b as f32 / n / 255.0]
}

pub struct Importer {
    arena2_dir: PathBuf,
    palette: Palette,
    climate_base: u16,
    textured: bool,
    mesh_cache: HashMap<String, Rc<Mesh>>,
    texfile_cache: HashMap<u16, Option<Rc<TextureFile>>>,
    texture_keys: HashMap<(u16, u16), usize>, // (archive, record) -> output texture index
    textures: Vec<TextureInput>,
    texture_failures: Vec<String>,
}

impl Importer {
    pub fn new(arena2_dir: &Path, layout: &DungeonLayout, textured: bool) -> Result<Self, String> {
        let palette = Palette::load(&arena2_dir.join("PAL.PAL"))
            .map_err(|e| format!("PAL.PAL: {e}"))?;
        let (px, py) = maps::lon_lat_to_map_pixel(layout.longitude, layout.latitude);
        let climate_base = PakFile::load(&arena2_dir.join("CLIMATE.PAK"))
            .ok()
            .and_then(|pak| pak.get(px, py))
            .map(|wc| climate_base_type(wc) as u16)
            .unwrap_or(300); // Temperate fallback
        Ok(Importer {
            arena2_dir: arena2_dir.to_path_buf(),
            palette,
            climate_base,
            textured,
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
            self.texture_failures.push(format!("TEXTURE.{archive:03} unreadable"));
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
            name: format!("TEXTURE.{archive:03}[{record}] ({info_w}x{info_h})", info_w = info.width, info_h = info.height),
            png,
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

pub fn build_dungeon(
    arena2_dir: &Path,
    region: usize,
    location_name: &str,
    textured: bool,
) -> Result<BuildOutput, String> {
    let maps_bsa = BsaArchive::load(&arena2_dir.join("MAPS.BSA"))
        .map_err(|e| format!("MAPS.BSA: {e}"))?;
    let blocks_bsa = BsaArchive::load(&arena2_dir.join("BLOCKS.BSA"))
        .map_err(|e| format!("BLOCKS.BSA: {e}"))?;
    let arch = Arch3dFile::load(&arena2_dir.join("ARCH3D.BSA"))
        .map_err(|e| format!("ARCH3D.BSA: {e}"))?;

    let layout = maps::resolve_dungeon(&maps_bsa, region, location_name)?;
    let mut imp = Importer::new(arena2_dir, &layout, textured)?;

    let mut stats = BuildStats { blocks: layout.blocks.len(), ..Default::default() };
    stats.bounds_min = [f32::MAX; 3];
    stats.bounds_max = [f32::MIN; 3];

    // One primitive per texture key (None = untextured/default material).
    let mut prims: HashMap<Option<(u16, u16)>, PrimitiveBuild> = HashMap::new();

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
            let rot = mat_mul(rot_z(deg(obj.z_rot)), mat_mul(rot_x(deg(obj.x_rot)), rot_y(deg(obj.y_rot))));
            let obj_pos = [
                obj.x as f32 * GLOBAL_SCALE,
                -obj.y as f32 * GLOBAL_SCALE,
                obj.z as f32 * GLOBAL_SCALE,
            ];

            for plane in &mesh.planes {
                if plane.points.len() < 3 {
                    continue;
                }
                let remapped = apply_texture_table(plane.texture_archive, imp.climate_base);
                let tex_key = if textured { Some((remapped, plane.texture_record)) } else { None };

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
                    for k in 0..3 {
                        stats.bounds_min[k] = stats.bounds_min[k].min(gltf[k]);
                        stats.bounds_max[k] = stats.bounds_max[k].max(gltf[k]);
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

    // Finalize primitives; map texture keys to output texture indices
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

    stats.textures = imp.textures.len();
    stats.texture_failures = imp.texture_failures.clone();
    Ok(BuildOutput { primitives, textures: imp.textures, stats })
}
