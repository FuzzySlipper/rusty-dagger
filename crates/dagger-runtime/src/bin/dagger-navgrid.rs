//! Navigation grid derivation proof for Privateer's Hold (task 6639).
//!
//! Like the walkthrough, this command reads the committed generated project
//! and proves facts through the real collision authority — never a re-derived
//! or approximate geometry path. It:
//!
//! 1. Derives the walkable nav grid from the dungeon trimesh (bounded sweep
//!    of ray-down support samples over the mesh AABB; see `navgrid` module).
//! 2. Asserts known-walkable spots are walkable (start room main floor, the
//!    spawn ledge ABOVE that same column — a multi-level column), coverage
//!    reaches beyond the spawn RDB block, and a void/wall probe stays
//!    unwalkable.
//! 3. Grounds all 43 authored enemy spawns through the same authority and
//!    reports where each lands when snapped (most authored spawns float;
//!    landing data feeds the patrol task 6641).
//! 4. Writes content/projects/privateers-hold.navgrid.json for the flycam
//!    grid gizmo (`--check` verifies freshness instead).
//!
//! Usage:
//!   dagger-navgrid [project.json] [--write | --check]

use std::collections::BTreeSet;
use std::env;
use std::fs;

use dagger_runtime::navgrid::MAX_SPAWN_DROP_UNITS;
use dagger_runtime::{derive_nav_grid, ground_spawn, DaggerRuntime};

const BLOCK_SIDE: f32 = 51.2;

fn block_of(x: f32, z: f32) -> (i32, i32) {
    (
        (x / BLOCK_SIDE).floor() as i32,
        (z / BLOCK_SIDE).floor() as i32,
    )
}

fn round3(value: f32) -> f32 {
    (value * 1000.0).round() / 1000.0
}

/// Explain why a landing cell failed derivation: report the up-ray
/// obstruction (headroom/interior checks) at the cell center.
fn diagnose_cell(
    scene: &engine_spatial::VoxelCollisionScene,
    spawn: [f32; 3],
    support_y: f32,
) -> String {
    use dagger_runtime::navgrid::{CELL_SIZE, MAX_CEILING_HEIGHT_UNITS};
    let cx = (spawn[0] / CELL_SIZE).floor();
    let cz = (spawn[2] / CELL_SIZE).floor();
    let x = ((cx + 0.5) * CELL_SIZE) as f64;
    let z = ((cz + 0.5) * CELL_SIZE) as f64;
    let origin = [x, support_y as f64 + 0.05, z];
    match scene.raycast_world(origin, [0.0, 1.0, 0.0], MAX_CEILING_HEIGHT_UNITS) {
        Some(hit) => {
            let y = match hit {
                engine_spatial::SpatialCollisionHit::Voxel(hit) => hit.point[1],
                engine_spatial::SpatialCollisionHit::StaticMesh(hit) => hit.point.to_array()[1],
            };
            format!(" [up-ray obstruction at +{:.3}m]", y - support_y as f64)
        }
        None => format!(" [no enclosure within {MAX_CEILING_HEIGHT_UNITS}m — exterior?]"),
    }
}

fn main() {
    let mut args = env::args().skip(1);
    let mut project_path: Option<String> = None;
    let mut mode = String::from("--write");
    while let Some(arg) = args.next() {
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
    let navgrid_path = project_path.replace(".project.json", ".navgrid.json");
    let document = fs::read_to_string(&project_path).expect("read project document");

    let runtime = DaggerRuntime::from_project_json(&document).expect("admit project");
    let (min, max) = runtime
        .dungeon_bounds()
        .expect("committed project has dungeon bounds");
    let grid = derive_nav_grid(runtime.collision_scene(), min, max);
    println!(
        "navgrid: cells={} columns={} surfaces={} bounds x[{:.1}..{:.1}] y[{:.1}..{:.1}] z[{:.1}..{:.1}]",
        grid.len(),
        grid.columns_with_support,
        grid.surfaces_found,
        min[0],
        max[0],
        min[1],
        max[1],
        min[2],
        max[2]
    );

    let mut failures = Vec::new();

    // 1. Start room main floor is walkable at its known support height.
    let main_floor = grid.levels_in_column(56, -25);
    let main_floor_ok = main_floor
        .iter()
        .any(|(_, support_y)| (*support_y - 32.0).abs() < 0.5);
    println!("start room main floor levels: {main_floor_ok} {main_floor:?}");
    if !main_floor_ok {
        failures.push(format!(
            "start room main floor column (56, -25) has no level near y=32.0: {main_floor:?}"
        ));
    }

    // 2. The spawn ledge is walkable ABOVE the main floor in the same column
    //    region — multi-level derivation must record both.
    let spawn_column = grid.levels_in_column(56, -25);
    let ledge_ok = spawn_column
        .iter()
        .any(|(_, support_y)| (*support_y - 38.4).abs() < 0.75);
    if !ledge_ok {
        failures.push(format!(
            "spawn ledge column has no level near y=38.4: {spawn_column:?}"
        ));
    }
    if spawn_column.len() < 2 {
        failures.push(format!(
            "spawn column should hold multiple levels (ledge + main floor): {spawn_column:?}"
        ));
    }

    // 3. Coverage reaches beyond the spawn RDB block (the dungeon spans
    //    several 51.2m blocks; a start-room-only grid is a derivation bug).
    let blocks: BTreeSet<(i32, i32)> = grid
        .iter()
        .map(|(cell, _)| {
            let [x, z] = cell.center();
            block_of(x, z)
        })
        .collect();
    println!("navgrid covers {} RDB blocks: {blocks:?}", blocks.len());
    if blocks.len() < 4 {
        failures.push(format!(
            "nav grid covers only {} RDB blocks: {blocks:?}",
            blocks.len()
        ));
    }

    // 4. Negative probes: solid rock between rooms has no walkable cell at
    //    any level — (18,-170) sits in a rock gap directly beside walkable
    //    floor (3/8 neighbours walkable), (38,-185) is deep rock. The mesh is
    //    interior shells, so vertical rays through walls/rock find no
    //    up-facing support: walls are structurally unwalkable.
    for (x, z) in [(18, -170), (38, -185)] {
        if !grid.levels_in_column(x, z).is_empty() {
            failures.push(format!(
                "rock column ({x}, {z}) has walkable cells: {:?}",
                grid.levels_in_column(x, z)
            ));
        }
    }

    // 5. Ground all authored enemy spawns through the same authority.
    let project: serde_json::Value = serde_json::from_str(&document).expect("parse project");
    let mut spawns = project["scenes"][0]["entities"]
        .as_array()
        .expect("entities")
        .iter()
        .filter_map(|entity| {
            let name = entity["name"].as_str()?;
            if !name.starts_with("enemy-") {
                return None;
            }
            let translation = entity["translation"].as_array()?;
            Some((
                name.to_string(),
                [
                    translation[0].as_f64()? as f32,
                    translation[1].as_f64()? as f32,
                    translation[2].as_f64()? as f32,
                ],
            ))
        })
        .collect::<Vec<_>>();
    spawns.sort_by(|a, b| a.0.cmp(&b.0));
    println!("grounding {} enemy spawns:", spawns.len());
    let mut spawn_entries = Vec::new();
    let mut floaters = 0usize;
    for (name, spawn) in &spawns {
        let grounding = ground_spawn(runtime.collision_scene(), *spawn, MAX_SPAWN_DROP_UNITS);
        match grounding {
            Some(grounding) => {
                if grounding.drop > 0.05 {
                    floaters += 1;
                }
                let in_grid = grid.contains(grounding.cell);
                if !in_grid {
                    failures.push(format!(
                        "{name} lands on cell {:?} which is not walkable in the grid",
                        grounding.cell
                    ));
                }
                let diagnosis = if in_grid {
                    String::new()
                } else {
                    diagnose_cell(runtime.collision_scene(), *spawn, grounding.support_y)
                };
                println!(
                    "  {name}: spawn_y={:.3} lands_y={:.3} drop={:.3} cell={:?} walkable={in_grid}{diagnosis}",
                    spawn[1], grounding.support_y, grounding.drop, grounding.cell
                );
                spawn_entries.push(serde_json::json!({
                    "name": name,
                    "spawn": [round3(spawn[0]), round3(spawn[1]), round3(spawn[2])],
                    "cell": [grounding.cell.x, grounding.cell.z, grounding.cell.level],
                    "landingY": round3(grounding.support_y),
                    "drop": round3(grounding.drop),
                }));
            }
            None => {
                failures.push(format!(
                    "{name} found no up-facing support within {MAX_SPAWN_DROP_UNITS}m below spawn"
                ));
            }
        }
    }
    println!("spawn grounding: {} spawns, {floaters} floaters", spawns.len());
    if spawns.len() != 43 {
        failures.push(format!("expected 43 enemy spawns, found {}", spawns.len()));
    }

    // 6. Artifact for the flycam grid gizmo.
    let cells: Vec<serde_json::Value> = grid
        .iter()
        .map(|(cell, support_y)| {
            serde_json::json!([cell.x, cell.z, cell.level, round3(support_y)])
        })
        .collect();
    let artifact = serde_json::json!({
        "version": 1,
        "cellSize": dagger_runtime::navgrid::CELL_SIZE,
        "levelQuantum": dagger_runtime::navgrid::LEVEL_QUANTUM,
        "bounds": { "min": min, "max": max },
        "cells": cells,
        "spawns": spawn_entries,
    });
    let text = serde_json::to_string(&artifact).expect("encode navgrid") + "\n";
    if mode == "--check" {
        let actual = fs::read_to_string(&navgrid_path).unwrap_or_default();
        if actual != text {
            eprintln!("{navgrid_path} is stale; run dagger-navgrid --write");
            std::process::exit(1);
        }
        println!("{navgrid_path} up to date ({} cells)", cells.len());
    } else {
        fs::write(&navgrid_path, &text).expect("write navgrid");
        println!("navgrid: wrote {navgrid_path} ({} cells)", cells.len());
    }

    if failures.is_empty() {
        println!("DAGGER NAVGRID PASSED");
    } else {
        println!("DAGGER NAVGRID FAILED:");
        for failure in failures {
            println!(" - {failure}");
        }
        std::process::exit(1);
    }
}
