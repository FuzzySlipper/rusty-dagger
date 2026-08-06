//! dagger-import: extract a Daggerfall dungeon from classic Arena2 data files
//! to a single GLB (textured by default, --untextured for a flat material).

mod dungeon;
mod glb;
mod meshjson;
mod png;

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
}

fn parse_args() -> Result<Args, String> {
    let mut arena2_dir = PathBuf::from("local/arena2");
    let mut region = 17usize;
    let mut location = "Privateer's Hold".to_string();
    let mut out = PathBuf::from("content/privateers-hold.glb");
    let mut textured = true;
    let mut format = "glb".to_string();
    let mut texture_dir: Option<PathBuf> = None;
    let mut table_mode = dungeon::TextureTableMode::Classic;
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
        "{{\n  \"schemaVersion\": 1,\n  \"location\": \"{}\",\n  \"startMarker\": {},\n  \"enterMarker\": {},\n  \"lightCount\": {},\n  \"flatCount\": {},\n  \"lights\": [{}],\n  \"billboards\": [{}],\n  \"enemies\": [{}],\n  \"doors\": [{}],\n  \"bounds\": {{\"min\": {:?}, \"max\": {:?}}}\n}}\n",
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
        publish_billboard_textures(dir, &args.arena2_dir, &output.scene.billboards);
        publish_enemy_atlases(dir, &args.arena2_dir, &output.scene.enemies);
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

/// Sprite PNGs are stored bottom-up: renderer-three samples v=0 at the
/// first PNG row and maps uvMin to the quad's bottom vertices, so a
/// top-down PNG renders upside down (and the contract forbids
/// uvMin.y > uvMax.y, so the flip must be in the pixels).
fn flip_rgba_rows(rgba: &mut [u8], width: usize, height: usize) {
    let stride = width * 4;
    for y in 0..height / 2 {
        for x in 0..stride {
            rgba.swap(y * stride + x, (height - 1 - y) * stride + x);
        }
    }
}

/// Decode unique billboard (archive, record) textures to transparent PNGs
/// (palette index 0 = transparent, the Daggerfall billboard rule) plus a
/// billboard manifest mapping each texture to its PNG sourcePath/hash/dims.
/// generate-project.py consumes this to stamp billboard sprite resources.
fn publish_billboard_textures(
    dir: &std::path::Path,
    arena2_dir: &std::path::Path,
    billboards: &[dungeon::BillboardFlat],
) {
    use arena2::palette::Palette;
    use arena2::texture::TextureFile;
    use std::collections::BTreeMap;

    let palette = Palette::load(&arena2_dir.join("PAL.PAL")).expect("PAL.PAL");
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

        // Pack into atlas: single frame = plain PNG; multi-frame = horizontal strip.
        let (atlas_w, atlas_h, rgba) = if all_frames.len() == 1 {
            let mut rgba = all_frames.into_iter().next().unwrap();
            flip_rgba_rows(&mut rgba, w, h);
            (w, h, rgba)
        } else {
            let fc = all_frames.len();
            let mut strip = vec![0u8; w * fc * h * 4];
            for (i, frame) in all_frames.iter().enumerate() {
                // Each frame is bottom-up in source; flip into atlas row order.
                let mut flipped = frame.clone();
                flip_rgba_rows(&mut flipped, w, h);
                // Copy each row of the frame into its column range in the strip.
                for y in 0..h {
                    let src = &flipped[y * w * 4..(y + 1) * w * 4];
                    let dst_start = (y * w * fc + i * w) * 4;
                    strip[dst_start..dst_start + w * 4].copy_from_slice(src);
                }
            }
            (w * fc, h, strip)
        };

        let png = crate::png::encode_rgba(atlas_w as u32, atlas_h as u32, &rgba);
        let slug = format!("billboard-{archive}-{record}");
        let file = format!("{slug}.png");
        std::fs::write(dir.join(&file), &png).expect("write billboard png");
        let hash = format!("sha256:{:x}", Sha256::digest(&png));

        // Manifest entry: single-frame records stay backward-compatible
        // (no frameCount/frames). Multi-frame records add frameCount, fps,
        // and per-frame UV rects so generate-project.py can build a
        // multi-frame spriteAtlas.
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
            "    {{\"archive\":{archive},\"record\":{record},\"path\":\"{file}\",\"sha256\":\"{hash}\",\"byteLength\":{},\"width\":{w},\"height\":{h},\"worldSize\":[{:?},{:?}]{}}}",
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
    std::fs::write(dir.join("billboard-manifest.json"), manifest)
        .expect("write billboard manifest");
    println!(
        "billboards:  {} unique textures ({} decode failures)",
        count, failures
    );
}

/// Decode and pack one directional sprite atlas per unique enemy mobile id:
/// 8 orientations × M move-state frames in a horizontal strip (orientation *
/// M + anim_frame layout), plus an enemy-manifest.json mapping each atlas to
/// its PNG sourcePath/hash/dims, per-frame UV rects, and DFU world sizes.
/// generate-project.py consumes this to stamp enemy sprite resources and atlas
/// frame descriptors. The AnimationService uses the 8×M layout so direction
/// changes preserve the current anim frame position.
fn publish_enemy_atlases(
    dir: &std::path::Path,
    arena2_dir: &std::path::Path,
    enemies: &[dungeon::EnemyScene],
) {
    use arena2::mobile::{mobile_type, record_world_size, MOVE_ANIMS};
    use arena2::palette::Palette;
    use arena2::texture::TextureFile;
    use std::collections::BTreeMap;

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
        let anims = MOVE_ANIMS; // Move-state animation (4-8 frames per record)
        let tex_path = arena2_dir.join(format!("TEXTURE.{:03}", mobile.texture_archive));
        let Ok(tex) = TextureFile::load(&tex_path) else {
            failures += 1;
            eprintln!(
                "enemy atlas warning: TEXTURE.{:03} unreadable ({} skipped)",
                mobile.texture_archive, mobile.name
            );
            continue;
        };

        // Determine the frame count per orientation record (all move records
        // for a given enemy carry the same frame count).
        let first_rec = anims[0].record as usize;
        let anim_frame_count = tex
            .record_info(first_rec)
            .map(|i| i.frame_count.max(1) as usize)
            .unwrap_or(1);

        // Decode all frames for all 8 orientations.
        // Each entry: (flip, w, h, indexed_pixels, world_size).
        let mut decoded: Vec<(bool, usize, usize, Vec<u8>, [f32; 2])> = Vec::new();
        let mut failed = false;
        for anim in anims.iter() {
            let rec = anim.record as usize;
            let info = match tex.record_info(rec) {
                Some(i) => i,
                None => {
                    failures += 1;
                    eprintln!(
                        "enemy atlas warning: TEXTURE.{:03} rec {} missing",
                        mobile.texture_archive, rec
                    );
                    failed = true;
                    break;
                }
            };
            let size = record_world_size(info.width, info.height, info.scale_x, info.scale_y);
            for f in 0..anim_frame_count {
                match tex.frame_pixels(rec, f) {
                    Ok((w, h, indexed)) => {
                        decoded.push((anim.flip, w, h, indexed, size));
                    }
                    Err(e) => {
                        failures += 1;
                        eprintln!(
                            "enemy atlas warning: TEXTURE.{:03} rec {} frame {f}: {e}",
                            mobile.texture_archive, rec
                        );
                        failed = true;
                        break;
                    }
                }
            }
            if failed {
                break;
            }
        }
        if failed {
            continue;
        }

        let total_cells = 8 * anim_frame_count;
        let cell_w: usize = decoded.iter().map(|d| d.1).max().unwrap_or(0);
        let cell_h: usize = decoded.iter().map(|d| d.2).max().unwrap_or(0);
        let atlas_w: usize = cell_w * total_cells;
        let atlas_h: usize = cell_h;
        let mut atlas = vec![0u8; atlas_w * atlas_h * 4];
        let mut frame_entries: Vec<String> = Vec::new();
        let mut x0 = 0usize;
        for (idx, (flip, w, h, indexed, size)) in decoded.iter().enumerate() {
            let mut rgba = palette.to_rgba_transparent(indexed);
            if *flip {
                for row in rgba.chunks_mut(w * 4) {
                    for px in 0..w / 2 {
                        for c in 0..4 {
                            row.swap(px * 4 + c, (w - 1 - px) * 4 + c);
                        }
                    }
                }
            }
            let dx = x0 + (cell_w - w) / 2;
            let dy = cell_h - h;
            for (row_i, row) in rgba.chunks(w * 4).enumerate() {
                let dst = ((dy + row_i) * atlas_w + dx) * 4;
                atlas[dst..dst + w * 4].copy_from_slice(row);
            }
            frame_entries.push(format!(
                "      {{\"frame\":{idx},\"uvMin\":[{},0],\"uvMax\":[{},1],\"size\":[{:?},{:?}]}}",
                x0 as f64 / atlas_w as f64,
                (x0 + cell_w) as f64 / atlas_w as f64,
                size[0],
                size[1]
            ));
            x0 += cell_w;
        }
        let png = {
            let mut flipped = atlas.clone();
            flip_rgba_rows(&mut flipped, atlas_w, atlas_h);
            crate::png::encode_rgba(atlas_w as u32, atlas_h as u32, &flipped)
        };
        let slug = format!("enemy-{}-atlas", mobile.id);
        let file = format!("{slug}.png");
        std::fs::write(dir.join(&file), &png).expect("write enemy atlas png");
        let hash = format!("sha256:{:x}", Sha256::digest(&png));
        entries.push(format!(
            "    {{\"mobileId\":{},\"name\":\"{}\",\"archive\":{},\"path\":\"{file}\",\"sha256\":\"{hash}\",\"byteLength\":{},\"width\":{atlas_w},\"height\":{atlas_h},\"frames\":[\n{}\n    ]}}",
            mobile.id, mobile.name, mobile.texture_archive,
            png.len(),
            frame_entries.join(",\n")
        ));
        count += 1;
    }
    let manifest = format!(
        "{{\n  \"schemaVersion\": 1,\n  \"enemies\": [\n{}\n  ]\n}}\n",
        entries.join(",\n")
    );
    std::fs::write(dir.join("enemy-manifest.json"), manifest).expect("write enemy manifest");
    println!(
        "enemies:     {} atlases ({} decode failures)",
        count, failures
    );
}
