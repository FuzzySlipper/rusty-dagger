//! Dump a TEXTURE.nnn archive's record inventory for sprite pipeline
//! diagnostics: `cargo run -p arena2 --example dump-texture -- local/arena2/TEXTURE.270`.
//! With a record argument, dump that record's frames as raw RGBA:
//! `dump-texture local/arena2/TEXTURE.270 0 /tmp/sk` writes /tmp/sk_f0.rgba …
//! plus a /tmp/sk_dims.txt line per frame (`<frame> <w> <h>`).

use arena2::palette::Palette;
use arena2::texture::TextureFile;

fn main() {
    let mut args = std::env::args().skip(1);
    let path = args
        .next()
        .expect("usage: dump-texture <TEXTURE.nnn> [record out_prefix]");
    let tex = TextureFile::load(std::path::Path::new(&path)).expect("load texture archive");
    match args.next() {
        None => {
            for record in 0..40 {
                match tex.record_info(record) {
                    Some(info) => println!(
                        "record {record:2}: frames={:2} {:4}x{:<4} scale=({},{}) compression={:#06x}",
                        info.frame_count, info.width, info.height, info.scale_x, info.scale_y,
                        info.compression
                    ),
                    None => {
                        println!("record {record:2}: <none>");
                        break;
                    }
                }
            }
        }
        Some(record) => {
            let record: usize = record.parse().expect("record index");
            let prefix = args.next().expect("output prefix");
            let palette =
                Palette::load(std::path::Path::new("local/arena2/PAL.PAL")).expect("PAL.PAL");
            let info = tex.record_info(record).expect("record info");
            for frame in 0..info.frame_count.max(1) as usize {
                let (w, h, indexed) = tex.frame_pixels(record, frame).expect("frame pixels");
                let rgba = palette.to_rgba_transparent(&indexed);
                std::fs::write(format!("{prefix}_f{frame}.rgba"), rgba).expect("write rgba");
                println!("{frame} {w} {h}");
            }
        }
    }
}
