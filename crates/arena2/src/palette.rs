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
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    #[test]
    fn pal_pal_loads() {
        let dir = PathBuf::from(
            std::env::var("ARENA2_DIR").unwrap_or_else(|_| "/home/research/daggerfall-files".into()),
        );
        let pal = Palette::load(&dir.join("PAL.PAL")).unwrap();
        // Palette contains full-range values (verified max byte 255)
        assert!(pal.colors.iter().any(|c| c.iter().any(|&v| v > 63)));
    }
}
