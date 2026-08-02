//! Rust-owned runtime seam for the imported Privateer's Hold project.
//!
//! This crate deliberately owns only Daggerfall-specific admission and player
//! control. It consumes generic Rusty Engine spatial/entity services and does
//! not depend on the loading-bay demo or a sibling checkout.

#![forbid(unsafe_code)]

mod player;
mod project;
mod runtime;

pub use player::{
    PlayerControlFact, PlayerControlReceipt, PlayerControllerConfig, PlayerControllerState,
    PlayerInputBindings, ResolvedPlayerAction, FALL_SUBSTEP_UNITS,
    MAX_PLAYER_LOOK_DEGREES_PER_UNIT, MAX_PLAYER_SPEED_UNITS_PER_SECOND, MAX_PLAYER_STEP_UP_UNITS,
};
pub use project::{AdmittedProject, ProjectAdmissionError};
pub use runtime::{DaggerRuntime, RuntimeError};

#[cfg(test)]
mod tests {
    use super::{
        AdmittedProject, DaggerRuntime, PlayerControlFact, ProjectAdmissionError,
        ResolvedPlayerAction,
    };

    const PROJECT: &str = include_str!("../../../content/projects/privateers-hold.project.json");

    fn adversarial_wall_project() -> String {
        let mut project: serde_json::Value = serde_json::from_str(PROJECT).expect("project json");
        let scenes = project["scenes"].as_array_mut().expect("scenes");
        let scene = &mut scenes[0];
        scene["entities"][0]["playerController"]["fallSpeedUnitsPerSecond"] =
            serde_json::Value::from(0.1);
        let voxels = scene["voxelEnvironment"]["materialVoxels"]
            .as_array_mut()
            .expect("material voxels");
        for x in 54..61 {
            for y in 77..91 {
                voxels.push(serde_json::json!({
                    "address": [x, y, -24],
                    "materialSlot": 1,
                }));
            }
        }
        serde_json::to_string(&project).expect("adversarial project")
    }

    #[test]
    fn admits_the_committed_privateers_hold_project() {
        let runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        assert_eq!(runtime.player().raw(), 1);
        assert_eq!(runtime.collision_scene().solid_voxel_count(), 51_294);
        assert_eq!(runtime.player_controller().step_up_units, Some(0.75));
    }

    #[test]
    fn malformed_action_is_rejected_without_mutating_the_real_project_runtime() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let before = runtime.player_position().expect("player position");
        let error = runtime
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: 1.01,
                right: 0.0,
            })
            .expect_err("out-of-range input must fail closed");
        assert!(format!("{error}").contains("InvalidAction"));
        assert_eq!(runtime.player_position().expect("player position"), before);
    }

    #[test]
    fn dangling_entry_scene_is_rejected_while_absent_entry_scene_uses_the_first_scene() {
        let mut dangling: serde_json::Value = serde_json::from_str(PROJECT).expect("project json");
        dangling["entryScene"] = serde_json::Value::String("scene/missing".to_string());
        let error = AdmittedProject::from_json(
            &serde_json::to_string(&dangling).expect("encode dangling project"),
        )
        .expect_err("dangling entry scene must fail closed");
        assert!(matches!(
            error,
            ProjectAdmissionError::UnknownEntryScene { scene_id } if scene_id == "scene/missing"
        ));

        dangling["entryScene"] = serde_json::Value::Null;
        let admitted = AdmittedProject::from_json(
            &serde_json::to_string(&dangling).expect("encode default-scene project"),
        )
        .expect("absent entry scene may select the first scene");
        assert_eq!(admitted.scene_id, "scene/privateers-hold");
    }

    #[test]
    fn blocked_step_up_is_failure_atomic_against_a_real_project_wall() {
        let document = adversarial_wall_project();
        let mut runtime = DaggerRuntime::from_project_json(&document).expect("admit project");
        for _ in 0..30 {
            runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 0.0,
                    right: 0.0,
                })
                .expect("settle action");
        }

        for _ in 0..2 {
            let before = runtime.player_position().expect("before position");
            let receipt = runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 1.0,
                    right: 0.0,
                })
                .expect("blocked action");
            let after = runtime.player_position().expect("after position");
            assert!(receipt
                .facts
                .iter()
                .any(|fact| matches!(fact, PlayerControlFact::Blocked { .. })));
            assert!(((after.x - before.x).powi(2) + (after.z - before.z).powi(2)).sqrt() < 0.001);
            assert!(after.y <= before.y + 0.001);
        }
    }

    #[test]
    fn blocked_step_up_does_not_keep_rise_when_retry_only_slides() {
        for (forward, right, action_count) in [(1.0, 1.0, 8), (0.01, 0.02, 64)] {
            let mut runtime = DaggerRuntime::from_project_json(&adversarial_wall_project())
                .expect("admit project");
            for _ in 0..30 {
                runtime
                    .apply_player_action(ResolvedPlayerAction::Move {
                        forward: 0.0,
                        right: 0.0,
                    })
                    .expect("settle action");
            }

            let initial_y = runtime.player_position().expect("initial position").y;
            let mut horizontal_slide = 0.0_f32;
            let mut blocked_actions = 0;
            for _ in 0..action_count {
                let before = runtime.player_position().expect("before position");
                let receipt = runtime
                    .apply_player_action(ResolvedPlayerAction::Move { forward, right })
                    .expect("diagonal blocked action");
                let after = runtime.player_position().expect("after position");
                horizontal_slide +=
                    ((after.x - before.x).powi(2) + (after.z - before.z).powi(2)).sqrt();
                if receipt
                    .facts
                    .iter()
                    .any(|fact| matches!(fact, PlayerControlFact::Blocked { .. }))
                {
                    blocked_actions += 1;
                    assert!(after.y <= initial_y + 0.001);
                }
            }
            assert!(
                blocked_actions >= 2,
                "diagonal wall regression did not repeat a blocked slide for input ({forward}, {right})"
            );
            assert!(
                horizontal_slide > 0.001,
                "diagonal slide was lost for input ({forward}, {right})"
            );
        }
    }
}
