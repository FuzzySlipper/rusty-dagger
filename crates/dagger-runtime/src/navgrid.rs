//! Walkable navigation grid derived from the dungeon trimesh collision
//! authority (task 6639).
//!
//! The grid is projection *construction*, not a pathfinder: it samples the
//! admitted collision scene (the same trimesh authority the walkthrough
//! drives) and records which columns/levels an agent can stand on. Path
//! queries over the grid are an upstream svc-pathfinding concern
//! (rusty-engine tasks 6642/6643); patrol (6641) will consume whichever seam
//! lands there.
//!
//! Derivation: a bounded sweep over the dungeon AABB casts one downward ray
//! per 0.5m column, then keeps re-casting below each hit so multi-level rooms
//! (the start room's ledge above its main floor) record every standable
//! level, not just the topmost. A surface is walkable when it faces up
//! (slope), has agent headroom above it, and has a ceiling overhead — the
//! dungeon is interior, so open-sky surfaces (rooftop geometry) are rejected.
//! The trimesh raycast is backface-culled (`solid = false` in svc-collision),
//! so downward rays only ever see up-facing surfaces and ceilings/walls never
//! register as floors.

use std::collections::BTreeMap;

use core_space::Direction6;
use engine_spatial::{SpatialCollisionHit, VoxelCollisionScene};

/// Horizontal column size (matches the derive-route column grid).
pub const CELL_SIZE: f32 = 0.5;
/// Vertical quantization of support heights into distinct levels.
pub const LEVEL_QUANTUM: f32 = 0.25;
/// Headroom required for a cell to be standable.
pub const AGENT_HEIGHT_UNITS: f64 = 2.0;
/// Interior test: a standable cell must be enclosed — a down-facing surface
/// (ceiling, lintel, ledge underside) within this height overhead. Sized to
/// the full mesh height so tall halls and open shafts (the start room's ~30m
/// vertical space) stay walkable while genuinely open-sky surfaces do not.
pub const MAX_CEILING_HEIGHT_UNITS: f64 = 64.0;
/// Floors must face at least this far up (~45 degree max walkable slope).
pub const MIN_FLOOR_NORMAL_Y: f64 = 0.7;
/// Spawn grounding accepts landings this far below the authored point.
pub const MAX_SPAWN_DROP_UNITS: f32 = 12.0;
/// Nudge applied to ray origins so they start clear of the sampled surface.
const RAY_EPSILON: f64 = 0.05;
/// Hard cap on surfaces recorded per column (geometry pathology guard).
const MAX_SURFACES_PER_COLUMN: usize = 64;

/// A grid node: one 0.5m column at one quantized support level.
#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Debug)]
pub struct NavCell {
    pub x: i32,
    pub z: i32,
    pub level: i32,
}

impl NavCell {
    pub fn of(x: f32, z: f32, support_y: f32) -> Self {
        Self {
            x: (x / CELL_SIZE).floor() as i32,
            z: (z / CELL_SIZE).floor() as i32,
            level: level_of(support_y),
        }
    }

    pub fn center(self) -> [f32; 2] {
        [
            (self.x as f32 + 0.5) * CELL_SIZE,
            (self.z as f32 + 0.5) * CELL_SIZE,
        ]
    }
}

/// Quantize a support height into a level index (0.25m steps).
pub fn level_of(support_y: f32) -> i32 {
    (support_y / LEVEL_QUANTUM).round() as i32
}

/// The derived walkable grid: standable cells mapped to their support height.
#[derive(Debug, Clone, Default)]
pub struct NavGrid {
    cells: BTreeMap<NavCell, f32>,
    /// Every up-facing surface seen, walkable or not (diagnostic).
    pub surfaces_found: usize,
    /// Columns holding at least one walkable cell (diagnostic).
    pub columns_with_support: usize,
}

impl NavGrid {
    pub fn len(&self) -> usize {
        self.cells.len()
    }

    pub fn is_empty(&self) -> bool {
        self.cells.is_empty()
    }

    pub fn contains(&self, cell: NavCell) -> bool {
        self.cells.contains_key(&cell)
    }

    pub fn support_y(&self, cell: NavCell) -> Option<f32> {
        self.cells.get(&cell).copied()
    }

    pub fn iter(&self) -> impl Iterator<Item = (NavCell, f32)> + '_ {
        self.cells
            .iter()
            .map(|(cell, support_y)| (*cell, *support_y))
    }

    /// All walkable levels in one column, lowest first.
    pub fn levels_in_column(&self, x: i32, z: i32) -> Vec<(NavCell, f32)> {
        self.cells
            .range(
                NavCell {
                    x,
                    z,
                    level: i32::MIN,
                }..=NavCell {
                    x,
                    z,
                    level: i32::MAX,
                },
            )
            .map(|(cell, support_y)| (*cell, *support_y))
            .collect()
    }
}

#[derive(Debug, Clone, Copy)]
struct RaySurface {
    point: [f64; 3],
    normal_y: f64,
}

fn ray_surface(
    scene: &VoxelCollisionScene,
    origin: [f64; 3],
    direction: [f64; 3],
    max_distance: f64,
) -> Option<RaySurface> {
    match scene.raycast_world(origin, direction, max_distance)? {
        SpatialCollisionHit::StaticMesh(hit) => Some(RaySurface {
            point: hit.point.to_array(),
            normal_y: hit.normal.y,
        }),
        // Voxels are axis-aligned: a downward ray can only strike a +Y face.
        SpatialCollisionHit::Voxel(hit) => Some(RaySurface {
            point: hit.point,
            normal_y: if matches!(hit.face, Direction6::PosY) {
                1.0
            } else {
                0.0
            },
        }),
    }
}

/// Derive the walkable grid over the dungeon AABB from the admitted
/// collision scene's trimesh (plus any additive voxel authority).
pub fn derive_nav_grid(scene: &VoxelCollisionScene, min: [f64; 3], max: [f64; 3]) -> NavGrid {
    let mut grid = NavGrid::default();
    let col_min_x = (min[0] as f32 / CELL_SIZE).floor() as i32;
    let col_max_x = (max[0] as f32 / CELL_SIZE).floor() as i32;
    let col_min_z = (min[2] as f32 / CELL_SIZE).floor() as i32;
    let col_max_z = (max[2] as f32 / CELL_SIZE).floor() as i32;
    let top = max[1] + 1.0;
    let bottom = min[1] - 1.0;

    for cx in col_min_x..=col_max_x {
        for cz in col_min_z..=col_max_z {
            let x = ((cx as f32 + 0.5) * CELL_SIZE) as f64;
            let z = ((cz as f32 + 0.5) * CELL_SIZE) as f64;
            let mut y = top;
            let mut column_walkable = false;
            for _ in 0..MAX_SURFACES_PER_COLUMN {
                if y <= bottom {
                    break;
                }
                let Some(hit) = ray_surface(scene, [x, y, z], [0.0, -1.0, 0.0], y - bottom) else {
                    break;
                };
                if hit.point[1] >= y - RAY_EPSILON * 0.5 {
                    // No downward progress (a ray starting inside a solid
                    // voxel reports toi=0); stop the column.
                    break;
                }
                y = hit.point[1] - RAY_EPSILON;
                grid.surfaces_found += 1;
                if hit.normal_y < MIN_FLOOR_NORMAL_Y {
                    continue;
                }
                // One upward ray answers both headroom and interior: no
                // ceiling at all means open sky (rooftop/void), a ceiling
                // closer than agent height means the cell is not standable.
                let above = ray_surface(
                    scene,
                    [x, hit.point[1] + RAY_EPSILON, z],
                    [0.0, 1.0, 0.0],
                    MAX_CEILING_HEIGHT_UNITS,
                );
                let Some(ceiling) = above else {
                    continue;
                };
                if ceiling.point[1] - hit.point[1] < AGENT_HEIGHT_UNITS {
                    continue;
                }
                column_walkable = true;
                grid.cells.insert(
                    NavCell::of(x as f32, z as f32, hit.point[1] as f32),
                    hit.point[1] as f32,
                );
            }
            if column_walkable {
                grid.columns_with_support += 1;
            }
        }
    }
    grid
}

/// Where an authored spawn point lands when snapped to ground.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SpawnGrounding {
    pub support_y: f32,
    /// `spawn.y - support_y`: positive for floaters, negative if the authored
    /// point was embedded below its floor.
    pub drop: f32,
    pub cell: NavCell,
}

/// Snap an authored spawn point to the highest up-facing support at or below
/// it (never onto a floor above the spawn), within `max_drop` units.
pub fn ground_spawn(
    scene: &VoxelCollisionScene,
    spawn: [f32; 3],
    max_drop: f32,
) -> Option<SpawnGrounding> {
    // Start a hair above the authored point so a spawn sitting exactly on its
    // floor still registers, but never so high that a ledge above the spawn
    // could win the ray.
    let origin = [spawn[0] as f64, (spawn[1] + 0.25) as f64, spawn[2] as f64];
    let hit = ray_surface(scene, origin, [0.0, -1.0, 0.0], (max_drop + 0.25) as f64)?;
    if hit.normal_y < MIN_FLOOR_NORMAL_Y {
        return None;
    }
    let support_y = hit.point[1] as f32;
    Some(SpawnGrounding {
        support_y,
        drop: spawn[1] - support_y,
        cell: NavCell::of(spawn[0], spawn[2], support_y),
    })
}
