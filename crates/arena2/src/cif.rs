//! CIF/RCI image parsing, initially bounded to classic WEAPON*.CIF records.
//!
//! Donor semantics: DFU `CifRciFile.cs`. Weapon files contain one standard
//! IMG record followed by animation records with 31 frame offsets and
//! PackBits-like RLE payloads. This module is read-only and retains source
//! dimensions/offsets; publication and semantic action mapping belong to
//! `dagger-import`.

use std::path::Path;

use crate::{require_range, Cursor};

const IMG_HEADER_LEN: usize = 12;
const WEAPON_ANIM_HEADER_LEN: usize = 76;
const COMPRESSION_UNCOMPRESSED: u16 = 0;
const COMPRESSION_RLE: u16 = 2;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CifRecordInfo {
    pub width: u16,
    pub height: u16,
    pub x_offset: i16,
    pub y_offset: i16,
    pub frame_count: u16,
}

#[derive(Debug)]
enum Record {
    Image {
        info: CifRecordInfo,
        compression: u16,
        data_position: usize,
        data_length: usize,
    },
    WeaponAnimation {
        info: CifRecordInfo,
        record_position: usize,
        total_size: usize,
        frame_offsets: Vec<usize>,
    },
}

/// Parsed classic first-person weapon CIF.
pub struct WeaponCif {
    data: Vec<u8>,
    records: Vec<Record>,
}

impl WeaponCif {
    pub fn load(path: &Path) -> std::io::Result<Self> {
        let data = std::fs::read(path)?;
        Self::parse(data)
            .map_err(|error| std::io::Error::new(std::io::ErrorKind::InvalidData, error))
    }

    pub fn parse(data: Vec<u8>) -> Result<Self, String> {
        require_range(&data, 0, IMG_HEADER_LEN, "weapon wield image header")?;
        let mut first = Cursor::new(&data);
        let x_offset = first.i16();
        let y_offset = first.i16();
        let width = positive_dimension(first.i16(), "wield image width")?;
        let height = positive_dimension(first.i16(), "wield image height")?;
        let compression = first.u16();
        if !matches!(compression, COMPRESSION_UNCOMPRESSED | COMPRESSION_RLE) {
            return Err(format!(
                "unsupported wield image compression {compression:#06x}"
            ));
        }
        let data_length = first.u16() as usize;
        require_range(
            &data,
            IMG_HEADER_LEN,
            data_length,
            "weapon wield image data",
        )?;
        let mut records = vec![Record::Image {
            info: CifRecordInfo {
                width,
                height,
                x_offset,
                y_offset,
                frame_count: 1,
            },
            compression,
            data_position: IMG_HEADER_LEN,
            data_length,
        }];

        let mut position = IMG_HEADER_LEN + data_length;
        while position < data.len() {
            require_range(
                &data,
                position,
                WEAPON_ANIM_HEADER_LEN,
                "weapon animation header",
            )?;
            let mut header = Cursor::at(&data, position);
            let width = header.u16();
            let height = header.u16();
            if width == 0 || height == 0 {
                return Err(format!("zero-sized weapon animation at {position}"));
            }
            let _last_frame_width = header.u16();
            let x_offset = header.i16();
            let last_frame_y_offset = header.i16();
            let _data_length = header.i16();
            let mut frame_offsets = Vec::new();
            for _ in 0..31 {
                let offset = header.u16() as usize;
                if offset != 0 {
                    frame_offsets.push(offset);
                }
            }
            let total_size = header.u16() as usize;
            if frame_offsets.is_empty() {
                return Err(format!("weapon animation at {position} has no frames"));
            }
            if total_size < WEAPON_ANIM_HEADER_LEN {
                return Err(format!(
                    "weapon animation at {position} has invalid total size {total_size}"
                ));
            }
            require_range(&data, position, total_size, "weapon animation record")?;
            for offset in &frame_offsets {
                if *offset < WEAPON_ANIM_HEADER_LEN || *offset >= total_size {
                    return Err(format!(
                        "weapon animation frame offset {offset} outside record size {total_size}"
                    ));
                }
            }
            records.push(Record::WeaponAnimation {
                info: CifRecordInfo {
                    width,
                    height,
                    x_offset,
                    y_offset: last_frame_y_offset,
                    frame_count: frame_offsets.len() as u16,
                },
                record_position: position,
                total_size,
                frame_offsets,
            });
            position = position
                .checked_add(total_size)
                .ok_or_else(|| "weapon CIF record position overflow".to_string())?;
        }
        if position != data.len() {
            return Err("weapon CIF records do not end at file boundary".to_string());
        }
        Ok(Self { data, records })
    }

    pub fn len(&self) -> usize {
        self.records.len()
    }

    pub fn is_empty(&self) -> bool {
        self.records.is_empty()
    }

    pub fn record_info(&self, record: usize) -> Option<CifRecordInfo> {
        match self.records.get(record)? {
            Record::Image { info, .. } | Record::WeaponAnimation { info, .. } => Some(*info),
        }
    }

    pub fn frame_pixels(&self, record: usize, frame: usize) -> Result<Vec<u8>, String> {
        let record = self
            .records
            .get(record)
            .ok_or_else(|| format!("weapon CIF record {record} out of range"))?;
        match record {
            Record::Image {
                info,
                compression,
                data_position,
                data_length,
            } => {
                if frame != 0 {
                    return Err(format!("wield image frame {frame} out of range"));
                }
                let expected = info.width as usize * info.height as usize;
                let bytes = require_range(
                    &self.data,
                    *data_position,
                    *data_length,
                    "weapon wield pixels",
                )?;
                match *compression {
                    COMPRESSION_UNCOMPRESSED if bytes.len() == expected => Ok(bytes.to_vec()),
                    COMPRESSION_UNCOMPRESSED => Err(format!(
                        "wield image has {} bytes, expected {expected}",
                        bytes.len()
                    )),
                    COMPRESSION_RLE => decode_rle(bytes, expected),
                    other => Err(format!("unsupported wield image compression {other:#06x}")),
                }
            }
            Record::WeaponAnimation {
                info,
                record_position,
                total_size,
                frame_offsets,
            } => {
                let offset = *frame_offsets
                    .get(frame)
                    .ok_or_else(|| format!("weapon animation frame {frame} out of range"))?;
                let start = record_position
                    .checked_add(offset)
                    .ok_or_else(|| "weapon animation frame position overflow".to_string())?;
                let end = frame_offsets
                    .get(frame + 1)
                    .and_then(|next| record_position.checked_add(*next))
                    .or_else(|| record_position.checked_add(*total_size))
                    .ok_or_else(|| "weapon animation frame end overflow".to_string())?;
                // RLE decoding is output-bounded, so the remaining record/file
                // bytes are a safe input window even for the final frame.
                let bytes = self
                    .data
                    .get(start..end.max(start).min(self.data.len()))
                    .ok_or_else(|| "weapon animation frame bytes out of range".to_string())?;
                decode_rle(bytes, info.width as usize * info.height as usize)
            }
        }
    }
}

fn positive_dimension(value: i16, label: &str) -> Result<u16, String> {
    if value <= 0 {
        Err(format!("{label} must be positive, got {value}"))
    } else {
        Ok(value as u16)
    }
}

fn decode_rle(bytes: &[u8], expected: usize) -> Result<Vec<u8>, String> {
    let mut output = Vec::with_capacity(expected);
    let mut position = 0usize;
    while output.len() < expected {
        let code = *bytes
            .get(position)
            .ok_or_else(|| "weapon RLE ended before output was complete".to_string())?;
        position += 1;
        if code > 127 {
            let pixel = *bytes
                .get(position)
                .ok_or_else(|| "weapon RLE repeat is missing its pixel".to_string())?;
            position += 1;
            let count = usize::from(code - 127);
            if output.len() + count > expected {
                return Err("weapon RLE repeat exceeds frame bounds".to_string());
            }
            output.extend(std::iter::repeat_n(pixel, count));
        } else {
            let count = usize::from(code) + 1;
            let literal = require_range(bytes, position, count, "weapon RLE literal")?;
            if output.len() + count > expected {
                return Err("weapon RLE literal exceeds frame bounds".to_string());
            }
            output.extend_from_slice(literal);
            position += count;
        }
    }
    Ok(output)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn arena2_file(name: &str) -> std::path::PathBuf {
        std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../local/arena2")
            .join(name)
    }

    #[test]
    fn parses_wield_image_and_weapon_animation_frames() {
        let mut bytes = Vec::new();
        bytes.extend_from_slice(&0i16.to_le_bytes());
        bytes.extend_from_slice(&0i16.to_le_bytes());
        bytes.extend_from_slice(&2i16.to_le_bytes());
        bytes.extend_from_slice(&2i16.to_le_bytes());
        bytes.extend_from_slice(&COMPRESSION_UNCOMPRESSED.to_le_bytes());
        bytes.extend_from_slice(&4u16.to_le_bytes());
        bytes.extend_from_slice(&[1, 2, 3, 4]);

        let record_start = bytes.len();
        bytes.extend_from_slice(&2u16.to_le_bytes());
        bytes.extend_from_slice(&2u16.to_le_bytes());
        bytes.extend_from_slice(&2u16.to_le_bytes());
        bytes.extend_from_slice(&0i16.to_le_bytes());
        bytes.extend_from_slice(&0i16.to_le_bytes());
        bytes.extend_from_slice(&0i16.to_le_bytes());
        bytes.extend_from_slice(&76u16.to_le_bytes());
        bytes.extend_from_slice(&81u16.to_le_bytes());
        for _ in 2..31 {
            bytes.extend_from_slice(&0u16.to_le_bytes());
        }
        bytes.extend_from_slice(&86u16.to_le_bytes());
        bytes.extend_from_slice(&[3, 5, 6, 7, 8]);
        bytes.extend_from_slice(&[3, 9, 10, 11, 12]);
        assert_eq!(bytes.len(), record_start + 86);

        let cif = WeaponCif::parse(bytes).unwrap();
        assert_eq!(cif.len(), 2);
        assert_eq!(cif.record_info(1).unwrap().frame_count, 2);
        assert_eq!(cif.frame_pixels(0, 0).unwrap(), [1, 2, 3, 4]);
        assert_eq!(cif.frame_pixels(1, 0).unwrap(), [5, 6, 7, 8]);
        assert_eq!(cif.frame_pixels(1, 1).unwrap(), [9, 10, 11, 12]);
    }

    #[test]
    fn malformed_weapon_records_fail_closed() {
        assert!(WeaponCif::parse(vec![0; 11]).is_err());
        let mut bad = vec![0; 12];
        bad[4..6].copy_from_slice(&2i16.to_le_bytes());
        bad[6..8].copy_from_slice(&2i16.to_le_bytes());
        bad[10..12].copy_from_slice(&4u16.to_le_bytes());
        assert!(WeaponCif::parse(bad).is_err());
    }

    #[test]
    fn configured_classic_dagger_exercises_real_weapon_records() {
        let path = arena2_file("WEAPON02.CIF");
        if !path.exists() {
            eprintln!(
                "skipping real Arena2 CIF check: {} is absent",
                path.display()
            );
            return;
        }
        let cif = WeaponCif::load(&path).expect("parse real WEAPON02.CIF");
        assert_eq!(cif.len(), 7);
        assert_eq!(cif.record_info(0).unwrap().frame_count, 1);
        for record in 1..7 {
            assert_eq!(cif.record_info(record).unwrap().frame_count, 5);
            for frame in 0..5 {
                let info = cif.record_info(record).unwrap();
                assert_eq!(
                    cif.frame_pixels(record, frame).unwrap().len(),
                    info.width as usize * info.height as usize
                );
            }
        }
    }
}
