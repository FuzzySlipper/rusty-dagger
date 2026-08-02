//! Real-project collision and controller proof for Privateer's Hold.
//!
//! This command intentionally reads the committed generated project and route
//! rather than a fixture. It proves grounding, route traversal, blocked motion,
//! and that changing the admitted material voxels changes the authoritative
//! outcome.

use std::env;
use std::fs;

use dagger_runtime::{DaggerRuntime, PlayerControlFact, ResolvedPlayerAction};
use serde::Deserialize;
use serde_json::Value;

const BODY_HALF: f32 = 0.25;
const BLOCK_SIDE: f32 = 51.2;

#[derive(Debug, Deserialize)]
struct Route {
    cell: f32,
    waypoints: Vec<[f32; 3]>,
}

fn block_of(x: f32, z: f32) -> (i32, i32) {
    (
        (x / BLOCK_SIDE).floor() as i32,
        (z / BLOCK_SIDE).floor() as i32,
    )
}

fn supported(runtime: &DaggerRuntime, eye: [f32; 3], window: f32) -> Option<f32> {
    // Probe the actual kinematic footprint edges. Using 0.24m leaves a
    // 0.01m gap at a voxel boundary and can falsely report a supported body
    // as airborne at route corners.
    const OFFSETS: [f32; 3] = [0.0, BODY_HALF, -BODY_HALF];
    let bottom = eye[1] - BODY_HALF;
    let steps = (window / 0.025).ceil() as i32;
    for step in 0..=steps {
        let probe_y = bottom - step as f32 * 0.025;
        for ox in OFFSETS {
            for oz in OFFSETS {
                if runtime.collision_scene().contains_point([
                    (eye[0] + ox) as f64,
                    probe_y as f64,
                    (eye[2] + oz) as f64,
                ]) {
                    return Some(probe_y);
                }
            }
        }
    }
    None
}

fn settle(runtime: &mut DaggerRuntime, actions: usize) -> Vec<f32> {
    let mut trace = Vec::with_capacity(actions + 1);
    trace.push(runtime.player_position().expect("player position").y);
    for _ in 0..actions {
        runtime
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: 0.0,
                right: 0.0,
            })
            .expect("idle action");
        trace.push(runtime.player_position().expect("player position").y);
    }
    trace
}

fn steer_toward(runtime: &mut DaggerRuntime, target: [f32; 3]) {
    let position = runtime.player_position().expect("player position");
    let dx = target[0] - position.x;
    let dz = target[2] - position.z;
    let desired = (-dx).atan2(-dz).to_degrees();
    for _ in 0..40 {
        let current = runtime.player_state().yaw_degrees;
        let delta = (desired - current + 180.0).rem_euclid(360.0) - 180.0;
        if delta.abs() < 0.5 {
            return;
        }
        runtime
            .apply_player_action(ResolvedPlayerAction::Look {
                yaw_delta: (delta / 12.0).clamp(-1.0, 1.0),
                pitch_delta: 0.0,
            })
            .expect("steer look");
    }
    panic!("steering did not converge for {target:?}");
}

fn displace_voxel_floor(document: &str, delta: i64) -> String {
    let mut value: Value = serde_json::from_str(document).expect("parse project document");
    if let Some(scenes) = value.get_mut("scenes").and_then(Value::as_array_mut) {
        for scene in scenes {
            if let Some(voxels) = scene
                .get_mut("voxelEnvironment")
                .and_then(|environment| environment.get_mut("materialVoxels"))
                .and_then(Value::as_array_mut)
            {
                for voxel in voxels {
                    if let Some(address) = voxel.get_mut("address").and_then(Value::as_array_mut) {
                        if let Some(y) = address.get(1).and_then(Value::as_i64) {
                            address[1] = Value::from(y + delta);
                        }
                    }
                }
            }
        }
    }
    serde_json::to_string(&value).expect("encode displaced project")
}

fn delete_voxel_column(document: &str, column: [i64; 2]) -> String {
    let mut value: Value = serde_json::from_str(document).expect("parse project document");
    if let Some(scenes) = value.get_mut("scenes").and_then(Value::as_array_mut) {
        for scene in scenes {
            if let Some(voxels) = scene
                .get_mut("voxelEnvironment")
                .and_then(|environment| environment.get_mut("materialVoxels"))
                .and_then(Value::as_array_mut)
            {
                voxels.retain(|voxel| {
                    let Some(address) = voxel.get("address").and_then(Value::as_array) else {
                        return true;
                    };
                    address.first().and_then(Value::as_i64) != Some(column[0])
                        || address.get(2).and_then(Value::as_i64) != Some(column[1])
                });
            }
        }
    }
    serde_json::to_string(&value).expect("encode column-deleted project")
}

fn add_adversarial_wall(document: &str) -> String {
    let mut value: Value = serde_json::from_str(document).expect("parse project document");
    if let Some(scenes) = value.get_mut("scenes").and_then(Value::as_array_mut) {
        for scene in scenes {
            if let Some(player) = scene
                .get_mut("entities")
                .and_then(Value::as_array_mut)
                .and_then(|entities| {
                    entities
                        .iter_mut()
                        .find(|entity| entity.get("id") == Some(&Value::from(1)))
                })
            {
                if let Some(controller) = player.get_mut("playerController") {
                    controller["fallSpeedUnitsPerSecond"] = Value::from(0.1);
                }
            }
            if let Some(voxels) = scene
                .get_mut("voxelEnvironment")
                .and_then(|environment| environment.get_mut("materialVoxels"))
                .and_then(Value::as_array_mut)
            {
                // A tall wall immediately in front of the authored spawn. It
                // is derived from the real project and exists only for this
                // adversarial controller probe.
                for x in 54..61 {
                    for y in 77..91 {
                        voxels.push(serde_json::json!({
                            "address": [x, y, -24],
                            "materialSlot": 1,
                        }));
                    }
                }
            }
        }
    }
    serde_json::to_string(&value).expect("encode adversarial project")
}

fn main() {
    let mut args = env::args().skip(1);
    let project_path = args.next().unwrap_or_else(|| {
        format!(
            "{}/../../content/projects/privateers-hold.project.json",
            env!("CARGO_MANIFEST_DIR")
        )
    });
    let route_path = args
        .next()
        .unwrap_or_else(|| project_path.replace(".project.json", ".route.json"));
    let document = fs::read_to_string(&project_path).expect("read project document");
    let route: Route =
        serde_json::from_str(&fs::read_to_string(&route_path).expect("read route document"))
            .expect("parse route document");
    assert!(route.waypoints.len() >= 8, "route is too short");

    let mut runtime = DaggerRuntime::from_project_json(&document).expect("admit project");
    let spawn = runtime.player_position().expect("player position");
    println!(
        "project admitted: player={} voxels={} spawn=[{:.3}, {:.3}, {:.3}]",
        runtime.player().raw(),
        runtime.collision_scene().solid_voxel_count(),
        spawn.x,
        spawn.y,
        spawn.z
    );
    let mut failures = Vec::new();

    let settle_trace = settle(&mut runtime, 30);
    let settled = runtime.player_position().expect("settled position");
    let fell = settle_trace.windows(2).any(|pair| pair[1] < pair[0] - 0.05);
    let stable = settle_trace
        .iter()
        .rev()
        .take(4)
        .collect::<Vec<_>>()
        .windows(2)
        .all(|pair| (*pair[1] - *pair[0]).abs() < 0.001);
    let support = supported(&runtime, settled.to_array(), 0.45);
    println!(
        "settle: spawn_y={:.3} settled_y={:.3} fell={} stable={} support={support:?}",
        spawn.y, settled.y, fell, stable
    );
    if !fell {
        failures.push("the player did not descend during idle settle".to_string());
    }
    if !stable {
        failures.push("the player did not become stable on support".to_string());
    }
    if support.is_none() {
        failures.push("settled player has no voxel support within 0.45m".to_string());
    }
    let rest = settle(&mut runtime, 3);
    if (rest[3] - rest[0]).abs() > 0.001 {
        failures.push(format!("player moved after settling: {rest:?}"));
    }

    let spawn_block = block_of(spawn.x, spawn.z);
    let route_min_y = route
        .waypoints
        .iter()
        .map(|waypoint| waypoint[1])
        .fold(f32::INFINITY, f32::min);
    let mut moves = 0usize;
    let mut blocked = 0usize;
    let mut traversal_failed = false;
    for (index, waypoint) in route.waypoints.iter().enumerate() {
        let mut reached = false;
        for _ in 0..8 {
            let before = runtime.player_position().expect("player position");
            let distance =
                ((waypoint[0] - before.x).powi(2) + (waypoint[2] - before.z).powi(2)).sqrt();
            if distance < 0.35 {
                reached = true;
                break;
            }
            steer_toward(&mut runtime, *waypoint);
            let receipt = runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 1.0,
                    right: 0.0,
                })
                .expect("forward action");
            moves += 1;
            if receipt
                .facts
                .iter()
                .any(|fact| matches!(fact, PlayerControlFact::Blocked { .. }))
            {
                blocked += 1;
            }
            let after = runtime.player_position().expect("player position");
            if after.y < route_min_y - 2.5 {
                failures.push(format!("route sank below envelope at waypoint {index}"));
                traversal_failed = true;
                break;
            }
            if supported(&runtime, after.to_array(), 1.35).is_none() && after.y >= before.y - 0.05 {
                failures.push(format!(
                    "route lost support without falling at waypoint {index}"
                ));
                traversal_failed = true;
                break;
            }
        }
        if traversal_failed {
            break;
        }
        if !reached {
            failures.push(format!("waypoint {index} was not reached"));
            break;
        }
    }
    let end = runtime.player_position().expect("final position");
    let end_block = block_of(end.x, end.z);
    let travelled = ((end.x - spawn.x).powi(2) + (end.z - spawn.z).powi(2)).sqrt();
    println!(
        "route: waypoints={} moves={} blocked={} end=[{:.2}, {:.2}, {:.2}] blocks={spawn_block:?}->{end_block:?} distance={travelled:.1}m",
        route.waypoints.len(), moves, blocked, end.x, end.y, end.z
    );
    if end_block == spawn_block || travelled < 20.0 {
        failures.push(format!(
            "route did not cross the dungeon (end block {end_block:?}, distance {travelled:.1}m)"
        ));
    }

    let adversarial_document = add_adversarial_wall(&document);
    let mut blocked_runtime =
        DaggerRuntime::from_project_json(&adversarial_document).expect("admit adversarial project");
    settle(&mut blocked_runtime, 30);
    let mut blocked_actions = 0usize;
    let mut horizontal_drift = 0.0_f32;
    let mut maximum_upward_displacement = 0.0_f32;
    for _ in 0..2 {
        let before = blocked_runtime
            .player_position()
            .expect("adversarial player position");
        let receipt = blocked_runtime
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: 1.0,
                right: 0.0,
            })
            .expect("adversarial forward action");
        let after = blocked_runtime
            .player_position()
            .expect("adversarial player position");
        if receipt
            .facts
            .iter()
            .any(|fact| matches!(fact, PlayerControlFact::Blocked { .. }))
        {
            blocked_actions += 1;
        }
        horizontal_drift += ((after.x - before.x).powi(2) + (after.z - before.z).powi(2)).sqrt();
        maximum_upward_displacement = maximum_upward_displacement.max(after.y - before.y);
    }
    println!(
        "negative controller boundary: blocked_actions={} horizontal_drift={horizontal_drift:.4} max_upward={maximum_upward_displacement:.4}",
        blocked_actions
    );
    if blocked_actions != 2 {
        failures.push(format!(
            "adversarial wall did not report Blocked for both controller actions: {blocked_actions}"
        ));
    }
    if horizontal_drift > 0.001 || maximum_upward_displacement > 0.001 {
        failures.push(format!(
            "blocked controller input changed coherent transform: horizontal_drift={horizontal_drift:.4} max_upward={maximum_upward_displacement:.4}"
        ));
    }

    let displaced = displace_voxel_floor(&document, -4);
    let mut displaced_runtime =
        DaggerRuntime::from_project_json(&displaced).expect("admit displaced project");
    let displaced_trace = settle(&mut displaced_runtime, 40);
    let displaced_y = *displaced_trace.last().expect("displaced trace");
    let displaced_support = supported(&displaced_runtime, settled.to_array(), 0.45);
    println!(
        "negative floor displacement: y={displaced_y:.3} support-at-original-settle={displaced_support:?}"
    );
    if displaced_y > settled.y - 1.5 || displaced_support.is_some() {
        failures.push("moving the authored floor did not change authoritative support".to_string());
    }

    let outside = runtime.collision_scene().contains_point([
        200.0,
        (settled.y - 0.5) as f64,
        settled.z as f64,
    ]);
    println!("negative outside bounds: occupied={outside}");
    if outside {
        failures.push("collision scene occupies an outside probe".to_string());
    }

    let midpoint = route.waypoints[route.waypoints.len() / 2];
    let column = [
        (midpoint[0] / route.cell).floor() as i64,
        (midpoint[2] / route.cell).floor() as i64,
    ];
    let deleted = delete_voxel_column(&document, column);
    let deleted_runtime =
        DaggerRuntime::from_project_json(&deleted).expect("admit deleted project");
    let mut column_occupied = false;
    let mut probe_y = 60.0;
    while probe_y > -10.0 {
        if deleted_runtime.collision_scene().contains_point([
            midpoint[0] as f64,
            probe_y,
            midpoint[2] as f64,
        ]) {
            column_occupied = true;
            break;
        }
        probe_y -= 0.05;
    }
    println!("negative route-column deletion: column={column:?} occupied={column_occupied}");
    if column_occupied {
        failures.push("deleting a route column left collision support behind".to_string());
    }

    runtime
        .apply_player_action(ResolvedPlayerAction::Look {
            yaw_delta: 0.5,
            pitch_delta: 0.0,
        })
        .expect("look action");
    if runtime.player_position().expect("look position") != end {
        failures.push("look changed player translation".to_string());
    }

    if failures.is_empty() {
        println!("DAGGER WALKTHROUGH PASSED");
    } else {
        println!("DAGGER WALKTHROUGH FAILED:");
        for failure in failures {
            println!(" - {failure}");
        }
        std::process::exit(1);
    }
}
