//! dagger-import: extract a Daggerfall dungeon from classic Arena2 data files
//! to a single GLB (textured by default, --untextured for a flat material).

mod combat_assets;
mod dungeon;
mod glb;
mod meshjson;
mod png;
mod ui_assets;

use std::path::PathBuf;

use sha2::{Digest, Sha256};

struct Args {
    arena2_dir: PathBuf,
    region: usize,
    location: String,
    out: PathBuf,
    textured: bool,
    format: String,
    texture_dir: Option<PathBuf>,
    table_mode: dungeon::TextureTableMode,
    /// Ignore hand-edit markers in existing sprite manifests and rewrite
    /// every tunable field from classic defaults.
    clobber_sprites: bool,
}

fn parse_args() -> Result<Args, String> {
    let mut arena2_dir = PathBuf::from("local/arena2");
    let mut region = 17usize;
    let mut location = "Privateer's Hold".to_string();
    let mut out = PathBuf::from("content/privateers-hold.glb".to_string());
    let mut textured = true;
    let mut format = "glb".to_string();
    let mut texture_dir: Option<PathBuf> = None;
    let mut table_mode = dungeon::TextureTableMode::Classic;
    let mut clobber_sprites = false;
    let mut it = std::env::args().skip(1);
    while let Some(a) = it.next() {
        match a.as_str() {
            "--arena2" => arena2_dir = PathBuf::from(it.next().ok_or("--arena2 needs a value")?),
            "--region" => {
                region = it
                    .next()
                    .ok_or("--region needs a value")?
                    .parse()
                    .map_err(|_| "--region must be a number")?
            }
            "--location" => location = it.next().ok_or("--location needs a value")?,
            "--out" => out = PathBuf::from(it.next().ok_or("--out needs a value")?),
            "--format" => format = it.next().ok_or("--format needs a value")?,
            "--texture-dir" => {
                texture_dir = Some(PathBuf::from(
                    it.next().ok_or("--texture-dir needs a value")?,
                ))
            }
            "--texture-table" => {
                table_mode = match it.next().as_deref() {
                    Some("default") => dungeon::TextureTableMode::Default,
                    Some("classic") => dungeon::TextureTableMode::Classic,
                    Some(other) => {
                        return Err(format!(
                            "--texture-table must be default or classic, got {other:?}"
                        ))
                    }
                    None => return Err("--texture-table needs a value".to_string()),
                }
            }
            "--untextured" => textured = false,
            "--clobber-sprites" => clobber_sprites = true,
            "--help" | "-h" => return Err(usage()),
            other => return Err(format!("unknown arg {other}\n{}", usage())),
        }
    }
    if format != "glb" && format != "mesh-json" {
        return Err(format!("--format must be glb or mesh-json, got {format:?}"));
    }
    Ok(Args {
        arena2_dir,
        region,
        location,
        out,
        textured,
        format,
        texture_dir,
        table_mode,
        clobber_sprites,
    })
}

fn usage() -> String {
    "usage: dagger-import [--arena2 DIR] [--region N] [--location NAME] [--out FILE] [--format glb|mesh-json] [--texture-dir DIR] [--texture-table default|classic] [--untextured]"
        .to_string()
}

fn main() {
    let args = match parse_args() {
        Ok(a) => a,
        Err(e) => {
            eprintln!("{e}");
            std::process::exit(2);
        }
    };

    let output = match dungeon::build_dungeon(
        &args.arena2_dir,
        args.region,
        &args.location,
        args.textured,
        args.table_mode,
    ) {
        Ok(o) => o,
        Err(e) => {
            eprintln!("dagger-import: {e}");
            std::process::exit(1);
        }
    };

    let s = &output.stats;
    println!("location:    {} (region {})", args.location, args.region);
    println!("blocks:      {}", s.blocks);
    println!(
        "models:      {} used, {} missing",
        s.models_used, s.models_missing
    );
    println!("verts:       {}", s.verts);
    println!("tris:        {}", s.tris);
    println!("primitives:  {}", output.primitives.len());
    println!("textures:    {}", s.textures);
    println!("texture table: {:?}", s.texture_table);
    for f in &s.texture_failures {
        println!("texture warning: {f}");
    }
    println!(
        "bounds:      [{:.2},{:.2},{:.2}] .. [{:.2},{:.2},{:.2}]",
        s.bounds_min[0],
        s.bounds_min[1],
        s.bounds_min[2],
        s.bounds_max[0],
        s.bounds_max[1],
        s.bounds_max[2]
    );
    println!(
        "scene:       start={:?} enter={:?} lights={} flats={}",
        output.scene.start_marker,
        output.scene.enter_marker,
        output.scene.light_count,
        output.scene.flat_count
    );

    let name = args
        .location
        .replace('\'', "")
        .replace(' ', "-")
        .to_lowercase();
    let bytes = match args.format.as_str() {
        "mesh-json" => {
            let out = meshjson::write_mesh_json(
                &name,
                &output.primitives,
                &output.textures,
                args.textured,
            );
            if args.textured {
                println!(
                    "mesh-json:   textured ({} texture references)",
                    out.referenced.len()
                );
            }
            out.json.into_bytes()
        }
        _ => {
            // The GLB carries the combined static mesh node (render) plus one
            // named glTF node per carved door (door-N-<model_id>), so every
            // door is addressable by name in the engine-consumable artifact.
            // The collision mesh.json uses only `output.primitives` (no
            // doors), so doorways stay open for route derivation.
            glb::write_glb(
                &name,
                &output.primitives,
                &output.door_primitives,
                &output.textures,
            )
        }
    };
    if let Some(parent) = args.out.parent() {
        if !parent.as_os_str().is_empty() {
            std::fs::create_dir_all(parent).expect("create output dir");
        }
    }
    std::fs::write(&args.out, &bytes).expect("write output");
    println!(
        "wrote:       {} ({} bytes)",
        args.out.display(),
        bytes.len()
    );

    // Scene metadata sidecar (markers in glTF world space), consumed by
    // scripts/generate-project.py for the player spawn.
    fn v3(v: Option<[f32; 3]>) -> String {
        match v {
            Some([x, y, z]) => format!("[{x}, {y}, {z}]"),
            None => "null".to_string(),
        }
    }
    let scene_json = format!(
        "{{\n  \"schemaVersion\": 1,\n  \"location\": \"{}\",\n  \"startMarker\": {},\n  \"enterMarker\": {},\n  \"lightCount\": {},\n  \"flatCount\": {},\n  \"lights\": [{}],\n  \"billboards\": [{}],\n  \"enemies\": [{}],\n  \"treasure\": [{}],\n  \"doors\": [{}],\n  \"bounds\": {{\"min\": {:?}, \"max\": {:?}}}\n}}\n",
        args.location.replace('"', "'"),
        v3(output.scene.start_marker), v3(output.scene.enter_marker),
        output.scene.light_count, output.scene.flat_count,
        output.scene.lights.iter()
            .map(|(p, r)| format!("{{\"position\": {:?}, \"range\": {r}}}", p))
            .collect::<Vec<_>>()
            .join(","),
        output.scene.billboards.iter()
            .map(|b| format!(
                "{{\"position\": {:?}, \"textureArchive\": {}, \"textureRecord\": {}}}",
                b.position, b.texture_archive, b.texture_record
            ))
            .collect::<Vec<_>>()
            .join(","),
        output.scene.enemies.iter()
            .map(|e| format!(
                "{{\"position\": {:?}, \"mobileId\": {}, \"name\": \"{}\", \"textureArchive\": {}}}",
                e.position, e.mobile_id, e.name, e.texture_archive
            ))
            .collect::<Vec<_>>()
            .join(","),
        output.scene.treasure.iter()
            .map(|t| format!(
                "{{\"position\": {:?}, \"flags\": {}, \"lootKey\": {}}}",
                t.position,
                t.flags,
                t.loot_key
                    .as_deref()
                    .map(|key| format!("\"{key}\""))
                    .unwrap_or_else(|| "null".to_string())
            ))
            .collect::<Vec<_>>()
            .join(","),
        output.scene.doors.iter()
            .map(|d| {
                let action = d
                    .action
                    .as_ref()
                    .map(|a| {
                        format!(
                            "{{\"axis\": {}, \"duration\": {}, \"magnitude\": {}}}",
                            a.axis, a.duration, a.magnitude
                        )
                    })
                    .unwrap_or_else(|| "null".to_string());
                format!(
                    "{{\"position\": {:?}, \"rotationDeg\": {:?}, \"modelId\": \"{}\", \"hinged\": {}, \"action\": {}}}",
                    d.position, d.rotation_deg, d.model_id, d.hinged, action
                )
            })
            .collect::<Vec<_>>()
            .join(","),
        s.bounds_min, s.bounds_max
    );
    let scene_path = args.out.with_extension("scene.json");
    std::fs::write(&scene_path, scene_json).expect("write scene metadata");
    println!("wrote:       {}", scene_path.display());

    // Publish decoded texture PNGs + a content-hash manifest. The studio host
    // serves these as content-addressed render resources (`.png` is on its
    // allowlist); generate-project.py stamps the catalog entries with the
    // manifest's sourcePath/hash, and the adapter re-derives resource identity
    // from the same bytes.
    if let Some(dir) = &args.texture_dir {
        publish_textures(dir, &output.textures);
        // The treasure icon (TEXTURE.216[0], one deterministic pick from the
        // donor's randomTreasureIconIndices) rides the billboard publication
        // path so generate-project.py stamps it as an ordinary billboard
        // texture asset; it is NOT added to scene billboards, so no extra
        // billboard entity appears.
        let mut billboard_sources = output.scene.billboards.clone();
        billboard_sources.push(dungeon::BillboardFlat {
            position: [0.0; 3],
            texture_archive: dungeon::TREASURE_ICON_ARCHIVE,
            texture_record: dungeon::TREASURE_ICON_RECORD,
        });
        publish_billboard_textures(
            dir,
            &args.arena2_dir,
            &billboard_sources,
            args.clobber_sprites,
        );
        publish_enemy_atlases(
            dir,
            &args.arena2_dir,
            &output.scene.enemies,
            args.clobber_sprites,
        );
        combat_assets::publish(dir, &args.arena2_dir, args.clobber_sprites)
            .expect("publish classic combat assets");
        let ui_dir = dir
            .parent()
            .expect("texture output must have a content parent")
            .join("ui");
        ui_assets::publish(&ui_dir, &args.arena2_dir).expect("publish classic UI assets");
    }
}

fn publish_textures(dir: &std::path::Path, textures: &[glb::TextureInput]) {
    std::fs::create_dir_all(dir).expect("create texture dir");
    let mut entries: Vec<String> = Vec::new();
    let mut count = 0usize;
    let mut bytes_total = 0usize;
    for tex in textures {
        let slug = glb::texture_slug(tex.id);
        let file = format!("{slug}.png");
        std::fs::write(dir.join(&file), &tex.png).expect("write texture png");
        let hash = format!("sha256:{:x}", Sha256::digest(&tex.png));
        entries.push(format!(
            "    {{\"path\":\"{file}\",\"sha256\":\"{hash}\",\"byteLength\":{}}}",
            tex.png.len()
        ));
        count += 1;
        bytes_total += tex.png.len();
    }
    let manifest = format!(
        "{{\n  \"schemaVersion\": 1,\n  \"textures\": [\n{}\n  ]\n}}\n",
        entries.join(",\n")
    );
    std::fs::write(dir.join("manifest.json"), manifest).expect("write texture manifest");
    println!(
        "wrote:       {} ({} pngs, {} bytes) + manifest.json",
        dir.display(),
        count,
        bytes_total
    );
}

/// A manifest entry carrying
/// `"edited": true` keeps its tunable fields when regeneration rewrites the
/// manifest from classic data; everything else takes the freshly computed
/// defaults. `--clobber-sprites` ignores all markers. What is preserved is
/// deliberately the operator-tunable layer (pivots, sizes, fps/loop, playback
/// sequences) — never the derived pixel layout (frame UVs always follow the
/// freshly packed atlas).
fn preserve_manifest_edits(
    existing: Option<&str>,
    new_text: String,
    manifest_kind: &str,
    clobber: bool,
) -> String {
    if clobber {
        return new_text;
    }
    let Some(existing) = existing else {
        return new_text;
    };
    let Ok(old) = serde_json::from_str::<serde_json::Value>(existing) else {
        return new_text;
    };
    let Ok(mut new) = serde_json::from_str::<serde_json::Value>(&new_text) else {
        return new_text;
    };
    let (list_key, id_of): (&str, fn(&serde_json::Value) -> String) = match manifest_kind {
        "enemy" => ("enemies", |entry| {
            entry
                .get("mobileId")
                .and_then(|id| id.as_u64())
                .map(|id| id.to_string())
                .unwrap_or_default()
        }),
        _ => ("billboards", |entry| {
            format!(
                "{}.{}",
                entry.get("archive").and_then(|v| v.as_u64()).unwrap_or(0),
                entry.get("record").and_then(|v| v.as_u64()).unwrap_or(0)
            )
        }),
    };
    let (Some(old_entries), Some(new_entries)) = (
        old.get(list_key).and_then(|list| list.as_array()),
        new.get_mut(list_key).and_then(|list| list.as_array_mut()),
    ) else {
        return new_text;
    };
    for new_entry in new_entries.iter_mut() {
        let id = id_of(new_entry);
        let Some(old_entry) = old_entries.iter().find(|entry| id_of(entry) == id) else {
            continue;
        };
        if old_entry.get("edited").and_then(|flag| flag.as_bool()) != Some(true) {
            continue;
        }
        let mut preserved = Vec::new();
        // Top-level tunables.
        let top: &[&str] = match manifest_kind {
            "enemy" => &["pivot", "normalizedSize"],
            _ => &["pivot", "worldSize", "fps"],
        };
        for field in top {
            if let Some(value) = old_entry.get(field) {
                new_entry[field] = value.clone();
                preserved.push(field.to_string());
            }
        }
        // Per-state animation tunables (enemy move/idle/attack/hurt).
        if let (Some(old_states), Some(new_states)) = (
            old_entry.get("states").and_then(|s| s.as_object()),
            new_entry.get_mut("states").and_then(|s| s.as_object_mut()),
        ) {
            for (state_name, old_state) in old_states {
                let Some(new_state) = new_states.get_mut(state_name) else {
                    continue;
                };
                for field in ["fps", "loop", "sequence", "alternateSequences"] {
                    if let Some(value) = old_state.get(field) {
                        new_state[field] = value.clone();
                        preserved.push(format!("states.{state_name}.{field}"));
                    }
                }
            }
        }
        // Per-frame world sizes (match on frame index).
        if let (Some(old_frames), Some(new_frames)) = (
            old_entry.get("frames").and_then(|f| f.as_array()),
            new_entry.get_mut("frames").and_then(|f| f.as_array_mut()),
        ) {
            for old_frame in old_frames {
                let Some(frame_id) = old_frame.get("frame").and_then(|f| f.as_u64()) else {
                    continue;
                };
                let Some(new_frame) = new_frames.iter_mut().find(|candidate| {
                    candidate.get("frame").and_then(|f| f.as_u64()) == Some(frame_id)
                }) else {
                    continue;
                };
                if let Some(size) = old_frame.get("size") {
                    new_frame["size"] = size.clone();
                    preserved.push(format!("frames[{frame_id}].size"));
                }
            }
        }
        new_entry["edited"] = serde_json::Value::Bool(true);
        // Summarize: one entry per field kind, not per frame.
        let mut kinds: Vec<String> = preserved
            .iter()
            .map(|field| field.split('[').next().unwrap_or(field).to_string())
            .collect();
        kinds.sort();
        kinds.dedup();
        eprintln!(
            "sprite preservation: {manifest_kind} entry {id} keeps hand-edited {}",
            kinds.join(", ")
        );
    }
    serde_json::to_string_pretty(&new).unwrap_or(new_text)
}

/// Remove previously generated PNGs with an exporter-owned prefix so renames
/// never leave stale files in the content tree.
fn remove_generated(dir: &std::path::Path, prefix: &str) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let name = entry.file_name();
        let Some(name) = name.to_str() else { continue };
        if name.starts_with(prefix) && name.ends_with(".png") {
            let _ = std::fs::remove_file(entry.path());
        }
    }
}

/// Hand-authored sprite display names (data/sprite-names.json). Content
/// configuration: missing or malformed files just mean numeric slugs.
fn load_sprite_names() -> std::collections::BTreeMap<String, String> {
    let Ok(text) = std::fs::read_to_string("data/sprite-names.json") else {
        return Default::default();
    };
    let Ok(value) = serde_json::from_str::<serde_json::Value>(&text) else {
        eprintln!(
            "sprite names warning: data/sprite-names.json is not valid JSON, using numeric slugs"
        );
        return Default::default();
    };
    value
        .get("billboards")
        .and_then(|billboards| billboards.as_object())
        .map(|billboards| {
            billboards
                .iter()
                .filter_map(|(key, name)| name.as_str().map(|name| (key.clone(), name.to_string())))
                .collect()
        })
        .unwrap_or_default()
}

/// Decode unique billboard (archive, record) textures to transparent PNGs
/// (palette index 0 = transparent, the Daggerfall billboard rule) plus a
/// billboard manifest mapping each texture to its PNG sourcePath/hash/dims.
/// generate-project.py consumes this to stamp billboard sprite resources.
fn publish_billboard_textures(
    dir: &std::path::Path,
    arena2_dir: &std::path::Path,
    billboards: &[dungeon::BillboardFlat],
    clobber_sprites: bool,
) {
    use arena2::palette::Palette;
    use arena2::texture::TextureFile;
    use std::collections::BTreeMap;

    // The exporter owns billboard-*.png; clear stale outputs so renamed
    // exports never linger beside their replacements.
    remove_generated(dir, "billboard-");
    let palette = Palette::load(&arena2_dir.join("PAL.PAL")).expect("PAL.PAL");
    let sprite_names = load_sprite_names();
    let mut used_slugs = std::collections::BTreeSet::new();
    let mut unique: BTreeMap<(u16, u16), ()> = BTreeMap::new();
    for b in billboards {
        unique.insert((b.texture_archive, b.texture_record), ());
    }
    let mut entries: Vec<String> = Vec::new();
    let mut count = 0usize;
    let mut failures = 0usize;
    for (archive, record) in unique.keys() {
        let tex_path = arena2_dir.join(format!("TEXTURE.{archive:03}"));
        let Ok(tex) = TextureFile::load(&tex_path) else {
            failures += 1;
            eprintln!("billboard texture warning: TEXTURE.{archive:03} unreadable");
            continue;
        };
        if tex.record_info(*record as usize).is_none() {
            failures += 1;
            eprintln!("billboard texture warning: TEXTURE.{archive:03} rec {record} missing");
            continue;
        };
        let info = tex.record_info(*record as usize).unwrap();
        let frame_count = info.frame_count.max(1) as usize;
        // DFU GetScaledBillboardSize: (size + size*scale/256) * GlobalScale.
        let world =
            arena2::mobile::record_world_size(info.width, info.height, info.scale_x, info.scale_y);

        // Decode all frames (single-frame records decode just frame 0).
        // Multi-frame records (torch flames, animated lights) are packed into
        // a horizontal strip atlas so the engine can cycle frames via
        // updateSprite without texture swapping.
        let mut all_frames: Vec<Vec<u8>> = Vec::with_capacity(frame_count);
        let mut decode_ok = true;
        for f in 0..frame_count {
            match tex.frame_pixels(*record as usize, f) {
                Ok((_fw, _fh, indexed)) => {
                    all_frames.push(palette.to_rgba_transparent(&indexed));
                }
                Err(e) => {
                    failures += 1;
                    eprintln!(
                        "billboard texture warning: TEXTURE.{archive:03} rec {record} frame {f} decode failed: {e}"
                    );
                    decode_ok = false;
                    break;
                }
            }
        }
        if !decode_ok {
            continue;
        }
        let (w, h) = (info.width.max(1) as usize, info.height.max(1) as usize);

        // Pack into atlas: single frame = plain PNG; multi-frame = horizontal
        // strip. Engine's sprite contract samples upright decoded-image space
        // (top-left origin), so classic rows are stored as-is.
        let (atlas_w, atlas_h, rgba) = if all_frames.len() == 1 {
            (w, h, all_frames.into_iter().next().unwrap())
        } else {
            let fc = all_frames.len();
            let mut strip = vec![0u8; w * fc * h * 4];
            for (i, frame) in all_frames.iter().enumerate() {
                // Copy each row of the frame into its column range in the strip.
                for y in 0..h {
                    let src = &frame[y * w * 4..(y + 1) * w * 4];
                    let dst_start = (y * w * fc + i * w) * 4;
                    strip[dst_start..dst_start + w * 4].copy_from_slice(src);
                }
            }
            (w * fc, h, strip)
        };

        let png = crate::png::encode_rgba(atlas_w as u32, atlas_h as u32, &rgba);
        // Hand-authored nickname when the overlay has one (content config);
        // numeric slug otherwise. A nickname collision falls back to numeric
        // with a warning rather than overwriting a sibling export.
        let nickname = sprite_names.get(&format!("{archive}.{record}"));
        let slug = match nickname {
            Some(name) if used_slugs.insert(name.clone()) => format!("billboard-{name}"),
            Some(name) => {
                eprintln!(
                    "sprite names warning: nickname \"{name}\" is used more than once; billboard-{archive}-{record} keeps its numeric slug"
                );
                format!("billboard-{archive}-{record}")
            }
            None => format!("billboard-{archive}-{record}"),
        };
        let file = format!("{slug}.png");
        std::fs::write(dir.join(&file), &png).expect("write billboard png");
        let hash = format!("sha256:{:x}", Sha256::digest(&png));
        let name_field = match nickname {
            Some(name) if slug == format!("billboard-{name}") => format!(",\"name\":\"{name}\""),
            _ => String::new(),
        };

        // Manifest entry: single-frame records stay backward-compatible
        // (no frameCount/frames). Multi-frame records add frameCount, fps,
        // and per-frame UV rects so generate-project.py can build a
        // multi-frame spriteAtlas. width/height are one frame's dims (classic
        // record dims); atlasWidth/atlasHeight are the packed PNG dims, which
        // differ for multi-frame strips.
        let extra = if frame_count > 1 {
            let fc = frame_count as f32;
            let frames: Vec<String> = (0..frame_count)
                .map(|i| {
                    format!(
                        "{{\"frame\":{i},\"uvMin\":[{:?},{:?}],\"uvMax\":[{:?},{:?}]}}",
                        i as f32 / fc,
                        0.0,
                        (i + 1) as f32 / fc,
                        1.0
                    )
                })
                .collect();
            format!(
                ",\"frameCount\":{frame_count},\"fps\":{},\"frames\":[{}]",
                arena2::mobile::ENV_BILLBOARD_FPS,
                frames.join(",")
            )
        } else {
            String::new()
        };
        entries.push(format!(
            "    {{\"archive\":{archive},\"record\":{record},\"path\":\"{file}\",\"sha256\":\"{hash}\",\"byteLength\":{},\"width\":{w},\"height\":{h},\"atlasWidth\":{atlas_w},\"atlasHeight\":{atlas_h},\"pivot\":[0.5,0.5],\"worldSize\":[{:?},{:?}]{name_field}{}}}",
            png.len(),
            world[0],
            world[1],
            extra
        ));
        count += 1;
    }
    let manifest = format!(
        "{{\n  \"schemaVersion\": 1,\n  \"billboards\": [\n{}\n  ]\n}}\n",
        entries.join(",\n")
    );
    let manifest = preserve_manifest_edits(
        std::fs::read_to_string(dir.join("billboard-manifest.json"))
            .ok()
            .as_deref(),
        manifest,
        "billboard",
        clobber_sprites,
    );
    std::fs::write(dir.join("billboard-manifest.json"), manifest)
        .expect("write billboard manifest");
    println!(
        "billboards:  {} unique textures ({} decode failures)",
        count, failures
    );
}

/// Decode and pack one directional sprite atlas per unique enemy mobile id.
/// Move, idle, attack, and hurt state ranges are retained in one stable atlas;
/// classic corpse markers are published as separate ground sprites.
/// generate-project.py consumes this to stamp enemy sprite resources and atlas
/// frame descriptors. The AnimationService uses the 8×M layout so direction
/// changes preserve the current anim frame position.
fn publish_enemy_atlases(
    dir: &std::path::Path,
    arena2_dir: &std::path::Path,
    enemies: &[dungeon::EnemyScene],
    clobber_sprites: bool,
) {
    use arena2::mobile::{
        mobile_type, record_world_size, standing_anims, HURT_ANIMS, HURT_ANIM_SPEED,
        IDLE_ANIM_SPEED, MOVE_ANIMS, MOVE_ANIM_SPEED, PRIMARY_ATTACK_ANIMS,
        PRIMARY_ATTACK_ANIM_SPEED,
    };
    use arena2::palette::Palette;
    use arena2::texture::TextureFile;
    use std::collections::BTreeMap;

    // The exporter owns enemy-*.png; clear stale outputs so renames never
    // linger beside their replacements.
    remove_generated(dir, "enemy-");
    let palette = Palette::load(&arena2_dir.join("PAL.PAL")).expect("PAL.PAL");
    let mut unique: BTreeMap<u8, ()> = BTreeMap::new();
    for e in enemies {
        unique.insert(e.mobile_id, ());
    }
    let mut entries: Vec<String> = Vec::new();
    let mut count = 0usize;
    let mut failures = 0usize;
    for id in unique.keys() {
        let mobile = mobile_type(*id).expect("mobile type for collected enemy");
        let tex_path = arena2_dir.join(format!("TEXTURE.{:03}", mobile.texture_archive));
        let Ok(tex) = TextureFile::load(&tex_path) else {
            failures += 1;
            eprintln!(
                "enemy atlas warning: TEXTURE.{:03} unreadable ({} skipped)",
                mobile.texture_archive, mobile.name
            );
            continue;
        };

        let states = [
            ("move", &MOVE_ANIMS, MOVE_ANIM_SPEED, true),
            ("idle", standing_anims(mobile), IDLE_ANIM_SPEED, true),
            (
                "attack",
                &PRIMARY_ATTACK_ANIMS,
                PRIMARY_ATTACK_ANIM_SPEED,
                false,
            ),
            ("hurt", &HURT_ANIMS, HURT_ANIM_SPEED, false),
        ];

        // Decode state-major, then orientation-major, then frame-major. Each
        // state records its stable range so Rust presentation never derives
        // classic record numbers or atlas arithmetic in browser code.
        type DecodedEnemyFrame = (usize, bool, usize, usize, Vec<u8>, [f32; 2]);
        let mut decoded: Vec<DecodedEnemyFrame> = Vec::new();
        let mut state_layouts = Vec::new();
        let mut failed = false;
        for (state_name, anims, fps, loops) in states {
            let frame_start = decoded.len();
            let frame_counts = anims
                .iter()
                .filter_map(|anim| tex.record_info(anim.record as usize))
                .map(|info| info.frame_count.max(1) as usize)
                .collect::<Vec<_>>();
            let Some(&frames_per_orientation) = frame_counts.first() else {
                failed = true;
                break;
            };
            if frame_counts.len() != 8
                || frame_counts
                    .iter()
                    .any(|count| *count != frames_per_orientation)
            {
                failures += 1;
                eprintln!(
                    "enemy atlas warning: TEXTURE.{:03} {state_name} records have non-uniform frames",
                    mobile.texture_archive
                );
                failed = true;
                break;
            }
            for (orientation, anim) in anims.iter().enumerate() {
                let rec = anim.record as usize;
                let Some(info) = tex.record_info(rec) else {
                    failed = true;
                    break;
                };
                let size = record_world_size(info.width, info.height, info.scale_x, info.scale_y);
                for frame in 0..frames_per_orientation {
                    match tex.frame_pixels(rec, frame) {
                        Ok((w, h, indexed)) => {
                            decoded.push((orientation, anim.flip, w, h, indexed, size));
                        }
                        Err(error) => {
                            eprintln!(
                                "enemy atlas warning: TEXTURE.{:03} rec {rec} frame {frame}: {error}",
                                mobile.texture_archive
                            );
                            failed = true;
                            break;
                        }
                    }
                }
                if failed {
                    failures += 1;
                    break;
                }
            }
            if failed {
                break;
            }
            state_layouts.push((state_name, frame_start, frames_per_orientation, fps, loops));
        }
        if failed {
            continue;
        }

        // The Engine resizes sprite quads per frame (SpriteFrameRect.size), so
        // frames ship at native cropped pixels with tight UV rects and the
        // classic per-record world size, scaled by the crop ratio: DFU sizes
        // the billboard per record (front view narrower than side view), which
        // is authentic classic behavior that the old fixed-quad constraint
        // could not express. Per-axis crop mapping keeps that size truthful
        // for the visible art.
        // (trimmed_w, trimmed_h, trimmed rgba, source_size, world_size)
        type NormalizedEnemyFrame = (usize, usize, Vec<u8>, [f32; 2], [f32; 2]);
        let mut visible: Vec<NormalizedEnemyFrame> = Vec::with_capacity(decoded.len());
        for (_orientation, flip, w, h, indexed, source_size) in decoded {
            let mut rgba = palette.to_rgba_transparent(&indexed);
            if flip {
                flip_rgba_columns(&mut rgba, w, h);
            }
            let (trimmed_w, trimmed_h, trimmed) = crop_visible_rgba(&rgba, w, h);
            let world_size = [
                source_size[0] * trimmed_w as f32 / w.max(1) as f32,
                source_size[1] * trimmed_h as f32 / h.max(1) as f32,
            ];
            visible.push((trimmed_w, trimmed_h, trimmed, source_size, world_size));
        }
        let normalized: Vec<NormalizedEnemyFrame> = visible;

        let total_cells = normalized.len();
        // Representative fixed-quad fallback (entities still declare one
        // sprite size); per-frame sizes in the atlas override it.
        let mut frame_ws = normalized
            .iter()
            .map(|frame| frame.4[0])
            .collect::<Vec<_>>();
        let mut frame_hs = normalized
            .iter()
            .map(|frame| frame.4[1])
            .collect::<Vec<_>>();
        frame_ws.sort_by(f32::total_cmp);
        frame_hs.sort_by(f32::total_cmp);
        let normalized_size = [frame_ws[frame_ws.len() / 2], frame_hs[frame_hs.len() / 2]];
        // Engine's public texture contract bounds either dimension at 4096.
        // Classic enemies can have enough directional animation frames to
        // exceed that width in a historical one-row atlas, so pack a bounded
        // grid while retaining stable frame numbers and per-frame UVs.
        let cell_w: usize = normalized.iter().map(|frame| frame.0).max().unwrap_or(1);
        let cell_h: usize = normalized.iter().map(|frame| frame.1).max().unwrap_or(1);
        const MAX_ATLAS_DIMENSION: usize = 4096;
        let columns = total_cells.min((MAX_ATLAS_DIMENSION / cell_w).max(1));
        let rows = total_cells.div_ceil(columns);
        let atlas_w: usize = cell_w * columns;
        let atlas_h: usize = cell_h * rows;
        let mut atlas = vec![0u8; atlas_w * atlas_h * 4];
        let mut frame_entries: Vec<String> = Vec::new();
        for (idx, (w, h, rgba, source_size, world_size)) in normalized.iter().enumerate() {
            let x0 = (idx % columns) * cell_w;
            let y0 = (idx / columns) * cell_h;
            let dx = x0 + (cell_w - w) / 2;
            let dy = y0 + cell_h - h;
            // Engine's sprite contract samples upright decoded-image space
            // (top-left origin, V down), so each frame's rows are packed in
            // classic top-down order, bottom-aligned in its cell. The UV rect
            // is tight around the art and carries the frame's own world size.
            for (row_i, row) in rgba.chunks(w * 4).enumerate() {
                let dst = ((dy + row_i) * atlas_w + dx) * 4;
                atlas[dst..dst + w * 4].copy_from_slice(row);
            }
            frame_entries.push(format!(
                "      {{\"frame\":{idx},\"uvMin\":[{},{}],\"uvMax\":[{},{}],\"size\":[{:?},{:?}],\"sourceSize\":[{:?},{:?}]}}",
                dx as f64 / atlas_w as f64,
                dy as f64 / atlas_h as f64,
                (dx + w) as f64 / atlas_w as f64,
                (dy + h) as f64 / atlas_h as f64,
                world_size[0],
                world_size[1],
                source_size[0],
                source_size[1]
            ));
        }
        let png = crate::png::encode_rgba(atlas_w as u32, atlas_h as u32, &atlas);
        let slug = format!("enemy-{}-atlas", mobile.name.to_lowercase());
        let file = format!("{slug}.png");
        std::fs::write(dir.join(&file), &png).expect("write enemy atlas png");
        let hash = format!("sha256:{:x}", Sha256::digest(&png));
        let state_entries = state_layouts
            .iter()
            .map(|(name, frame_start, frames_per_orientation, fps, loops)| {
                let mut entry = format!(
                    "\"frameStart\":{frame_start},\"framesPerOrientation\":{frames_per_orientation},\"fps\":{fps},\"loop\":{loops}"
                );
                // Attack states carry the classic playback sequence (DFU
                // PrimaryAttackAnimFrames; -1 = melee damage beat) plus any
                // alternates with their cumulative Dice100 chances.
                if *name == "attack" {
                    let sequence = &mobile.attack_sequence;
                    let primary = sequence
                        .primary
                        .iter()
                        .map(|frame| frame.to_string())
                        .collect::<Vec<_>>()
                        .join(",");
                    entry.push_str(&format!(",\"sequence\":[{primary}]"));
                    if !sequence.alternates.is_empty() {
                        let alternates = sequence
                            .alternates
                            .iter()
                            .map(|alternate| {
                                let frames = alternate
                                    .frames
                                    .iter()
                                    .map(|frame| frame.to_string())
                                    .collect::<Vec<_>>()
                                    .join(",");
                                format!("{{\"chance\":{},\"sequence\":[{}]}}", alternate.chance, frames)
                            })
                            .collect::<Vec<_>>()
                            .join(",");
                        entry.push_str(&format!(",\"alternateSequences\":[{alternates}]"));
                    }
                }
                format!("\"{name}\":{{{entry}}}")
            })
            .collect::<Vec<_>>()
            .join(",");
        let corpse =
            publish_enemy_corpse(dir, arena2_dir, &palette, mobile).unwrap_or_else(|error| {
                failures += 1;
                eprintln!("enemy corpse warning: {}: {error}", mobile.name);
                "null".to_string()
            });
        entries.push(format!(
            "    {{\"mobileId\":{},\"name\":\"{}\",\"archive\":{},\"path\":\"{file}\",\"sha256\":\"{hash}\",\"byteLength\":{},\"width\":{atlas_w},\"height\":{atlas_h},\"normalizedSize\":[{:?},{:?}],\"pivot\":[0.5,0.0],\"states\":{{{state_entries}}},\"corpse\":{corpse},\"frames\":[\n{}\n    ]}}",
            mobile.id, mobile.name, mobile.texture_archive,
            png.len(),
            normalized_size[0], normalized_size[1],
            frame_entries.join(",\n")
        ));
        count += 1;
    }
    let manifest = format!(
        "{{\n  \"schemaVersion\": 1,\n  \"enemies\": [\n{}\n  ]\n}}\n",
        entries.join(",\n")
    );
    let manifest = preserve_manifest_edits(
        std::fs::read_to_string(dir.join("enemy-manifest.json"))
            .ok()
            .as_deref(),
        manifest,
        "enemy",
        clobber_sprites,
    );
    std::fs::write(dir.join("enemy-manifest.json"), manifest).expect("write enemy manifest");
    println!(
        "enemies:     {} atlases ({} decode failures)",
        count, failures
    );
}

fn publish_enemy_corpse(
    dir: &std::path::Path,
    arena2_dir: &std::path::Path,
    palette: &arena2::palette::Palette,
    mobile: &arena2::mobile::MobileType,
) -> Result<String, String> {
    use arena2::mobile::record_world_size;
    use arena2::texture::TextureFile;

    let Some(corpse) = mobile.corpse else {
        return Ok("null".to_string());
    };
    let source_file = format!("TEXTURE.{:03}", corpse.archive);
    let texture = TextureFile::load(&arena2_dir.join(&source_file))
        .map_err(|error| format!("load {source_file}: {error}"))?;
    let info = texture
        .record_info(corpse.record as usize)
        .ok_or_else(|| format!("{source_file} record {} is missing", corpse.record))?;
    let (width, height, indexed) = texture
        .frame_pixels(corpse.record as usize, 0)
        .map_err(|error| format!("decode {source_file} record {}: {error}", corpse.record))?;
    let rgba = palette.to_rgba_transparent(&indexed);
    let (trimmed_width, trimmed_height, trimmed) = crop_visible_rgba(&rgba, width, height);
    let png = crate::png::encode_rgba(trimmed_width as u32, trimmed_height as u32, &trimmed);
    let file = format!("enemy-{}-corpse.png", mobile.name.to_lowercase());
    std::fs::write(dir.join(&file), &png).map_err(|error| error.to_string())?;
    let hash = format!("sha256:{:x}", Sha256::digest(&png));
    let world_size = record_world_size(info.width, info.height, info.scale_x, info.scale_y);
    Ok(format!(
        "{{\"archive\":{},\"record\":{},\"path\":\"{file}\",\"sha256\":\"{hash}\",\"byteLength\":{},\"width\":{trimmed_width},\"height\":{trimmed_height},\"worldSize\":[{:?},{:?}]}}",
        corpse.archive,
        corpse.record,
        png.len(),
        world_size[0],
        world_size[1]
    ))
}

fn flip_rgba_columns(rgba: &mut [u8], width: usize, height: usize) {
    for row in rgba.chunks_mut(width * 4).take(height) {
        for x in 0..width / 2 {
            for channel in 0..4 {
                row.swap(x * 4 + channel, (width - 1 - x) * 4 + channel);
            }
        }
    }
}

fn crop_visible_rgba(rgba: &[u8], width: usize, height: usize) -> (usize, usize, Vec<u8>) {
    let mut min_x = width;
    let mut min_y = height;
    let mut max_x = 0usize;
    let mut max_y = 0usize;
    let mut found = false;
    for y in 0..height {
        for x in 0..width {
            if rgba[(y * width + x) * 4 + 3] == 0 {
                continue;
            }
            found = true;
            min_x = min_x.min(x);
            min_y = min_y.min(y);
            max_x = max_x.max(x);
            max_y = max_y.max(y);
        }
    }
    if !found {
        return (1, 1, vec![0; 4]);
    }
    let cropped_w = max_x - min_x + 1;
    let cropped_h = max_y - min_y + 1;
    let mut cropped = vec![0; cropped_w * cropped_h * 4];
    for y in 0..cropped_h {
        let source = ((min_y + y) * width + min_x) * 4;
        let destination = y * cropped_w * 4;
        cropped[destination..destination + cropped_w * 4]
            .copy_from_slice(&rgba[source..source + cropped_w * 4]);
    }
    (cropped_w, cropped_h, cropped)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn edited_entries_keep_tunables_across_regeneration() {
        let old = r#"{"enemies":[
            {"mobileId":0,"pivot":[0.6,0.0],"normalizedSize":[2.0,0.8],"edited":true,
             "states":{"attack":{"frameStart":72,"framesPerOrientation":6,"fps":13,"loop":false,"sequence":[0,1,2]}},
             "frames":[{"frame":0,"uvMin":[0,0],"uvMax":[0.1,0.1],"size":[1.6,1.5]}]},
            {"mobileId":1,"pivot":[0.5,0.0],"states":{"attack":{"fps":10,"loop":false}},
             "frames":[{"frame":0,"uvMin":[0,0],"uvMax":[0.1,0.1],"size":[2.0,1.7]}]}
        ]}"#;
        let new = r#"{"enemies":[
            {"mobileId":0,"pivot":[0.5,0.0],"normalizedSize":[1.6,0.8],
             "states":{"attack":{"frameStart":72,"framesPerOrientation":6,"fps":10,"loop":false,"sequence":[0,1,2,-1,3,4,5]}},
             "frames":[{"frame":0,"uvMin":[0,0],"uvMax":[0.2,0.2],"size":[1.6,1.5]}]},
            {"mobileId":1,"pivot":[0.5,0.0],"states":{"attack":{"fps":10,"loop":false}},
             "frames":[{"frame":0,"uvMin":[0,0],"uvMax":[0.2,0.2],"size":[2.0,1.7]}]}
        ]}"#
            .to_string();
        let merged = preserve_manifest_edits(Some(old), new, "enemy", false);
        let value: serde_json::Value = serde_json::from_str(&merged).expect("merged json");
        let rat = &value["enemies"][0];
        assert_eq!(rat["pivot"], serde_json::json!([0.6, 0.0]));
        assert_eq!(rat["states"]["attack"]["fps"], serde_json::json!(13));
        assert_eq!(
            rat["states"]["attack"]["sequence"],
            serde_json::json!([0, 1, 2]),
            "edited playback sequence preserved"
        );
        // Derived pixel layout always follows the fresh pack.
        assert_eq!(rat["frames"][0]["uvMax"], serde_json::json!([0.2, 0.2]));
        assert_eq!(rat["edited"], serde_json::json!(true));
        // Unmarked entries take freshly computed values.
        let imp = &value["enemies"][1];
        assert_eq!(imp["frames"][0]["uvMax"], serde_json::json!([0.2, 0.2]));
        assert!(imp.get("edited").is_none());
        // Clobber ignores all markers.
        let clobbered = preserve_manifest_edits(Some(old), merged, "enemy", true);
        let value: serde_json::Value = serde_json::from_str(&clobbered).expect("clobbered json");
        assert_eq!(value["enemies"][0]["pivot"], serde_json::json!([0.6, 0.0]));
    }
}
