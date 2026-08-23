//! Bounded publication of classic application chrome.  The importer preserves
//! Arena2 pixel/metric identity; the web product decides how to compose it.

use std::{fs, path::Path};

use arena2::{fnt::Font, img::Img, palette::Palette};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};

const PALETTE_FILE: &str = "ART_PAL.COL";
const UI_IMAGES: &[(&str, &str, bool)] = &[
    ("hud.chrome.main", "MAIN00I0.IMG", false),
    ("hud.vital.health", "MAIN03I0.IMG", false),
    ("hud.vital.fatigue", "MAIN04I0.IMG", false),
    ("hud.vital.magicka", "MAIN05I0.IMG", false),
    ("window.inventory.chrome", "INVE00I0.IMG", true),
    ("window.character-sheet.chrome", "INFO00I0.IMG", true),
];
const FONT_ID: &str = "font.classic.0003";
const FONT_FILE: &str = "FONT0003.FNT";
const AUTHORED_ASSET_MANIFEST: &str = "data/ui-authored-assets.json";
const AUTHORED_ASSET_ROOT: &str = "data/ui-original";

pub fn publish(ui_dir: &Path, arena2_dir: &Path) -> Result<(), String> {
    fs::create_dir_all(ui_dir).map_err(|error| error.to_string())?;
    let palette_path = arena2_dir.join(PALETTE_FILE);
    let palette_bytes = fs::read(&palette_path)
        .map_err(|error| format!("read {}: {error}", palette_path.display()))?;
    let palette = Palette::parse(&palette_bytes)?;
    let mut assets = Vec::new();
    for (id, source_file, headerless) in UI_IMAGES {
        assets.push(publish_image(
            ui_dir,
            arena2_dir,
            &palette,
            id,
            source_file,
            *headerless,
        )?);
    }
    publish_authored_assets(ui_dir, &mut assets)?;
    let font = publish_font(ui_dir, arena2_dir)?;
    let manifest = json!({
        "schemaVersion": 1,
        "source": {
            "palette": PALETTE_FILE,
            "paletteSha256": sha256(&palette_bytes),
            "paletteByteLength": palette_bytes.len(),
        },
        "assets": assets,
        "font": font,
    });
    fs::write(
        ui_dir.join("ui-manifest.json"),
        serde_json::to_vec_pretty(&manifest).map_err(|error| error.to_string())?,
    )
    .map_err(|error| error.to_string())?;
    println!(
        "ui:         {} classic images + font atlas + ui-manifest.json",
        UI_IMAGES.len()
    );
    Ok(())
}

/// Publish small, repository-authored UI pieces through the same manifest and
/// ID-only product route as the extracted classic images. Their source remains
/// outside the generated `content/ui` directory so a normal import can
/// recreate it and retain prompt/provenance metadata alongside the PNG digest.
fn publish_authored_assets(ui_dir: &Path, assets: &mut Vec<Value>) -> Result<(), String> {
    let repository_root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    let manifest_path = repository_root.join(AUTHORED_ASSET_MANIFEST);
    if !manifest_path.exists() {
        return Ok(());
    }
    let manifest: Value = serde_json::from_slice(
        &fs::read(&manifest_path)
            .map_err(|error| format!("read {}: {error}", manifest_path.display()))?,
    )
    .map_err(|error| format!("decode {}: {error}", manifest_path.display()))?;
    let entries = manifest
        .get("assets")
        .and_then(Value::as_array)
        .ok_or_else(|| format!("{} must contain an assets array", manifest_path.display()))?;
    for entry in entries {
        let id = entry
            .get("id")
            .and_then(Value::as_str)
            .filter(|value| !value.is_empty())
            .ok_or_else(|| "authored UI asset id must be a non-empty string".to_string())?;
        let file = entry
            .get("file")
            .and_then(Value::as_str)
            .filter(is_safe_png_filename)
            .ok_or_else(|| format!("authored UI asset {id} needs a safe PNG filename"))?;
        let source_file = entry
            .get("sourceFile")
            .and_then(Value::as_str)
            .filter(is_safe_png_filename)
            .ok_or_else(|| format!("authored UI asset {id} needs a safe sourceFile"))?;
        if assets
            .iter()
            .any(|asset| asset.get("id").and_then(Value::as_str) == Some(id))
        {
            return Err(format!(
                "authored UI asset {id} duplicates a published UI id"
            ));
        }
        let source_path = repository_root.join(AUTHORED_ASSET_ROOT).join(source_file);
        let png = fs::read(&source_path)
            .map_err(|error| format!("read {}: {error}", source_path.display()))?;
        if !png.starts_with(b"\x89PNG\r\n\x1a\n") {
            return Err(format!("authored UI asset {id} is not a PNG"));
        }
        fs::write(ui_dir.join(file), &png).map_err(|error| error.to_string())?;
        assets.push(json!({
            "id": id,
            "file": file,
            "mimeType": "image/png",
            "source": {
                "kind": "original-generated",
                "sourceFile": format!("{AUTHORED_ASSET_ROOT}/{source_file}"),
                "generator": entry.get("generator").cloned().unwrap_or(Value::String("unknown".to_string())),
                "prompt": entry.get("prompt").cloned().unwrap_or(Value::String("".to_string())),
            },
            "png": {"sha256": sha256(&png), "byteLength": png.len()},
        }));
    }
    Ok(())
}

fn is_safe_png_filename(file: &&str) -> bool {
    !file.is_empty()
        && file.ends_with(".png")
        && !file.contains("..")
        && !file.contains('/')
        && !file.contains('\\')
}

fn publish_image(
    ui_dir: &Path,
    arena2_dir: &Path,
    palette: &Palette,
    id: &str,
    source_file: &str,
    headerless: bool,
) -> Result<Value, String> {
    let source_path = arena2_dir.join(source_file);
    let source = fs::read(&source_path)
        .map_err(|error| format!("read {}: {error}", source_path.display()))?;
    let image = if headerless {
        Img::parse_headerless_ui(&source)?
    } else {
        Img::parse(&source)?
    };
    // DFU's UI texture path treats palette index zero as transparent. Publish
    // the explicit policy even when a particular supplied source has none.
    let rgba = palette.to_rgba_transparent(&image.pixels);
    let png = crate::png::encode_rgba(u32::from(image.width), u32::from(image.height), &rgba);
    let file = format!("{}.png", id.replace('.', "-"));
    fs::write(ui_dir.join(&file), &png).map_err(|error| error.to_string())?;
    Ok(json!({
        "id": id,
        "file": file,
        "mimeType": "image/png",
        "source": {
            "file": source_file,
            "sha256": sha256(&source),
            "byteLength": source.len(),
            "offset": [image.x_offset, image.y_offset],
            "dimensions": [image.width, image.height],
            "compression": image.compression,
            "payloadByteLength": image.payload_length,
        },
        "alphaPolicy": "palette-index-0-transparent",
        "png": {"sha256": sha256(&png), "byteLength": png.len()},
    }))
}

fn publish_font(ui_dir: &Path, arena2_dir: &Path) -> Result<Value, String> {
    let source_path = arena2_dir.join(FONT_FILE);
    let source = fs::read(&source_path)
        .map_err(|error| format!("read {}: {error}", source_path.display()))?;
    let font = Font::parse(&source)?;
    let columns = 16usize;
    let width = columns * 16;
    let height = font.glyphs.len().div_ceil(columns) * 16;
    let mut rgba = vec![0_u8; width * height * 4];
    let mut glyphs = Vec::with_capacity(font.glyphs.len());
    for (index, glyph) in font.glyphs.iter().enumerate() {
        let x = (index % columns) * 16;
        let y = (index / columns) * 16;
        for row in 0..16 {
            for column in 0..16 {
                if glyph.pixels[row * 16 + column] {
                    let at = ((y + row) * width + x + column) * 4;
                    rgba[at..at + 4].copy_from_slice(&[255, 255, 255, 255]);
                }
            }
        }
        glyphs.push(json!({
            "index": index,
            "sourceOffset": glyph.data_offset,
            "width": glyph.width,
            "rect": [x, y, 16, 16],
        }));
    }
    let png = crate::png::encode_rgba(width as u32, height as u32, &rgba);
    let file = "font-classic-0003-atlas.png";
    fs::write(ui_dir.join(file), &png).map_err(|error| error.to_string())?;
    Ok(json!({
        "id": FONT_ID,
        "file": file,
        "mimeType": "image/png",
        "source": {"file": FONT_FILE, "sha256": sha256(&source), "byteLength": source.len()},
        "atlas": {"dimensions": [width, height], "sha256": sha256(&png), "byteLength": png.len()},
        "nativeMetrics": {"fixedWidth": font.fixed_width, "fixedHeight": font.fixed_height, "glyphCell": [16, 16]},
        "glyphs": glyphs,
    }))
}

fn sha256(bytes: &[u8]) -> String {
    let mut hasher = Sha256::new();
    hasher.update(bytes);
    format!("{:x}", hasher.finalize())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn publishes_classic_ui_catalog_from_configured_real_arena2_files() {
        let arena2 = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../local/arena2");
        if !arena2.join(PALETTE_FILE).exists() {
            eprintln!(
                "skipping real UI publication check: {} is absent",
                arena2.display()
            );
            return;
        }
        let root =
            std::env::temp_dir().join(format!("rusty-dagger-ui-assets-{}", std::process::id()));
        let ui = root.join("ui");
        publish(&ui, &arena2).expect("publish real UI assets");
        let manifest: Value =
            serde_json::from_slice(&fs::read(ui.join("ui-manifest.json")).unwrap()).unwrap();
        assert_eq!(
            manifest["assets"].as_array().unwrap().len(),
            UI_IMAGES.len() + 1
        );
        assert_eq!(manifest["assets"][0]["id"], "hud.chrome.main");
        assert_eq!(
            manifest["assets"].as_array().unwrap().last().unwrap()["id"],
            "inventory.skin.panel-slate.v1"
        );
        assert_eq!(
            manifest["assets"][0]["source"]["dimensions"],
            json!([320, 46])
        );
        assert_eq!(manifest["font"]["id"], FONT_ID);
        assert_eq!(manifest["font"]["glyphs"].as_array().unwrap().len(), 240);
        assert_eq!(
            &fs::read(ui.join("hud-chrome-main.png")).unwrap()[..8],
            b"\x89PNG\r\n\x1a\n"
        );
        fs::remove_dir_all(root).expect("remove isolated UI output");
    }
}
