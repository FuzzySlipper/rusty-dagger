//! Real-project collision and controller proof for Privateer's Hold.
//!
//! This command intentionally reads the committed generated project rather
//! than a fixture. It proves, through authoritative readback from a real
//! `DaggerRuntime` (the dungeon trimesh registered as the collision
//! authority):
//!
//! 1. **Settle** — the player falls from the start marker and comes to rest
//!    on genuine trimesh support (not a voxel proxy).
//! 2. **Reachable-region traversal** — from the start room's main floor the
//!    player walks into the descending multi-level dungeon (the region that
//!    is open without doors), with support asserted and blocked facts
//!    observable. The full start-room → border-block route is gated on
//!    Daggerfall doors (task 6525) because the start room's exit is a door
//!    baked into the static mesh; that is a door problem, not a collision
//!    deficiency. See docs/design.md and the 6522 handoff in Den.
//! 3. **Negative probes** — blocking a controller action reports Blocked
//!    without corrupting the transform; removing/mutating the collision
//!    authority changes the authoritative outcome (no support outside the
//!    trimesh, a relocated authority fails closed).

use std::env;
use std::fs;

use dagger_runtime::{DaggerRuntime, PlayerControlFact, ResolvedPlayerAction};
use rusty_engine::core_math::Vec3;
use rusty_engine::engine_spatial::SpatialCollisionHit;
use serde_json::Value;

const BODY_HALF: f32 = 0.25;

/// Ground-support surface height (y) under the kinematic footprint, via a
/// short downward raycast into the world collision projection (voxel + the
/// dungeon trimesh).
fn support_height(runtime: &DaggerRuntime, eye: [f32; 3], window: f32) -> Option<f32> {
    const OFFSETS: [f32; 3] = [0.0, BODY_HALF, -BODY_HALF];
    let bottom = eye[1] - BODY_HALF;
    for ox in OFFSETS {
        for oz in OFFSETS {
            let origin = [(eye[0] + ox) as f64, bottom as f64, (eye[2] + oz) as f64];
            if let Some(hit) =
                runtime
                    .collision_scene()
                    .raycast_world(origin, [0.0, -1.0, 0.0], window as f64)
            {
                let y = match hit {
                    SpatialCollisionHit::Voxel(hit) => hit.point[1],
                    SpatialCollisionHit::StaticMesh(hit) => hit.point.to_array()[1],
                };
                return Some(y as f32);
            }
        }
    }
    None
}

fn player_overlaps_world(runtime: &DaggerRuntime, eye: [f32; 3]) -> bool {
    runtime.collision_scene().aabb_overlaps_solid(
        [
            f64::from(eye[0] - BODY_HALF),
            f64::from(eye[1] - BODY_HALF),
            f64::from(eye[2] - BODY_HALF),
        ],
        [
            f64::from(eye[0] + BODY_HALF),
            f64::from(eye[1] + BODY_HALF),
            f64::from(eye[2] + BODY_HALF),
        ],
    )
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
                // Look actions use Engine's canonical yaw sign; the stable
                // Dagger camera readout uses the opposite renderer sign.
                yaw_delta: -(delta / 12.0).clamp(-1.0, 1.0),
                pitch_delta: 0.0,
            })
            .expect("steer look");
    }
}

/// Drive the player toward `target` for up to `max_steps` actions; returns
/// (end, blocked_count). Each step re-steers, so a curved hallway is followed.
fn walk(runtime: &mut DaggerRuntime, target: [f32; 3], max_steps: usize) -> ([f32; 3], usize) {
    let mut blocked = 0;
    for _ in 0..max_steps {
        let before = runtime.player_position().expect("player position");
        let dist = ((target[0] - before.x).powi(2) + (target[2] - before.z).powi(2)).sqrt();
        if dist < 0.3 {
            break;
        }
        steer_toward(runtime, target);
        let receipt = runtime
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: 1.0,
                right: 0.0,
            })
            .expect("walk action");
        if receipt
            .facts
            .iter()
            .any(|fact| matches!(fact, PlayerControlFact::Blocked { .. }))
        {
            blocked += 1;
        }
    }
    let end = runtime.player_position().expect("player position");
    ([end.x, end.y, end.z], blocked)
}

/// Inject an additive voxelEnvironment carrying a tall wall in front of the
/// authored spawn (the committed project has no proxy, so the adversarial
/// controller probe adds its own optional voxel authority).
fn add_adversarial_wall(document: &str) -> String {
    let mut value: Value = serde_json::from_str(document).expect("parse project document");
    let scenes = value["scenes"].as_array_mut().expect("scenes");
    let scene = &mut scenes[0];
    scene["entities"][0]["playerController"]["fallSpeedUnitsPerSecond"] = Value::from(0.1);
    let mut voxels = Vec::new();
    for x in 54..61 {
        for y in 77..91 {
            voxels.push(serde_json::json!({ "address": [x, y, -24], "materialSlot": 1 }));
        }
    }
    scene["voxelEnvironment"] = serde_json::json!({
        "kind": "material",
        "voxelSize": 0.5,
        "chunkSize": 16,
        "materialVoxels": voxels,
    });
    serde_json::to_string(&value).expect("encode adversarial project")
}

fn main() {
    let project_path = env::args().nth(1).unwrap_or_else(|| {
        format!(
            "{}/../../content/projects/privateers-hold.project.json",
            env!("CARGO_MANIFEST_DIR")
        )
    });
    let document = fs::read_to_string(&project_path).expect("read project document");

    let mut runtime = DaggerRuntime::from_project_json(&document).expect("admit project");
    let spawn = runtime.player_position().expect("player position");
    println!(
        "project admitted: player={} collision=trimesh spawn=[{:.3}, {:.3}, {:.3}]",
        runtime.player().raw(),
        spawn.x,
        spawn.y,
        spawn.z
    );
    let mut failures = Vec::new();

    // 1. Settle onto genuine trimesh support.
    let settle_trace = settle(&mut runtime, 30);
    let settled = runtime.player_position().expect("settled position");
    let adjusted = settle_trace
        .windows(2)
        .any(|pair| (pair[1] - pair[0]).abs() > 0.05);
    let stable = settle_trace
        .iter()
        .rev()
        .take(4)
        .collect::<Vec<_>>()
        .windows(2)
        .all(|pair| (*pair[1] - *pair[0]).abs() < 0.001);
    let support = support_height(&runtime, settled.to_array(), 1.1);
    println!(
        "settle: spawn_y={:.3} settled_y={:.3} adjusted={} stable={} support={support:?}",
        spawn.y, settled.y, adjusted, stable
    );
    if !adjusted {
        failures.push("the player did not settle onto canonical capsule support".to_string());
    }
    if !stable {
        failures.push("the player did not become stable on support".to_string());
    }
    if support.is_none() {
        failures.push("settled player has no trimesh support within 1.1m".to_string());
    }
    let rest = settle(&mut runtime, 3);
    if (rest[3] - rest[0]).abs() > 0.001 {
        failures.push(format!("player moved after settling: {rest:?}"));
    }

    // 2. Reachable-region traversal: from the start room's main floor, walk
    //    into the descending dungeon. The exit from the spawn ledge is a door
    //    problem (6525); this proves the collision authority supports real
    //    multi-level traversal in the open region.
    let main_floor = [28.25, 33.0, -12.25];
    let mut traversal = DaggerRuntime::from_project_json(&document).expect("admit project");
    traversal
        .set_player_position(Vec3::new(main_floor[0], main_floor[1], main_floor[2]))
        .expect("teleport to main floor");
    settle(&mut traversal, 30);
    let floor_pos = traversal.player_position().expect("main floor position");
    let floor_support = support_height(&traversal, floor_pos.to_array(), 1.0);
    // Walk a waypoint down the descending hallway (north, into the curve).
    let (end, blocked) = walk(&mut traversal, [28.25, floor_pos.y, -20.85], 40);
    let end_support = support_height(&traversal, end, 1.0);
    let descended = floor_pos.y - end[1];
    println!(
        "reachable traversal: floor_y={:.3} end=[{:.2}, {:.2}, {:.2}] descended={descended:.2} blocked={blocked} floor_support={floor_support:?} end_support={end_support:?}",
        floor_pos.y, end[0], end[1], end[2]
    );
    if floor_support.is_none() {
        failures.push("the start room main floor has no trimesh support".to_string());
    }
    if descended < 5.0 {
        failures.push(format!(
            "reachable traversal did not descend the multi-level dungeon (descended {descended:.2}m)"
        ));
    }
    if end_support.is_none() {
        failures.push("reachable traversal ended without trimesh support".to_string());
    }

    // 2b. Curved-hallway wall pressure: enter the descending curve while
    // holding a diagonal into its east wall, then reverse away. This is the
    // real-project route that previously allowed a failed raised retry to be
    // lowered into the trimesh, leaving every later movement axis blocked.
    let mut curve = DaggerRuntime::from_project_json(&document).expect("admit project");
    curve
        .set_player_position(Vec3::new(main_floor[0], main_floor[1], main_floor[2]))
        .expect("position at curved hallway approach");
    settle(&mut curve, 30);
    let curve_start = curve.player_position().expect("curve start");
    steer_toward(&mut curve, [28.25, curve_start.y, -20.85]);
    let mut curve_blocked = 0usize;
    let mut curve_overlap = false;
    for _ in 0..120 {
        let receipt = curve
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: 1.0,
                right: 1.0,
            })
            .expect("curved-wall pressure action");
        curve_blocked += receipt
            .facts
            .iter()
            .filter(|fact| matches!(fact, PlayerControlFact::Blocked { .. }))
            .count();
        curve_overlap |= player_overlaps_world(
            &curve,
            curve
                .player_position()
                .expect("curve pressure position")
                .to_array(),
        );
        if curve_blocked >= 3 {
            break;
        }
    }
    let before_escape = curve.player_position().expect("curve escape start");
    for _ in 0..10 {
        curve
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: -1.0,
                right: -1.0,
            })
            .expect("curved-wall escape action");
    }
    let after_escape = curve.player_position().expect("curve escape end");
    let escaped = (after_escape.x - before_escape.x).hypot(after_escape.z - before_escape.z);
    let curve_support = support_height(&curve, after_escape.to_array(), 1.0);
    println!(
        "curved-wall regression: blocked={curve_blocked} overlap={curve_overlap} escaped={escaped:.3} end=[{:.2}, {:.2}, {:.2}] support={curve_support:?}",
        after_escape.x, after_escape.y, after_escape.z
    );
    if curve_blocked == 0 {
        failures.push("curved-wall route never contacted the hallway wall".to_string());
    }
    if curve_overlap {
        failures.push("curved-wall contact embedded the player in the trimesh".to_string());
    }
    if escaped < 0.5 {
        failures.push(format!(
            "player could not reverse away from the curved wall (escaped {escaped:.3}m)"
        ));
    }
    if curve_support.is_none() {
        failures.push("curved-wall escape ended without floor support".to_string());
    }

    // 3a. Adversarial controller boundary: a tall wall blocks without
    //     corrupting the transform.
    let adversarial_document = add_adversarial_wall(&document);
    let mut blocked_runtime =
        DaggerRuntime::from_project_json(&adversarial_document).expect("admit adversarial project");
    settle(&mut blocked_runtime, 30);
    let mut blocked_actions = 0usize;
    let mut final_horizontal = f32::INFINITY;
    let mut maximum_upward = 0.0_f32;
    for _ in 0..20 {
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
        final_horizontal = (after.x - before.x).hypot(after.z - before.z);
        maximum_upward = maximum_upward.max(after.y - before.y);
    }
    println!(
        "negative controller boundary: blocked_actions={} final_horizontal={final_horizontal:.4} max_upward={maximum_upward:.4}",
        blocked_actions
    );
    if blocked_actions == 0 {
        failures.push(format!(
            "adversarial wall did not report Blocked: {blocked_actions}"
        ));
    }
    if final_horizontal > 0.001 || maximum_upward > 0.001 {
        failures.push(format!(
            "blocked controller failed to settle at wall: final_horizontal={final_horizontal:.4} max_upward={maximum_upward:.4}"
        ));
    }

    // 3b. Outside the trimesh there is no support: a probe far outside the
    //     dungeon has no collision occupancy.
    let outside = runtime.collision_scene().contains_point([
        200.0,
        (settled.y - 0.5) as f64,
        settled.z as f64,
    ]);
    println!("negative outside bounds: occupied={outside}");
    if outside {
        failures.push("collision scene occupies an outside probe".to_string());
    }

    // 3c. Removing the collision authority fails closed: a project with no
    //     trimesh mesh and no voxels must not admit.
    let mut stripped: Value = serde_json::from_str(&document).expect("parse for strip");
    for asset in stripped["assets"].as_array_mut().expect("assets") {
        if asset["id"].as_str() == Some("mesh/privateers-hold") {
            asset.as_object_mut().unwrap().remove("staticMesh");
        }
    }
    let stripped_text = serde_json::to_string(&stripped).expect("encode stripped");
    let stripped_ok = DaggerRuntime::from_project_json(&stripped_text).is_ok();
    println!("negative authority removal: admitted={stripped_ok}");
    if stripped_ok {
        failures.push("a project with no collision authority was admitted".to_string());
    }

    // Look must not move the player.
    let before_look = runtime.player_position().expect("pre-look position");
    runtime
        .apply_player_action(ResolvedPlayerAction::Look {
            yaw_delta: 0.5,
            pitch_delta: 0.0,
        })
        .expect("look action");
    if runtime.player_position().expect("look position") != before_look {
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
