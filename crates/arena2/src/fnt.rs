//! Reader for classic fixed-grid FNT glyph tables.

use crate::require_range;

pub const GLYPH_COUNT: usize = 240;
pub const GLYPH_BYTES: usize = 32;
const HEADER_BYTES: usize = 4;
const TABLE_BYTES: usize = GLYPH_COUNT * 4;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FontGlyph {
    pub data_offset: u16,
    pub width: u16,
    /// Monochrome pixels in row-major 16x16 order. True is a set glyph bit.
    pub pixels: [bool; 16 * 16],
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Font {
    pub fixed_width: u16,
    pub fixed_height: u16,
    pub glyphs: Vec<FontGlyph>,
}

impl Font {
    pub fn load(path: &std::path::Path) -> Result<Self, String> {
        let bytes =
            std::fs::read(path).map_err(|error| format!("read {}: {error}", path.display()))?;
        Self::parse(&bytes)
    }

    pub fn parse(bytes: &[u8]) -> Result<Self, String> {
        let header = require_range(bytes, 0, HEADER_BYTES, "FNT header")?;
        let fixed_width = u16::from_le_bytes(header[0..2].try_into().expect("fixed FNT header"));
        let fixed_height = u16::from_le_bytes(header[2..4].try_into().expect("fixed FNT header"));
        if fixed_width == 0 || fixed_height == 0 || fixed_width > 16 || fixed_height > 16 {
            return Err(format!(
                "unsupported FNT fixed metrics {fixed_width}x{fixed_height}"
            ));
        }
        let table = require_range(bytes, HEADER_BYTES, TABLE_BYTES, "FNT glyph table")?;
        let mut glyphs = Vec::with_capacity(GLYPH_COUNT);
        for index in 0..GLYPH_COUNT {
            let at = index * 4;
            let data_offset =
                u16::from_le_bytes(table[at..at + 2].try_into().expect("fixed glyph table"));
            let width =
                u16::from_le_bytes(table[at + 2..at + 4].try_into().expect("fixed glyph table"));
            if width > 16 {
                return Err(format!("FNT glyph {index} width {width} exceeds 16 pixels"));
            }
            let source = require_range(
                bytes,
                usize::from(data_offset),
                GLYPH_BYTES,
                &format!("FNT glyph {index}"),
            )?;
            let mut pixels = [false; 16 * 16];
            for row in 0..16 {
                // DFU FntFile: the second byte encodes x=0..7 and the first
                // byte x=8..15. Keep the exact source orientation here.
                let left = source[row * 2 + 1];
                let right = source[row * 2];
                for bit in 0..8 {
                    // GetPixels writes bits in reverse x order: bit zero is
                    // the rightmost pixel of each eight-pixel source byte.
                    pixels[row * 16 + (7 - bit)] = left & (1 << bit) != 0;
                    pixels[row * 16 + 8 + (7 - bit)] = right & (1 << bit) != 0;
                }
            }
            glyphs.push(FontGlyph {
                data_offset,
                width,
                pixels,
            });
        }
        Ok(Self {
            fixed_width,
            fixed_height,
            glyphs,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn font_bytes() -> Vec<u8> {
        let start = (HEADER_BYTES + TABLE_BYTES) as u16;
        let mut bytes = vec![0; usize::from(start) + GLYPH_COUNT * GLYPH_BYTES];
        bytes[0..2].copy_from_slice(&14_u16.to_le_bytes());
        bytes[2..4].copy_from_slice(&11_u16.to_le_bytes());
        for index in 0..GLYPH_COUNT {
            let entry = HEADER_BYTES + index * 4;
            let offset = usize::from(start) + index * GLYPH_BYTES;
            bytes[entry..entry + 2].copy_from_slice(&(offset as u16).to_le_bytes());
            bytes[entry + 2..entry + 4].copy_from_slice(&14_u16.to_le_bytes());
        }
        bytes[usize::from(start)] = 0b0000_0001; // right half, x=15
        bytes[usize::from(start) + 1] = 0b0000_0001; // left half, x=7
        bytes
    }

    #[test]
    fn decodes_donor_bit_orientation_and_metrics() {
        let font = Font::parse(&font_bytes()).unwrap();
        assert_eq!((font.fixed_width, font.fixed_height), (14, 11));
        assert_eq!(font.glyphs.len(), GLYPH_COUNT);
        assert!(font.glyphs[0].pixels[7]);
        assert!(font.glyphs[0].pixels[15]);
        assert_eq!(font.glyphs[0].data_offset, 964);
    }

    #[test]
    fn truncated_or_invalid_font_fails_closed() {
        let bytes = font_bytes();
        assert!(Font::parse(&bytes[..963]).is_err());
        let mut invalid = bytes;
        invalid[4 + 2..4 + 4].copy_from_slice(&17_u16.to_le_bytes());
        assert!(Font::parse(&invalid).unwrap_err().contains("exceeds"));
    }
}
