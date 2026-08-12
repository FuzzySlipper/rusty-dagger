//! Rust-owned runtime seam for the imported Privateer's Hold project.
//!
//! This crate deliberately owns only Daggerfall-specific admission and player
//! control. It consumes generic Rusty Engine spatial/entity services and does
//! not depend on the loading-bay demo or a sibling checkout.

#![forbid(unsafe_code)]

pub mod animation;
pub mod combat_assets;
pub mod directional;
pub mod navgrid;
pub mod patrol;
mod player;
mod project;
mod runtime;

pub use animation::{AnimationService, FrameUpdate, SpriteEntry, SpriteKind};
pub use combat_assets::{
    AudioAsset, CombatAssetCatalog, CombatFrame, EffectAsset, WeaponAnimation, WeaponAsset,
};
pub use directional::evaluate_directional;
pub use navgrid::{derive_nav_grid, ground_spawn, level_of, NavCell, NavGrid, SpawnGrounding};
pub use patrol::{PatrolGrid, PatrolService, PositionUpdate};

pub use player::{
    PlayerControlFact, PlayerControlReceipt, PlayerControllerConfig, PlayerControllerState,
    PlayerInputBindings, ResolvedPlayerAction, MAX_PLAYER_LOOK_DEGREES_PER_UNIT,
    MAX_PLAYER_SPEED_UNITS_PER_SECOND, MAX_PLAYER_STEP_UP_UNITS,
};
pub use project::{AdmittedProject, ProjectAdmissionError};
pub use runtime::{
    ActorGameplayReadout, CombatAttemptRecord, CombatRecord, ContentEntityReadout, ContentError,
    ContentLiveReadout, DaggerRuntime, EnemyReferenceReadout, EnemyStatsReadout,
    ExperimentEvaluation, ExperimentReadout, LiveActorResources, MeleePresentationPhase,
    MeleePresentationReadout, RuntimeError, SessionCalculationRecord, CALCULATION_HISTORY_LIMIT,
    MELEE_ANTICIPATION_SECONDS, MELEE_CONTACT_SECONDS, MELEE_RECOVERY_SECONDS,
    MELEE_REJECTION_SECONDS, STARTER_EXPERIMENT_JSON,
};

#[cfg(test)]
mod tests {
    use super::{
        AdmittedProject, DaggerRuntime, PlayerControlFact, ProjectAdmissionError,
        ResolvedPlayerAction, RuntimeError, CALCULATION_HISTORY_LIMIT, STARTER_EXPERIMENT_JSON,
    };
    use rusty_engine::core_math::Vec3;

    const PROJECT: &str = include_str!("../../../content/projects/privateers-hold.project.json");
    const NAVGRID: &str = include_str!("../../../content/projects/privateers-hold.navgrid.json");

    fn adversarial_wall_project() -> String {
        // Inject an additive voxelEnvironment carrying a tall wall in front of
        // spawn. The committed project has no proxy (trimesh authority), so the
        // adversarial controller probe adds its own optional voxel authority.
        let mut project: serde_json::Value = serde_json::from_str(PROJECT).expect("project json");
        let scenes = project["scenes"].as_array_mut().expect("scenes");
        let scene = &mut scenes[0];
        scene["entities"][0]["playerController"]["fallSpeedUnitsPerSecond"] =
            serde_json::Value::from(0.1);
        let mut voxels = Vec::new();
        for x in 54..61 {
            for y in 77..91 {
                voxels.push(serde_json::json!({
                    "address": [x, y, -24],
                    "materialSlot": 1,
                }));
            }
        }
        scene["voxelEnvironment"] = serde_json::json!({
            "kind": "material",
            "voxelSize": 0.5,
            "chunkSize": 16,
            "materialVoxels": voxels,
        });
        serde_json::to_string(&project).expect("adversarial project")
    }

    #[test]
    fn admits_the_committed_privateers_hold_project() {
        let runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        assert_eq!(runtime.player().raw(), 1);
        // Trimesh authority: no voxel proxy; the dungeon mesh is the collider.
        assert_eq!(runtime.collision_scene().solid_voxel_count(), 0);
        assert_eq!(runtime.player_controller().step_up_units, Some(0.75));
    }

    #[test]
    fn the_committed_project_requires_a_collision_authority() {
        // A project with neither a trimesh dungeon mesh nor any voxels fails
        // closed rather than admitting a collision-less world.
        let mut value: serde_json::Value = serde_json::from_str(PROJECT).expect("project json");
        for asset in value["assets"].as_array_mut().expect("assets") {
            if asset["id"].as_str() == Some("mesh/privateers-hold") {
                asset.as_object_mut().unwrap().remove("staticMesh");
            }
        }
        let error = AdmittedProject::from_json(&serde_json::to_string(&value).unwrap())
            .expect_err("project without any collision authority must fail closed");
        // The mesh asset is present but its staticMesh payload was stripped:
        // no trimesh collider can be built and there are no voxels.
        assert!(matches!(
            error,
            ProjectAdmissionError::MissingDungeonCollider
        ));
    }

    #[test]
    fn a_project_with_no_collision_authority_at_all_fails_closed() {
        // Remove the mesh asset entirely AND there are no voxels: nothing can
        // authorize collision, so admission fails closed.
        let mut value: serde_json::Value = serde_json::from_str(PROJECT).expect("project json");
        let assets = value["assets"].as_array_mut().expect("assets");
        assets.retain(|asset| asset["id"].as_str() != Some("mesh/privateers-hold"));
        let error = AdmittedProject::from_json(&serde_json::to_string(&value).unwrap())
            .expect_err("project with no mesh and no voxels must fail closed");
        assert!(matches!(
            error,
            ProjectAdmissionError::MissingCollisionAuthority
        ));
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
    fn canonical_look_preserves_signs_wraps_yaw_and_clamps_pitch() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let initial = runtime.player_state();
        runtime
            .apply_player_action(ResolvedPlayerAction::Look {
                yaw_delta: 0.5,
                pitch_delta: 0.5,
            })
            .expect("canonical look");
        let changed = runtime.player_state();
        assert_ne!(changed.yaw_degrees, initial.yaw_degrees);
        assert!(changed.pitch_degrees > initial.pitch_degrees);
        for _ in 0..20 {
            runtime
                .apply_player_action(ResolvedPlayerAction::Look {
                    yaw_delta: 0.0,
                    pitch_delta: 1.0,
                })
                .expect("bounded pitch");
        }
        assert!((runtime.player_state().pitch_degrees - 89.0).abs() < 0.001);
        assert!((-180.0..180.0).contains(&runtime.player_state().yaw_degrees));
    }

    #[test]
    fn canonical_look_heading_drives_camera_relative_forward_motion() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let yaw_before = runtime.player_state().yaw_degrees;
        runtime
            .apply_player_action(ResolvedPlayerAction::Look {
                yaw_delta: 1.0,
                pitch_delta: 0.0,
            })
            .expect("turn right");
        let yaw = runtime.player_state().yaw_degrees.to_radians();
        let yaw_delta =
            (runtime.player_state().yaw_degrees - yaw_before + 540.0).rem_euclid(360.0) - 180.0;
        assert!(
            yaw_delta > 0.0,
            "positive horizontal look must publish positive yaw"
        );
        let receipt = runtime
            .apply_player_action(ResolvedPlayerAction::Move {
                forward: 1.0,
                right: 0.0,
            })
            .expect("move in the camera heading");
        let wish = receipt
            .motion
            .expect("canonical movement receipt")
            .wish_velocity;
        let expected_forward = rusty_engine::core_math::Vec3::new(yaw.sin(), 0.0, -yaw.cos());
        assert!(
            wish.dot(expected_forward) > 0.0,
            "W wish velocity {wish:?} must follow camera forward {expected_forward:?}"
        );
    }

    #[test]
    fn canonical_controller_retains_grounded_continuation_across_idle_ticks() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let mut last_sequence = 0;
        for _ in 0..30 {
            let receipt = runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 0.0,
                    right: 0.0,
                })
                .expect("settle tick");
            let motion = receipt.motion.expect("Engine movement receipt");
            assert!(motion.command_sequence > last_sequence);
            last_sequence = motion.command_sequence;
        }
        let settled = runtime.player_position().expect("settled position");
        let motion = runtime
            .entities()
            .character_motion(runtime.player())
            .expect("caller-owned Engine continuation state");
        assert!(motion.grounded);
        for _ in 0..10 {
            runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 0.0,
                    right: 0.0,
                })
                .expect("stable idle tick");
        }
        assert_eq!(runtime.player_position().expect("idle position"), settled);
        assert!(
            runtime
                .entities()
                .character_motion(runtime.player())
                .expect("continuation state")
                .grounded
        );
    }

    #[test]
    fn applies_a_complete_experiment_and_resets_the_live_run_to_spawn() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let spawn = runtime.player_position().expect("spawn position");
        let mut experiment: serde_json::Value =
            serde_json::from_str(STARTER_EXPERIMENT_JSON).expect("starter experiment");
        experiment["player"]["movement"]["speedUnitsPerSecond"] = serde_json::Value::from(7.0);
        experiment["player"]["stats"]["attributes"]["endurance"] = serde_json::Value::from(60.0);
        experiment["enemies"][0]["stats"]["attributes"]["strength"] = serde_json::Value::from(20.0);
        experiment["enemies"][0]["stats"]["resources"]["baseHealth"] = serde_json::Value::from(4.0);

        let readout = runtime
            .apply_experiment_json(&serde_json::to_string(&experiment).unwrap())
            .expect("apply experiment");
        assert_eq!(readout.move_speed_units_per_second, 7.0);
        assert_eq!(readout.max_health, 115.0);
        assert_eq!(readout.player_stats.max_stamina, 110.0);
        let rat_stats = readout
            .enemy_stats
            .iter()
            .find(|enemy| enemy.mobile_id == 0)
            .expect("Rat gameplay definition");
        assert_eq!(rat_stats.stats.attributes.strength, 20.0);
        assert_eq!(rat_stats.stats.max_health, 5.0);
        assert_eq!(rat_stats.stats.max_stamina, 15.0);
        assert_eq!(readout.calculations.last().unwrap().sequence, 2);

        runtime
            .set_player_position(Vec3::new(spawn.x + 5.0, spawn.y, spawn.z))
            .expect("move away from spawn");
        let reset = runtime.reset_play_session().expect("reset live run");
        assert_eq!(reset.player_position, [spawn.x, spawn.y, spawn.z]);
        assert_eq!(reset.move_speed_units_per_second, 7.0);
        assert_eq!(reset.current_health, 115.0);
        assert_eq!(reset.player_stats.current_stamina, 110.0);
    }

    #[test]
    fn browses_real_enemy_identity_and_jumps_using_live_runtime_state() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install committed live navigation");
        let spawn = runtime.player_position().expect("spawn position");
        let initial = runtime
            .experiment_readout()
            .expect("initial content readout");
        assert_eq!(initial.content.len(), 43);
        let thief = initial
            .content
            .iter()
            .find(|entity| entity.id == 2001)
            .expect("committed thief identity");
        assert_eq!(thief.name, "enemy-thief-1");
        assert_eq!(thief.reference.mobile_id, 138);
        assert_eq!(thief.reference.mobile_name, "Thief");
        assert_eq!(thief.reference.texture_archive, 484);
        assert_eq!(thief.live.resources, None);
        let live_thief = thief.live.position;
        let rat = initial
            .content
            .iter()
            .find(|entity| entity.reference.mobile_id == 0)
            .expect("committed Rat identity");
        let rat_resources = rat.live.resources.expect("Rat live resources");
        assert_eq!(rat.reference.mobile_name, "Rat");
        assert_eq!(rat_resources.current_health, 3.0);
        assert_eq!(rat_resources.current_stamina, 10.0);

        let jumped = runtime.jump_to_content(2001).expect("jump beside thief");
        assert_eq!(jumped.focused_content_id, Some(2001));
        let focused = jumped
            .content
            .iter()
            .find(|entity| entity.id == 2001)
            .expect("focused thief");
        assert_eq!(focused.live.position, live_thief);
        assert!(
            focused.live.distance_from_player < 4.0,
            "jump should land near the live thief, got {}",
            focused.live.distance_from_player
        );
        assert_ne!(jumped.player_position, [spawn.x, spawn.y, spawn.z]);

        let before_unknown = runtime.experiment_readout().unwrap();
        let error = runtime
            .jump_to_content(999_999)
            .expect_err("unknown content must fail closed");
        assert!(matches!(error, RuntimeError::Content(_)));
        assert_eq!(runtime.experiment_readout().unwrap(), before_unknown);

        let reset = runtime
            .reset_play_session()
            .expect("reset after content jump");
        assert_eq!(reset.focused_content_id, None);
        assert_eq!(reset.player_position, [spawn.x, spawn.y, spawn.z]);
    }

    #[test]
    fn fights_a_real_privateers_hold_rat_and_resets_death_authoritatively() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let mut experiment: serde_json::Value =
            serde_json::from_str(STARTER_EXPERIMENT_JSON).expect("starter experiment");
        experiment["player"]["combat"]["hitBonus"] = serde_json::Value::from(100.0);
        experiment["player"]["combat"]["baseDamage"] = serde_json::Value::from(10.0);
        experiment["enemies"][0]["combat"]["armor"] = serde_json::Value::from(0.0);
        runtime
            .apply_experiment_json(&serde_json::to_string(&experiment).unwrap())
            .expect("apply lethal profile");
        runtime.jump_to_content(2007).expect("jump beside real Rat");

        let swinging = runtime
            .attack_focused_target()
            .expect("attack focused real Rat");
        assert!(swinging.combat.is_empty());
        let accepted = swinging
            .combat_attempts
            .last()
            .expect("accepted physical attack attempt");
        assert!(accepted.accepted);
        assert_eq!(accepted.outcome, "swinging");
        assert_eq!(accepted.target_id, None);
        assert_eq!(accepted.stamina_before, 90.0);
        assert_eq!(accepted.stamina_after, 80.0);
        assert_eq!(accepted.cooldown_after, 0.75);
        let melee = swinging
            .melee_presentation
            .as_ref()
            .expect("accepted attack starts visible Rust action state");
        assert_eq!(melee.phase, super::MeleePresentationPhase::Anticipation);
        assert_eq!(melee.target_id, None);
        assert_eq!(melee.target_health_before, None);

        let attacked = runtime
            .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
            .and_then(|_| runtime.experiment_readout())
            .expect("resolve melee only at deterministic contact");
        let record = attacked.combat.last().expect("semantic combat record");
        assert_eq!(record.target_id, 2007);
        assert!(record.line_of_sight_clear);
        assert!(record.resolution.hit);
        assert!(record.resolution.died);
        assert_eq!(record.resolution.health_before, 3.0);
        assert_eq!(record.resolution.health_after, 0.0);
        let accepted = attacked
            .combat_attempts
            .last()
            .expect("accepted physical attack attempt");
        assert!(accepted.accepted);
        assert_eq!(accepted.outcome, "killed");
        assert_eq!(accepted.target_id, Some(2007));
        let melee = attacked
            .melee_presentation
            .as_ref()
            .expect("accepted attack starts visible Rust action state");
        assert_eq!(melee.phase, super::MeleePresentationPhase::Contact);
        assert_eq!(melee.target_id, Some(2007));
        assert_eq!(melee.target_health_before, Some(3.0));
        assert_eq!(melee.target_health_after, Some(0.0));
        assert_eq!(melee.target_max_health, Some(3.0));
        assert_eq!(melee.final_damage, Some(15.0));
        assert!(melee.died);
        let rat = attacked
            .content
            .iter()
            .find(|entity| entity.id == 2007)
            .expect("attacked Rat readout");
        assert_eq!(rat.live.resources.unwrap().current_health, 0.0);

        let rejected = runtime
            .attack_focused_target()
            .expect("cooldown rejection remains readable");
        let rejected = rejected
            .combat_attempts
            .last()
            .expect("cooldown attempt record");
        assert!(!rejected.accepted);
        assert_eq!(rejected.outcome, "cooldown");
        assert_eq!(rejected.stamina_before, rejected.stamina_after);
        assert_eq!(
            runtime.melee_presentation().unwrap().phase,
            super::MeleePresentationPhase::Contact
        );
        for _ in 0..3 {
            runtime
                .tick_play_session(0.25)
                .expect("advance authoritative attack cooldown");
        }
        let empty = runtime
            .attack_focused_target()
            .expect("dead target does not suppress a new swing");
        assert_eq!(
            empty.melee_presentation.unwrap().phase,
            super::MeleePresentationPhase::Anticipation
        );
        runtime
            .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
            .expect("resolve dead-target swing as empty contact");
        assert_eq!(
            runtime
                .experiment_readout()
                .unwrap()
                .combat_attempts
                .last()
                .unwrap()
                .outcome,
            "miss"
        );
        let reset = runtime.reset_play_session().expect("reset combat run");
        assert!(reset.combat.is_empty());
        assert!(reset.combat_attempts.is_empty());
        assert_eq!(reset.player_attack_cooldown_remaining, 0.0);
        assert!(reset.melee_presentation.is_none());
        assert_eq!(
            reset
                .content
                .iter()
                .find(|entity| entity.id == 2007)
                .unwrap()
                .live
                .resources
                .unwrap()
                .current_health,
            3.0
        );

        experiment["player"]["combat"]["staminaCost"] = serde_json::Value::from(100.0);
        runtime
            .apply_experiment_json(&serde_json::to_string(&experiment).unwrap())
            .expect("apply exhausting profile");
        runtime
            .jump_to_content(2007)
            .expect("jump beside Rat with exhausting profile");
        let exhausted = runtime
            .attack_focused_target()
            .expect("low stamina still starts the classic swing");
        assert!(exhausted.combat.is_empty());
        let exhausted = exhausted
            .combat_attempts
            .last()
            .expect("low-stamina attempt");
        assert!(exhausted.accepted);
        assert_eq!(exhausted.outcome, "swinging");
        assert_eq!(exhausted.stamina_before, 90.0);
        assert_eq!(exhausted.stamina_after, 0.0);
        assert_eq!(
            runtime.melee_presentation().unwrap().phase,
            super::MeleePresentationPhase::Anticipation
        );
        for _ in 0..3 {
            runtime
                .tick_play_session(0.25)
                .expect("finish low-stamina swing and cooldown");
        }
        let zero_stamina = runtime
            .attack_focused_target()
            .expect("zero stamina still starts another classic swing");
        let zero_stamina = zero_stamina
            .combat_attempts
            .last()
            .expect("zero-stamina attempt");
        assert!(zero_stamina.accepted);
        assert_eq!(zero_stamina.outcome, "swinging");
        assert_eq!(zero_stamina.stamina_before, 0.0);
        assert_eq!(zero_stamina.stamina_after, 0.0);
    }

    #[test]
    fn runtime_owns_tunable_rat_and_skeletal_warrior_encounters() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install committed navigation");

        runtime.jump_to_content(2007).expect("jump beside Rat");
        for _ in 0..20 {
            runtime.tick_play_session(0.1).expect("Rat encounter tick");
            if runtime
                .experiment_readout()
                .unwrap()
                .player_stats
                .current_health
                < 85.0
            {
                break;
            }
        }
        let rat = runtime.experiment_readout().expect("Rat encounter readout");
        assert!(rat
            .encounter_decisions
            .iter()
            .any(|record| record.enemy_id == 2007 && record.to.as_deref() == Some("chase")));
        assert!(rat.encounter_decisions.iter().any(|record| {
            record.enemy_id == 2007
                && record.decision == "melee attack"
                && record.damage == Some(4.0)
                && record.line_of_sight_clear == Some(true)
        }));
        assert_eq!(rat.player_stats.current_health, 81.0);

        runtime
            .reset_play_session()
            .expect("reset before Skeletal Warrior");
        runtime
            .jump_to_content(2000)
            .expect("jump beside Skeletal Warrior");
        for _ in 0..20 {
            runtime
                .tick_play_session(0.1)
                .expect("Skeletal Warrior encounter tick");
            if runtime
                .experiment_readout()
                .unwrap()
                .player_stats
                .current_health
                < 85.0
            {
                break;
            }
        }
        let skeleton = runtime
            .experiment_readout()
            .expect("Skeletal Warrior encounter readout");
        assert!(skeleton.encounter_decisions.iter().any(|record| {
            record.enemy_id == 2000
                && record.decision == "melee attack"
                && record.damage == Some(8.0)
        }));
        assert_eq!(skeleton.player_stats.current_health, 77.0);

        let mut quiet: serde_json::Value =
            serde_json::from_str(STARTER_EXPERIMENT_JSON).expect("starter experiment");
        quiet["enemies"][0]["behavior"]["detectionRange"] = serde_json::Value::from(0.5);
        quiet["enemies"][0]["behavior"]["attackRange"] = serde_json::Value::from(0.4);
        quiet["enemies"][0]["behavior"]["patrolSpeed"] = serde_json::Value::from(0.0);
        runtime
            .apply_experiment_json(&serde_json::to_string(&quiet).unwrap())
            .expect("apply quiet Rat behavior");
        runtime.reset_play_session().expect("reset quiet run");
        runtime
            .jump_to_content(2007)
            .expect("jump beside quiet Rat");
        for _ in 0..20 {
            runtime.tick_play_session(0.1).expect("quiet Rat tick");
        }
        let quiet = runtime.experiment_readout().expect("quiet Rat readout");
        assert_eq!(quiet.player_stats.current_health, 85.0);
        assert!(quiet
            .encounter_decisions
            .iter()
            .all(|record| record.enemy_id != 2007 || record.decision != "melee attack"));
    }

    #[test]
    fn evaluates_an_experiment_without_mutating_the_play_session() {
        let runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let before = runtime
            .experiment_readout()
            .expect("readout before preview");
        let mut experiment: serde_json::Value =
            serde_json::from_str(STARTER_EXPERIMENT_JSON).expect("starter experiment");
        experiment["player"]["movement"]["speedUnitsPerSecond"] = serde_json::Value::from(8.0);
        experiment["player"]["stats"]["resources"]["baseHealth"] = serde_json::Value::from(20.0);
        experiment["player"]["stats"]["attributes"]["endurance"] = serde_json::Value::from(70.0);
        experiment["player"]["stats"]["resources"]["healthPerEndurance"] =
            serde_json::Value::from(2.0);

        let evaluation = runtime
            .evaluate_experiment_json(&serde_json::to_string(&experiment).unwrap())
            .expect("preview experiment");
        assert_eq!(evaluation.move_speed_units_per_second, 8.0);
        assert_eq!(evaluation.max_health, 160.0);
        assert_eq!(evaluation.calculation.result, 160.0);
        assert_eq!(evaluation.player_stats.max_stamina, 120.0);
        assert_eq!(evaluation.enemy_stats[0].stats.max_health, 3.0);
        assert_eq!(runtime.experiment_readout().unwrap(), before);

        experiment["player"]["movement"]["speedUnitsPerSecond"] = serde_json::Value::from(0.0);
        let error = runtime
            .evaluate_experiment_json(&serde_json::to_string(&experiment).unwrap())
            .expect_err("invalid preview must fail closed");
        assert!(matches!(error, RuntimeError::Experiment(_)));
        assert_eq!(runtime.experiment_readout().unwrap(), before);
    }

    #[test]
    fn rejected_experiment_is_failure_atomic_and_history_is_bounded() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let before = runtime
            .experiment_readout()
            .expect("readout before rejection");
        let mut invalid: serde_json::Value =
            serde_json::from_str(STARTER_EXPERIMENT_JSON).expect("starter experiment");
        invalid["player"]["movement"]["speedUnitsPerSecond"] = serde_json::Value::from(0.0);
        let error = runtime
            .apply_experiment_json(&serde_json::to_string(&invalid).unwrap())
            .expect_err("zero speed must be rejected");
        assert!(matches!(error, RuntimeError::Experiment(_)));
        assert_eq!(
            runtime
                .experiment_readout()
                .expect("readout after rejection"),
            before
        );

        let mut missing_mobile: serde_json::Value =
            serde_json::from_str(STARTER_EXPERIMENT_JSON).expect("starter experiment");
        missing_mobile["enemies"][0]["mobileId"] = serde_json::Value::from(254);
        let error = runtime
            .apply_experiment_json(&serde_json::to_string(&missing_mobile).unwrap())
            .expect_err("unknown project mobile must be rejected");
        assert!(format!("{error}").contains("does not identify an enemy"));
        assert_eq!(runtime.experiment_readout().unwrap(), before);

        for speed_tenths in 1..=(CALCULATION_HISTORY_LIMIT + 4) {
            let speed = 1.0 + speed_tenths as f32 / 10.0;
            let mut experiment: serde_json::Value =
                serde_json::from_str(STARTER_EXPERIMENT_JSON).unwrap();
            experiment["player"]["movement"]["speedUnitsPerSecond"] =
                serde_json::Value::from(speed);
            runtime
                .apply_experiment_json(&serde_json::to_string(&experiment).unwrap())
                .expect("apply bounded-history experiment");
        }
        let history = runtime.experiment_readout().unwrap().calculations;
        assert_eq!(history.len(), CALCULATION_HISTORY_LIMIT);
        assert!(history.first().unwrap().sequence > 1);
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
    fn canonical_controller_stops_at_a_real_project_wall_without_rising() {
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

        let settled_y = runtime.player_position().expect("settled position").y;
        let mut blocked = 0;
        let mut last_horizontal = f32::INFINITY;
        for _ in 0..20 {
            let before = runtime.player_position().expect("before position");
            let receipt = runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 1.0,
                    right: 0.0,
                })
                .expect("blocked action");
            let after = runtime.player_position().expect("after position");
            blocked += usize::from(
                receipt
                    .facts
                    .iter()
                    .any(|fact| matches!(fact, PlayerControlFact::Blocked { .. })),
            );
            last_horizontal = (after.x - before.x).hypot(after.z - before.z);
            assert!(after.y <= settled_y + 0.001);
        }
        assert!(blocked > 0, "canonical receipt never reported wall contact");
        assert!(
            last_horizontal < 0.001,
            "controller did not come to rest at wall"
        );
    }

    #[test]
    fn canonical_controller_slides_along_wall_without_manufacturing_a_step() {
        for (forward, right) in [(1.0, 1.0), (0.5, 1.0)] {
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
            let mut accepted_steps = 0;
            for _ in 0..40 {
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
                accepted_steps += usize::from(
                    receipt
                        .motion
                        .as_ref()
                        .and_then(|motion| motion.step)
                        .is_some_and(|step| step.accepted),
                );
            }
            assert!(
                blocked_actions > 0,
                "diagonal route never contacted the wall for input ({forward}, {right})"
            );
            assert!(
                horizontal_slide > 0.001,
                "diagonal slide was lost for input ({forward}, {right})"
            );
            assert_eq!(accepted_steps, 0, "wall contact became a false up-step");
        }
    }
}
