use std::fs;
use std::path::Path;

use arena2::cif::WeaponCif;
use arena2::palette::Palette;
use arena2::snd::{SndFile, SAMPLE_RATE};
use arena2::texture::TextureFile;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};

const WEAPON_FILE: &str = "WEAPON02.CIF";
const WEAPON_PALETTE: &str = "ART_PAL.COL";
const EFFECT_ARCHIVE: u16 = 380;
const DAGGER_SOUND_FILE: &str = "DAGGER.SND";
const MAX_ATLAS_DIMENSION: usize = 4096;

struct Frame {
    width: usize,
    height: usize,
    rgba: Vec<u8>,
    source_offset: [i16; 2],
    placement: FramePlacement,
}

#[derive(Clone, Copy)]
enum FramePlacement {
    Left(f32),
    Center,
    Right(f32),
}

struct PackedAtlas {
    width: usize,
    height: usize,
    png: Vec<u8>,
    frames: Vec<Value>,
}

pub fn publish(texture_dir: &Path, arena2_dir: &Path, clobber_sprites: bool) -> Result<(), String> {
    fs::create_dir_all(texture_dir).map_err(|error| error.to_string())?;
    let audio_dir = texture_dir
        .parent()
        .ok_or_else(|| "texture output has no parent".to_string())?
        .join("audio");
    fs::create_dir_all(&audio_dir).map_err(|error| error.to_string())?;

    let weapon = publish_weapon(texture_dir, arena2_dir)?;
    let effects = publish_effects(texture_dir, arena2_dir)?;
    let audio = publish_audio(&audio_dir, arena2_dir)?;
    let mut manifest = json!({
        "schemaVersion": 1,
        "cloneBaseline": "classic-daggerfall-dfu",
        "weapon": weapon,
        "effects": effects,
        "audio": audio,
    });
    preserve_combat_edits(texture_dir, &mut manifest, clobber_sprites);
    fs::write(
        texture_dir.join("combat-manifest.json"),
        serde_json::to_vec_pretty(&manifest).map_err(|error| error.to_string())?,
    )
    .map_err(|error| error.to_string())?;
    println!(
        "combat:     classic dagger, {} effects, {} audio clips + combat-manifest.json",
        manifest["effects"].as_array().map_or(0, Vec::len),
        manifest["audio"].as_array().map_or(0, Vec::len)
    );
    Ok(())
}

fn publish_weapon(texture_dir: &Path, arena2_dir: &Path) -> Result<Value, String> {
    let cif = WeaponCif::load(&arena2_dir.join(WEAPON_FILE)).map_err(|error| error.to_string())?;
    let palette =
        Palette::load(&arena2_dir.join(WEAPON_PALETTE)).map_err(|error| error.to_string())?;
    let actions = [
        ("idle", "right", 0.04_f32),
        ("strikeDown", "right", 0.0),
        ("strikeDownLeft", "right", 0.0),
        ("strikeLeft", "right", 0.0),
        ("strikeRight", "left", 0.0),
        ("strikeDownRight", "left", 0.0),
        ("strikeUp", "right", 0.0),
    ];
    if cif.len() != actions.len() {
        return Err(format!(
            "{WEAPON_FILE} has {} records, expected {} for the DFU dagger action table",
            cif.len(),
            actions.len()
        ));
    }

    let mut frames = Vec::new();
    let mut animations = Vec::new();
    for (record, (action, alignment, screen_offset)) in actions.iter().enumerate() {
        let info = cif
            .record_info(record)
            .ok_or_else(|| format!("missing {WEAPON_FILE} record {record}"))?;
        let start = frames.len();
        for frame in 0..info.frame_count as usize {
            let indexed = cif.frame_pixels(record, frame)?;
            frames.push(Frame {
                width: info.width as usize,
                height: info.height as usize,
                rgba: palette.to_rgba_transparent(&indexed),
                source_offset: [info.x_offset, info.y_offset],
                placement: match *alignment {
                    "left" => FramePlacement::Left(*screen_offset),
                    "right" => FramePlacement::Right(*screen_offset),
                    _ => return Err(format!("unsupported weapon alignment {alignment}")),
                },
            });
        }
        animations.push(json!({
            "action": action,
            "sourceRecord": record,
            "fps": 10,
            "alignment": alignment,
            "screenOffset": screen_offset,
            "frameStart": start,
            "frameCount": info.frame_count,
        }));
    }

    let atlas = pack_fixed_cells(frames, Some([320, 200]))?;
    let file = "weapon-dagger-steel-atlas.png";
    fs::write(texture_dir.join(file), &atlas.png).map_err(|error| error.to_string())?;
    Ok(json!({
        "id": "weapon.dagger.steel",
        "textureAssetId": "texture/weapon-dagger-steel-atlas",
        "source": {"file": WEAPON_FILE, "palette": WEAPON_PALETTE},
        "path": file,
        "sha256": sha256(&atlas.png),
        "byteLength": atlas.png.len(),
        "width": atlas.width,
        "height": atlas.height,
        "referenceSize": [320, 200],
        "pivot": [0.5, 0.0],
        "frames": atlas.frames,
        "animations": animations,
    }))
}

fn publish_effects(texture_dir: &Path, arena2_dir: &Path) -> Result<Vec<Value>, String> {
    let file_name = format!("TEXTURE.{EFFECT_ARCHIVE:03}");
    let texture =
        TextureFile::load(&arena2_dir.join(&file_name)).map_err(|error| error.to_string())?;
    let palette = Palette::load(&arena2_dir.join("PAL.PAL")).map_err(|error| error.to_string())?;
    let semantics = [
        (0usize, "effect.blood.0"),
        (1, "effect.blood.1"),
        (2, "effect.blood.2"),
        (3, "effect.sparkle.magic"),
    ];
    let mut output = Vec::new();
    for (record, id) in semantics {
        let info = texture
            .record_info(record)
            .ok_or_else(|| format!("{file_name} record {record} is missing"))?;
        let mut frames = Vec::new();
        for frame in 0..info.frame_count.max(1) as usize {
            let (width, height, indexed) = texture.frame_pixels(record, frame)?;
            frames.push(Frame {
                width,
                height,
                rgba: palette.to_rgba_transparent(&indexed),
                source_offset: [0, 0],
                placement: FramePlacement::Center,
            });
        }
        let atlas = pack_fixed_cells(frames, None)?;
        let slug = id.replace('.', "-");
        let file = format!("{slug}-atlas.png");
        fs::write(texture_dir.join(&file), &atlas.png).map_err(|error| error.to_string())?;
        output.push(json!({
            "id": id,
            "textureAssetId": format!("texture/{slug}-atlas"),
            "source": {"file": file_name, "archive": EFFECT_ARCHIVE, "record": record},
            "path": file,
            "sha256": sha256(&atlas.png),
            "byteLength": atlas.png.len(),
            "width": atlas.width,
            "height": atlas.height,
            "fps": 10,
            "loop": false,
            "pivot": [0.5, 0.5],
            "frames": atlas.frames,
        }));
    }
    Ok(output)
}

fn publish_audio(audio_dir: &Path, arena2_dir: &Path) -> Result<Vec<Value>, String> {
    let sounds =
        SndFile::load(&arena2_dir.join(DAGGER_SOUND_FILE)).map_err(|error| error.to_string())?;
    let clips = [
        (106usize, "audio.melee.dagger.swing"),
        (108, "audio.melee.hit.1"),
        (109, "audio.melee.hit.2"),
        (110, "audio.melee.hit.3"),
        (111, "audio.melee.hit.4"),
        (112, "audio.melee.hit.5"),
    ];
    let mut output = Vec::new();
    for (index, id) in clips {
        let wav = sounds.wav_bytes(index)?;
        let file = format!("{}.wav", id.replace('.', "-"));
        fs::write(audio_dir.join(&file), &wav).map_err(|error| error.to_string())?;
        output.push(json!({
            "id": id,
            "source": {
                "file": DAGGER_SOUND_FILE,
                "recordIndex": index,
                "recordId": sounds.record_id(index),
                "encoding": "unsigned-pcm8-mono",
                "sampleRate": SAMPLE_RATE,
            },
            "path": format!("content/audio/{file}"),
            "sha256": sha256(&wav),
            "byteLength": wav.len(),
            "mimeType": "audio/wav",
        }));
    }
    Ok(output)
}

fn pack_fixed_cells(
    mut frames: Vec<Frame>,
    reference_size: Option<[usize; 2]>,
) -> Result<PackedAtlas, String> {
    if frames.is_empty() {
        return Err("cannot pack an empty combat atlas".to_string());
    }
    let cell_width = reference_size
        .map(|size| size[0])
        .unwrap_or_else(|| frames.iter().map(|frame| frame.width).max().unwrap_or(1));
    let cell_height = reference_size
        .map(|size| size[1])
        .unwrap_or_else(|| frames.iter().map(|frame| frame.height).max().unwrap_or(1));
    if cell_width > MAX_ATLAS_DIMENSION || cell_height > MAX_ATLAS_DIMENSION {
        return Err(format!(
            "combat frame {cell_width}x{cell_height} exceeds Engine texture bounds"
        ));
    }
    let columns = frames.len().min((MAX_ATLAS_DIMENSION / cell_width).max(1));
    let rows = frames.len().div_ceil(columns);
    let width = cell_width * columns;
    let height = cell_height * rows;
    if height > MAX_ATLAS_DIMENSION {
        return Err(format!(
            "combat atlas {width}x{height} exceeds Engine texture bounds"
        ));
    }
    let mut rgba = vec![0u8; width * height * 4];
    let mut entries = Vec::new();
    for (index, frame) in frames.iter_mut().enumerate() {
        if frame.width > cell_width || frame.height > cell_height {
            return Err(format!(
                "combat frame {}x{} exceeds fixed cell {cell_width}x{cell_height}",
                frame.width, frame.height
            ));
        }
        let cell_x = (index % columns) * cell_width;
        let cell_y = (index / columns) * cell_height;
        let x = cell_x + horizontal_frame_offset(frame.placement, cell_width, frame.width);
        // Engine's sprite contract samples upright image space (v=0 = quad
        // top). Classic weapon canvases anchor their art to the canvas bottom
        // (the hilt sits at the screen bottom), so weapon frames pack
        // bottom-aligned; effect frames keep their historic top alignment.
        let y = cell_y + vertical_frame_offset(reference_size.is_some(), cell_height, frame.height);
        for row in 0..frame.height {
            let source = &frame.rgba[row * frame.width * 4..(row + 1) * frame.width * 4];
            let target = ((y + row) * width + x) * 4;
            rgba[target..target + source.len()].copy_from_slice(source);
        }
        entries.push(json!({
            "frame": index,
            "uvMin": [cell_x as f64 / width as f64, cell_y as f64 / height as f64],
            "uvMax": [
                (cell_x + cell_width) as f64 / width as f64,
                (cell_y + cell_height) as f64 / height as f64,
            ],
            "sourceSize": [frame.width, frame.height],
            "sourceOffset": frame.source_offset,
        }));
    }
    let png = crate::png::encode_rgba(width as u32, height as u32, &rgba);
    Ok(PackedAtlas {
        width,
        height,
        png,
        frames: entries,
    })
}

fn horizontal_frame_offset(
    placement: FramePlacement,
    cell_width: usize,
    frame_width: usize,
) -> usize {
    let available = cell_width.saturating_sub(frame_width);
    match placement {
        FramePlacement::Left(offset) => {
            ((offset * cell_width as f32).round() as usize).min(available)
        }
        FramePlacement::Center => available / 2,
        FramePlacement::Right(offset) => {
            available.saturating_sub(((offset * cell_width as f32).round() as usize).min(available))
        }
    }
}

/// Vertical cell placement under Engine's upright image-space contract
/// (v=0 = quad top): classic weapon canvases anchor art to the canvas bottom
/// (hilt at the screen bottom); effect frames keep their top alignment.
fn vertical_frame_offset(
    classic_reference_canvas: bool,
    cell_height: usize,
    frame_height: usize,
) -> usize {
    if classic_reference_canvas {
        cell_height.saturating_sub(frame_height)
    } else {
        0
    }
}

fn sha256(bytes: &[u8]) -> String {
    format!("sha256:{:x}", Sha256::digest(bytes))
}

/// Hand-edit preservation for the combat manifest (Den 6945): entries marked
/// `"edited": true` keep their operator tunables — weapon pivot and per-action
/// fps/alignment/screenOffset, effect pivot/fps/loop — across regeneration.
/// `--clobber-sprites` ignores all markers.
fn preserve_combat_edits(texture_dir: &Path, manifest: &mut Value, clobber: bool) {
    if clobber {
        return;
    }
    let Ok(existing) = fs::read_to_string(texture_dir.join("combat-manifest.json")) else {
        return;
    };
    let Ok(old) = serde_json::from_str::<Value>(&existing) else {
        return;
    };

    if let (Some(old_weapon), Some(new_weapon)) = (
        old.get("weapon")
            .filter(|w| w.get("edited") == Some(&Value::Bool(true))),
        manifest.get_mut("weapon"),
    ) {
        let mut preserved = Vec::new();
        if let Some(pivot) = old_weapon.get("pivot") {
            new_weapon["pivot"] = pivot.clone();
            preserved.push("pivot".to_string());
        }
        if let (Some(old_anims), Some(new_anims)) = (
            old_weapon.get("animations").and_then(|a| a.as_array()),
            new_weapon
                .get_mut("animations")
                .and_then(|a| a.as_array_mut()),
        ) {
            for old_anim in old_anims {
                let Some(action) = old_anim.get("action").and_then(|a| a.as_str()) else {
                    continue;
                };
                let Some(new_anim) = new_anims.iter_mut().find(|candidate| {
                    candidate.get("action").and_then(|a| a.as_str()) == Some(action)
                }) else {
                    continue;
                };
                for field in ["fps", "alignment", "screenOffset"] {
                    if let Some(value) = old_anim.get(field) {
                        new_anim[field] = value.clone();
                        preserved.push(format!("animations.{action}.{field}"));
                    }
                }
            }
        }
        new_weapon["edited"] = Value::Bool(true);
        eprintln!(
            "sprite preservation: combat weapon keeps hand-edited {}",
            preserved.join(", ")
        );
    }

    if let (Some(old_effects), Some(new_effects)) = (
        old.get("effects").and_then(|e| e.as_array()),
        manifest.get_mut("effects").and_then(|e| e.as_array_mut()),
    ) {
        for new_effect in new_effects.iter_mut() {
            let Some(id) = new_effect
                .get("id")
                .and_then(|i| i.as_str())
                .map(str::to_owned)
            else {
                continue;
            };
            let Some(old_effect) = old_effects.iter().find(|candidate| {
                candidate.get("id").and_then(|i| i.as_str()) == Some(id.as_str())
                    && candidate.get("edited") == Some(&Value::Bool(true))
            }) else {
                continue;
            };
            let mut preserved = Vec::new();
            for field in ["pivot", "fps", "loop"] {
                if let Some(value) = old_effect.get(field) {
                    new_effect[field] = value.clone();
                    preserved.push(field.to_string());
                }
            }
            new_effect["edited"] = Value::Bool(true);
            eprintln!("sprite preservation: combat effect {id} keeps hand-edited {preserved:?}");
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn publishes_semantic_catalog_from_configured_real_arena2_files() {
        let arena2 = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../local/arena2");
        if !arena2.join(WEAPON_FILE).exists() {
            eprintln!(
                "skipping real combat publication check: {} is absent",
                arena2.display()
            );
            return;
        }
        let unique = format!(
            "rusty-dagger-combat-assets-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        );
        let root = std::env::temp_dir().join(unique);
        let textures = root.join("textures");
        publish(&textures, &arena2, false).expect("publish real classic combat assets");
        let manifest: Value = serde_json::from_slice(
            &fs::read(textures.join("combat-manifest.json")).expect("read combat manifest"),
        )
        .expect("decode combat manifest");
        assert_eq!(manifest["weapon"]["id"], "weapon.dagger.steel");
        assert_eq!(manifest["weapon"]["referenceSize"], json!([320, 200]));
        assert_eq!(manifest["weapon"]["width"], 3840);
        assert_eq!(manifest["weapon"]["height"], 600);
        assert_eq!(manifest["weapon"]["frames"].as_array().unwrap().len(), 31);
        assert_eq!(manifest["effects"].as_array().unwrap().len(), 4);
        assert_eq!(manifest["audio"].as_array().unwrap().len(), 6);
        for entry in manifest["audio"].as_array().unwrap() {
            let path = root.join(
                entry["path"]
                    .as_str()
                    .unwrap()
                    .strip_prefix("content/")
                    .unwrap(),
            );
            assert_eq!(&fs::read(path).unwrap()[..4], b"RIFF");
        }
        fs::remove_dir_all(&root).expect("remove isolated combat publication output");
    }

    #[test]
    fn classic_frame_alignment_uses_the_320_pixel_reference_canvas() {
        assert_eq!(
            horizontal_frame_offset(FramePlacement::Right(0.0), 320, 162),
            158
        );
        assert_eq!(
            horizontal_frame_offset(FramePlacement::Right(0.04), 320, 65),
            242
        );
        assert_eq!(
            horizontal_frame_offset(FramePlacement::Left(0.0), 320, 311),
            0
        );
        assert_eq!(
            horizontal_frame_offset(FramePlacement::Center, 320, 162),
            79
        );
        assert_eq!(vertical_frame_offset(true, 200, 143), 57);
        assert_eq!(vertical_frame_offset(false, 200, 143), 0);
    }
}
