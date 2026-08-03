//! RDB dungeon block parser (DFU BlocksFile.cs ReadRdb*).
//!
//! Layout:
//!   header (20B): u32 unk, u32 width, u32 height, u32 objectRootOffset, u32 unk
//!   model reference list: 750 x (5B cstring id + 3B cstring description)
//!   model data list: 750 x u32 (unknown)
//!   object section header: 512B ("DAGR" at +56)
//!   object root list at objectRootOffset: width*height x i32 (-1 = empty)
//!   object node: i32 next, i32 prev, i32 x, i32 y, i32 z, u8 type, u32 resourceOffset
//!     type: 0x01 model, 0x02 light, 0x03 flat
//!   model resource @resourceOffset: i32 xrot, i32 yrot, i32 zrot, u16 modelIndex,
//!     u32 triggerFlag, u8 soundIndex, i32 actionOffset

use crate::Cursor;

#[derive(Debug, Clone)]
pub struct RdbModelObject {
    pub x: i32,
    pub y: i32,
    pub z: i32,
    pub x_rot: i32,
    pub y_rot: i32,
    pub z_rot: i32,
    pub model_index: u16,
    pub model_id: String,
    pub description: String,
    pub has_action: bool,
}

#[derive(Debug, Clone)]
pub struct RdbFlatObject {
    pub x: i32,
    pub y: i32,
    pub z: i32,
    pub texture_archive: u16,
    pub texture_record: u16,
    pub flags: u16,
    pub next_object_offset: i32,
    pub action: u8,
}

#[derive(Debug, Clone)]
pub struct RdbLightObject {
    pub x: i32,
    pub y: i32,
    pub z: i32,
    pub radius: u16,
}

/// DFU TextureReader editor-flat archive; record 10 = start marker, 8 = enter marker.
pub const EDITOR_FLATS_ARCHIVE: u16 = 199;
pub const START_MARKER_RECORD: u16 = 10;
pub const ENTER_MARKER_RECORD: u16 = 8;

#[derive(Debug, Default)]
pub struct RdbBlock {
    pub width: u32,
    pub height: u32,
    pub models: Vec<RdbModelObject>,
    pub flats: Vec<RdbFlatObject>,
    pub lights: Vec<RdbLightObject>,
}

impl RdbBlock {
    /// World-space start marker (block-local raw coords, same units as models).
    pub fn start_marker(&self) -> Option<(&RdbFlatObject, [i32; 3])> {
        self.flats
            .iter()
            .find(|f| {
                f.texture_archive == EDITOR_FLATS_ARCHIVE && f.texture_record == START_MARKER_RECORD
            })
            .map(|f| (f, [f.x, f.y, f.z]))
    }

    pub fn enter_marker(&self) -> Option<[i32; 3]> {
        self.flats
            .iter()
            .find(|f| {
                f.texture_archive == EDITOR_FLATS_ARCHIVE && f.texture_record == ENTER_MARKER_RECORD
            })
            .map(|f| [f.x, f.y, f.z])
    }
}

pub fn parse_rdb(data: &[u8]) -> Result<RdbBlock, String> {
    if data.len() < 20 + 750 * 8 {
        return Err(format!("RDB too small: {} bytes", data.len()));
    }
    let mut c = Cursor::new(data);
    let _unk1 = c.u32();
    let width = c.u32();
    let height = c.u32();
    let object_root_offset = c.u32() as usize;
    let _unk2 = c.u32();
    if width == 0 || height == 0 || width * height > 4096 {
        return Err(format!("bad RDB dimensions {width}x{height}"));
    }

    let mut model_ids = Vec::with_capacity(750);
    let mut descriptions = Vec::with_capacity(750);
    for i in 0..750 {
        let base = 20 + i * 8;
        let id_end = data[base..base + 5]
            .iter()
            .position(|&b| b == 0)
            .unwrap_or(5);
        let desc_end = data[base + 5..base + 8]
            .iter()
            .position(|&b| b == 0)
            .unwrap_or(3);
        model_ids.push(String::from_utf8_lossy(&data[base..base + id_end]).into_owned());
        descriptions
            .push(String::from_utf8_lossy(&data[base + 5..base + 5 + desc_end]).into_owned());
    }

    let mut block = RdbBlock {
        width,
        height,
        ..Default::default()
    };
    for cell in 0..(width * height) as usize {
        let root = Cursor::at(data, object_root_offset + cell * 4).i32();
        if root < 0 {
            continue;
        }
        let mut p = root as usize;
        let mut guard = 0usize;
        loop {
            if p + 25 > data.len() {
                return Err(format!(
                    "object node at {p} out of bounds (len {})",
                    data.len()
                ));
            }
            let mut n = Cursor::at(data, p);
            let next = n.i32();
            let _prev = n.i32();
            let x = n.i32();
            let y = n.i32();
            let z = n.i32();
            let obj_type = n.u8();
            let resource_offset = n.u32() as usize;
            match obj_type {
                0x01 => {
                    let mut r = Cursor::at(data, resource_offset);
                    let x_rot = r.i32();
                    let y_rot = r.i32();
                    let z_rot = r.i32();
                    let model_index = r.u16();
                    let _trigger = r.u32();
                    let _sound = r.u8();
                    let action_offset = r.i32();
                    let mi = model_index as usize;
                    block.models.push(RdbModelObject {
                        x,
                        y,
                        z,
                        x_rot,
                        y_rot,
                        z_rot,
                        model_index,
                        model_id: model_ids.get(mi).cloned().unwrap_or_default(),
                        description: descriptions.get(mi).cloned().unwrap_or_default(),
                        has_action: action_offset > 0,
                    });
                }
                0x02 => {
                    let mut r = Cursor::at(data, resource_offset);
                    let _unk1 = r.u32();
                    let _unk2 = r.u32();
                    let radius = r.u16();
                    block.lights.push(RdbLightObject { x, y, z, radius });
                }
                0x03 => {
                    let mut r = Cursor::at(data, resource_offset);
                    let bitfield = r.u16();
                    let flags = r.u16();
                    let _magnitude = r.u8();
                    let _sound = r.u8();
                    let next_object_offset = r.i32();
                    let action = r.u8();
                    block.flats.push(RdbFlatObject {
                        x,
                        y,
                        z,
                        texture_archive: bitfield >> 7,
                        texture_record: bitfield & 0x7F,
                        flags,
                        next_object_offset,
                        action,
                    });
                }
                other => return Err(format!("unknown RDB object type {other:#x} at {p}")),
            }
            if next < 0 {
                break;
            }
            p = next as usize;
            guard += 1;
            if guard > 100_000 {
                return Err("object linked list cycle guard tripped".into());
            }
        }
    }
    Ok(block)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::arena2_dir;
    use crate::bsa::BsaArchive;

    #[test]
    fn s0000999_model_count() {
        let dir = arena2_dir();
        let bsa = BsaArchive::load(&dir.join("BLOCKS.BSA")).unwrap();
        let block = parse_rdb(bsa.get("S0000999.RDB").unwrap()).unwrap();
        assert_eq!((block.width, block.height), (8, 8));
        assert_eq!(block.models.len(), 209);
        // Object raw positions must stay within the 2048-unit block (Y negative down)
        for m in &block.models {
            assert!((0..=2048).contains(&m.x), "x {} out of range", m.x);
            assert!((-2048..=0).contains(&m.y), "y {} out of range", m.y);
            assert!((0..=2048).contains(&m.z), "z {} out of range", m.z);
        }
    }
}

#[cfg(test)]
mod marker_tests {
    use super::*;
    use crate::arena2_dir;
    use crate::bsa::BsaArchive;

    #[test]
    fn s0000999_start_marker_exists() {
        let dir = arena2_dir();
        let bsa = BsaArchive::load(&dir.join("BLOCKS.BSA")).unwrap();
        let block = parse_rdb(bsa.get("S0000999.RDB").unwrap()).unwrap();
        assert!(block.flats.len() > 0, "expected flats in start block");
        let (flat, pos) = block
            .start_marker()
            .expect("start marker flat (199/10) must exist in S0000999");
        assert_eq!(flat.texture_archive, EDITOR_FLATS_ARCHIVE);
        assert_eq!(flat.texture_record, START_MARKER_RECORD);
        // Marker must sit inside the block's raw extents
        assert!((0..=2048).contains(&pos[0]), "x {} out of block", pos[0]);
        assert!((-2048..=0).contains(&pos[1]), "y {} out of block", pos[1]);
        assert!((0..=2048).contains(&pos[2]), "z {} out of block", pos[2]);
        println!(
            "start marker raw: {pos:?} lights={} flats={}",
            block.lights.len(),
            block.flats.len()
        );
    }
}
