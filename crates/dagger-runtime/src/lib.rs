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

pub use animation::{
    AnimationService, AttackSequence, EnemyAnimationLayout, EnemyAnimationStateLayout,
    EnemyAnimationUpdate, FrameUpdate, SpriteEntry, SpriteKind,
};
pub use combat_assets::{
    AudioAsset, CombatAssetCatalog, CombatFrame, EffectAsset, WeaponAnimation, WeaponAsset,
};
pub use directional::evaluate_directional;
pub use navgrid::{derive_nav_grid, ground_spawn, level_of, NavCell, NavGrid, SpawnGrounding};
pub use patrol::{PatrolGrid, PatrolService, PositionUpdate};

pub use player::{
    PlayerControlFact, PlayerControlReceipt, PlayerControllerConfig, PlayerControllerState,
    PlayerFrameReceipt, PlayerInputBindings, ResolvedPlayerAction, ResolvedPlayerFrame,
    MAX_PLAYER_FRAME_LOOK_UNITS, MAX_PLAYER_FRAME_STEP_SECONDS, MAX_PLAYER_LOOK_DEGREES_PER_UNIT,
    MAX_PLAYER_SPEED_UNITS_PER_SECOND, MAX_PLAYER_STEP_UP_UNITS,
};
pub use project::{AdmittedProject, ProjectAdmissionError};
pub use runtime::{
    ActorAttributeReadout, ActorGameplayReadout, CombatAttemptRecord, CombatRecord,
    ContentEntityReadout, ContentError, ContentLiveReadout, DaggerRuntime,
    EnemyPresentationReadout, EnemyReferenceReadout, GameplayPackageReadout, LabReadout,
    LiveActorResources, MeleePresentationPhase, MeleePresentationReadout, NamedEncounterReadout,
    RuntimeError, MELEE_ACTION_ID, MELEE_ANTICIPATION_SECONDS, MELEE_CONTACT_SECONDS,
    MELEE_RECOVERY_SECONDS, MELEE_REJECTION_SECONDS,
};

#[cfg(test)]
mod tests {
    use super::{
        AdmittedProject, DaggerRuntime, PlayerControlFact, ProjectAdmissionError,
        ResolvedPlayerAction, ResolvedPlayerFrame, RuntimeError,
    };
    use rusty_engine::core_math::Vec3;

    const PROJECT: &str = include_str!("../../../content/projects/privateers-hold.project.json");
    const NAVGRID: &str = include_str!("../../../content/projects/privateers-hold.navgrid.json");
    const NAMED_ENCOUNTERS: &str = include_str!("../../../data/encounters/privateers-hold.json");

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
    fn sampled_frame_applies_complete_look_before_declared_duration_motion() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let yaw_before = runtime.player_state().yaw_degrees;
        let receipt = runtime
            .apply_player_frame(ResolvedPlayerFrame {
                forward: 1.0,
                right: 0.0,
                yaw_delta: 0.5,
                pitch_delta: 0.25,
                step_seconds: 0.04,
            })
            .expect("one sampled input frame");
        let yaw_after = runtime.player_state().yaw_degrees;
        let yaw_delta = (yaw_after - yaw_before + 540.0).rem_euclid(360.0) - 180.0;
        let yaw = yaw_after.to_radians();
        assert!(yaw_delta > 0.0);
        let expected_forward = rusty_engine::core_math::Vec3::new(yaw.sin(), 0.0, -yaw.cos());
        assert!(
            receipt.motion.wish_velocity.dot(expected_forward) > 0.0,
            "same-frame movement must use the post-look heading"
        );
        assert_eq!(
            receipt.motion.command_sequence, 3,
            "40ms product frames split into three bounded Engine ticks, not the authored 100ms action duration"
        );
        assert!(receipt
            .facts
            .iter()
            .any(|fact| matches!(fact, PlayerControlFact::LookChanged { .. })));
    }

    #[test]
    fn sampled_frame_accepts_a_bounded_accumulated_pointer_burst() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        runtime
            .apply_player_frame(ResolvedPlayerFrame {
                forward: 0.0,
                right: 0.0,
                yaw_delta: 10.0,
                pitch_delta: 10.0,
                step_seconds: 0.04,
            })
            .expect("accumulated look is deterministically partitioned");
        let state = runtime.player_state();
        assert!(state.yaw_degrees.is_finite());
        assert!((state.pitch_degrees - 89.0).abs() < 0.001);
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
    fn committed_gameplay_package_drives_the_live_run_and_reset() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let spawn = runtime.player_position().expect("spawn position");
        let readout = runtime.lab_readout().expect("initial readout");
        // Movement speed, durable stats, and definitions all come from the
        // committed gameplay package — there is no editable document.
        assert_eq!(readout.move_speed_units_per_second, 3.5);
        assert_eq!(readout.max_health, 85.0);
        assert_eq!(readout.player_stats.max_stamina, 90.0);
        assert_eq!(readout.player_stats.attributes.strength, 50.0);
        assert!(!readout.gameplay_package.fingerprint.is_empty());
        assert!(readout
            .gameplay_package
            .payload
            .actors
            .iter()
            .any(|actor| actor.id == "rat" && actor.mobile_id.map(|id| id.0) == Some(0)));
        assert!(readout
            .gameplay_package
            .payload
            .actions
            .iter()
            .any(|action| action.id == "melee-attack"));

        runtime
            .set_player_position(Vec3::new(spawn.x + 5.0, spawn.y, spawn.z))
            .expect("move away from spawn");
        let reset = runtime.reset_play_session().expect("reset live run");
        assert_eq!(reset.player_position, [spawn.x, spawn.y, spawn.z]);
        assert_eq!(reset.move_speed_units_per_second, 3.5);
        assert_eq!(reset.current_health, 85.0);
        assert_eq!(reset.player_stats.current_stamina, 90.0);
    }

    #[test]
    fn browses_real_enemy_identity_and_jumps_using_live_runtime_state() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install committed live navigation");
        let spawn = runtime.player_position().expect("spawn position");
        let initial = runtime.lab_readout().expect("initial content readout");
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
        // 7056: the Thief is a combatant with an enemy-class actor — live
        // resources are the deterministic spawn roll of its 11-20 range.
        let thief_resources = thief.live.resources.expect("Thief live resources");
        assert!((11.0..=20.0).contains(&thief_resources.current_health));
        assert_eq!(thief_resources.current_stamina, 0.0);
        let live_thief = thief.live.position;
        let rat = initial
            .content
            .iter()
            .find(|entity| entity.reference.mobile_id == 0)
            .expect("committed Rat identity");
        let rat_resources = rat.live.resources.expect("Rat live resources");
        assert_eq!(rat.reference.mobile_name, "Rat");
        // 7045: live resources come from the gameplay package — Rat health is
        // the deterministic spawn roll of the classic 9-16 range.
        assert!((9.0..=16.0).contains(&rat_resources.current_health));
        assert_eq!(rat_resources.current_stamina, 0.0);

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

        let before_unknown = runtime.lab_readout().unwrap();
        let error = runtime
            .jump_to_content(999_999)
            .expect_err("unknown content must fail closed");
        assert!(matches!(error, RuntimeError::Content(_)));
        assert_eq!(runtime.lab_readout().unwrap(), before_unknown);

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
        runtime.jump_to_content(2007).expect("jump beside real Rat");

        // Player attacks resolve through the authored melee action with
        // deterministic player-stream rolls: swings 1-6 miss (40% chance),
        // swing 7 hits for 7, swings 8-9 miss, swing 10 hits for 14 and
        // kills the 14-health Rat.
        for _ in 0..10 {
            runtime.attack_focused_target().expect("physical swing");
            runtime
                .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
                .expect("resolve melee contact");
            for _ in 0..3 {
                runtime
                    .tick_play_session(0.25)
                    .expect("advance authoritative attack cooldown");
            }
        }
        let attacked = runtime.lab_readout().expect("fight readout");
        let records = &attacked.combat;
        assert_eq!(records.len(), 10);
        assert!(
            records[..6]
                .iter()
                .all(|record| !record.hit && record.damage == 0 && record.health_after == 14.0),
            "first six swings must miss without touching Rat health: {records:?}"
        );
        let seventh = &records[6];
        assert!(seventh.hit);
        assert_eq!(seventh.damage, 7);
        assert_eq!(seventh.health_before, 14.0);
        assert_eq!(seventh.health_after, 7.0);
        let tenth = &records[9];
        assert!(tenth.hit);
        // Rolled 14 against 7 remaining health; the plan clamps damage to
        // what can apply (health floors at zero).
        assert_eq!(tenth.damage, 7);
        assert_eq!(tenth.health_after, 0.0);
        assert!(tenth.died);
        assert!(records
            .iter()
            .all(|record| record.action == "melee-attack" && record.line_of_sight_clear));
        // Every accepted swing spent the authored cost: 90 - 10 * 5.
        assert_eq!(attacked.player_stats.current_stamina, 40.0);
        let rat = attacked
            .content
            .iter()
            .find(|entity| entity.id == 2007)
            .expect("attacked Rat readout");
        assert_eq!(rat.live.resources.unwrap().current_health, 0.0);

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
            14.0
        );
        assert_eq!(reset.player_stats.current_health, 85.0);
        assert_eq!(reset.player_stats.current_stamina, 90.0);
    }

    #[test]
    fn exhausted_player_attack_is_rejected_at_contact_without_mutation() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        runtime
            .jump_to_content(2000)
            .expect("jump beside Skeletal Warrior");

        // 18 swings at the authored cost of 5 drain stamina to zero (the 57
        // health Skeletal Warrior survives the drain); the 19th swing still
        // starts (classic) but the authored action rejects at contact with
        // InsufficientTrack and no damage or further spend.
        for _ in 0..18 {
            runtime.attack_focused_target().expect("physical swing");
            runtime
                .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
                .expect("resolve melee contact");
            for _ in 0..3 {
                runtime
                    .tick_play_session(0.25)
                    .expect("advance authoritative attack cooldown");
            }
        }
        assert_eq!(
            runtime.lab_readout().unwrap().player_stats.current_stamina,
            0.0
        );
        let before = runtime.lab_readout().unwrap();
        runtime.attack_focused_target().expect("zero-stamina swing");
        runtime
            .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
            .expect("resolve zero-stamina contact");
        let rejected = runtime.lab_readout().expect("rejected readout");
        let attempt = rejected.combat_attempts.last().expect("rejected attempt");
        assert_eq!(attempt.outcome, "rejected");
        assert_eq!(attempt.stamina_before, 0.0);
        assert_eq!(attempt.stamina_after, 0.0);
        let record = rejected.combat.last().expect("rejected combat record");
        assert!(!record.hit);
        assert_eq!(record.damage, 0);
        assert!(record.status.starts_with("rejected"));
        assert_eq!(
            rejected.player_stats.current_stamina,
            before.player_stats.current_stamina
        );
    }

    #[test]
    fn runtime_owns_tunable_rat_and_skeletal_warrior_encounters() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install committed navigation");

        // AI attacks resolve through authored actions: rat-bite lands 1-4
        // damage on a 10% check, so the first hit takes a while of ticks.
        runtime.jump_to_content(2007).expect("jump beside Rat");
        for _ in 0..400 {
            runtime.tick_play_session(0.1).expect("Rat encounter tick");
            if runtime.lab_readout().unwrap().player_stats.current_health < 85.0 {
                break;
            }
        }
        let rat = runtime.lab_readout().expect("Rat encounter readout");
        assert!(rat
            .encounter_decisions
            .iter()
            .any(|record| record.enemy_id == 2007 && record.to.as_deref() == Some("chase")));
        let rat_hit = rat
            .encounter_decisions
            .iter()
            .find(|record| {
                record.enemy_id == 2007
                    && record.decision == "melee attack"
                    && record.line_of_sight_clear == Some(true)
                    && record.damage.is_some_and(|damage| damage > 0.0)
            })
            .expect("Rat eventually lands rat-bite through authored resolution");
        let rat_damage = rat_hit.damage.unwrap();
        assert!(
            (1.0..=4.0).contains(&rat_damage),
            "rat-bite damage must be the authored 1-4 dice, got {rat_damage}"
        );
        assert_eq!(rat.player_stats.current_health, 85.0 - rat_damage);

        runtime
            .reset_play_session()
            .expect("reset before Skeletal Warrior");
        runtime
            .jump_to_content(2000)
            .expect("jump beside Skeletal Warrior");
        for _ in 0..400 {
            runtime
                .tick_play_session(0.1)
                .expect("Skeletal Warrior encounter tick");
            if runtime.lab_readout().unwrap().player_stats.current_health < 85.0 {
                break;
            }
        }
        let skeleton = runtime
            .lab_readout()
            .expect("Skeletal Warrior encounter readout");
        let skeleton_hit = skeleton
            .encounter_decisions
            .iter()
            .find(|record| {
                record.enemy_id == 2000
                    && record.decision == "melee attack"
                    && record.damage.is_some_and(|damage| damage > 0.0)
            })
            .expect("Skeletal Warrior lands skeleton-strike through authored resolution");
        let skeleton_damage = skeleton_hit.damage.unwrap();
        assert!(
            (5.0..=15.0).contains(&skeleton_damage),
            "skeleton-strike damage must be the authored 5-15 dice, got {skeleton_damage}"
        );
        assert_eq!(skeleton.player_stats.current_health, 85.0 - skeleton_damage);
    }

    #[test]
    fn named_combat_routes_own_victory_defeat_and_fast_retry() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install navigation");
        runtime
            .install_named_encounters_json(NAMED_ENCOUNTERS)
            .expect("install named encounters");
        let initial = runtime.lab_readout().expect("initial readout");
        assert_eq!(initial.named_encounters.len(), 2);
        assert!(initial.active_encounter.is_none());

        assert!(runtime
            .route_named_encounter("Digit1")
            .expect("route Rat room"));
        let active = runtime.lab_readout().unwrap().active_encounter.unwrap();
        assert_eq!(active.id, "rat-introduction");
        assert_eq!(active.status, "active");
        assert!(
            runtime.player_state().pitch_degrees < -5.0,
            "Rat route should aim down at the low classic sprite"
        );

        for _ in 0..80 {
            runtime
                .tick_play_session(0.1)
                .expect("named encounter waits for player attack");
        }
        assert_eq!(
            runtime
                .lab_readout()
                .expect("waiting readout")
                .player_stats
                .current_health,
            85.0
        );

        // Victory requires actually killing the Rat through authored
        // resolution: the deterministic player stream kills on swing 10.
        for _ in 0..10 {
            runtime.attack_focused_target().expect("Rat room swing");
            runtime
                .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
                .expect("resolve Rat room contact");
            for _ in 0..3 {
                runtime.tick_play_session(0.25).expect("advance cooldown");
            }
        }
        assert_eq!(
            runtime
                .lab_readout()
                .unwrap()
                .active_encounter
                .unwrap()
                .status,
            "victory"
        );
        let retried = runtime.reset_play_session().expect("retry Rat room");
        assert_eq!(retried.active_encounter.unwrap().status, "active");
        assert_eq!(retried.focused_content_id, Some(2007));

        // Defeat: the Skeletal Warrior's authored behavior drives the fight
        // (2s cooldown, 15% check, 5-15 damage) — defeat takes a couple of
        // game-minutes of ticks.
        assert!(runtime
            .route_named_encounter("Digit2")
            .expect("route Skeleton room"));
        runtime
            .attack_focused_target()
            .expect("engage Skeleton room");
        for _ in 0..2500 {
            runtime.tick_play_session(0.1).expect("Skeleton room tick");
            if runtime
                .lab_readout()
                .unwrap()
                .active_encounter
                .as_ref()
                .is_some_and(|encounter| encounter.status == "defeat")
            {
                break;
            }
        }
        let defeated = runtime.lab_readout().unwrap();
        assert_eq!(defeated.active_encounter.unwrap().status, "defeat");
        assert_eq!(defeated.player_stats.current_health, 0.0);
        assert!(defeated.encounter_decisions.iter().any(|record| {
            record.enemy_id == 2000
                && record.decision == "melee attack"
                && record
                    .damage
                    .is_some_and(|damage| (5.0..=15.0).contains(&damage))
        }));
        let attack_sequences = runtime
            .enemy_presentation()
            .into_iter()
            .map(|enemy| (enemy.handle, enemy.attack_sequence))
            .collect::<Vec<_>>();
        let history_len = defeated.encounter_decisions.len();
        for _ in 0..30 {
            runtime
                .tick_play_session(0.1)
                .expect("post-defeat enemies drop the dead player target");
        }
        assert_eq!(
            runtime
                .enemy_presentation()
                .into_iter()
                .map(|enemy| (enemy.handle, enemy.attack_sequence))
                .collect::<Vec<_>>(),
            attack_sequences,
            "enemy attack animation counters must stop after defeat"
        );
        assert!(
            runtime.lab_readout().unwrap().encounter_decisions.len() <= history_len + 1,
            "post-defeat ticks may record one state transition, not repeated attacks"
        );
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
