//! BSA archive reader (DFU BsaFile.cs).
//!
//! Header: i16 count, u16 dir_type (0x0100 named / 0x0200 numeric).
//! Directory at EOF: named = 18B entries (14B cstring + i32 size),
//! numeric = 8B entries (u32 id + i32 size). Records contiguous from offset 4
//! in directory order.

use crate::{require_range, Cursor};
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
        require_range(&data, 0, 4, "BSA header")?;
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
                let directory_len = 18usize
                    .checked_mul(count)
                    .ok_or_else(|| "named BSA directory size overflow".to_string())?;
                let base = data
                    .len()
                    .checked_sub(directory_len)
                    .ok_or_else(|| "named BSA directory exceeds file".to_string())?;
                for i in 0..count {
                    let entry = base + i * 18;
                    require_range(&data, entry, 18, "named BSA directory entry")?;
                    let mut d = Cursor::at(&data, entry);
                    let name = d.cstring(14);
                    d.seek(entry + 14);
                    let size = d.i32();
                    if size < 0 {
                        return Err(format!("negative size for BSA record {name:?}"));
                    }
                    let size = size as usize;
                    records.push(Record {
                        name,
                        offset: pos,
                        size,
                    });
                    pos = pos
                        .checked_add(size)
                        .ok_or_else(|| "BSA record offsets overflow".to_string())?;
                }
                if pos != base {
                    return Err(format!(
                        "named BSA records end at {pos}, directory starts at {base}"
                    ));
                }
            }
            0x0200 => {
                let directory_len = 8usize
                    .checked_mul(count)
                    .ok_or_else(|| "numeric BSA directory size overflow".to_string())?;
                let base = data
                    .len()
                    .checked_sub(directory_len)
                    .ok_or_else(|| "numeric BSA directory exceeds file".to_string())?;
                for i in 0..count {
                    let entry = base + i * 8;
                    require_range(&data, entry, 8, "numeric BSA directory entry")?;
                    let mut d = Cursor::at(&data, entry);
                    let id = d.u32();
                    let size = d.i32();
                    if size < 0 {
                        return Err(format!("negative size for BSA record {id}"));
                    }
                    let size = size as usize;
                    records.push(Record {
                        name: id.to_string(),
                        offset: pos,
                        size,
                    });
                    pos = pos
                        .checked_add(size)
                        .ok_or_else(|| "BSA record offsets overflow".to_string())?;
                }
                if pos != base {
                    return Err(format!(
                        "numeric BSA records end at {pos}, directory starts at {base}"
                    ));
                }
            }
            other => return Err(format!("unknown BSA directory type {other:#x}")),
        }
        if pos > data.len() {
            return Err(format!(
                "records overrun file: end {pos} > len {}",
                data.len()
            ));
        }
        let by_name = records
            .iter()
            .enumerate()
            .map(|(i, r)| (r.name.clone(), i))
            .collect();
        Ok(BsaArchive {
            data,
            records,
            by_name,
        })
    }

    pub fn len(&self) -> usize {
        self.records.len()
    }

    pub fn is_empty(&self) -> bool {
        self.records.is_empty()
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
    use crate::test_fixtures::{named_bsa, numeric_bsa};

    #[test]
    fn named_records_decode_from_bounded_fixture() {
        let data = named_bsa(&[("ONE.DAT", b"one"), ("TWO.DAT", b"twice")]);
        let bsa = BsaArchive::parse(data).unwrap();
        assert_eq!(bsa.len(), 2);
        assert_eq!(bsa.names().collect::<Vec<_>>(), ["ONE.DAT", "TWO.DAT"]);
        assert_eq!(bsa.get("TWO.DAT"), Some(b"twice".as_slice()));
    }

    #[test]
    fn numeric_records_decode_and_malformed_archives_fail_closed() {
        let data = numeric_bsa(&[(42, b"mesh"), (61000, b"geometry")]);
        let bsa = BsaArchive::parse(data).unwrap();
        assert_eq!(bsa.get("42"), Some(b"mesh".as_slice()));
        assert_eq!(bsa.get("61000"), Some(b"geometry".as_slice()));

        assert!(BsaArchive::parse(vec![]).err().unwrap().contains("header"));
        let mut truncated = numeric_bsa(&[(42, b"mesh")]);
        truncated.pop();
        assert!(BsaArchive::parse(truncated).is_err());
        let mut negative_size = numeric_bsa(&[(42, b"mesh")]);
        let size_at = negative_size.len() - 4;
        negative_size[size_at..].copy_from_slice(&(-1i32).to_le_bytes());
        assert!(BsaArchive::parse(negative_size).is_err());
    }
}
