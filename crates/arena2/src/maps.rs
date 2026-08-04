//! MAPS.BSA resolution: location name -> dungeon block layout (DFU MapsFile.cs).

use crate::bsa::BsaArchive;
use crate::Cursor;

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
    let mut c = Cursor::new(data);
    let count = c.u32() as usize;
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
    let mut c = Cursor::at(table, location_index * 17);
    let map_id = c.i32();
    let bitfield = c.u32();
    let longitude = ((bitfield & 0x1FF_FFFF) >> 8) as i32;
    let latitude = ((c.i32() & 0xFF_FFFF) >> 8) as i32;
    let dungeon_type = c.u8();

    // MAPPITEM: u32 offset table; record at count*4 + offset.
    // LocationRecordElement: u32 doorCount + 6B doors, then header; LocationId (u16) at +33.
    let pitem = region_record(bsa, "MAPPITEM", region)?;
    let rec_off = Cursor::at(pitem, location_index * 4).u32() as usize;
    let mut c = Cursor::at(pitem, location_count * 4 + rec_off);
    let door_count = c.u32() as usize;
    c.skip(door_count * 6);
    let exterior_location_id = Cursor::at(pitem, c.pos + 33).u16() as u32;

    // MAPDITEM: u32 count, then count x 8B {u32 offset, u16 isDungeon, u16 exteriorLocationId}
    let ditem = region_record(bsa, "MAPDITEM", region)?;
    if ditem.is_empty() {
        return Err(format!("location {location_name:?} has no dungeon data"));
    }
    let mut c = Cursor::new(ditem);
    let dungeon_count = c.u32() as usize;
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
    let mut c = Cursor::at(ditem, 4 + dungeon_count * 8 + dungeon_offset);
    let door_count = c.u32() as usize;
    c.skip(door_count * 6);
    // LocationRecordElementHeader = 112 bytes (LocationId u32 at +33).
    // DFU seeds the dungeon texture table with this LocationId
    // (DaggerfallDungeon: Summary.LocationData.Dungeon.RecordElement.Header.LocationId).
    let location_id = Cursor::at(ditem, c.pos + 33).u32();
    c.skip(112);
    // DungeonHeader: u16 null, u32 unk, u32 unk, u16 blockCount, 5B unk = 17 bytes
    c.skip(10);
    let block_count = c.u16() as usize;
    c.skip(5);

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
    use crate::arena2_dir;

    #[test]
    fn resolves_privateers_hold() {
        let dir = arena2_dir();
        let bsa = BsaArchive::load(&dir.join("MAPS.BSA")).unwrap();
        let layout = resolve_dungeon(&bsa, 17, "Privateer's Hold").unwrap();
        assert_eq!(layout.map_id, 187853213);
        println!("Privateer's Hold dungeon LocationId: {}", layout.location_id);
        assert_eq!(layout.location_index, 179);
        assert_eq!(layout.dungeon_type, 2);
        let expect = [
            ("S0000999.RDB", 0i8, 0i8, true),
            ("B0000009.RDB", -1, 0, false),
            ("B0000006.RDB", 0, -1, false),
            ("B0000003.RDB", 1, 0, false),
            ("B0000012.RDB", 0, 1, false),
        ];
        assert_eq!(layout.blocks.len(), expect.len());
        for (got, (name, x, z, start)) in layout.blocks.iter().zip(expect) {
            assert_eq!(got.name, name);
            assert_eq!((got.x, got.z, got.is_start), (x, z, start));
        }
    }
}
