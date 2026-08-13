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

use std::collections::{BTreeMap, BTreeSet, HashMap};

use dagger_rpg::EnemyBehaviorExperiment;

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
        let level = (y / 0.25).round() as i32;
        self.cells.get(&(cx, cz, level)).copied()
    }

    /// Resolve the closest support surface in a world-space column. Player
    /// positions are body/camera positions rather than nav support heights,
    /// so an exact level lookup is not meaningful for encounter sensing.
    fn nearest_support(&self, x: f32, z: f32, y: f32) -> Option<f32> {
        if !x.is_finite() || !y.is_finite() || !z.is_finite() {
            return None;
        }
        let cx = (x / CELL_SIZE).floor() as i32;
        let cz = (z / CELL_SIZE).floor() as i32;
        self.cells
            .iter()
            .filter_map(|(&(cell_x, cell_z, _), &support_y)| {
                (cell_x == cx && cell_z == cz).then_some(support_y)
            })
            .min_by(|left, right| (y - *left).abs().total_cmp(&(y - *right).abs()))
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
    mode: EnemyAiMode,
    attack_cooldown_remaining: f32,
}

impl PatrolNpc {
    fn reset(&mut self) {
        self.position = self.spawn;
        self.target = None;
        self.is_moving = false;
        self.idle_timer = 0.5;
        self.heading = 0.0;
        self.rng = PatrolRng::new(self.handle);
        self.mode = EnemyAiMode::Patrol;
        self.attack_cooldown_remaining = 0.0;
    }
}

/// A position update from the patrol service.
#[derive(Debug, Clone, Copy)]
pub struct PositionUpdate {
    pub handle: u32,
    pub translation: [f32; 3],
    pub heading: f32,
    pub is_moving: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EnemyAiMode {
    Patrol,
    Chase,
    Attack,
    Dead,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct EnemyDecision {
    pub handle: u32,
    pub from: EnemyAiMode,
    pub to: EnemyAiMode,
    pub distance_to_player: f32,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct EnemyAttackIntent {
    pub handle: u32,
    pub damage: f32,
    pub distance_to_player: f32,
}

#[derive(Debug, Default)]
pub struct PatrolEvaluation {
    pub positions: Vec<PositionUpdate>,
    pub decisions: Vec<EnemyDecision>,
    pub attacks: Vec<EnemyAttackIntent>,
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
                    let cx = (spawn[0] / CELL_SIZE).floor() as i32;
                    let cz = (spawn[2] / CELL_SIZE).floor() as i32;
                    let mut best = spawn[1];
                    let mut best_horizontal = i32::MAX;
                    let mut best_vertical = f32::MAX;
                    for &(x, z, _level, sy) in navgrid_cells.iter() {
                        let x = x as i32;
                        let z = z as i32;
                        let horizontal = (x - cx).pow(2) + (z - cz).pow(2);
                        let vertical = (spawn[1] - sy as f32).abs();
                        if horizontal < best_horizontal
                            || (horizontal == best_horizontal && vertical < best_vertical)
                        {
                            best_horizontal = horizontal;
                            best_vertical = vertical;
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
                mode: EnemyAiMode::Patrol,
                attack_cooldown_remaining: 0.0,
            });
        }

        PatrolService { npcs, grid }
    }

    /// Advance all NPCs by `dt` seconds. Returns position updates for NPCs
    /// that moved this tick.
    pub fn evaluate(&mut self, dt: f32) -> Vec<PositionUpdate> {
        self.evaluate_encounters(dt, [f32::INFINITY; 3], &BTreeMap::new(), &BTreeSet::new())
            .positions
    }

    pub fn evaluate_encounters(
        &mut self,
        dt: f32,
        player_position: [f32; 3],
        behaviors: &BTreeMap<u32, EnemyBehaviorExperiment>,
        dead: &BTreeSet<u32>,
    ) -> PatrolEvaluation {
        let mut updates = Vec::new();
        let mut decisions = Vec::new();
        let mut attacks = Vec::new();
        let player_level = self
            .grid
            .nearest_support(player_position[0], player_position[2], player_position[1])
            .map(|support_y| (support_y / 0.25).round() as i32);
        for npc in &mut self.npcs {
            let was_moving = npc.is_moving;
            let distance_to_player = if player_level == Some(npc.level) {
                (player_position[0] - npc.position[0]).hypot(player_position[2] - npc.position[2])
            } else {
                f32::INFINITY
            };
            let behavior = behaviors.get(&npc.handle);
            let next_mode = if dead.contains(&npc.handle) {
                EnemyAiMode::Dead
            } else if let Some(behavior) = behavior {
                if distance_to_player <= behavior.attack_range {
                    EnemyAiMode::Attack
                } else if distance_to_player <= behavior.detection_range {
                    EnemyAiMode::Chase
                } else {
                    EnemyAiMode::Patrol
                }
            } else {
                EnemyAiMode::Patrol
            };
            if next_mode != npc.mode {
                decisions.push(EnemyDecision {
                    handle: npc.handle,
                    from: npc.mode,
                    to: next_mode,
                    distance_to_player,
                });
                npc.mode = next_mode;
                npc.target = None;
                npc.idle_timer = 0.0;
            }

            match (npc.mode, behavior) {
                (EnemyAiMode::Dead, _) => {
                    npc.is_moving = false;
                }
                (EnemyAiMode::Attack, Some(behavior)) => {
                    npc.is_moving = false;
                    npc.attack_cooldown_remaining -= dt;
                    if npc.attack_cooldown_remaining <= 0.0 {
                        attacks.push(EnemyAttackIntent {
                            handle: npc.handle,
                            damage: behavior.attack_damage,
                            distance_to_player,
                        });
                        npc.attack_cooldown_remaining = behavior.attack_cooldown_seconds;
                    }
                }
                (EnemyAiMode::Chase, Some(behavior)) => {
                    npc.attack_cooldown_remaining = 0.0;
                    move_toward(npc, &self.grid, player_position, behavior.chase_speed, dt);
                }
                (EnemyAiMode::Patrol, behavior) => {
                    npc.attack_cooldown_remaining = 0.0;
                    let speed = behavior.map_or(WALK_SPEED, |behavior| behavior.patrol_speed);
                    evaluate_patrol(npc, &self.grid, speed, dt);
                }
                _ => unreachable!("admitted encounter mode requires behavior"),
            }

            // Emit update if moving or state changed — includes heading so gizmos track rotation
            if npc.is_moving || was_moving != npc.is_moving {
                updates.push(PositionUpdate {
                    handle: npc.handle,
                    translation: npc.position,
                    heading: npc.heading,
                    is_moving: npc.is_moving,
                });
            }
        }
        PatrolEvaluation {
            positions: updates,
            decisions,
            attacks,
        }
    }

    /// Current positions of all NPCs (for animation service updates).
    pub fn positions(&self) -> Vec<(u32, [f32; 3], f32, bool)> {
        self.npcs
            .iter()
            .map(|n| (n.handle, n.position, n.heading, n.is_moving))
            .collect()
    }

    pub fn states(&self) -> Vec<(u32, EnemyAiMode)> {
        self.npcs.iter().map(|npc| (npc.handle, npc.mode)).collect()
    }

    pub fn reset(&mut self) {
        for npc in &mut self.npcs {
            npc.reset();
        }
    }

    pub fn len(&self) -> usize {
        self.npcs.len()
    }

    pub fn is_empty(&self) -> bool {
        self.npcs.is_empty()
    }

    /// Validate that every loaded NPC has at least one usable patrol candidate
    /// at its grounded level within the patrol radius. Returns Err with a
    /// diagnostic if any NPC is stranded (no walkable neighbors).
    pub fn validate(&self) -> Result<(), String> {
        let mut stranded = 0;
        let mut first_stranded = None;
        for npc in &self.npcs {
            let ccx = (npc.position[0] / CELL_SIZE).floor() as i32;
            let ccz = (npc.position[2] / CELL_SIZE).floor() as i32;
            let found = (-PATROL_RADIUS_CELLS..=PATROL_RADIUS_CELLS).any(|dx| {
                (-PATROL_RADIUS_CELLS..=PATROL_RADIUS_CELLS).any(|dz| {
                    if dx == 0 && dz == 0 {
                        return false;
                    }
                    self.grid
                        .cells
                        .contains_key(&(ccx + dx, ccz + dz, npc.level))
                })
            });
            if !found {
                stranded += 1;
                if first_stranded.is_none() {
                    first_stranded = Some(npc.handle);
                }
            }
        }
        if stranded > 0 {
            return Err(format!(
                "navgrid cannot support {stranded} NPC(s) — no walkable patrol candidates near their grounded positions (first stranded handle: {})",
                first_stranded.unwrap_or(0)
            ));
        }
        Ok(())
    }
}

fn evaluate_patrol(npc: &mut PatrolNpc, grid: &PatrolGrid, speed: f32, dt: f32) {
    if speed <= 0.0 {
        npc.is_moving = false;
        return;
    }
    if npc.idle_timer > 0.0 {
        npc.idle_timer -= dt;
        npc.is_moving = false;
        if npc.idle_timer <= 0.0 {
            npc.target =
                grid.random_walkable_near(npc.spawn, PATROL_RADIUS_CELLS, npc.level, &mut npc.rng);
            npc.is_moving = npc.target.is_some();
        }
    } else if let Some(target) = npc.target {
        move_toward(npc, grid, target, speed, dt);
        if (target[0] - npc.position[0]).hypot(target[2] - npc.position[2]) < 0.1 {
            npc.position = target;
            npc.target = None;
            npc.idle_timer = IDLE_DURATION;
            npc.is_moving = false;
        }
    } else {
        npc.is_moving = false;
        npc.idle_timer = IDLE_DURATION;
    }
}

fn move_toward(npc: &mut PatrolNpc, grid: &PatrolGrid, target: [f32; 3], speed: f32, dt: f32) {
    let dx = target[0] - npc.position[0];
    let dz = target[2] - npc.position[2];
    let distance = dx.hypot(dz);
    if distance <= 0.001 {
        npc.is_moving = false;
        return;
    }
    // Movement is not turn-rate constrained, so presentation must face the
    // displacement immediately. Heading zero is glTF -Z, matching authored
    // Dagger sprites and the renderer transform convention.
    npc.heading = dx.atan2(-dz);
    let ratio = (speed * dt / distance).min(1.0);
    let new_x = npc.position[0] + dx * ratio;
    let new_z = npc.position[2] + dz * ratio;
    if let Some(support_y) = grid.is_walkable(new_x, new_z, npc.position[1]) {
        npc.position = [new_x, support_y, new_z];
        npc.is_moving = true;
    } else {
        npc.target = None;
        npc.idle_timer = IDLE_DURATION * 0.5;
        npc.is_moving = false;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn encounter_behavior() -> EnemyBehaviorExperiment {
        EnemyBehaviorExperiment {
            detection_range: 20.0,
            patrol_speed: 0.0,
            chase_speed: 2.0,
            attack_range: 2.0,
            attack_cooldown_seconds: 1.0,
            attack_damage: 4.0,
        }
    }

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
        let (handle, pos, _, _) = svc.positions()[0];
        assert_eq!(handle, 100);
        assert!(
            (pos[1] - 0.0).abs() < 0.1,
            "NPC should be grounded, got y={}",
            pos[1]
        );
    }

    #[test]
    fn multilevel_spawn_uses_nearest_floor_in_its_column() {
        let cells = vec![
            (0.0, 0.0, 4.0, 1.0),
            (0.0, 0.0, 40.0, 10.0),
            (1.0, 0.0, 128.0, 32.0),
        ];
        let svc = PatrolService::new(&cells, &[(2001, [0.1, 11.0, 0.1])]);
        let positions = svc.positions();
        assert_eq!(positions[0].1[1], 10.0);
    }

    #[test]
    fn encounter_sensing_requires_the_players_walkable_level() {
        let mut cells = make_grid();
        for x in -5..=5 {
            for z in -5..=5 {
                cells.push((x as f64, z as f64, 40.0, 10.0));
            }
        }
        let behaviors = BTreeMap::from([(1, encounter_behavior())]);
        let mut service = PatrolService::new(&cells, &[(1, [0.25, 0.0, 0.25])]);

        let upper_floor =
            service.evaluate_encounters(0.1, [0.25, 10.9, 0.25], &behaviors, &BTreeSet::new());
        assert!(upper_floor.attacks.is_empty());
        assert_eq!(service.states(), vec![(1, EnemyAiMode::Patrol)]);

        let same_floor =
            service.evaluate_encounters(0.1, [0.25, 0.9, 0.25], &behaviors, &BTreeSet::new());
        assert_eq!(same_floor.attacks.len(), 1);
        assert_eq!(service.states(), vec![(1, EnemyAiMode::Attack)]);
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
                    (-5..=5).contains(&cx),
                    "NPC {} at cx={} outside grid",
                    u.handle,
                    cx
                );
                assert!(
                    (-5..=5).contains(&cz),
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

    #[test]
    fn moving_npc_heading_matches_displacement() {
        let grid = make_grid();
        let mut svc = PatrolService::new(&grid, &[(1, [0.25, 0.0, 0.25])]);
        let mut previous = svc.positions()[0].1;
        for _ in 0..200 {
            for update in svc.evaluate(0.1) {
                let dx = update.translation[0] - previous[0];
                let dz = update.translation[2] - previous[2];
                if dx.hypot(dz) > 0.001 {
                    let expected = dx.atan2(-dz);
                    assert!(
                        (update.heading - expected).abs() < 0.001,
                        "heading {} did not face displacement ({dx}, {dz})",
                        update.heading
                    );
                    return;
                }
                previous = update.translation;
            }
        }
        panic!("NPC did not move during heading proof");
    }
}
