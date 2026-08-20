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
    LiveActorResources, LootContainerReadout, MeleePresentationPhase, MeleePresentationReadout,
    NamedEncounterReadout, ProgressionReadout, RuntimeError, LOOT_INTERACT_REACH, MELEE_ACTION_ID,
    MELEE_ANTICIPATION_SECONDS, MELEE_CONTACT_SECONDS, MELEE_RECOVERY_SECONDS,
    MELEE_REJECTION_SECONDS,
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
        // 43 enemies + 8 treasure containers (S0000999 archive-199/record-19
        // random-treasure markers).
        assert_eq!(initial.content.len(), 43 + 8);
        assert_eq!(
            initial
                .content
                .iter()
                .filter(|entity| entity.kind == "enemy")
                .count(),
            43
        );
        assert_eq!(
            initial
                .content
                .iter()
                .filter(|entity| entity.kind == "treasure")
                .count(),
            8
        );
        let thief = initial
            .content
            .iter()
            .find(|entity| entity.id == 2001)
            .expect("committed thief identity");
        assert_eq!(thief.name, "enemy-thief-1");
        let thief_reference = thief.reference.as_ref().expect("enemy reference");
        assert_eq!(thief_reference.mobile_id, 138);
        assert_eq!(thief_reference.mobile_name, "Thief");
        assert_eq!(thief_reference.texture_archive, 484);
        // 7056: the Thief is a combatant with an enemy-class actor — live
        // resources are the deterministic spawn roll of its 11-20 range.
        let thief_resources = thief.live.resources.expect("Thief live resources");
        assert!((11.0..=20.0).contains(&thief_resources.current_health));
        assert_eq!(thief_resources.current_stamina, 0.0);
        let live_thief = thief.live.position;
        let rat = initial
            .content
            .iter()
            .find(|entity| {
                entity
                    .reference
                    .as_ref()
                    .is_some_and(|reference| reference.mobile_id == 0)
            })
            .expect("committed Rat identity");
        let rat_resources = rat.live.resources.expect("Rat live resources");
        assert_eq!(
            rat.reference.as_ref().expect("enemy reference").mobile_name,
            "Rat"
        );
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
    fn equip_cycle_swaps_gear_and_logs_receipts() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        let initial = runtime.lab_readout().expect("initial readout");
        assert_eq!(
            initial
                .player_inventory
                .items
                .iter()
                .map(|item| (item.item.as_str(), item.equip_slot.as_deref()))
                .collect::<Vec<_>>(),
            [
                ("iron-longsword", Some("right-hand")),
                ("iron-dagger", None),
                ("iron-cuirass", None),
            ]
        );
        assert!(initial.equipment_log.is_empty());

        // First press: the next unequipped equippable (the dagger) swaps into
        // the occupied right hand.
        let readout = runtime.equip_cycle().expect("equip cycle");
        let record = readout.equipment_log.last().expect("log entry");
        assert_eq!(record.operation, "swap");
        assert_eq!(record.item, "iron-dagger");
        assert_eq!(record.slots, ["right-hand".to_string()]);
        assert_eq!(record.replaced_item.as_deref(), Some("iron-longsword"));
        assert!(readout
            .player_inventory
            .items
            .iter()
            .any(|item| item.item == "iron-dagger"
                && item.equip_slot.as_deref() == Some("right-hand")));

        // Second press: the cuirass equips into the free chest-armor slot.
        let readout = runtime.equip_cycle().expect("equip cycle");
        let record = readout.equipment_log.last().expect("log entry");
        assert_eq!(record.operation, "equip");
        assert_eq!(record.item, "iron-cuirass");
        assert_eq!(record.slots, ["chest-armor".to_string()]);

        // Third press: only the longsword is unequipped; it swaps back into
        // the right hand, replacing the dagger.
        let readout = runtime.equip_cycle().expect("equip cycle");
        let record = readout.equipment_log.last().expect("log entry");
        assert_eq!(record.operation, "swap");
        assert_eq!(record.item, "iron-longsword");
        assert_eq!(record.replaced_item.as_deref(), Some("iron-dagger"));
        assert_eq!(readout.equipment_log.len(), 3);

        // Revisions advance monotonically with each receipt.
        let revisions = readout
            .equipment_log
            .iter()
            .map(|record| {
                record
                    .committed_revision
                    .expect("accepted mutation has a revision")
            })
            .collect::<Vec<_>>();
        assert!(revisions.windows(2).all(|pair| pair[0] < pair[1]));
        assert!(readout.equipment_log.iter().all(|record| record.accepted));
    }

    #[test]
    fn equipment_verbs_log_rejections_with_reasons() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");

        // Not a carried equippable item: rejection is logged, not thrown.
        let readout = runtime.equip_item(99_999).expect("rejection readout");
        let record = readout.equipment_log.last().expect("log entry");
        assert!(!record.accepted);
        assert_eq!(record.operation, "equip");
        assert!(record
            .reason
            .as_deref()
            .expect("rejection reason")
            .contains("not a carried equippable item"));
        assert!(record.committed_revision.is_none());

        // Unequipping an empty slot: rejection logged.
        let readout = runtime
            .unequip_slot("left-hand")
            .expect("rejection readout");
        let record = readout.equipment_log.last().expect("log entry");
        assert!(!record.accepted);
        assert_eq!(record.operation, "unequip");
        assert_eq!(record.slots, ["left-hand".to_string()]);
        assert_eq!(record.reason.as_deref(), Some("slot is empty"));

        // Equip the carried dagger through the explicit verb (swaps the
        // longsword out of the right hand), then unequip the slot.
        let dagger = readout
            .player_inventory
            .items
            .iter()
            .find(|item| item.item == "iron-dagger")
            .expect("carried dagger")
            .entity;
        let readout = runtime.equip_item(dagger).expect("equip readout");
        let record = readout.equipment_log.last().expect("log entry");
        assert!(record.accepted);
        assert_eq!(record.operation, "swap");
        assert_eq!(record.item, "iron-dagger");
        let readout = runtime.unequip_slot("right-hand").expect("unequip readout");
        let record = readout.equipment_log.last().expect("log entry");
        assert!(record.accepted);
        assert_eq!(record.operation, "unequip");
        assert_eq!(record.item, "iron-dagger");
        assert!(readout
            .player_inventory
            .items
            .iter()
            .all(|item| item.equip_slot.is_none()));
    }

    #[test]
    fn grant_item_logs_grants_and_capacity_rejections() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");

        // A small arrow grant succeeds and is logged.
        let readout = runtime.grant_item("arrow", 10).expect("grant readout");
        let record = readout.equipment_log.last().expect("log entry");
        assert!(record.accepted);
        assert_eq!(record.operation, "grant");
        assert_eq!(record.item, "arrow");
        assert_eq!(record.quantity, Some(10));
        assert!(readout
            .player_inventory
            .stacks
            .iter()
            .any(|stack| stack.item == "arrow" && stack.quantity == 10));

        // The player's weight limit is 300 quarter-kg with 70 used; 400
        // arrows (1 unit each) blow past it and the upstream rejection is
        // logged with the reason.
        let readout = runtime
            .grant_item("arrow", 400)
            .expect("capacity rejection readout");
        let record = readout.equipment_log.last().expect("log entry");
        assert!(!record.accepted);
        assert_eq!(record.operation, "grant");
        assert!(record
            .reason
            .as_deref()
            .expect("rejection reason")
            .contains("InventoryCapacityExceeded"));
        assert!(readout
            .player_inventory
            .stacks
            .iter()
            .any(|stack| stack.item == "arrow" && stack.quantity == 10));

        // Unique (equippable) items and unknown items reject too.
        let readout = runtime
            .grant_item("iron-dagger", 1)
            .expect("non-fungible rejection readout");
        assert!(!readout.equipment_log.last().expect("log entry").accepted);
        let readout = runtime
            .grant_item("mithril-ladle", 1)
            .expect("unknown item rejection readout");
        let record = readout.equipment_log.last().expect("log entry");
        assert!(!record.accepted);
        assert_eq!(record.reason.as_deref(), Some("unknown item"));
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

    /// Swing until the focused enemy dies (deterministic player stream), then
    /// stop. Used by the corpse-loot tests.
    fn fight_until_dead(runtime: &mut DaggerRuntime, cap: usize) {
        for _ in 0..cap {
            runtime.attack_focused_target().expect("physical swing");
            runtime
                .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
                .expect("resolve melee contact");
            for _ in 0..3 {
                runtime.tick_play_session(0.25).expect("advance cooldown");
            }
            if runtime
                .lab_readout()
                .expect("fight readout")
                .combat
                .last()
                .is_some_and(|record| record.died)
            {
                return;
            }
        }
        panic!("target did not die within {cap} swings");
    }

    #[test]
    fn killing_a_monster_awards_kill_xp_into_the_lab_readout() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        let initial = runtime.lab_readout().expect("initial readout");
        assert_eq!(initial.progression.xp, 0);
        assert_eq!(initial.progression.level, 1);
        // 500 xp per level (the authored xp-level divisor): 500 to go.
        assert_eq!(initial.progression.xp_to_next_level, 500);
        assert_eq!(initial.progression.max_health, 85.0);
        assert!(initial.progression.history.is_empty());

        runtime.jump_to_content(2007).expect("jump beside Rat");
        fight_until_dead(&mut runtime, 40);
        let readout = runtime.lab_readout().expect("post-kill readout");
        assert_eq!(readout.progression.history.len(), 1);
        let award = &readout.progression.history[0];
        assert_eq!(award.victim, "rat");
        assert_eq!(
            (award.xp_awarded, award.xp_before, award.xp_after),
            (50, 0, 50)
        );
        assert_eq!((award.level_before, award.level_after), (1, 1));
        assert!(award.level_ups.is_empty());
        assert_eq!(readout.progression.xp, 50);
        assert_eq!(readout.progression.xp_to_next_level, 450);
        assert_eq!(readout.progression.level, 1);
        assert_eq!(readout.progression.current_health, 85.0);

        // The lab jump verb only heals: progression survives it within a
        // session. The explicit session reset restores spawn progression:
        // xp 0, level 1, spawn health maximum, empty history.
        let reset = runtime.reset_play_session().expect("reset");
        assert_eq!((reset.progression.xp, reset.progression.level), (0, 1));
        assert_eq!(reset.progression.xp_to_next_level, 500);
        assert_eq!(reset.progression.max_health, 85.0);
        assert_eq!(reset.progression.current_health, 85.0);
        assert!(reset.progression.history.is_empty());
    }

    #[test]
    fn a_real_kill_sequence_crosses_a_level_through_the_product_combat_path() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        // A named sequence of real hold enemies through the physical swing
        // path: the Orc (2003, xpReward 250), then two Giant Bats (2002 and
        // 2005, 150 each) — the cumulative 550 crosses the authored 500
        // threshold on the third kill. (The jump verb restores stamina
        // between fights; every kill lands inside its 90-stamina budget, so
        // the whole sequence resolves through real melee combat.)
        runtime.jump_to_content(2003).expect("jump beside Orc");
        fight_until_dead(&mut runtime, 80);
        let readout = runtime.lab_readout().expect("post-orc readout");
        assert_eq!(readout.progression.history.len(), 1);
        let orc_award = &readout.progression.history[0];
        assert_eq!(orc_award.victim, "orc");
        assert_eq!(
            (
                orc_award.xp_awarded,
                orc_award.xp_before,
                orc_award.xp_after
            ),
            (250, 0, 250)
        );
        assert_eq!((orc_award.level_before, orc_award.level_after), (1, 1));
        assert!(orc_award.level_ups.is_empty());
        assert_eq!(readout.progression.xp, 250);
        assert_eq!(readout.progression.level, 1);
        assert_eq!(readout.progression.xp_to_next_level, 250);
        assert_eq!(readout.progression.max_health, 85.0);

        runtime
            .jump_to_content(2002)
            .expect("jump beside Giant Bat");
        fight_until_dead(&mut runtime, 80);
        let readout = runtime.lab_readout().expect("post-first-bat readout");
        assert_eq!(readout.progression.history.len(), 2);
        let bat_award = &readout.progression.history[1];
        assert_eq!(bat_award.victim, "giant-bat");
        assert_eq!(
            (
                bat_award.xp_awarded,
                bat_award.xp_before,
                bat_award.xp_after
            ),
            (150, 250, 400)
        );
        assert!(bat_award.level_ups.is_empty());
        assert_eq!(readout.progression.xp, 400);
        assert_eq!(readout.progression.level, 1);
        assert_eq!(readout.progression.xp_to_next_level, 100);

        // The third kill crosses the threshold through the runtime's kill
        // hook and its salt-5 deterministic hp-roll stream.
        runtime
            .jump_to_content(2005)
            .expect("jump beside Giant Bat");
        fight_until_dead(&mut runtime, 80);
        let readout = runtime.lab_readout().expect("post-second-bat readout");
        assert_eq!(readout.progression.history.len(), 3);
        let award = &readout.progression.history[2];
        assert_eq!(award.victim, "giant-bat");
        assert_eq!(
            (award.xp_awarded, award.xp_before, award.xp_after),
            (150, 400, 550)
        );
        assert_eq!((award.level_before, award.level_after), (1, 2));
        assert_eq!(award.level_ups.len(), 1);
        let level_up = &award.level_ups[0];
        assert_eq!(level_up.level, 2);
        assert_eq!(level_up.roll_evidence, "player.level-up.2.hp-roll");
        assert!((4..=8).contains(&level_up.roll));
        // Endurance 40 gives modifier -1, so hp = roll - 1 (>= 3); applied
        // to health-max AND current health.
        assert_eq!(level_up.hit_points, level_up.roll - 1);
        assert_eq!(level_up.health_max_before, 85);
        assert_eq!(level_up.health_max_after, 85 + level_up.hit_points);
        assert_eq!(readout.progression.xp, 550);
        assert_eq!(readout.progression.level, 2);
        assert_eq!(readout.progression.xp_to_next_level, 450);
        assert_eq!(
            readout.progression.max_health,
            85.0 + level_up.hit_points as f32
        );
        assert_eq!(
            readout.progression.current_health,
            readout.progression.max_health
        );
    }

    /// The committed package with the `xp-level` pacing divisor rewritten —
    /// the same mutation an author would make in `derived.ts`, applied to
    /// the package bytes the runtime admits.
    fn package_with_xp_level_divisor(divisor: i64) -> Vec<u8> {
        let mut package: serde_json::Value = serde_json::from_slice(include_bytes!(
            "../../../data/gameplay/dagger-core.package.json"
        ))
        .expect("parse committed package");
        let rule = package["payload"]["derived"]
            .as_array_mut()
            .expect("derived rules")
            .iter_mut()
            .find(|rule| rule["id"] == "xp-level")
            .expect("xp-level rule");
        rule["expr"]["right"]["value"] = serde_json::Value::from(divisor);
        serde_json::to_vec(&package).expect("encode mutated package")
    }

    #[test]
    fn authored_xp_curve_drives_pacing_through_the_same_real_kill() {
        let kill_one_rat = |package: &[u8]| {
            let admitted = AdmittedProject::from_json(PROJECT).expect("project admission");
            let mut runtime =
                DaggerRuntime::from_admitted_project_with_gameplay_package(admitted, package)
                    .expect("runtime with package");
            runtime.jump_to_content(2007).expect("jump beside Rat");
            fight_until_dead(&mut runtime, 40);
            runtime.lab_readout().expect("post-kill readout")
        };

        // Committed pacing (500 xp/level): the rat's 50 xp does not level.
        let committed = kill_one_rat(include_bytes!(
            "../../../data/gameplay/dagger-core.package.json"
        ));
        assert_eq!(committed.progression.xp, 50);
        assert_eq!(committed.progression.level, 1);
        assert!(committed.progression.history[0].level_ups.is_empty());
        assert_eq!(committed.progression.max_health, 85.0);

        // The SAME kill under an authored curve edit (divisor 500 -> 50):
        // the rat's 50 xp crosses one threshold immediately — catalog
        // authoring drives pacing through the product path.
        let faster = kill_one_rat(&package_with_xp_level_divisor(50));
        assert_eq!(faster.progression.xp, 50);
        assert_eq!(faster.progression.level, 2);
        let award = &faster.progression.history[0];
        assert_eq!((award.level_before, award.level_after), (1, 2));
        assert_eq!(award.level_ups.len(), 1);
        let level_up = &award.level_ups[0];
        assert_eq!(level_up.roll_evidence, "player.level-up.2.hp-roll");
        assert!((4..=8).contains(&level_up.roll));
        assert_eq!(level_up.hit_points, level_up.roll - 1);
        assert_eq!(
            faster.progression.max_health,
            85.0 + level_up.hit_points as f32
        );
        assert_eq!(
            faster.progression.current_health,
            faster.progression.max_health
        );
        // The faster curve also shortens the remaining pace: 50 - 50 % 50.
        assert_eq!(faster.progression.xp_to_next_level, 50);
    }

    #[test]
    fn loot_containers_spawn_with_the_dungeon_key_and_per_enemy_tables() {
        let runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project admission");
        let readout = runtime.lab_readout().expect("initial readout");
        // Eight treasure piles from the S0000999 random-treasure markers, all
        // on the dungeon's loot key (MAPS.BSA type 2 Human Stronghold -> "N").
        let treasure = readout
            .loot_containers
            .iter()
            .filter(|container| container.kind == "treasure")
            .collect::<Vec<_>>();
        assert_eq!(treasure.len(), 8);
        assert!(treasure.iter().all(|container| container.loot_key == "N"));
        // The N table's gold range is 1-80 at level 1, so every pile holds at
        // least gold; contents match the generation receipt.
        for container in &treasure {
            assert!(!container.emptied, "{} should hold loot", container.id);
            assert_eq!(container.generation.key, "N");
            let stack_total: u64 = container
                .contents
                .stacks
                .iter()
                .map(|stack| stack.quantity)
                .sum();
            let receipt_total: u64 = container
                .generation
                .items
                .iter()
                .map(|(_, quantity)| quantity)
                .sum();
            assert_eq!(
                stack_total + container.contents.items.len() as u64,
                receipt_total
            );
        }
        // Loot-keyed enemies spawn with contents in their own inventory (the
        // donor corpse-loot model): imps (D), orc (A), skeletal warriors (H),
        // thieves (T).
        let corpse_keys = readout
            .loot_containers
            .iter()
            .filter(|container| container.kind == "corpse")
            .map(|container| (container.id.as_str(), container.loot_key.as_str()))
            .collect::<Vec<_>>();
        for expected in [
            ("enemy-2009", "D"),
            ("enemy-2015", "D"),
            ("enemy-2018", "D"),
            ("enemy-2003", "A"),
            ("enemy-2000", "H"),
            ("enemy-2012", "H"),
            ("enemy-2020", "H"),
            ("enemy-2001", "T"),
            ("enemy-2021", "T"),
            ("enemy-2023", "T"),
        ] {
            assert!(
                corpse_keys.contains(&expected),
                "missing corpse container {expected:?} in {corpse_keys:?}"
            );
        }
        // Rats and giant bats have no loot table, like classic.
        assert!(readout
            .loot_containers
            .iter()
            .all(|container| container.id != "enemy-2007"));
        // Unsupported-category coverage rides the receipt: every N pile rolls
        // the pool-less groups (clothing, books, religious, ...) and records
        // them with `supported: false`.
        let pile = readout
            .loot_containers
            .iter()
            .find(|container| container.id == "treasure-3000")
            .expect("treasure container");
        assert!(pile
            .generation
            .categories
            .iter()
            .any(|category| !category.supported));
    }

    #[test]
    fn loot_generation_is_deterministic_across_sessions() {
        let first = DaggerRuntime::from_project_json(PROJECT)
            .expect("first session")
            .lab_readout()
            .expect("first readout");
        let second = DaggerRuntime::from_project_json(PROJECT)
            .expect("second session")
            .lab_readout()
            .expect("second readout");
        assert_eq!(
            first.loot_containers, second.loot_containers,
            "identical sessions must generate identical loot"
        );
    }

    #[test]
    fn interact_loot_take_all_empties_a_treasure_container() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        runtime
            .jump_to_content(3000)
            .expect("jump beside treasure pile");
        let before = runtime.lab_readout().expect("pre-loot readout");
        let container = before
            .loot_containers
            .iter()
            .find(|container| container.id == "treasure-3000")
            .expect("treasure container");
        let expected_stacks = container.contents.stacks.clone();
        let expected_items = container.contents.items.len();
        assert!(!expected_stacks.is_empty() || expected_items > 0);
        let player_gold_before = before
            .player_inventory
            .stacks
            .iter()
            .find(|stack| stack.item == "gold-piece")
            .map_or(0, |stack| stack.quantity);

        let readout = runtime.interact_loot().expect("loot the pile");
        let loot_records = readout
            .equipment_log
            .iter()
            .filter(|record| record.operation == "loot:treasure-3000")
            .collect::<Vec<_>>();
        assert!(!loot_records.is_empty());
        assert!(
            loot_records.iter().all(|record| record.accepted),
            "fresh player must carry the whole pile: {loot_records:?}"
        );
        let after = readout
            .loot_containers
            .iter()
            .find(|container| container.id == "treasure-3000")
            .expect("treasure container");
        assert!(after.emptied, "take-all empties the container");
        for stack in &expected_stacks {
            let player_stack = readout
                .player_inventory
                .stacks
                .iter()
                .find(|candidate| candidate.item == stack.item)
                .unwrap_or_else(|| panic!("player holds {}", stack.item));
            let baseline = if stack.item == "gold-piece" {
                player_gold_before
            } else {
                0
            };
            assert_eq!(player_stack.quantity, baseline + stack.quantity);
        }
        assert_eq!(
            readout
                .player_inventory
                .items
                .iter()
                .filter(|item| item.equip_slot.is_none())
                .count(),
            2 + expected_items,
            "carried unequipped items grow by the looted uniques"
        );
        // A second press on the emptied pile logs the donor's note.
        let readout = runtime.interact_loot().expect("loot again");
        let note = readout.equipment_log.last().expect("note entry");
        assert!(!note.accepted);
        assert_eq!(note.operation, "loot:treasure-3000");
        assert!(note
            .reason
            .as_deref()
            .expect("note reason")
            .contains("nothing to take"));
    }

    #[test]
    fn interact_loot_on_a_dead_rat_notes_no_treasure_and_melee_still_excludes_the_dead() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        runtime.jump_to_content(2007).expect("jump beside Rat");
        fight_until_dead(&mut runtime, 40);
        let dead = runtime.lab_readout().expect("dead rat readout");
        let rat = dead
            .content
            .iter()
            .find(|entity| entity.id == 2007)
            .expect("rat");
        assert_eq!(
            rat.live.resources.expect("rat resources").current_health,
            0.0
        );
        let combat_count = dead.combat.len();

        // The melee aimed query still excludes the dead: the next swing
        // resolves as a miss with no new combat record.
        runtime.attack_focused_target().expect("swing at corpse");
        runtime
            .tick_play_session(super::MELEE_ANTICIPATION_SECONDS)
            .expect("resolve swing");
        let after_swing = runtime.lab_readout().expect("post-swing readout");
        assert_eq!(after_swing.combat.len(), combat_count);
        assert_eq!(
            after_swing.combat_attempts.last().expect("attempt").outcome,
            "miss"
        );

        // But the aimed interact query sees the corpse: a rat has no loot
        // table (classic), so the press logs the nothing-to-take note.
        let readout = runtime.interact_loot().expect("loot the corpse");
        let note = readout.equipment_log.last().expect("note entry");
        assert!(!note.accepted);
        assert_eq!(note.operation, "loot:enemy-2007");
        assert!(note
            .reason
            .as_deref()
            .expect("note reason")
            .contains("nothing to take"));
    }

    #[test]
    fn interact_loot_transfers_a_dead_orcs_contents() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        runtime.jump_to_content(2003).expect("jump beside Orc");
        fight_until_dead(&mut runtime, 80);
        let dead = runtime.lab_readout().expect("dead orc readout");
        let corpse = dead
            .loot_containers
            .iter()
            .find(|container| container.id == "enemy-2003")
            .expect("orc corpse container");
        // A: gold 1-10; this orc's deterministic stream rolls no item
        // category successes.
        let expected_gold = corpse
            .contents
            .stacks
            .iter()
            .find(|stack| stack.item == "gold-piece")
            .expect("orc gold")
            .quantity;
        assert!(expected_gold > 0);
        let player_gold_before = dead
            .player_inventory
            .stacks
            .iter()
            .find(|stack| stack.item == "gold-piece")
            .map_or(0, |stack| stack.quantity);

        let readout = runtime.interact_loot().expect("loot the orc");
        let loot_records = readout
            .equipment_log
            .iter()
            .filter(|record| record.operation == "loot:enemy-2003")
            .collect::<Vec<_>>();
        assert_eq!(loot_records.len(), 1);
        assert!(loot_records.iter().all(|record| record.accepted));
        assert_eq!(loot_records[0].quantity, Some(expected_gold));
        assert!(
            readout
                .loot_containers
                .iter()
                .find(|container| container.id == "enemy-2003")
                .expect("corpse")
                .emptied
        );
        assert!(readout
            .player_inventory
            .stacks
            .iter()
            .any(|stack| stack.item == "gold-piece"
                && stack.quantity == player_gold_before + expected_gold));
    }

    #[test]
    fn loot_take_all_stops_at_the_first_capacity_rejection() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real project");
        runtime
            .jump_to_content(3002)
            .expect("jump beside the pile with a bow and helm");
        // Fill the player's weight budget (300 quarter-kg, 70 used by the
        // loadout) with arrows so the pile's unique items cannot fit.
        let readout = runtime.grant_item("arrow", 230).expect("fill capacity");
        assert!(readout.equipment_log.last().expect("grant").accepted);
        let pile = readout
            .loot_containers
            .iter()
            .find(|container| container.id == "treasure-3002")
            .expect("treasure container");
        assert_eq!(pile.contents.items.len(), 2, "bow + helm generated");

        let readout = runtime.interact_loot().expect("loot the pile");
        let loot_records = readout
            .equipment_log
            .iter()
            .filter(|record| record.operation == "loot:treasure-3002")
            .collect::<Vec<_>>();
        // Gold is authored weightless (1/400 kg < unit resolution), so the
        // stack transfers; the first unique item then exceeds capacity and
        // the take-all stops there — the rest stays in the pile.
        let rejected = loot_records
            .iter()
            .find(|record| !record.accepted)
            .expect("capacity rejection");
        assert!(rejected
            .reason
            .as_deref()
            .expect("rejection reason")
            .contains("InventoryCapacityExceeded"));
        assert!(
            !loot_records.last().expect("last record").accepted,
            "the rejection is where the transfer stopped"
        );
        let pile = readout
            .loot_containers
            .iter()
            .find(|container| container.id == "treasure-3002")
            .expect("pile");
        assert!(!pile.emptied, "rejected items remain in the pile");
        assert!(pile.contents.stacks.is_empty(), "gold was taken");
        assert_eq!(pile.contents.items.len(), 2);
    }
}
