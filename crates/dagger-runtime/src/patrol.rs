//! NPC patrol service (task 6641): deterministic seeded random-walk on the
//! nav grid, grounded to real floor support. Each NPC picks waypoints near
//! its spawn, moves toward them at DFU walk speed, and pauses between
//! waypoints. All movement stays on walkable cells.
//!
//! The service owns NPC state and advances it per tick. Movement positions
//! feed back into the AnimationService so directional sprites track the
//! NPC's new position (camera-relative orientation still applies).
//!
//! Design choice: simple random-walk, not DFU's full EnemyMotor (which is
//! combat AI with detection, pursuit, and melee). DFU has no standalone
//! wander behavior — its AI is detection-based. A deterministic seeded
//! random-walk is sufficient and simpler for this demo.

use std::collections::HashMap;

/// Cell size from the nav grid (0.5m).
const CELL_SIZE: f32 = 0.5;

/// DFU walk speed: ~3 m/s (classic units * GlobalScale).
/// Classic enemies move at Stats.LiveSpeed + dfWalkBase; ~120 classic units
/// * 0.025 = 3.0 m/s. Slow enough to observe in the flycam.
const WALK_SPEED: f32 = 3.0;

/// Max cells from spawn for patrol waypoints.
const PATROL_RADIUS_CELLS: i32 = 5;

/// Seconds to pause idle between waypoints.
const IDLE_DURATION: f32 = 1.5;

/// Simple deterministic LCG RNG (per-NPC, seeded from handle).
/// Same seed → same patrol path. This is NOT arena2::dfrandom — it's a
/// self-contained deterministic generator for patrol waypoints only.
struct PatrolRng {
    state: u32,
}

impl PatrolRng {
    fn new(seed: u32) -> Self {
        PatrolRng {
            state: seed.wrapping_mul(2654435761).wrapping_add(1),
        }
    }

    fn next(&mut self) -> u32 {
        // Numerical Recipes LCG
        self.state = self.state.wrapping_mul(1664525).wrapping_add(1013904223);
        self.state
    }

    fn range(&mut self, min: i32, max: i32) -> i32 {
        let span = (max - min + 1) as u32;
        min + (self.next() % span) as i32
    }
}

/// Walkable-cell grid loaded from navgrid.json. Provides O(1) walkability
/// checks for patrol movement.
pub struct PatrolGrid {
    /// (cell_x, cell_z, level) → support_y
    cells: HashMap<(i32, i32, i32), f32>,
}

impl PatrolGrid {
    /// Build from the navgrid.json cells array: each entry is
    /// [cell_x, cell_z, level, support_y].
    pub fn from_cells(cells: &[(f64, f64, f64, f64)]) -> Self {
        let mut map = HashMap::with_capacity(cells.len());
        for &(cx, cz, level, sy) in cells {
            map.insert((cx as i32, cz as i32, level as i32), sy as f32);
        }
        PatrolGrid { cells: map }
    }

    /// Check if a world-space position is on a walkable cell.
    fn is_walkable(&self, x: f32, z: f32, y: f32) -> Option<f32> {
        let cx = (x / CELL_SIZE).floor() as i32;
        let cz = (z / CELL_SIZE).floor() as i32;
        let level = ((y / 0.25).round() as i32);
        self.cells.get(&(cx, cz, level)).copied()
    }

    /// Find a random walkable cell within `radius_cells` of the given center.
    /// Returns the world-space center of the chosen cell + its support Y.
    fn random_walkable_near(
        &self,
        center: [f32; 3],
        radius_cells: i32,
        level: i32,
        rng: &mut PatrolRng,
    ) -> Option<[f32; 3]> {
        let ccx = (center[0] / CELL_SIZE).floor() as i32;
        let ccz = (center[2] / CELL_SIZE).floor() as i32;

        // Try up to 8 random directions
        for _ in 0..8 {
            let dx = rng.range(-radius_cells, radius_cells);
            let dz = rng.range(-radius_cells, radius_cells);
            if dx == 0 && dz == 0 {
                continue;
            }
            let tx = ccx + dx;
            let tz = ccz + dz;
            if let Some(&sy) = self.cells.get(&(tx, tz, level)) {
                let wx = (tx as f32 + 0.5) * CELL_SIZE;
                let wz = (tz as f32 + 0.5) * CELL_SIZE;
                return Some([wx, sy, wz]);
            }
        }
        None
    }
}

/// One NPC in the patrol system.
struct PatrolNpc {
    handle: u32,
    spawn: [f32; 3],
    position: [f32; 3],
    target: Option<[f32; 3]>,
    level: i32,
    is_moving: bool,
    idle_timer: f32,
    /// Current heading (radians, 0 = +X, increasing toward +Z).
    /// Smoothly interpolated toward the target heading for natural turns.
    heading: f32,
    rng: PatrolRng,
}

/// A position update from the patrol service.
#[derive(Debug, Clone, Copy)]
pub struct PositionUpdate {
    pub handle: u32,
    pub translation: [f32; 3],
    pub is_moving: bool,
}

/// The NPC patrol authority. Owns all NPC state and advances it per tick.
pub struct PatrolService {
    npcs: Vec<PatrolNpc>,
    grid: PatrolGrid,
}

impl PatrolService {
    /// Create from navgrid.json data + enemy spawn positions.
    /// Each spawn is grounded to the navgrid's floor support.
    pub fn new(
        navgrid_cells: &[(f64, f64, f64, f64)],
        spawns: &[(u32, [f32; 3])], // (handle, spawn_position)
    ) -> Self {
        let grid = PatrolGrid::from_cells(navgrid_cells);
        let mut npcs = Vec::with_capacity(spawns.len());

        for &(handle, spawn) in spawns {
            // Ground the spawn: find the floor support Y at this position.
            // The navgrid already has grounded spawn data; we try the spawn
            // position first, then search nearby cells.
            let grounded_y = grid
                .is_walkable(spawn[0], spawn[2], spawn[1])
                .unwrap_or_else(|| {
                    // Search for nearest walkable cell at any level near spawn
                    let cx = (spawn[0] / CELL_SIZE).round() as i32;
                    let cz = (spawn[2] / CELL_SIZE).round() as i32;
                    let mut best = spawn[1];
                    let mut best_dist = f32::MAX;
                    for &(x, z, level, sy) in navgrid_cells.iter() {
                        let x = x as i32;
                        let z = z as i32;
                        let d = ((x - cx).pow(2) + (z - cz).pow(2)) as f32;
                        if d < best_dist {
                            best_dist = d;
                            best = sy as f32;
                        }
                    }
                    best
                });

            let position = [spawn[0], grounded_y, spawn[2]];
            let level = ((grounded_y / 0.25).round()) as i32;

            npcs.push(PatrolNpc {
                handle,
                spawn: position,
                position,
                target: None,
                level,
                is_moving: false,
                idle_timer: 0.5,
                heading: 0.0,
                rng: PatrolRng::new(handle),
            });
        }

        PatrolService { npcs, grid }
    }

    /// Advance all NPCs by `dt` seconds. Returns position updates for NPCs
    /// that moved this tick.
    pub fn evaluate(&mut self, dt: f32) -> Vec<PositionUpdate> {
        let mut updates = Vec::new();
        for npc in &mut self.npcs {
            let was_moving = npc.is_moving;

            if npc.idle_timer > 0.0 {
                npc.idle_timer -= dt;
                npc.is_moving = false;
                if npc.idle_timer <= 0.0 {
                    // Pick a new waypoint
                    npc.target = self.grid.random_walkable_near(
                        npc.spawn,
                        PATROL_RADIUS_CELLS,
                        npc.level,
                        &mut npc.rng,
                    );
                    npc.is_moving = npc.target.is_some();
                }
            } else if let Some(target) = npc.target {
                // Move toward target
                let dx = target[0] - npc.position[0];
                let dz = target[2] - npc.position[2];
                let dist = (dx * dx + dz * dz).sqrt();
                npc.is_moving = true;

                // Smoothly rotate heading toward target direction (max 3 rad/s)
                let target_heading = dz.atan2(dx);
                let mut diff = target_heading - npc.heading;
                while diff > std::f32::consts::PI {
                    diff -= std::f32::consts::TAU;
                }
                while diff < -std::f32::consts::PI {
                    diff += std::f32::consts::TAU;
                }
                let turn_rate = 3.0; // rad/s
                let turn = diff.clamp(-turn_rate * dt, turn_rate * dt);
                npc.heading += turn;

                if dist < 0.1 {
                    // Reached target
                    npc.position = target;
                    npc.target = None;
                    npc.idle_timer = IDLE_DURATION;
                    npc.is_moving = false;
                } else {
                    let step = WALK_SPEED * dt;
                    let ratio = (step / dist).min(1.0);
                    let new_x = npc.position[0] + dx * ratio;
                    let new_z = npc.position[2] + dz * ratio;

                    // Check walkability: snap Y to floor support at new position
                    if let Some(sy) = self.grid.is_walkable(new_x, new_z, npc.position[1]) {
                        npc.position = [new_x, sy, new_z];
                    } else {
                        // Blocked — pick a new target
                        npc.target = None;
                        npc.idle_timer = IDLE_DURATION * 0.5;
                        npc.is_moving = false;
                    }
                }
            } else {
                npc.is_moving = false;
                npc.idle_timer = IDLE_DURATION;
            }

            // Emit update if position changed or move/idle state changed
            if npc.is_moving || was_moving != npc.is_moving {
                updates.push(PositionUpdate {
                    handle: npc.handle,
                    translation: npc.position,
                    is_moving: npc.is_moving,
                });
            }
        }
        updates
    }

    /// Current positions of all NPCs (for animation service updates).
    pub fn positions(&self) -> Vec<(u32, [f32; 3], bool)> {
        self.npcs
            .iter()
            .map(|n| (n.handle, n.position, n.is_moving))
            .collect()
    }

    pub fn len(&self) -> usize {
        self.npcs.len()
    }

    pub fn is_empty(&self) -> bool {
        self.npcs.is_empty()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn make_grid() -> Vec<(f64, f64, f64, f64)> {
        // Simple 11x11 grid at level 0 (y=0)
        let mut cells = Vec::new();
        for x in -5..=5 {
            for z in -5..=5 {
                cells.push((x as f64, z as f64, 0.0, 0.0));
            }
        }
        cells
    }

    #[test]
    fn npcs_ground_at_spawn() {
        let grid = make_grid();
        let spawns = vec![(100u32, [0.25, 5.0, 0.25])]; // floating at y=5
        let svc = PatrolService::new(&grid, &spawns);
        assert_eq!(svc.len(), 1);
        // Should be grounded to y=0
        let (handle, pos, _) = svc.positions()[0];
        assert_eq!(handle, 100);
        assert!(
            (pos[1] - 0.0).abs() < 0.1,
            "NPC should be grounded, got y={}",
            pos[1]
        );
    }

    #[test]
    fn patrol_is_deterministic() {
        let grid = make_grid();
        let spawns = vec![(1u32, [0.25, 0.0, 0.25]), (2u32, [-0.25, 0.0, -0.25])];

        let mut svc_a = PatrolService::new(&grid, &spawns);
        let mut svc_b = PatrolService::new(&grid, &spawns);

        for _ in 0..100 {
            let a = svc_a.evaluate(0.1);
            let b = svc_b.evaluate(0.1);
            assert_eq!(a.len(), b.len(), "update count differs");
            for (ua, ub) in a.iter().zip(b.iter()) {
                assert_eq!(ua.handle, ub.handle);
                assert_eq!(
                    ua.translation, ub.translation,
                    "position differs for handle {}",
                    ua.handle
                );
                assert_eq!(ua.is_moving, ub.is_moving);
            }
        }
    }

    #[test]
    fn npcs_stay_on_walkable_cells() {
        let grid = make_grid();
        let spawns = vec![
            (1u32, [0.25, 0.0, 0.25]),
            (2u32, [-0.25, 0.0, -0.25]),
            (3u32, [1.25, 0.0, 1.25]),
        ];
        let mut svc = PatrolService::new(&grid, &spawns);

        // Simulate 200 ticks (20 seconds at 10Hz)
        for _tick in 0..200 {
            let updates = svc.evaluate(0.1);
            for u in &updates {
                let cx = (u.translation[0] / CELL_SIZE).floor() as i32;
                let cz = (u.translation[2] / CELL_SIZE).floor() as i32;
                // Must be within grid bounds (-5..5)
                assert!(
                    cx >= -5 && cx <= 5,
                    "NPC {} at cx={} outside grid",
                    u.handle,
                    cx
                );
                assert!(
                    cz >= -5 && cz <= 5,
                    "NPC {} at cz={} outside grid",
                    u.handle,
                    cz
                );
            }
        }
    }

    #[test]
    fn npcs_move_from_spawn() {
        let grid = make_grid();
        let spawns = vec![(1u32, [0.25, 0.0, 0.25])];
        let mut svc = PatrolService::new(&grid, &spawns);

        let initial = svc.positions()[0].1;
        let mut moved = false;
        for _ in 0..200 {
            svc.evaluate(0.1);
            let pos = svc.positions()[0].1;
            let d = ((pos[0] - initial[0]).powi(2) + (pos[2] - initial[2]).powi(2)).sqrt();
            if d > 0.1 {
                moved = true;
                break;
            }
        }
        assert!(moved, "NPC should have moved from spawn after 20s");
    }
}
