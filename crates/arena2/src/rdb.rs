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

use crate::{require_range, Cursor};

/// An RDB model action record (DFU BlocksFile.ReadRdbModelActionRecords):
/// 8 bytes at the model's actionOffset — axis, duration, magnitude,
/// next-object offset, flags. Doors translate along `axis` by `magnitude`
/// (raw units, same scale as model positions) over `duration`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RdbActionRecord {
    /// Axis about which the object rotates/translates (0/1/2 = x/y/z per DFU).
    pub axis: u8,
    /// Time to reach final state (raw units per DFU Duration semantics).
    pub duration: u16,
    /// Amount to translate/rotate around the axis (raw units per DFU).
    pub magnitude: u16,
    /// Offset to the object activated directly after this one (chaining), -1 = none.
    pub next_object_offset: i32,
    /// Action flags (non-zero = actionable).
    pub flags: u8,
}

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
    /// Parsed action record when has_action is set (DFU IsActionDoor + slide).
    pub action: Option<RdbActionRecord>,
}

/// DFU RDBLayout rejects the red-brick door model even though it carries a
/// "DOR" tag — it is not an action door.
pub const RED_BRICK_DOOR_MODEL_ID: u32 = 72100;

impl RdbModelObject {
    /// Whether this model is a hinged action door. Per DFU RDBLayout
    /// IsActionDoor: the model's Description tag is DOR/DDR/NEW/CAV and it is
    /// not the red-brick door model (72100). DFU does NOT require an action
    /// record — a DOR-tagged model is a hinged action door that swings open
    /// (DaggerfallActionDoor, OpenAngle=-90) rather than sliding. The action
    /// record is only for special chained action doors.
    pub fn is_action_door(&self) -> bool {
        if self.model_id.parse::<u32>().ok() == Some(RED_BRICK_DOOR_MODEL_ID) {
            return false;
        }
        matches!(self.description.as_str(), "DOR" | "DDR" | "NEW" | "CAV")
    }
}

#[derive(Debug, Clone)]
pub struct RdbFlatObject {
    pub x: i32,
    pub y: i32,
    pub z: i32,
    pub texture_archive: u16,
    pub texture_record: u16,
    pub flags: u16,
    pub magnitude: u8,
    pub sound_index: u8,
    /// Faction/Mobile id (DFU RdbFlatResource.FactionOrMobileId). Range 0-42 =
    /// monster in MONSTER.BSA, 128-146 = humanoid mobile type; & 0xFF = mobile id.
    pub faction_or_mobile_id: u16,
    pub next_object_offset: i32,
    pub action: u8,
}

impl RdbFlatObject {
    /// Whether this flat is a fixed enemy marker (DFU AddFixedRDBEnemy): an
    /// editor-archive (199) flat whose record is 15 (random) or 16 (fixed), or
    /// any flat carrying a non-zero mobile id. These become directional enemy
    /// sprites, not static billboards.
    pub fn is_enemy(&self) -> bool {
        if self.texture_archive == EDITOR_FLATS_ARCHIVE
            && (self.texture_record == 15 || self.texture_record == 16)
        {
            return true;
        }
        self.faction_or_mobile_id & 0xFF != 0 && self.faction_or_mobile_id & 0xFF != 99
    }
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
/// Editor enemy-marker records (DFU disables these — they are spawn markers,
/// not visible flats). See RDBLayout AddFlat: records 15 and 16 in archive 199.
pub const ENEMY_MARKER_RECORDS: [u16; 2] = [15, 16];
/// Editor random-treasure marker record (DFU RDBLayout.cs:391-400 maps
/// archive 199 record 19 to AddRandomTreasure — a RandomTreasure loot
/// container, not a visible flat).
pub const RANDOM_TREASURE_MARKER_RECORD: u16 = 19;

impl RdbFlatObject {
    /// Whether this flat renders as a visible billboard. Per DFU RDBLayout
    /// AddFlat/AddFlats: every flat is a billboard EXCEPT the editor flats
    /// archive (199 — start/enter/quest/treasure markers, hidden) and the
    /// enemy spawn markers (records 15/16 **in archive 199 only**, also
    /// hidden). The marker-record exclusion is scoped to archive 199: in
    /// other archives (e.g. 210 — torch/light flats), records 15 and 16 are
    /// real billboards. Real billboards come from archives like 210
    /// (lights/furniture), 213, 203, 206, and the NPC archives (handled
    /// separately by enemies).
    pub fn is_visible_billboard(&self) -> bool {
        if self.texture_archive == EDITOR_FLATS_ARCHIVE {
            return false;
        }
        true
    }

    /// Whether this flat is a random-treasure marker (DFU RDBLayout
    /// AddRandomTreasure): an editor-archive (199) flat with record 19. These
    /// are not visible billboards — they mark where a lootable treasure pile
    /// is placed. The other editor records dropped alongside (199/11 quest
    /// item, 199/18 quest marker) stay hidden: quest/item markers are out of
    /// scope for this pipeline.
    pub fn is_treasure_marker(&self) -> bool {
        self.texture_archive == EDITOR_FLATS_ARCHIVE
            && self.texture_record == RANDOM_TREASURE_MARKER_RECORD
    }
}

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
    let cell_count = width
        .checked_mul(height)
        .ok_or_else(|| format!("RDB dimensions overflow {width}x{height}"))?;
    if width == 0 || height == 0 || cell_count > 4096 {
        return Err(format!("bad RDB dimensions {width}x{height}"));
    }
    let root_bytes = (cell_count as usize)
        .checked_mul(4)
        .ok_or_else(|| "RDB object root list overflow".to_string())?;
    require_range(data, object_root_offset, root_bytes, "RDB object root list")?;

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
    for cell in 0..cell_count as usize {
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
                    require_range(data, resource_offset, 23, "RDB model resource")?;
                    let mut r = Cursor::at(data, resource_offset);
                    let x_rot = r.i32();
                    let y_rot = r.i32();
                    let z_rot = r.i32();
                    let model_index = r.u16();
                    let _trigger = r.u32();
                    let _sound = r.u8();
                    let action_offset = r.i32();
                    let mi = model_index as usize;
                    let action = if action_offset > 0 {
                        require_range(data, action_offset as usize, 10, "RDB action resource")?;
                        let mut ar = Cursor::at(data, action_offset as usize);
                        let axis = ar.u8();
                        let duration = ar.u16();
                        let magnitude = ar.u16();
                        let next_object_offset = ar.i32();
                        let flags = ar.u8();
                        Some(RdbActionRecord {
                            axis,
                            duration,
                            magnitude,
                            next_object_offset,
                            flags,
                        })
                    } else {
                        None
                    };
                    if mi >= model_ids.len() {
                        return Err(format!("RDB model index {mi} out of bounds"));
                    }
                    block.models.push(RdbModelObject {
                        x,
                        y,
                        z,
                        x_rot,
                        y_rot,
                        z_rot,
                        model_index,
                        model_id: model_ids[mi].clone(),
                        description: descriptions[mi].clone(),
                        has_action: action_offset > 0,
                        action,
                    });
                }
                0x02 => {
                    require_range(data, resource_offset, 10, "RDB light resource")?;
                    let mut r = Cursor::at(data, resource_offset);
                    let _unk1 = r.u32();
                    let _unk2 = r.u32();
                    let radius = r.u16();
                    block.lights.push(RdbLightObject { x, y, z, radius });
                }
                0x03 => {
                    // DFU ReadRdbFlatResource (11 bytes): TextureBitfield(2) +
                    // Flags(2) + Magnitude(1) + SoundIndex(1) +
                    // NextObjectOffset(4) + Action(1). FactionOrMobileId is
                    // synthesized from (Magnitude, SoundIndex), not read
                    // separately — reading it as a distinct u16 overruns the
                    // final flat record in border blocks by 2 bytes.
                    require_range(data, resource_offset, 11, "RDB flat resource")?;
                    let mut r = Cursor::at(data, resource_offset);
                    let bitfield = r.u16();
                    let flags = r.u16();
                    let magnitude = r.u8();
                    let sound_index = r.u8();
                    let faction_or_mobile_id = u16::from_le_bytes([magnitude, sound_index]);
                    let next_object_offset = r.i32();
                    let action = r.u8();
                    block.flats.push(RdbFlatObject {
                        x,
                        y,
                        z,
                        texture_archive: bitfield >> 7,
                        texture_record: bitfield & 0x7F,
                        flags,
                        magnitude,
                        sound_index,
                        faction_or_mobile_id,
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

    fn write_node(
        data: &mut [u8],
        offset: usize,
        next: i32,
        position: [i32; 3],
        object_type: u8,
        resource_offset: u32,
    ) {
        data[offset..offset + 4].copy_from_slice(&next.to_le_bytes());
        data[offset + 4..offset + 8].copy_from_slice(&(-1i32).to_le_bytes());
        for (axis, value) in position.into_iter().enumerate() {
            let start = offset + 8 + axis * 4;
            data[start..start + 4].copy_from_slice(&value.to_le_bytes());
        }
        data[offset + 20] = object_type;
        data[offset + 21..offset + 25].copy_from_slice(&resource_offset.to_le_bytes());
    }

    fn rdb_fixture() -> Vec<u8> {
        const ROOTS: usize = 6020;
        const MODEL_NODE: usize = 6024;
        const FLAT_NODE: usize = 6049;
        const LIGHT_NODE: usize = 6074;
        const MODEL_RESOURCE: usize = 6099;
        const FLAT_RESOURCE: usize = 6122;
        const LIGHT_RESOURCE: usize = 6133;
        let mut data = vec![0u8; 6143];
        data[4..8].copy_from_slice(&1u32.to_le_bytes());
        data[8..12].copy_from_slice(&1u32.to_le_bytes());
        data[12..16].copy_from_slice(&(ROOTS as u32).to_le_bytes());
        data[20..23].copy_from_slice(b"42\0");
        data[25..28].copy_from_slice(b"DOR");
        data[ROOTS..ROOTS + 4].copy_from_slice(&(MODEL_NODE as i32).to_le_bytes());
        write_node(
            &mut data,
            MODEL_NODE,
            FLAT_NODE as i32,
            [100, -200, 300],
            0x01,
            MODEL_RESOURCE as u32,
        );
        write_node(
            &mut data,
            FLAT_NODE,
            LIGHT_NODE as i32,
            [10, -20, 30],
            0x03,
            FLAT_RESOURCE as u32,
        );
        write_node(
            &mut data,
            LIGHT_NODE,
            -1,
            [1, -2, 3],
            0x02,
            LIGHT_RESOURCE as u32,
        );
        data[MODEL_RESOURCE + 12..MODEL_RESOURCE + 14].copy_from_slice(&0u16.to_le_bytes());
        let flat = (EDITOR_FLATS_ARCHIVE << 7) | START_MARKER_RECORD;
        data[FLAT_RESOURCE..FLAT_RESOURCE + 2].copy_from_slice(&flat.to_le_bytes());
        data[FLAT_RESOURCE + 4] = 7;
        data[FLAT_RESOURCE + 6..FLAT_RESOURCE + 10].copy_from_slice(&(-1i32).to_le_bytes());
        data[LIGHT_RESOURCE + 8..LIGHT_RESOURCE + 10].copy_from_slice(&512u16.to_le_bytes());
        data
    }

    #[test]
    fn bounded_object_chain_decodes_and_bad_offsets_fail_closed() {
        let fixture = rdb_fixture();
        let block = parse_rdb(&fixture).unwrap();
        assert_eq!((block.width, block.height), (1, 1));
        assert_eq!(block.models.len(), 1);
        assert_eq!(block.models[0].model_id, "42");
        assert!(block.models[0].is_action_door());
        assert_eq!(block.flats.len(), 1);
        assert!(block.flats[0].is_enemy());
        assert_eq!(
            block.start_marker().map(|(_, pos)| pos),
            Some([10, -20, 30])
        );
        assert_eq!(block.lights[0].radius, 512);

        assert!(parse_rdb(&fixture[..6140]).is_err());
        let mut bad_roots = fixture;
        bad_roots[12..16].copy_from_slice(&u32::MAX.to_le_bytes());
        assert!(parse_rdb(&bad_roots).is_err());
    }
}

#[cfg(test)]
mod billboard_tests {
    use super::*;

    fn flat(archive: u16, record: u16) -> RdbFlatObject {
        RdbFlatObject {
            x: 0,
            y: 0,
            z: 0,
            texture_archive: archive,
            texture_record: record,
            flags: 0,
            magnitude: 0,
            sound_index: 0,
            faction_or_mobile_id: 0,
            next_object_offset: -1,
            action: 0,
        }
    }

    #[test]
    fn editor_archive_flats_are_never_visible_billboards() {
        // Archive 199 is the editor archive: start/enter/quest/treasure
        // markers AND enemy spawn markers (records 15/16) are all hidden.
        for record in [START_MARKER_RECORD, ENTER_MARKER_RECORD, 15, 16, 3] {
            assert!(
                !flat(EDITOR_FLATS_ARCHIVE, record).is_visible_billboard(),
                "editor archive record {record} must not be a visible billboard"
            );
        }
    }

    #[test]
    fn non_editor_record_16_is_a_real_billboard() {
        // The marker-record exclusion is scoped to archive 199. Archive 210
        // record 16 is a normal torch/light flat (DFU
        // IsTorchFlat includes record 16) and must render as a billboard, not
        // be hidden as an enemy marker. Same for record 15 in other archives.
        assert!(flat(210, 16).is_visible_billboard());
        assert!(flat(210, 15).is_visible_billboard());
        assert!(flat(213, 16).is_visible_billboard());
    }

    #[test]
    fn only_editor_archive_record_19_is_a_treasure_marker() {
        assert!(flat(EDITOR_FLATS_ARCHIVE, RANDOM_TREASURE_MARKER_RECORD).is_treasure_marker());
        // Scoped to the editor archive: a 199-looking record in a real
        // billboard archive stays an ordinary billboard.
        assert!(!flat(210, RANDOM_TREASURE_MARKER_RECORD).is_treasure_marker());
        for record in [START_MARKER_RECORD, ENTER_MARKER_RECORD, 15, 16, 11, 18] {
            assert!(
                !flat(EDITOR_FLATS_ARCHIVE, record).is_treasure_marker(),
                "editor archive record {record} is not a treasure marker"
            );
        }
        // Treasure markers are never visible billboards themselves.
        assert!(!flat(EDITOR_FLATS_ARCHIVE, RANDOM_TREASURE_MARKER_RECORD).is_visible_billboard());
    }
}
