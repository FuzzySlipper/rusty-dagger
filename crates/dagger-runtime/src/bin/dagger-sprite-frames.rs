//! dagger-sprite-frames: emit runtime-authoritative directional sprite
//! assignments (arena2::mobile semantics via dagger-runtime) for a set of
//! camera poses. The engine-render-check harness consumes this instead of
//! re-implementing the orientation math in JavaScript.
//!
//! usage: dagger-sprite-frames <scene.json> <cam-x,cam-y,cam-z>...
//! stdout: {"enemyCount":N,"poses":[{"camera":[...],"assignments":[{"index":I,"frame":F,"rotation":[x,y,z,w]}]}]}

use dagger_runtime::evaluate_directional;

fn main() {
    let mut args = std::env::args().skip(1);
    let scene_path = args.next().unwrap_or_else(|| {
        eprintln!("usage: dagger-sprite-frames <scene.json> <cam-x,cam-y,cam-z>...");
        std::process::exit(2);
    });
    let mut cameras = Vec::new();
    for arg in args {
        let parts: Vec<&str> = arg.split(',').collect();
        if parts.len() != 3 {
            eprintln!("bad camera {arg:?}; expected x,y,z");
            std::process::exit(2);
        }
        let parse = |s: &str| {
            s.parse::<f32>()
                .unwrap_or_else(|_| {
                    eprintln!("bad camera component {s:?}");
                    std::process::exit(2);
                })
        };
        cameras.push([parse(parts[0]), parse(parts[1]), parse(parts[2])]);
    }
    if cameras.is_empty() {
        eprintln!("at least one camera is required");
        std::process::exit(2);
    }

    let text = std::fs::read_to_string(&scene_path).unwrap_or_else(|e| {
        eprintln!("read {scene_path}: {e}");
        std::process::exit(1);
    });
    let scene: serde_json::Value = serde_json::from_str(&text).unwrap_or_else(|e| {
        eprintln!("parse {scene_path}: {e}");
        std::process::exit(1);
    });
    let enemies = scene
        .get("enemies")
        .and_then(serde_json::Value::as_array)
        .cloned()
        .unwrap_or_default();

    let mut positions = Vec::with_capacity(enemies.len());
    for enemy in &enemies {
        let pos = enemy
            .get("position")
            .and_then(serde_json::Value::as_array)
            .and_then(|a| {
                Some([
                    a.first()?.as_f64()? as f32,
                    a.get(1)?.as_f64()? as f32,
                    a.get(2)?.as_f64()? as f32,
                ])
            })
            .unwrap_or_else(|| {
                eprintln!("enemy without position in {scene_path}");
                std::process::exit(1);
            });
        positions.push(pos);
    }

    let mut poses = Vec::new();
    for camera in &cameras {
        let assignments: Vec<String> = positions
            .iter()
            .enumerate()
            .map(|(index, position)| {
                let a = evaluate_directional(*position, *camera);
                format!(
                    "{{\"index\":{index},\"frame\":{},\"rotation\":[{:?},{:?},{:?},{:?}]}}",
                    a.frame, a.rotation[0], a.rotation[1], a.rotation[2], a.rotation[3]
                )
            })
            .collect();
        poses.push(format!(
            "{{\"camera\":[{:?},{:?},{:?}],\"assignments\":[{}]}}",
            camera[0],
            camera[1],
            camera[2],
            assignments.join(",")
        ));
    }
    println!(
        "{{\"enemyCount\":{},\"poses\":[{}]}}",
        positions.len(),
        poses.join(",")
    );
}
