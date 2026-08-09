//! Read-only parsers for Elder Scrolls II: Daggerfall (Arena2) data files.
//!
//! Semantics are ported from Daggerfall Unity (MIT, dfworkshop.net):
//! BsaFile.cs, MapsFile.cs, BlocksFile.cs, Arch3dFile.cs, MeshReader.cs,
//! TextureFile.cs, DFPalette.cs, PakFile.cs, DungeonTextureTables.cs,
//! DFRandom.cs, EnemyBasics.cs, DaggerfallMobileUnit.cs.
//!
//! Conventions (matching DFU):
//! - GlobalScale = 0.025 (raw units -> meters)
//! - Mesh vertex coordinates are 1/256 sub-units (pointDivisor = 256)
//! - Mesh UVs are 1/16 sub-units of a texture pixel (textureDivisor = 16)
//! - Rotations are 1/2048-turn, negated (RotationDivisor = 5.688888...)
//! - Daggerfall space is Y-down; DFU emits Unity (left-handed, Y-up) via (x, -y, z)

pub mod arch3d;
pub mod bsa;
pub mod dfrandom;
pub mod maps;
pub mod mobile;
pub mod pak;
pub mod palette;
pub mod rdb;
pub mod texture;
pub mod texture_table;

/// MeshReader.GlobalScale — raw Daggerfall units to meters.
pub const GLOBAL_SCALE: f32 = 0.025;
/// Arch3dFile pointDivisor — ARCH3D mesh coords are 1/256 sub-units.
pub const POINT_DIVISOR: f32 = 256.0;
/// Arch3dFile textureDivisor — ARCH3D UV coords are 1/16 sub-units of a texel.
pub const TEXTURE_DIVISOR: f32 = 16.0;
/// BlocksFile.RotationDivisor — rotations are 1/2048 turn (2048 * 360/2048 = 360 deg).
pub const ROTATION_DIVISOR: f32 = 5.688_889;
/// BlocksFile.RDBDimension — raw units per dungeon block side.
pub const RDB_DIMENSION: f32 = 2048.0;
/// RDBLayout.RDBSide — meters per dungeon block side.
pub const RDB_SIDE: f32 = RDB_DIMENSION * GLOBAL_SCALE; // 51.2

/// Little-endian read cursor over a byte slice.
pub struct Cursor<'a> {
    data: &'a [u8],
    pub pos: usize,
}

impl<'a> Cursor<'a> {
    pub fn new(data: &'a [u8]) -> Self {
        Cursor { data, pos: 0 }
    }
    pub fn at(data: &'a [u8], pos: usize) -> Self {
        Cursor { data, pos }
    }
    pub fn seek(&mut self, pos: usize) {
        self.pos = pos;
    }
    pub fn skip(&mut self, n: usize) {
        self.pos += n;
    }
    pub fn u8(&mut self) -> u8 {
        let v = self.data[self.pos];
        self.pos += 1;
        v
    }
    pub fn i8(&mut self) -> i8 {
        self.u8() as i8
    }
    pub fn u16(&mut self) -> u16 {
        let v = u16::from_le_bytes([self.data[self.pos], self.data[self.pos + 1]]);
        self.pos += 2;
        v
    }
    pub fn i16(&mut self) -> i16 {
        self.u16() as i16
    }
    pub fn u32(&mut self) -> u32 {
        let v = u32::from_le_bytes([
            self.data[self.pos],
            self.data[self.pos + 1],
            self.data[self.pos + 2],
            self.data[self.pos + 3],
        ]);
        self.pos += 4;
        v
    }
    pub fn i32(&mut self) -> i32 {
        self.u32() as i32
    }
    pub fn u64(&mut self) -> u64 {
        let lo = self.u32() as u64;
        let hi = self.u32() as u64;
        (hi << 32) | lo
    }
    pub fn bytes(&mut self, n: usize) -> &'a [u8] {
        let s = &self.data[self.pos..self.pos + n];
        self.pos += n;
        s
    }
    /// Read a NUL-terminated string limited to `max` bytes (DFU FileProxy.ReadCString semantics).
    pub fn cstring(&mut self, max: usize) -> String {
        let start = self.pos;
        let mut end = start;
        let limit = (start + max).min(self.data.len());
        while end < limit && self.data[end] != 0 {
            end += 1;
        }
        let s = String::from_utf8_lossy(&self.data[start..end]).into_owned();
        self.pos = if end < limit { end + 1 } else { end };
        s
    }
}

/// Test data root: $ARENA2_DIR or <repo>/local/arena2 (gitignored classic data copy).
#[cfg(test)]
pub(crate) fn arena2_dir() -> std::path::PathBuf {
    std::env::var("ARENA2_DIR")
        .map(std::path::PathBuf::from)
        .unwrap_or_else(|_| {
            std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../local/arena2")
        })
}

/// True when classic Daggerfall Arena2 data is present locally. CI has no
/// data; data-dependent tests early-return (pass) when this is false and run
/// the full assertion suite when data exists.
#[cfg(test)]
pub(crate) fn have_arena2_data() -> bool {
    arena2_dir().join("BLOCKS.BSA").exists()
}
