//! MAPS.BSA resolution: location name -> dungeon block layout (DFU MapsFile.cs).

use crate::bsa::BsaArchive;
use crate::{require_range, Cursor};

#[derive(Debug, Clone)]
pub struct DungeonBlockRef {
    pub name: String,
    pub x: i8,
    pub z: i8,
    pub is_start: bool,
}

#[derive(Debug, Clone)]
pub struct DungeonLayout {
    pub region: usize,
    pub location_index: usize,
    pub location_name: String,
    pub map_id: i32,
    pub location_id: u32,
    pub longitude: i32,
    pub latitude: i32,
    pub dungeon_type: u8,
    pub blocks: Vec<DungeonBlockRef>,
}

const RDB_BLOCK_LETTERS: [&str; 6] = ["N", "W", "L", "S", "B", "M"];

fn region_record<'a>(bsa: &'a BsaArchive, stem: &str, region: usize) -> Result<&'a [u8], String> {
    let name = format!("{stem}.{region:03}");
    bsa.get(&name)
        .ok_or_else(|| format!("record {name} not found"))
}

/// Read MAPNAMES: u32 count, then count x 32-byte name strides.
pub fn read_map_names(bsa: &BsaArchive, region: usize) -> Result<Vec<String>, String> {
    let data = region_record(bsa, "MAPNAMES", region)?;
    require_range(data, 0, 4, "MAPNAMES header")?;
    let mut c = Cursor::new(data);
    let count = c.u32() as usize;
    let names_len = count
        .checked_mul(32)
        .ok_or_else(|| "MAPNAMES size overflow".to_string())?;
    require_range(data, 4, names_len, "MAPNAMES entries")?;
    let mut names = Vec::with_capacity(count);
    for i in 0..count {
        let stride = &data[4 + i * 32..4 + i * 32 + 32];
        let end = stride.iter().position(|&b| b == 0).unwrap_or(32);
        names.push(String::from_utf8_lossy(&stride[..end]).into_owned());
    }
    Ok(names)
}

/// Resolve a dungeon's block layout by location name (e.g. "Privateer's Hold").
pub fn resolve_dungeon(
    bsa: &BsaArchive,
    region: usize,
    location_name: &str,
) -> Result<DungeonLayout, String> {
    let names = read_map_names(bsa, region)?;
    let location_count = names.len();
    let location_index = names
        .iter()
        .position(|n| n == location_name)
        .ok_or_else(|| format!("location {location_name:?} not found in region {region}"))?;

    // MAPTABLE: 17-byte entries
    let table = region_record(bsa, "MAPTABLE", region)?;
    let table_offset = location_index
        .checked_mul(17)
        .ok_or_else(|| "MAPTABLE offset overflow".to_string())?;
    require_range(table, table_offset, 17, "MAPTABLE entry")?;
    let mut c = Cursor::at(table, table_offset);
    let map_id = c.i32();
    let bitfield = c.u32();
    let longitude = ((bitfield & 0x1FF_FFFF) >> 8) as i32;
    let latitude = (c.i32() & 0xFF_FFFF) >> 8;
    let dungeon_type = c.u8();

    // MAPPITEM: u32 offset table; record at count*4 + offset.
    // LocationRecordElement: u32 doorCount + 6B doors, then header; LocationId (u16) at +33.
    let pitem = region_record(bsa, "MAPPITEM", region)?;
    let pitem_index = location_index
        .checked_mul(4)
        .ok_or_else(|| "MAPPITEM index overflow".to_string())?;
    require_range(pitem, pitem_index, 4, "MAPPITEM offset entry")?;
    let rec_off = Cursor::at(pitem, pitem_index).u32() as usize;
    let offset_table_len = location_count
        .checked_mul(4)
        .ok_or_else(|| "MAPPITEM table size overflow".to_string())?;
    let record_start = offset_table_len
        .checked_add(rec_off)
        .ok_or_else(|| "MAPPITEM record offset overflow".to_string())?;
    require_range(pitem, record_start, 4, "MAPPITEM record")?;
    let mut c = Cursor::at(pitem, record_start);
    let door_count = c.u32() as usize;
    let door_bytes = door_count
        .checked_mul(6)
        .ok_or_else(|| "MAPPITEM door list overflow".to_string())?;
    require_range(pitem, c.pos, door_bytes, "MAPPITEM doors")?;
    c.skip(door_bytes);
    let exterior_id_offset = c
        .pos
        .checked_add(33)
        .ok_or_else(|| "MAPPITEM LocationId offset overflow".to_string())?;
    require_range(pitem, exterior_id_offset, 2, "MAPPITEM LocationId")?;
    let exterior_location_id = Cursor::at(pitem, exterior_id_offset).u16() as u32;

    // MAPDITEM: u32 count, then count x 8B {u32 offset, u16 isDungeon, u16 exteriorLocationId}
    let ditem = region_record(bsa, "MAPDITEM", region)?;
    if ditem.is_empty() {
        return Err(format!("location {location_name:?} has no dungeon data"));
    }
    require_range(ditem, 0, 4, "MAPDITEM header")?;
    let mut c = Cursor::new(ditem);
    let dungeon_count = c.u32() as usize;
    let dungeon_table_len = dungeon_count
        .checked_mul(8)
        .ok_or_else(|| "MAPDITEM table size overflow".to_string())?;
    require_range(ditem, 4, dungeon_table_len, "MAPDITEM table")?;
    let mut found = None;
    for _ in 0..dungeon_count {
        let offset = c.u32() as usize;
        let _is_dungeon = c.u16();
        let elid = c.u16() as u32;
        if elid == exterior_location_id {
            found = Some(offset);
            break;
        }
    }
    let dungeon_offset = found.ok_or_else(|| {
        format!("no dungeon linked to exterior LocationId {exterior_location_id}")
    })?;

    // Dungeon record at 4 + count*8 + offset: LocationRecordElement then DungeonHeader.
    let dungeon_record_start = 4usize
        .checked_add(dungeon_table_len)
        .and_then(|start| start.checked_add(dungeon_offset))
        .ok_or_else(|| "MAPDITEM dungeon record offset overflow".to_string())?;
    require_range(ditem, dungeon_record_start, 4, "MAPDITEM dungeon record")?;
    let mut c = Cursor::at(ditem, dungeon_record_start);
    let door_count = c.u32() as usize;
    let door_bytes = door_count
        .checked_mul(6)
        .ok_or_else(|| "MAPDITEM door list overflow".to_string())?;
    require_range(ditem, c.pos, door_bytes, "MAPDITEM doors")?;
    c.skip(door_bytes);
    // LocationRecordElementHeader = 112 bytes (LocationId u32 at +33).
    // DFU seeds the dungeon texture table with this LocationId
    // (DaggerfallDungeon: Summary.LocationData.Dungeon.RecordElement.Header.LocationId).
    require_range(ditem, c.pos, 112, "MAPDITEM location header")?;
    let location_id_offset = c
        .pos
        .checked_add(33)
        .ok_or_else(|| "MAPDITEM LocationId offset overflow".to_string())?;
    let location_id = Cursor::at(ditem, location_id_offset).u32();
    c.skip(112);
    // DungeonHeader: u16 null, u32 unk, u32 unk, u16 blockCount, 5B unk = 17 bytes
    require_range(ditem, c.pos, 17, "MAPDITEM dungeon header")?;
    c.skip(10);
    let block_count = c.u16() as usize;
    c.skip(5);

    let block_bytes = block_count
        .checked_mul(4)
        .ok_or_else(|| "MAPDITEM block list overflow".to_string())?;
    require_range(ditem, c.pos, block_bytes, "MAPDITEM block list")?;
    let mut blocks = Vec::with_capacity(block_count);
    for _ in 0..block_count {
        let x = c.i8();
        let z = c.i8();
        let bf = c.u16();
        let block_number = bf & 0x3FF;
        let is_start = bf & 0x400 != 0;
        let block_index = (bf >> 11) as usize;
        if block_index >= RDB_BLOCK_LETTERS.len() {
            return Err(format!("invalid block index {block_index}"));
        }
        blocks.push(DungeonBlockRef {
            name: format!("{}{:07}.RDB", RDB_BLOCK_LETTERS[block_index], block_number),
            x,
            z,
            is_start,
        });
    }

    Ok(DungeonLayout {
        region,
        location_index,
        location_name: location_name.to_string(),
        map_id,
        location_id,
        longitude,
        latitude,
        dungeon_type,
        blocks,
    })
}

/// Map pixel coordinates (DFU MapsFile.LongitudeLatitudeToMapPixel). World is 1000x500.
pub fn lon_lat_to_map_pixel(longitude: i32, latitude: i32) -> (i32, i32) {
    (longitude / 128, 499 - latitude / 128)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_fixtures::named_bsa;

    fn maps_fixture() -> BsaArchive {
        let mut names = vec![0u8; 36];
        names[..4].copy_from_slice(&1u32.to_le_bytes());
        names[4..16].copy_from_slice(b"Fixture Hold");

        let mut table = vec![0u8; 17];
        table[..4].copy_from_slice(&42i32.to_le_bytes());
        table[4..8].copy_from_slice(&(1280u32 << 8).to_le_bytes());
        table[8..12].copy_from_slice(&(2560i32 << 8).to_le_bytes());
        table[12] = 2;

        let mut pitem = vec![0u8; 47];
        pitem[..4].copy_from_slice(&0u32.to_le_bytes());
        pitem[4..8].copy_from_slice(&0u32.to_le_bytes());
        pitem[41..43].copy_from_slice(&7u16.to_le_bytes());

        let mut ditem = vec![0u8; 149];
        ditem[..4].copy_from_slice(&1u32.to_le_bytes());
        ditem[4..8].copy_from_slice(&0u32.to_le_bytes());
        ditem[8..10].copy_from_slice(&1u16.to_le_bytes());
        ditem[10..12].copy_from_slice(&7u16.to_le_bytes());
        ditem[12..16].copy_from_slice(&0u32.to_le_bytes());
        ditem[49..53].copy_from_slice(&99u32.to_le_bytes());
        ditem[138..140].copy_from_slice(&1u16.to_le_bytes());
        ditem[145] = 1;
        ditem[146] = 255;
        let block = (3u16 << 11) | 0x400 | 7;
        ditem[147..149].copy_from_slice(&block.to_le_bytes());

        BsaArchive::parse(named_bsa(&[
            ("MAPNAMES.017", &names),
            ("MAPTABLE.017", &table),
            ("MAPPITEM.017", &pitem),
            ("MAPDITEM.017", &ditem),
        ]))
        .unwrap()
    }

    #[test]
    fn bounded_location_resolves_and_truncation_fails_closed() {
        let bsa = maps_fixture();
        let layout = resolve_dungeon(&bsa, 17, "Fixture Hold").unwrap();
        assert_eq!(layout.map_id, 42);
        assert_eq!(layout.location_id, 99);
        assert_eq!((layout.longitude, layout.latitude), (1280, 2560));
        assert_eq!(layout.location_index, 0);
        assert_eq!(layout.dungeon_type, 2);
        assert_eq!(layout.blocks.len(), 1);
        assert_eq!(layout.blocks[0].name, "S0000007.RDB");
        assert_eq!((layout.blocks[0].x, layout.blocks[0].z), (1, -1));
        assert!(layout.blocks[0].is_start);

        let truncated_names =
            BsaArchive::parse(named_bsa(&[("MAPNAMES.017", &1u32.to_le_bytes())])).unwrap();
        assert!(read_map_names(&truncated_names, 17).is_err());
    }
}
