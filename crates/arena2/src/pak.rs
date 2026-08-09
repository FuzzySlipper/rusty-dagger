//! CLIMATE.PAK / POLITIC.PAK reader (DFU PakFile.cs).
//!
//! 500 x u32 row offsets at file start; each row is RLE runs of
//! (u16 count, u8 value) filling 1000 pixels. World is 1000x500 map pixels.

use crate::{require_range, Cursor};
use std::path::Path;

pub const PAK_WIDTH: usize = 1001; // DFU PakFile.pakWidthValue (1000 pixels + 1 sentinel)
pub const PAK_HEIGHT: usize = 500;

pub struct PakFile {
    buffer: Vec<u8>,
}

impl PakFile {
    pub fn load(path: &Path) -> std::io::Result<Self> {
        let data = std::fs::read(path)?;
        Self::parse(&data).map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))
    }

    pub fn parse(data: &[u8]) -> Result<Self, String> {
        if data.len() < PAK_HEIGHT * 4 {
            return Err("PAK file too small".into());
        }
        let mut buffer = vec![0u8; PAK_WIDTH * PAK_HEIGHT];
        for row in 0..PAK_HEIGHT {
            let offset = Cursor::at(data, row * 4).u32() as usize;
            require_range(data, offset, 3, &format!("PAK row {row} first run"))?;
            let mut c = Cursor::at(data, offset);
            let mut row_pos = 0usize;
            while row_pos < PAK_WIDTH {
                require_range(data, c.pos, 3, &format!("PAK row {row} run"))?;
                let count = c.u16() as usize;
                let value = c.u8();
                if count == 0 {
                    return Err(format!("PAK row {row} contains an empty run"));
                }
                for _ in 0..count {
                    if row_pos >= PAK_WIDTH {
                        return Err(format!("PAK row {row} overrun"));
                    }
                    buffer[PAK_WIDTH * row + row_pos] = value;
                    row_pos += 1;
                }
            }
        }
        Ok(PakFile { buffer })
    }

    pub fn get(&self, x: i32, y: i32) -> Option<u8> {
        if x < 0 || y < 0 || x >= PAK_WIDTH as i32 || y >= PAK_HEIGHT as i32 {
            return None;
        }
        Some(self.buffer[y as usize * PAK_WIDTH + x as usize])
    }
}

/// DFU MapsFile.Climates values stored in CLIMATE.PAK.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WorldClimate {
    Ocean = 223,
    Desert = 224,
    Desert2 = 225,
    Mountain = 226,
    Rainforest = 227,
    Swamp = 228,
    Subtropical = 229,
    MountainWoods = 230,
    Woodlands = 231,
    HauntedWoodlands = 232,
}

/// DFLocation.ClimateBaseType as texture archive offset.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClimateBaseType {
    Desert = 0,
    Mountain = 100,
    Temperate = 300,
    Swamp = 400,
}

/// DFU MapsFile.GetWorldClimateSettings -> ClimateBaseType mapping.
pub fn climate_base_type(world_climate: u8) -> ClimateBaseType {
    match world_climate {
        223 | 227 | 228 => ClimateBaseType::Swamp, // Ocean, Rainforest, Swamp
        224 | 225 | 229 => ClimateBaseType::Desert, // Desert, Desert2, Subtropical
        226 => ClimateBaseType::Mountain,          // Mountain
        _ => ClimateBaseType::Temperate,           // Woods variants + default
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_fixtures::constant_pak;

    #[test]
    fn bounded_rows_decode_and_malformed_runs_fail_closed() {
        let bytes = constant_pak(WorldClimate::Woodlands as u8);
        let pak = PakFile::parse(&bytes).unwrap();
        assert_eq!(pak.get(0, 0), Some(WorldClimate::Woodlands as u8));
        assert_eq!(pak.get(1000, 499), Some(WorldClimate::Woodlands as u8));
        assert_eq!(pak.get(1001, 0), None);

        assert!(PakFile::parse(&bytes[..PAK_HEIGHT * 4 - 1]).is_err());
        let mut zero_run = bytes.clone();
        let first_run = PAK_HEIGHT * 4;
        zero_run[first_run..first_run + 2].copy_from_slice(&0u16.to_le_bytes());
        assert!(PakFile::parse(&zero_run).is_err());
        let mut bad_offset = bytes;
        bad_offset[..4].copy_from_slice(&u32::MAX.to_le_bytes());
        assert!(PakFile::parse(&bad_offset).is_err());
    }
}
