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
}

fn parse_args() -> Result<Args, String> {
    let mut arena2_dir = PathBuf::from("local/arena2");
    let mut region = 17usize;
    let mut location = "Privateer's Hold".to_string();
    let mut out = PathBuf::from("content/privateers-hold.glb");
    let mut textured = true;
    let mut format = "glb".to_string();
    let mut texture_dir: Option<PathBuf> = None;
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
    })
}

fn usage() -> String {
    "usage: dagger-import [--arena2 DIR] [--region N] [--location NAME] [--out FILE] [--format glb|mesh-json] [--texture-dir DIR] [--untextured]"
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
        _ => glb::write_glb(&name, &output.primitives, &output.textures),
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
        "{{\n  \"schemaVersion\": 1,\n  \"location\": \"{}\",\n  \"startMarker\": {},\n  \"enterMarker\": {},\n  \"lightCount\": {},\n  \"flatCount\": {},\n  \"lights\": [{}],\n  \"billboards\": [{}],\n  \"bounds\": {{\"min\": {:?}, \"max\": {:?}}}\n}}\n",
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
        let Ok((w, h, indexed)) = tex.frame_pixels(*record as usize, 0) else {
            failures += 1;
            eprintln!("billboard texture warning: TEXTURE.{archive:03} rec {record} decode failed");
            continue;
        };
        let rgba = palette.to_rgba_transparent(&indexed);
        let png = crate::png::encode_rgba(w as u32, h as u32, &rgba);
        let slug = format!("billboard-{archive}-{record}");
        let file = format!("{slug}.png");
        std::fs::write(dir.join(&file), &png).expect("write billboard png");
        let hash = format!("sha256:{:x}", Sha256::digest(&png));
        entries.push(format!(
            "    {{\"archive\":{archive},\"record\":{record},\"path\":\"{file}\",\"sha256\":\"{hash}\",\"byteLength\":{},\"width\":{w},\"height\":{h}}}",
            png.len()
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
