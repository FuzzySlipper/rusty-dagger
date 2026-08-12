//! Runtime-derived walk-through route for Privateer's Hold.
//!
//! This replaces the retired Python router (scripts/find-route.py), which had
//! become a second, approximate collision system next to the real one. Here
//! the runtime IS the only collision authority: every route transition is
//! decided by driving a real `DaggerRuntime` — admitting the committed
//! project (trimesh registered, as in production), placing the player at a
//! reached node, settling to ground, stepping toward a neighbouring column,
//! and reading back the authoritative landing position, blocked facts, and
//! fall distance. No geometry is re-derived or re-approximated; the same
//! sweep that runs the walkthrough decides what is a floor, a wall, or a step.
//!
//! Method (bounded flood fill over a 0.5m column grid):
//! - From the spawn, BFS over columns. Each node stores the authoritative
//!   eye position the runtime reached there.
//! - A transition is tested by teleporting a fresh runtime to the parent
//!   node, settling, applying a few small `Move` steps toward the neighbour,
//!   and accepting when the mover crosses the column boundary with ground
//!   support and without a fatal fall.
//! - The search stops at the first column >= 1m inside a different RDB
//!   block; the parent chain becomes the route's waypoints.
//!
//! Usage:
//!   dagger-derive-route [project.json] [--write | --check]
//! Default project: the committed generated project doc. Writes
//! content/projects/privateers-hold.route.json next to it.

use std::collections::{BTreeMap, VecDeque};
use std::env;
use std::fs;

use dagger_runtime::{DaggerRuntime, ResolvedPlayerAction};
use rusty_engine::core_math::Vec3;

const BODY_HALF: f32 = 0.25;
const BLOCK_SIDE: f32 = 51.2;
const CELL: f32 = 0.5;
/// Small Move steps used to carry the mover across one column boundary.
const STEPS_PER_TRANSITION: usize = 6;
/// Extra descent accepted while crossing into a neighbour (m). The start
/// room's intentional ledge drop to its main floor is ~6.4m; a fall into the
/// void is effectively unbounded, so this bounds only "walkable" drops.
const MAX_STEP_DROP: f32 = 7.0;
/// Settle actions to reach ground before judging a node/transition.
const SETTLE_ACTIONS: usize = 24;
/// Exploration budget (columns) to keep the search bounded.
const MAX_EXPLORED: usize = 64_000;

#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Debug)]
struct Col(i32, i32);

impl Col {
    fn of(x: f32, z: f32) -> Self {
        Self((x / CELL).floor() as i32, (z / CELL).floor() as i32)
    }
    fn center(self) -> [f32; 2] {
        [(self.0 as f32 + 0.5) * CELL, (self.1 as f32 + 0.5) * CELL]
    }
    fn neighbors(self) -> [Col; 4] {
        [
            Col(self.0 + 1, self.1),
            Col(self.0 - 1, self.1),
            Col(self.0, self.1 + 1),
            Col(self.0, self.1 - 1),
        ]
    }
}

fn block_of(x: f32, z: f32) -> (i32, i32) {
    (
        (x / BLOCK_SIDE).floor() as i32,
        (z / BLOCK_SIDE).floor() as i32,
    )
}

#[derive(Clone, Copy, Debug)]
struct NodeState {
    /// Authoritative eye position after settling at this node.
    position: [f32; 3],
}

/// A graph node: a column at a distinct standable level. Daggerfall's start
/// room has a ledge (38.4) and a main floor (32.0) in the same columns, so a
/// column alone is not a unique node — the mover transitions *between levels*
/// by walking off a ledge. Tracking (column, rounded support level) lets the
/// flood fill descend as well as traverse.
#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Debug)]
struct Node(Col, i32);

fn level_of(support_y: f32) -> i32 {
    (support_y * 4.0).round() as i32
}

fn support_height(runtime: &DaggerRuntime, eye: [f32; 3], window: f32) -> Option<f32> {
    use rusty_engine::engine_spatial::SpatialCollisionHit;
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

fn settle(runtime: &mut DaggerRuntime, actions: usize) {
    for _ in 0..actions {
        runtime
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: 0.0,
                right: 0.0,
            })
            .expect("idle settle action");
    }
}

fn main() {
    let args = env::args().skip(1);
    let mut project_path: Option<String> = None;
    let mut mode = String::from("--write");
    for arg in args {
        match arg.as_str() {
            "--write" | "--check" => mode = arg,
            _ if project_path.is_none() => project_path = Some(arg),
            _ => {}
        }
    }
    let project_path = project_path.unwrap_or_else(|| {
        format!(
            "{}/../../content/projects/privateers-hold.project.json",
            env!("CARGO_MANIFEST_DIR")
        )
    });
    let route_path = project_path.replace(".project.json", ".route.json");
    let document = fs::read_to_string(&project_path).expect("read project document");

    // Authoritative spawn: settle and read the grounded start position.
    let mut runtime = DaggerRuntime::from_project_json(&document).expect("admit project");
    settle(&mut runtime, SETTLE_ACTIONS);
    let spawn_pos = runtime.player_position().expect("spawn position");
    let spawn_eye = [spawn_pos.x, spawn_pos.y, spawn_pos.z];
    if support_height(&runtime, spawn_eye, 1.0).is_none() {
        eprintln!("derive-route: spawn has no ground support on the trimesh");
        std::process::exit(1);
    }
    let spawn_col = Col::of(spawn_eye[0], spawn_eye[2]);
    let spawn_block = block_of(spawn_eye[0], spawn_eye[2]);
    println!(
        "derive-route: spawn eye=[{:.3}, {:.3}, {:.3}] col={spawn_col:?} block={spawn_block:?}",
        spawn_eye[0], spawn_eye[1], spawn_eye[2]
    );

    let mut states: BTreeMap<Node, NodeState> = BTreeMap::new();
    let mut parent: BTreeMap<Node, Node> = BTreeMap::new();
    let mut queue: VecDeque<Node> = VecDeque::new();
    let spawn_support = support_height(&runtime, spawn_eye, 1.0).expect("spawn support");
    let spawn_node = Node(spawn_col, level_of(spawn_support));
    states.insert(
        spawn_node,
        NodeState {
            position: spawn_eye,
        },
    );
    queue.push_back(spawn_node);

    let mut goal: Option<Node> = None;
    let mut explored = 0usize;
    'search: while let Some(node) = queue.pop_front() {
        explored += 1;
        if explored > MAX_EXPLORED {
            eprintln!("derive-route: exploration budget exhausted");
            break;
        }
        let from = states[&node];
        // Probe the 4 horizontal neighbours AND a downward ledge-drop (the
        // start room's ledge -> main-floor descent is in the same columns, so
        // horizontal-only probing can never leave the ledge).
        let mut landing_pairs: Vec<(Node, NodeState)> = Vec::new();
        for ncol in node.0.neighbors() {
            landing_pairs.extend(probe_transitions(&document, from, node, ncol));
        }
        landing_pairs.extend(probe_descent(&document, from, node));
        for (next_node, next) in landing_pairs {
            if states.contains_key(&next_node) {
                continue;
            }
            let blk = block_of(next.position[0], next.position[2]);
            let is_goal = blk != spawn_block && {
                let x0 = blk.0 as f32 * BLOCK_SIDE;
                let z0 = blk.1 as f32 * BLOCK_SIDE;
                (next.position[0] - x0)
                    .min(x0 + BLOCK_SIDE - next.position[0])
                    .min(next.position[2] - z0)
                    .min(z0 + BLOCK_SIDE - next.position[2])
                    >= 1.0
            };
            states.insert(next_node, next);
            parent.insert(next_node, node);
            if is_goal {
                goal = Some(next_node);
                break 'search;
            }
            queue.push_back(next_node);
        }
    }

    let Some(goal_node) = goal else {
        eprintln!(
            "derive-route: no traversable route from the start marker to a border block (explored {explored} nodes)"
        );
        std::process::exit(1);
    };

    let mut nodes = vec![goal_node];
    while let Some(&pnode) = parent.get(&nodes[0]) {
        nodes.insert(0, pnode);
    }
    let waypoints: Vec<[f32; 3]> = nodes
        .iter()
        .map(|node| {
            let p = states[node].position;
            [round3(p[0]), round3(p[1]), round3(p[2])]
        })
        .collect();
    let goal_block = block_of(waypoints.last().unwrap()[0], waypoints.last().unwrap()[2]);
    let route = serde_json::json!({
        "version": 1,
        "cell": CELL,
        "spawnBlock": [spawn_block.0, spawn_block.1],
        "goalBlock": [goal_block.0, goal_block.1],
        "waypoints": waypoints,
    });
    let text = serde_json::to_string_pretty(&route).expect("encode route") + "\n";

    if mode == "--check" {
        let actual = fs::read_to_string(&route_path).unwrap_or_default();
        if actual != text {
            eprintln!("{route_path} is stale; run dagger-derive-route --write");
            std::process::exit(1);
        }
        println!("{route_path} up to date ({} waypoints)", waypoints.len());
        return;
    }
    fs::write(&route_path, &text).expect("write route");
    println!(
        "derive-route: wrote {route_path} ({} waypoints, block {spawn_block:?} -> {goal_block:?}, explored {explored} columns)",
        waypoints.len()
    );
}

/// Probe a horizontal transition by teleporting a fresh runtime to the parent
/// node, settling, and stepping toward the neighbour. Returns every landing
/// node the mover reaches: the target column at its level, AND any in-column
/// descent (walking off a ledge lands the mover at a lower level of the
/// parent's or a neighbouring column — Daggerfall's ledge → main-floor drop).
/// Every position is the runtime's authoritative readback.
fn probe_transitions(
    document: &str,
    from: NodeState,
    from_node: Node,
    target: Col,
) -> Vec<(Node, NodeState)> {
    let mut runtime = DaggerRuntime::from_project_json(document).expect("admit project");
    runtime
        .set_player_position(Vec3::new(
            from.position[0],
            from.position[1],
            from.position[2],
        ))
        .expect("teleport to parent node");
    settle(&mut runtime, SETTLE_ACTIONS);
    let start = runtime.player_position().expect("player position");
    if (start.y - from.position[1]).abs() > 1.0 {
        return Vec::new();
    }
    let [tx, tz] = target.center();
    let start_y = start.y;
    for _ in 0..STEPS_PER_TRANSITION {
        let before = runtime.player_position().expect("player position");
        let dx = tx - before.x;
        let dz = tz - before.z;
        let dist = (dx * dx + dz * dz).sqrt();
        if dist < 0.05 {
            break;
        }
        steer_toward(&mut runtime, [tx, before.y, tz]);
        runtime
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: (dist / 4.0).clamp(0.05, 1.0),
                right: 0.0,
            })
            .expect("transition step");
        let after = runtime.player_position().expect("player position");
        if after.y < start_y - MAX_STEP_DROP {
            return Vec::new();
        }
    }
    settle(&mut runtime, SETTLE_ACTIONS);
    let end = runtime.player_position().expect("player position");
    let end_col = Col::of(end.x, end.z);
    // Must have moved toward/past the target (or descended in place off a ledge).
    if end_col != target && end_col != from_node.0 {
        return Vec::new();
    }
    let Some(support) = support_height(&runtime, [end.x, end.y, end.z], 1.5) else {
        return Vec::new();
    };
    let node = Node(end_col, level_of(support));
    if node == from_node {
        return Vec::new();
    }
    vec![(
        node,
        NodeState {
            position: [end.x, end.y, end.z],
        },
    )]
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

/// Probe a downward ledge-drop: from the parent node, step in the direction
/// that descends most steeply (the ledge edge), letting the runtime settle to
/// a lower standable level. The start room's ledge → main-floor transition is
/// a genuine Daggerfall move that horizontal-only probing cannot represent.
fn probe_descent(document: &str, from: NodeState, from_node: Node) -> Vec<(Node, NodeState)> {
    // Try each of the 4 cardinal drop directions; the runtime's settle tells
    // us where (if anywhere) the ledge edge lets the mover down.
    let mut found = Vec::new();
    for dir in [[1.0, 0.0], [-1.0, 0.0], [0.0, 1.0], [0.0, -1.0]] {
        let mut runtime = DaggerRuntime::from_project_json(document).expect("admit project");
        runtime
            .set_player_position(Vec3::new(
                from.position[0],
                from.position[1],
                from.position[2],
            ))
            .expect("teleport for descent");
        settle(&mut runtime, SETTLE_ACTIONS);
        let start = runtime.player_position().expect("player position");
        if (start.y - from.position[1]).abs() > 1.0 {
            continue;
        }
        let start_y = start.y;
        // Walk a short distance in the drop direction and settle; a ledge
        // edge lets the mover fall to a lower level with support.
        let target = [start.x + dir[0] * 2.0, start.y, start.z + dir[1] * 2.0];
        for _ in 0..6 {
            steer_toward(&mut runtime, target);
            runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 0.5,
                    right: 0.0,
                })
                .expect("descent step");
            let pos = runtime.player_position().expect("player position");
            if pos.y < start_y - MAX_STEP_DROP {
                break;
            }
        }
        settle(&mut runtime, SETTLE_ACTIONS);
        let end = runtime.player_position().expect("player position");
        // A descent is a landing at a LOWER level than the parent with support.
        if end.y >= start_y - 1.0 {
            continue;
        }
        let end_col = Col::of(end.x, end.z);
        if let Some(support) = support_height(&runtime, [end.x, end.y, end.z], 1.5) {
            let node = Node(end_col, level_of(support));
            if node != from_node && !found.iter().any(|(n, _)| *n == node) {
                found.push((
                    node,
                    NodeState {
                        position: [end.x, end.y, end.z],
                    },
                ));
            }
        }
    }
    found
}

fn round3(value: f32) -> f32 {
    (value * 1000.0).round() / 1000.0
}
