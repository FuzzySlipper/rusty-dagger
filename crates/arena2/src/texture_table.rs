//! DungeonTextureTables.cs port: per-location dungeon texture tables.
//!
//! Classic Daggerfall remaps the dungeon texture archives {119, 120, 122,
//! 123, 124, 168} through a 6-slot table. Main-story dungeons use a
//! per-location randomized table seeded by the dungeon's LocationId via
//! DFRandom; other archives (notably 74, the door texture) pass through with
//! a climate offset.

use crate::dfrandom::DFRandom;

/// DFU DungeonTextureTables.DefaultTextureTable (classic data at linear
/// offset 0x28617C) — the identity table.
pub const DEFAULT_TEXTURE_TABLE: [u16; 6] = [119, 120, 122, 123, 124, 168];

// DFU MapsFile.Climates values the classic algorithm indexes from.
const CLIMATE_OCEAN: u8 = 223;
const CLIMATE_DESERT: u8 = 224;
const CLIMATE_SWAMP: u8 = 228;
const CLIMATE_HAUNTED_WOODLANDS: u8 = 232;

/// DFU DungeonTextureTables.RandomTextureTableClassic(seed,
/// randomDungeonTextures: 0) — the classic algorithm used for main-story
/// dungeons. `world_climate` is the CLIMATE.PAK value at the location's map
/// pixel (MapsFile.Climates, 223..=232). Returns a typed error for climate
/// values outside the classic range rather than panicking on the DFU index
/// arithmetic — invalid climate authority must not silently produce a table.
pub fn random_texture_table_classic(seed: u32, world_climate: u8) -> Result<[u16; 6], String> {
    const CLIMATE_TEXTURE_ARCHIVE_INDICES: [u8; 10] = [0, 0, 1, 4, 4, 0, 3, 3, 3, 0];
    // Values from classic, used in the classic algorithm.
    const CLIMATE_TEXTURE_ARCHIVES: [u16; 5] = [19, 119, 319, 419, 119];
    // DFU TravelTimeCalculator.climateIndices.
    const CLIMATE_INDICES: [u8; 10] = [0, 0, 0, 1, 2, 3, 4, 5, 5, 5];

    if !(CLIMATE_OCEAN..=CLIMATE_HAUNTED_WOODLANDS).contains(&world_climate) {
        return Err(format!(
            "world climate {world_climate} outside classic range {CLIMATE_OCEAN}..={CLIMATE_HAUNTED_WOODLANDS}"
        ));
    }

    let mut climate = world_climate;
    if climate == CLIMATE_OCEAN {
        climate = CLIMATE_SWAMP;
    }
    let classic_index_value = CLIMATE_INDICES[(climate - CLIMATE_OCEAN) as usize] as usize;
    let climate_texture_archive_index =
        CLIMATE_TEXTURE_ARCHIVE_INDICES[classic_index_value] as usize;
    let climate_based_index_value = (climate - CLIMATE_DESERT) as usize;

    // DFU note: classic skips this loop when climateTextureArchiveIndex == 1
    // (rainforest only), leaving the previous/default table — a classic bug
    // DFU deliberately does not reproduce. We match DFU: always assign.
    let mut rng = DFRandom::new(seed);
    let mut table = [0u16; 6];
    for slot in table.iter_mut().take(5) {
        let mut offset = rng.random_range_inclusive(0, 4);
        if offset == 2 {
            offset = 4; // archive offset 2 is invalid in classic
        }
        *slot = CLIMATE_TEXTURE_ARCHIVES[climate_texture_archive_index] + offset as u16;
    }
    // DFLocation.ClimateTextureSet.Interior_Sewer (68) + 100 * climate set.
    table[5] = 68 + 100 * CLIMATE_TEXTURE_ARCHIVE_INDICES[climate_based_index_value] as u16;
    Ok(table)
}

/// DFU DungeonTextureTables.ApplyTextureTable: remap a base texture archive
/// through the table; archive 74 (doors) takes the climate base offset;
/// anything else passes through.
pub fn apply_texture_table(archive: u16, table: &[u16; 6], climate_base: u16) -> u16 {
    match archive {
        74 => archive + climate_base,
        119 => table[0],
        120 => table[1],
        122 => table[2],
        123 => table[3],
        124 => table[4],
        168 => table[5],
        a => a,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn privateers_hold_classic_table() {
        // Seed 50050 = Privateer's Hold dungeon LocationId (maps.rs test pins
        // this from MAPS.BSA); climate 231 = CLIMATE.PAK value at its map
        // pixel (pak.rs test pins 223..=232; 231 printed from real data).
        // Expected values cross-checked against an independent implementation
        // of DFU RandomTextureTableClassic.
        let table = random_texture_table_classic(50050, 231).unwrap();
        assert_eq!(table, [23, 22, 19, 22, 20, 368]);
        assert_ne!(table, DEFAULT_TEXTURE_TABLE);
    }

    #[test]
    fn out_of_range_climate_rejects_without_panic() {
        // Climate authority outside the classic 223..=232 range must produce
        // a typed error, not a panic on the DFU index arithmetic.
        for wc in [0u8, 100, 222, 233, 255] {
            let err = random_texture_table_classic(50050, wc).unwrap_err();
            assert!(err.contains("outside classic range"), "climate {wc}: {err}");
        }
        // Boundary values remain valid.
        assert!(random_texture_table_classic(50050, 223).is_ok());
        assert!(random_texture_table_classic(50050, 232).is_ok());
    }

    #[test]
    fn apply_maps_base_archives_through_table() {
        let table = [23, 22, 19, 22, 20, 368];
        assert_eq!(apply_texture_table(119, &table, 300), 23);
        assert_eq!(apply_texture_table(168, &table, 300), 368);
        // Doors: 74 + climate base (Temperate=300 -> TEXTURE.374).
        assert_eq!(apply_texture_table(74, &table, 300), 374);
        // Unlisted archives pass through.
        assert_eq!(apply_texture_table(199, &table, 300), 199);
    }
}
