//! BSA archive reader (DFU BsaFile.cs).
//!
//! Header: i16 count, u16 dir_type (0x0100 named / 0x0200 numeric).
//! Directory at EOF: named = 18B entries (14B cstring + i32 size),
//! numeric = 8B entries (u32 id + i32 size). Records contiguous from offset 4
//! in directory order.

use crate::Cursor;
use std::collections::HashMap;
use std::path::Path;

pub struct BsaArchive {
    data: Vec<u8>,
    records: Vec<Record>,
    by_name: HashMap<String, usize>,
}

struct Record {
    name: String,
    offset: usize,
    size: usize,
}

impl BsaArchive {
    pub fn load(path: &Path) -> std::io::Result<Self> {
        let data = std::fs::read(path)?;
        Self::parse(data).map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))
    }

    pub fn parse(data: Vec<u8>) -> Result<Self, String> {
        let mut c = Cursor::new(&data);
        let count = c.i16() as i64;
        let dir_type = c.u16();
        if count < 0 {
            return Err("negative record count".into());
        }
        let count = count as usize;
        let mut records = Vec::with_capacity(count);
        let mut pos = 4usize;
        match dir_type {
            0x0100 => {
                let base = data.len() - 18 * count;
                for i in 0..count {
                    let mut d = Cursor::at(&data, base + i * 18);
                    let name = d.cstring(14);
                    d.seek(base + i * 18 + 14);
                    let size = d.i32() as usize;
                    records.push(Record { name, offset: pos, size });
                    pos += size;
                }
            }
            0x0200 => {
                let base = data.len() - 8 * count;
                for i in 0..count {
                    let mut d = Cursor::at(&data, base + i * 8);
                    let id = d.u32();
                    let size = d.i32() as usize;
                    records.push(Record { name: id.to_string(), offset: pos, size });
                    pos += size;
                }
            }
            other => return Err(format!("unknown BSA directory type {other:#x}")),
        }
        if pos > data.len() {
            return Err(format!("records overrun file: end {pos} > len {}", data.len()));
        }
        let by_name = records
            .iter()
            .enumerate()
            .map(|(i, r)| (r.name.clone(), i))
            .collect();
        Ok(BsaArchive { data, records, by_name })
    }

    pub fn len(&self) -> usize {
        self.records.len()
    }

    pub fn contains(&self, name: &str) -> bool {
        self.by_name.contains_key(name)
    }

    pub fn names(&self) -> impl Iterator<Item = &str> {
        self.records.iter().map(|r| r.name.as_str())
    }

    /// Get record bytes by name (case-sensitive, as stored).
    pub fn get(&self, name: &str) -> Option<&[u8]> {
        let i = *self.by_name.get(name)?;
        let r = &self.records[i];
        Some(&self.data[r.offset..r.offset + r.size])
    }
}



#[cfg(test)]
mod tests {
    use super::*;
    use crate::arena2_dir;



    #[test]
    fn blocks_bsa_record_counts() {
        let bsa = BsaArchive::load(&arena2_dir().join("BLOCKS.BSA")).unwrap();
        assert_eq!(bsa.len(), 1295);
        assert!(bsa.contains("S0000999.RDB"));
        assert_eq!(bsa.get("S0000999.RDB").unwrap().len(), 34490);
    }

    #[test]
    fn arch3d_numeric_records() {
        let bsa = BsaArchive::load(&arena2_dir().join("ARCH3D.BSA")).unwrap();
        assert_eq!(bsa.len(), 10251);
        assert!(bsa.contains("61000"));
    }
}
