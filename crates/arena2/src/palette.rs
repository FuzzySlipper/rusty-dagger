//! DFPalette: 256-colour palette from PAL.PAL (776 bytes: 8-byte header + 768 RGB).

use std::path::Path;

pub struct Palette {
    /// 256 x [r, g, b], full 8-bit channels.
    pub colors: [[u8; 3]; 256],
}

impl Palette {
    pub fn load(path: &Path) -> std::io::Result<Self> {
        let data = std::fs::read(path)?;
        Self::parse(&data).map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))
    }

    pub fn parse(data: &[u8]) -> Result<Self, String> {
        let rgb: &[u8] = match data.len() {
            768 => data,
            776 => &data[8..],
            other => return Err(format!("unexpected palette size {other}")),
        };
        let mut colors = [[0u8; 3]; 256];
        for i in 0..256 {
            colors[i] = [rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]];
        }
        Ok(Palette { colors })
    }

    /// Convert indexed pixels to RGBA (alpha 255; index 0 stays opaque here —
    /// billboards handle transparency separately).
    pub fn to_rgba(&self, indexed: &[u8]) -> Vec<u8> {
        let mut out = Vec::with_capacity(indexed.len() * 4);
        for &i in indexed {
            let [r, g, b] = self.colors[i as usize];
            out.extend_from_slice(&[r, g, b, 255]);
        }
        out
    }

    /// Convert indexed pixels to RGBA with palette index 0 made fully
    /// transparent. This is the Daggerfall billboard rule (DFU TextureFile
    /// writes 0 for transparent runs; billboards render with alpha cutout).
    pub fn to_rgba_transparent(&self, indexed: &[u8]) -> Vec<u8> {
        let mut out = Vec::with_capacity(indexed.len() * 4);
        for &i in indexed {
            let [r, g, b] = self.colors[i as usize];
            let a = if i == 0 { 0 } else { 255 };
            out.extend_from_slice(&[r, g, b, a]);
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bounded_palette_decodes_and_bad_sizes_fail_closed() {
        let mut bytes = vec![0u8; 768];
        for (index, byte) in bytes.iter_mut().enumerate() {
            *byte = (index % 256) as u8;
        }
        let palette = Palette::parse(&bytes).unwrap();
        assert_eq!(palette.colors[0], [0, 1, 2]);
        assert_eq!(palette.colors[255], [253, 254, 255]);
        assert_eq!(
            palette.to_rgba(&[0, 255]),
            [0, 1, 2, 255, 253, 254, 255, 255]
        );
        assert_eq!(
            palette.to_rgba_transparent(&[0, 1]),
            [0, 1, 2, 0, 3, 4, 5, 255]
        );
        assert!(Palette::parse(&bytes[..767]).is_err());
    }
}
