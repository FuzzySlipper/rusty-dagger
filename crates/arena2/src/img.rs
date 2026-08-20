//! Bounded reader for the uncompressed IMG records used by the product UI.
//!
//! IMG files can use several encodings.  The application-host slice needs
//! only the ordinary 12-byte header form and known, headerless 320x200 UI
//! canvases, so unsupported encodings fail before a caller can mistake them
//! for decoded pixels.

use crate::require_range;

pub const HEADER_BYTES: usize = 12;
pub const HEADERLESS_UI_BYTES: usize = 320 * 200;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Img {
    pub x_offset: i16,
    pub y_offset: i16,
    pub width: u16,
    pub height: u16,
    pub compression: u16,
    pub payload_length: u16,
    /// Indexed pixels in their original row-major source order.
    pub pixels: Vec<u8>,
}

impl Img {
    pub fn load(path: &std::path::Path) -> Result<Self, String> {
        let bytes =
            std::fs::read(path).map_err(|error| format!("read {}: {error}", path.display()))?;
        Self::parse(&bytes)
    }

    pub fn parse(bytes: &[u8]) -> Result<Self, String> {
        let header = require_range(bytes, 0, HEADER_BYTES, "IMG header")?;
        let x_offset = i16::from_le_bytes(header[0..2].try_into().expect("fixed IMG header"));
        let y_offset = i16::from_le_bytes(header[2..4].try_into().expect("fixed IMG header"));
        let width = u16::from_le_bytes(header[4..6].try_into().expect("fixed IMG header"));
        let height = u16::from_le_bytes(header[6..8].try_into().expect("fixed IMG header"));
        let compression = u16::from_le_bytes(header[8..10].try_into().expect("fixed IMG header"));
        let payload_length =
            u16::from_le_bytes(header[10..12].try_into().expect("fixed IMG header"));
        if compression != 0 {
            return Err(format!("unsupported IMG compression {compression}"));
        }
        if width == 0 || height == 0 {
            return Err(format!("invalid IMG dimensions {width}x{height}"));
        }
        let pixels_len = usize::from(width)
            .checked_mul(usize::from(height))
            .ok_or_else(|| "IMG dimensions overflow".to_string())?;
        if pixels_len != usize::from(payload_length) {
            return Err(format!(
                "uncompressed IMG payload length {payload_length} does not match {width}x{height}"
            ));
        }
        let pixels = require_range(bytes, HEADER_BYTES, pixels_len, "IMG pixels")?.to_vec();
        if bytes.len() != HEADER_BYTES + pixels_len {
            return Err(format!(
                "IMG has trailing or missing bytes: expected {}, got {}",
                HEADER_BYTES + pixels_len,
                bytes.len()
            ));
        }
        Ok(Self {
            x_offset,
            y_offset,
            width,
            height,
            compression,
            payload_length,
            pixels,
        })
    }

    /// Decode the one known headerless IMG canvas shape. Callers must opt in
    /// by source-file identity; ordinary IMG parsing never guesses from size.
    pub fn parse_headerless_ui(bytes: &[u8]) -> Result<Self, String> {
        if bytes.len() != HEADERLESS_UI_BYTES {
            return Err(format!(
                "headerless UI IMG must be {HEADERLESS_UI_BYTES} bytes, got {}",
                bytes.len()
            ));
        }
        Ok(Self {
            x_offset: 0,
            y_offset: 0,
            width: 320,
            height: 200,
            compression: 0,
            payload_length: HEADERLESS_UI_BYTES as u16,
            pixels: bytes.to_vec(),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn header(width: u16, height: u16, compression: u16, bytes: u16) -> Vec<u8> {
        [
            0_i16.to_le_bytes().as_slice(),
            154_i16.to_le_bytes().as_slice(),
            width.to_le_bytes().as_slice(),
            height.to_le_bytes().as_slice(),
            compression.to_le_bytes().as_slice(),
            bytes.to_le_bytes().as_slice(),
        ]
        .concat()
    }

    #[test]
    fn reads_uncompressed_headered_img_without_reordering_pixels() {
        let mut bytes = header(2, 2, 0, 4);
        bytes.extend([4, 3, 2, 1]);
        let image = Img::parse(&bytes).unwrap();
        assert_eq!((image.x_offset, image.y_offset), (0, 154));
        assert_eq!((image.width, image.height), (2, 2));
        assert_eq!(image.pixels, [4, 3, 2, 1]);
    }

    #[test]
    fn headerless_ui_canvas_requires_explicit_parser() {
        let bytes = vec![7; HEADERLESS_UI_BYTES];
        assert!(Img::parse(&bytes).is_err());
        let image = Img::parse_headerless_ui(&bytes).unwrap();
        assert_eq!((image.width, image.height), (320, 200));
        assert_eq!(image.pixels.len(), HEADERLESS_UI_BYTES);
        assert!(Img::parse_headerless_ui(&bytes[..bytes.len() - 1]).is_err());
    }

    #[test]
    fn unsupported_or_malformed_img_fails_closed() {
        let mut compressed = header(2, 2, 1, 4);
        compressed.extend([0; 4]);
        assert!(Img::parse(&compressed).unwrap_err().contains("compression"));
        let mut mismatch = header(2, 2, 0, 3);
        mismatch.extend([0; 3]);
        assert!(Img::parse(&mismatch)
            .unwrap_err()
            .contains("payload length"));
        assert!(Img::parse(&header(2, 2, 0, 4)).is_err());
    }
}
