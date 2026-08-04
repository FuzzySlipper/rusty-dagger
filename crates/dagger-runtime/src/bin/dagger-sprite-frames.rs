//! dagger-sprite-frames: runtime-authoritative directional sprite frames
//! (arena2::mobile semantics via dagger-runtime) for camera poses, so
//! consumers never re-implement the orientation math.
//!
//! One-shot: dagger-sprite-frames <scene.json> <cam-x,cam-y,cam-z>...
//!   stdout: {"enemyCount":N,"poses":[{"camera":[...],"assignments":[{"index":I,"frame":F}]}]}
//!
//! Live server: dagger-sprite-frames <scene.json> --serve <host:port>
//!   GET /assignments?cam=x,y,z -> {"assignments":[{"index":I,"frame":F}]}
//!   The engine-render-check live driver polls this per animation frame.

use dagger_runtime::evaluate_directional;
use std::io::{BufRead, BufReader, Write};
use std::net::TcpListener;

fn parse_camera(arg: &str) -> [f32; 3] {
    parse_camera_opt(arg).unwrap_or_else(|| {
        eprintln!("bad camera {arg:?}; expected x,y,z");
        std::process::exit(2);
    })
}

fn parse_camera_opt(arg: &str) -> Option<[f32; 3]> {
    let parts: Vec<&str> = arg.split(',').collect();
    if parts.len() != 3 {
        return None;
    }
    let mut out = [0.0f32; 3];
    for (i, part) in parts.iter().enumerate() {
        out[i] = part.parse().ok()?;
    }
    Some(out)
}

fn load_enemy_positions(scene_path: &str) -> Vec<[f32; 3]> {
    let text = std::fs::read_to_string(scene_path).unwrap_or_else(|e| {
        eprintln!("read {scene_path}: {e}");
        std::process::exit(1);
    });
    let scene: serde_json::Value = serde_json::from_str(&text).unwrap_or_else(|e| {
        eprintln!("parse {scene_path}: {e}");
        std::process::exit(1);
    });
    scene
        .get("enemies")
        .and_then(serde_json::Value::as_array)
        .cloned()
        .unwrap_or_default()
        .iter()
        .map(|enemy| {
            enemy
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
                })
        })
        .collect()
}

fn assignments_json(positions: &[[f32; 3]], camera: [f32; 3]) -> String {
    let entries: Vec<String> = positions
        .iter()
        .enumerate()
        .map(|(index, position)| {
            format!(
                "{{\"index\":{index},\"frame\":{}}}",
                evaluate_directional(*position, camera)
            )
        })
        .collect();
    format!("[{}]", entries.join(","))
}

fn serve(positions: &[[f32; 3]], addr: &str) {
    let listener = TcpListener::bind(addr).unwrap_or_else(|e| {
        eprintln!("bind {addr}: {e}");
        std::process::exit(1);
    });
    eprintln!("dagger-sprite-frames serving {} enemies on {addr}", positions.len());
    for stream in listener.incoming() {
        let Ok(mut stream) = stream else { continue };
        let mut reader = BufReader::new(stream.try_clone().unwrap());
        let mut line = String::new();
        if reader.read_line(&mut line).is_err() {
            continue;
        }
        // GET /assignments?cam=x,y,z HTTP/1.1
        let camera = line
            .split_whitespace()
            .nth(1)
            .and_then(|path| path.strip_prefix("/assignments?cam="))
            .and_then(parse_camera_opt);
        let (status, body) = match camera {
            Some(cam) => (
                "200 OK",
                format!("{{\"assignments\":{}}}", assignments_json(positions, cam)),
            ),
            None => ("400 Bad Request", "{\"error\":\"expected /assignments?cam=x,y,z\"}".into()),
        };
        let response = format!(
            "HTTP/1.1 {status}\r\ncontent-type: application/json\r\naccess-control-allow-origin: *\r\ncontent-length: {}\r\nconnection: close\r\n\r\n{body}",
            body.len()
        );
        let _ = stream.write_all(response.as_bytes());
    }
}

fn main() {
    let mut args = std::env::args().skip(1);
    let scene_path = args.next().unwrap_or_else(|| {
        eprintln!(
            "usage: dagger-sprite-frames <scene.json> <cam-x,cam-y,cam-z>...\n       dagger-sprite-frames <scene.json> --serve <host:port>"
        );
        std::process::exit(2);
    });
    let positions = load_enemy_positions(&scene_path);
    let rest: Vec<String> = args.collect();
    if rest.first().map(String::as_str) == Some("--serve") {
        let addr = rest.get(1).map(String::as_str).unwrap_or("127.0.0.1:4193");
        serve(&positions, addr);
        return;
    }
    if rest.is_empty() {
        eprintln!("at least one camera is required");
        std::process::exit(2);
    }
    let mut poses = Vec::new();
    for arg in &rest {
        let camera = parse_camera(arg);
        poses.push(format!(
            "{{\"camera\":[{:?},{:?},{:?}],\"assignments\":{}}}",
            camera[0],
            camera[1],
            camera[2],
            assignments_json(&positions, camera)
        ));
    }
    println!(
        "{{\"enemyCount\":{},\"poses\":[{}]}}",
        positions.len(),
        poses.join(",")
    );
}
