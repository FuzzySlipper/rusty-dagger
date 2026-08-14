//! Minimal PNG encoder (RGBA8): real zlib/deflate compression via
//! miniz_oxide (texture quotas assume compressed PNGs), no other dependencies.

fn crc32(data: &[u8]) -> u32 {
    let mut table = [0u32; 256];
    for (i, e) in table.iter_mut().enumerate() {
        let mut c = i as u32;
        for _ in 0..8 {
            c = if c & 1 != 0 {
                0xEDB8_8320 ^ (c >> 1)
            } else {
                c >> 1
            };
        }
        *e = c;
    }
    let mut crc = 0xFFFF_FFFFu32;
    for &b in data {
        crc = table[((crc ^ b as u32) & 0xFF) as usize] ^ (crc >> 8);
    }
    crc ^ 0xFFFF_FFFF
}

fn chunk(out: &mut Vec<u8>, kind: &[u8; 4], data: &[u8]) {
    out.extend_from_slice(&(data.len() as u32).to_be_bytes());
    out.extend_from_slice(kind);
    out.extend_from_slice(data);
    let mut crc_data = Vec::with_capacity(4 + data.len());
    crc_data.extend_from_slice(kind);
    crc_data.extend_from_slice(data);
    out.extend_from_slice(&crc32(&crc_data).to_be_bytes());
}

/// Encode RGBA8 image (row-major, 4 bytes/pixel) to PNG.
pub fn encode_rgba(width: u32, height: u32, rgba: &[u8]) -> Vec<u8> {
    assert_eq!(rgba.len(), (width * height * 4) as usize);

    // Raw scanlines with filter byte 0
    let stride = (width * 4 + 1) as usize;
    let mut raw = Vec::with_capacity(stride * height as usize);
    for y in 0..height as usize {
        raw.push(0);
        raw.extend_from_slice(&rgba[y * width as usize * 4..(y + 1) * width as usize * 4]);
    }

    // zlib stream: deflate-compressed scanlines. Atlases are mostly
    // transparent padding, so compression is the difference between fitting
    // Engine's 16 MiB encoded-texture quota and exceeding it.
    let z = miniz_oxide::deflate::compress_to_vec_zlib(&raw, 8);

    let mut png = Vec::with_capacity(z.len() + 64);
    png.extend_from_slice(&[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    let mut ihdr = Vec::with_capacity(13);
    ihdr.extend_from_slice(&width.to_be_bytes());
    ihdr.extend_from_slice(&height.to_be_bytes());
    ihdr.extend_from_slice(&[8, 6, 0, 0, 0]); // 8-bit RGBA, no interlace
    chunk(&mut png, b"IHDR", &ihdr);
    chunk(&mut png, b"IDAT", &z);
    chunk(&mut png, b"IEND", &[]);
    png
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn png_header_and_chunks() {
        let png = encode_rgba(2, 1, &[255, 0, 0, 255, 0, 255, 0, 255]);
        assert_eq!(
            &png[0..8],
            &[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
        );
        assert_eq!(&png[12..16], b"IHDR");
        assert!(png.windows(4).any(|w| w == b"IDAT"));
        assert!(png.ends_with(&[0xAE, 0x42, 0x60, 0x82])); // IEND CRC of empty data
    }

    #[test]
    fn idat_zlib_round_trips_scanlines() {
        let mut rgba = vec![0u8; 64 * 4];
        for (index, pixel) in rgba.chunks_mut(4).enumerate() {
            pixel[0] = index as u8;
            pixel[3] = 255;
        }
        let png = encode_rgba(64, 1, &rgba);
        let idat_at = png
            .windows(4)
            .position(|w| w == b"IDAT")
            .expect("IDAT chunk");
        let len =
            u32::from_be_bytes(png[idat_at - 4..idat_at].try_into().expect("IDAT length")) as usize;
        let z = &png[idat_at + 4..idat_at + 4 + len];
        let raw = miniz_oxide::inflate::decompress_to_vec_zlib(z).expect("inflate scanlines");
        let mut expected = vec![0u8];
        expected.extend_from_slice(&rgba);
        assert_eq!(raw, expected);
    }
}
