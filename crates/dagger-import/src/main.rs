//! dagger-import: extract a Daggerfall dungeon from classic Arena2 data files
//! to a single GLB (textured by default, --untextured for a flat material).

mod dungeon;
mod glb;
mod meshjson;
mod png;

use std::path::PathBuf;

struct Args {
    arena2_dir: PathBuf,
    region: usize,
    location: String,
    out: PathBuf,
    textured: bool,
    format: String,
}

fn parse_args() -> Result<Args, String> {
    let mut arena2_dir = PathBuf::from("local/arena2");
    let mut region = 17usize;
    let mut location = "Privateer's Hold".to_string();
    let mut out = PathBuf::from("content/privateers-hold.glb");
    let mut textured = true;
    let mut format = "glb".to_string();
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
            "--untextured" => textured = false,
            "--help" | "-h" => return Err(usage()),
            other => return Err(format!("unknown arg {other}\n{}", usage())),
        }
    }
    if format != "glb" && format != "mesh-json" {
        return Err(format!("--format must be glb or mesh-json, got {format:?}"));
    }
    Ok(Args { arena2_dir, region, location, out, textured, format })
}

fn usage() -> String {
    "usage: dagger-import [--arena2 DIR] [--region N] [--location NAME] [--out FILE] [--untextured]"
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

    let output = match dungeon::build_dungeon(&args.arena2_dir, args.region, &args.location, args.textured) {
        Ok(o) => o,
        Err(e) => {
            eprintln!("dagger-import: {e}");
            std::process::exit(1);
        }
    };

    let s = &output.stats;
    println!("location:    {} (region {})", args.location, args.region);
    println!("blocks:      {}", s.blocks);
    println!("models:      {} used, {} missing", s.models_used, s.models_missing);
    println!("verts:       {}", s.verts);
    println!("tris:        {}", s.tris);
    println!("primitives:  {}", output.primitives.len());
    println!("textures:    {}", s.textures);
    for f in &s.texture_failures {
        println!("texture warning: {f}");
    }
    println!(
        "bounds:      [{:.2},{:.2},{:.2}] .. [{:.2},{:.2},{:.2}]",
        s.bounds_min[0], s.bounds_min[1], s.bounds_min[2],
        s.bounds_max[0], s.bounds_max[1], s.bounds_max[2]
    );

    let name = args.location.replace('\'', "").replace(' ', "-").to_lowercase();
    let bytes = match args.format.as_str() {
        "mesh-json" => meshjson::write_mesh_json(&name, &output.primitives, &output.textures).into_bytes(),
        _ => glb::write_glb(&name, &output.primitives, &output.textures),
    };
    if let Some(parent) = args.out.parent() {
        if !parent.as_os_str().is_empty() {
            std::fs::create_dir_all(parent).expect("create output dir");
        }
    }
    std::fs::write(&args.out, &bytes).expect("write output");
    println!("wrote:       {} ({} bytes)", args.out.display(), bytes.len());
}
