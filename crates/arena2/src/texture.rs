//! TEXTURE.nnn archive parser (DFU TextureFile.cs).
//!
//! File header: i16 recordCount + name; record headers at offset 26, each 20B:
//!   i16 type1, i32 recordPosition, i16 type2, i32 unknown1, i64 null.
//! Record at recordPosition (26B header): i16 offsetX, i16 offsetY, i16 width,
//!   i16 height, u16 compression, u32 recordSize, u32 dataOffset, i16 isNormal,
//!   u16 frameCount, i16 unk, i16 scaleX, i16 scaleY.
//! Compression: 0x0000 uncompressed (rows padded to 256-byte stride for
//!   single-frame; multi-frame = offset table + per-frame RLE with transparent
//!   runs), 0x0108 ImageRle / 0x1108 RecordRle = row-header RLE.

use crate::Cursor;
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TextureRecordInfo {
    pub width: i16,
    pub height: i16,
    pub compression: u16,
    pub frame_count: u16,
}

pub struct TextureFile {
    data: Vec<u8>,
    records: Vec<RecordEntry>,
    solid: Option<SolidType>,
}

struct RecordEntry {
    position: usize,
    info: TextureRecordInfo,
    data_offset: usize,
}

pub const COMPRESSION_UNCOMPRESSED: u16 = 0x0000;
pub const COMPRESSION_IMAGE_RLE: u16 = 0x0108;
pub const COMPRESSION_RECORD_RLE: u16 = 0x1108;
/// DFU TextureFile.solidSize
pub const SOLID_SIZE: i16 = 32;

/// DFU TextureFile.SolidTypes — TEXTURE.000/.001 are virtual solid-colour archives.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SolidType {
    /// Palette index = record index
    ColoursA,
    /// Palette index = 128 + record index
    ColoursB,
}

impl TextureFile {
    pub fn load(path: &Path) -> std::io::Result<Self> {
        let data = std::fs::read(path)?;
        let solid = match path.file_name().and_then(|n| n.to_str()) {
            // DFU TextureFile.Load: TEXTURE.000/.001 are virtual solid-colour archives
            Some("TEXTURE.000") => Some(SolidType::ColoursA),
            Some("TEXTURE.001") => Some(SolidType::ColoursB),
            _ => None,
        };
        Self::parse(data, solid)
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))
    }

    pub fn parse(data: Vec<u8>, solid: Option<SolidType>) -> Result<Self, String> {
        if data.len() < 26 {
            return Err("texture file too small".into());
        }
        let record_count = Cursor::new(&data).i16() as usize;
        if let Some(solid) = solid {
            // Virtual records: 32x32, filled with palette index (record or 128+record)
            let records = (0..record_count.max(1))
                .map(|_| RecordEntry {
                    position: 0,
                    info: TextureRecordInfo {
                        width: SOLID_SIZE,
                        height: SOLID_SIZE,
                        compression: 0,
                        frame_count: 1,
                    },
                    data_offset: 0,
                })
                .collect();
            return Ok(TextureFile {
                data,
                records,
                solid: Some(solid),
            });
        }
        let mut records = Vec::with_capacity(record_count);
        for r in 0..record_count {
            let mut h = Cursor::at(&data, 26 + r * 20);
            let _type1 = h.i16();
            let record_position = h.i32() as usize;
            // Record header
            let mut c = Cursor::at(&data, record_position);
            let _offset_x = c.i16();
            let _offset_y = c.i16();
            let width = c.i16();
            let height = c.i16();
            let compression = c.u16();
            let _record_size = c.u32();
            let data_offset = c.u32() as usize;
            let _is_normal = c.i16();
            let frame_count = c.u16();
            records.push(RecordEntry {
                position: record_position,
                info: TextureRecordInfo {
                    width,
                    height,
                    compression,
                    frame_count,
                },
                data_offset,
            });
        }
        Ok(TextureFile {
            data,
            records,
            solid: None,
        })
    }

    pub fn len(&self) -> usize {
        self.records.len()
    }

    pub fn record_info(&self, record: usize) -> Option<TextureRecordInfo> {
        self.records.get(record).map(|r| r.info)
    }

    /// Decode one frame of a record to indexed pixels (row-major, width*height).
    pub fn frame_pixels(
        &self,
        record: usize,
        frame: usize,
    ) -> Result<(usize, usize, Vec<u8>), String> {
        let r = self
            .records
            .get(record)
            .ok_or_else(|| format!("record {record} out of range"))?;
        let (w, h) = (r.info.width as usize, r.info.height as usize);
        if w == 0 || h == 0 || w > 4096 || h > 4096 {
            return Err(format!(
                "bad texture dims {}x{}",
                r.info.width, r.info.height
            ));
        }
        // DFU TextureFile.ReadSolid: fill with palette index
        if let Some(solid) = self.solid {
            let index = match solid {
                SolidType::ColoursA => record as u8,
                SolidType::ColoursB => 128u8.wrapping_add(record as u8),
            };
            return Ok((w, h, vec![index; w * h]));
        }
        let data = match r.info.compression {
            COMPRESSION_IMAGE_RLE | COMPRESSION_RECORD_RLE => self.read_rle(r, frame)?,
            _ => self.read_image(r, frame)?,
        };
        Ok((w, h, data))
    }

    /// Uncompressed path (DFU TextureFile.ReadImage).
    fn read_image(&self, r: &RecordEntry, frame: usize) -> Result<Vec<u8>, String> {
        let (w, h) = (r.info.width as usize, r.info.height as usize);
        let mut data = vec![0u8; w * h];
        let position = r.position + r.data_offset;
        if r.info.frame_count == 1 {
            // Rows padded to 256-byte stride
            let mut src = position;
            for y in 0..h {
                if src + w > self.data.len() {
                    return Err("image data out of bounds".into());
                }
                data[y * w..(y + 1) * w].copy_from_slice(&self.data[src..src + w]);
                src += 256; // width + (256 - width)
            }
        } else if r.info.frame_count > 1 {
            if frame >= r.info.frame_count as usize {
                return Err(format!("frame {frame} out of range"));
            }
            let frame_off = Cursor::at(&self.data, position + frame * 4).i32() as usize;
            let mut c = Cursor::at(&self.data, position + frame_off);
            let cx = c.i16() as usize;
            let cy = c.i16() as usize;
            let mut dst = 0usize;
            for _y in 0..cy {
                let mut x = 0usize;
                while x < cx {
                    // Transparent run
                    let run = c.u8() as usize;
                    x += run;
                    dst += run;
                    // Opaque run
                    let run = c.u8() as usize;
                    for _ in 0..run {
                        data[dst] = c.u8();
                        dst += 1;
                    }
                    x += run;
                }
            }
        } else {
            return Err("record has no frames".into());
        }
        Ok(data)
    }

    /// RecordRle/ImageRle path (DFU TextureFile.ReadRle).
    fn read_rle(&self, r: &RecordEntry, frame: usize) -> Result<Vec<u8>, String> {
        let (w, h) = (r.info.width as usize, r.info.height as usize);
        let mut data = vec![0u8; w * h];
        let row_table = r.position + r.data_offset + (h * frame) * 4;
        let mut dst = 0usize;
        for row in 0..h {
            let mut rh = Cursor::at(&self.data, row_table + row * 4);
            let row_offset = rh.i16() as usize;
            let row_encoding = rh.u16();
            let mut c = Cursor::at(&self.data, r.position + row_offset);
            if row_encoding == 0x8000 {
                let row_width = c.u16() as usize;
                let mut row_pos = 0usize;
                while row_pos < row_width {
                    let probe = c.i16();
                    if probe < 0 {
                        let count = (-probe) as usize;
                        let pixel = c.u8();
                        for _ in 0..count {
                            data[dst] = pixel;
                            dst += 1;
                        }
                        row_pos += count;
                    } else if probe > 0 {
                        let count = probe as usize;
                        for _ in 0..count {
                            data[dst] = c.u8();
                            dst += 1;
                        }
                        row_pos += count;
                    } else {
                        break;
                    }
                }
            } else {
                for _ in 0..w {
                    data[dst] = c.u8();
                    dst += 1;
                }
            }
        }
        Ok(data)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::arena2_dir;

    #[test]
    fn texture_120_decodes() {
        let dir = arena2_dir();
        let tex = TextureFile::load(&dir.join("TEXTURE.120")).unwrap();
        assert!(tex.len() > 0);
        let info = tex.record_info(0).unwrap();
        let (w, h, pixels) = tex.frame_pixels(0, 0).unwrap();
        assert_eq!((w, h), (info.width as usize, info.height as usize));
        assert_eq!(pixels.len(), w * h);
        // A real wall texture should not be a single flat color
        let first = pixels[0];
        assert!(
            pixels.iter().any(|&p| p != first),
            "texture decoded as flat color"
        );
    }
}
