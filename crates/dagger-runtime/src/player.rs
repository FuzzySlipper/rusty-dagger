use std::collections::BTreeSet;

use core_ids::EntityId;
use core_math::Vec3;
use engine_spatial::{
    KinematicMotionSystem, MotionAxis, MotionFact, MotionPhaseError, MotionPhaseReceipt,
    VoxelCollisionScene, MAX_MOTION_DELTA_SECONDS,
};
use entity_state::{EntityCommand, EntityCommandBatch, EntityState, EntityView};
use serde::{Deserialize, Serialize};

pub const MAX_PLAYER_SPEED_UNITS_PER_SECOND: f32 = 1_000.0;
pub const MAX_PLAYER_LOOK_DEGREES_PER_UNIT: f32 = 180.0;
pub const MAX_PLAYER_STEP_UP_UNITS: f32 = 4.0;
pub const MAX_INPUT_CONTROL_LENGTH: usize = 64;
pub const FALL_SUBSTEP_UNITS: f32 = 0.1;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlayerInputBindings {
    pub move_forward: String,
    pub move_backward: String,
    pub move_left: String,
    pub move_right: String,
    pub mouse_look: String,
    pub primary_fire: String,
    pub select_weapon: Vec<String>,
}

impl PlayerInputBindings {
    pub fn new(
        move_forward: impl Into<String>,
        move_backward: impl Into<String>,
        move_left: impl Into<String>,
        move_right: impl Into<String>,
        mouse_look: impl Into<String>,
        primary_fire: impl Into<String>,
        select_weapon: impl IntoIterator<Item = String>,
    ) -> Self {
        Self {
            move_forward: move_forward.into(),
            move_backward: move_backward.into(),
            move_left: move_left.into(),
            move_right: move_right.into(),
            mouse_look: mouse_look.into(),
            primary_fire: primary_fire.into(),
            select_weapon: select_weapon.into_iter().collect(),
        }
    }

    pub(crate) fn is_valid(&self) -> bool {
        let controls = [
            self.move_forward.as_str(),
            self.move_backward.as_str(),
            self.move_left.as_str(),
            self.move_right.as_str(),
            self.mouse_look.as_str(),
            self.primary_fire.as_str(),
        ]
        .into_iter()
        .chain(self.select_weapon.iter().map(String::as_str))
        .collect::<Vec<_>>();
        controls
            .iter()
            .all(|control| !control.is_empty() && control.len() <= MAX_INPUT_CONTROL_LENGTH)
            && controls
                .iter()
                .enumerate()
                .all(|(index, control)| !controls[..index].contains(control))
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct PlayerControllerConfig {
    pub move_speed_units_per_second: f32,
    pub move_step_seconds: f32,
    pub look_degrees_per_unit: f32,
    pub initial_yaw_degrees: f32,
    pub initial_pitch_degrees: f32,
    pub fall_speed_units_per_second: Option<f32>,
    pub step_up_units: Option<f32>,
    pub bindings: PlayerInputBindings,
}

impl PlayerControllerConfig {
    pub(crate) fn is_valid(&self) -> bool {
        self.move_speed_units_per_second.is_finite()
            && self.move_speed_units_per_second > 0.0
            && self.move_speed_units_per_second <= MAX_PLAYER_SPEED_UNITS_PER_SECOND
            && self.move_step_seconds.is_finite()
            && self.move_step_seconds > 0.0
            && self.move_step_seconds <= MAX_MOTION_DELTA_SECONDS
            && self.look_degrees_per_unit.is_finite()
            && self.look_degrees_per_unit > 0.0
            && self.look_degrees_per_unit <= MAX_PLAYER_LOOK_DEGREES_PER_UNIT
            && self.initial_yaw_degrees.is_finite()
            && self.initial_pitch_degrees.is_finite()
            && (-89.0..=89.0).contains(&self.initial_pitch_degrees)
            && self.fall_speed_units_per_second.is_none_or(|speed| {
                speed.is_finite() && speed > 0.0 && speed <= MAX_PLAYER_SPEED_UNITS_PER_SECOND
            })
            && self.step_up_units.is_none_or(|step| {
                step.is_finite() && step > 0.0 && step <= MAX_PLAYER_STEP_UP_UNITS
            })
            && self.bindings.is_valid()
    }
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PlayerControllerState {
    pub yaw_degrees: f32,
    pub pitch_degrees: f32,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase",
    deny_unknown_fields
)]
pub enum ResolvedPlayerAction {
    Move { forward: f32, right: f32 },
    Look { yaw_delta: f32, pitch_delta: f32 },
}

#[derive(Debug, Clone, PartialEq)]
pub enum PlayerControlFact {
    Moved {
        entity: EntityId,
        before: Vec3,
        after: Vec3,
    },
    Blocked {
        entity: EntityId,
        attempted_velocity: Vec3,
    },
    LookChanged {
        entity: EntityId,
        before_yaw_degrees: f32,
        after_yaw_degrees: f32,
        before_pitch_degrees: f32,
        after_pitch_degrees: f32,
    },
}

#[derive(Debug, Clone, PartialEq)]
pub struct PlayerControlReceipt {
    pub action: ResolvedPlayerAction,
    pub facts: Vec<PlayerControlFact>,
    pub motion: Option<MotionPhaseReceipt>,
}

pub(crate) fn apply_player_action(
    entities: &mut EntityState,
    scene: &VoxelCollisionScene,
    player: EntityId,
    component: &mut PlayerControllerState,
    config: &PlayerControllerConfig,
    action: ResolvedPlayerAction,
    move_delta_seconds: f32,
) -> Result<PlayerControlReceipt, PlayerError> {
    if !player_action_is_valid(action) {
        return Err(PlayerError::InvalidAction(action));
    }
    let entity_view = entities
        .view(player)
        .map_err(|_| PlayerError::UnknownPlayer { player })?;
    if entity_view.kinematic.is_none() || entity_view.transform.is_none() {
        return Err(PlayerError::MissingKinematicBody { player });
    }

    match action {
        ResolvedPlayerAction::Look {
            yaw_delta,
            pitch_delta,
        } => {
            let before = *component;
            component.yaw_degrees =
                normalize_yaw(before.yaw_degrees + yaw_delta * config.look_degrees_per_unit);
            component.pitch_degrees = (before.pitch_degrees
                + pitch_delta * config.look_degrees_per_unit)
                .clamp(-89.0, 89.0);
            Ok(PlayerControlReceipt {
                action,
                facts: vec![PlayerControlFact::LookChanged {
                    entity: player,
                    before_yaw_degrees: before.yaw_degrees,
                    after_yaw_degrees: component.yaw_degrees,
                    before_pitch_degrees: before.pitch_degrees,
                    after_pitch_degrees: component.pitch_degrees,
                }],
                motion: None,
            })
        }
        ResolvedPlayerAction::Move { forward, right } => {
            let input_length = (forward * forward + right * right).sqrt();
            if config.fall_speed_units_per_second.is_none()
                && config.step_up_units.is_none()
                && input_length == 0.0
            {
                return Ok(PlayerControlReceipt {
                    action,
                    facts: Vec::new(),
                    motion: None,
                });
            }

            let start = player_translation(entities, player)?;
            let mut blocked_velocity = None;
            let mut last_motion = None;
            if input_length > 0.0 {
                let velocity =
                    move_velocity(config, component.yaw_degrees, forward, right, input_length);
                let mut horizontal =
                    run_player_motion(entities, scene, player, velocity, move_delta_seconds)?;
                if motion_blocked(&horizontal, player) {
                    if let Some(step) = config.step_up_units {
                        let horizontal_after = player_translation(entities, player)?;
                        // The first sweep may already have slid along an open
                        // axis. Retry the complete request from the action's
                        // original position so a successful step cannot apply
                        // that displacement twice. If the rise is not usable,
                        // restore the initial horizontal slide below.
                        entities
                            .apply_batch(EntityCommandBatch::new([EntityCommand::SetTranslation {
                                entity: player,
                                translation: start,
                            }]))
                            .map_err(PlayerError::EntityBatch)?;
                        let rise = run_player_motion(
                            entities,
                            scene,
                            player,
                            Vec3::new(0.0, step / move_delta_seconds, 0.0),
                            move_delta_seconds,
                        )?;
                        if motion_moved(&rise, player) {
                            let retry = run_player_motion(
                                entities,
                                scene,
                                player,
                                velocity,
                                move_delta_seconds,
                            )?;
                            // A retry that made horizontal progress can still
                            // report a blocked secondary axis while sliding
                            // around the obstacle. It is only a successful step
                            // when every axis blocked before the rise is clear.
                            let original_blocked = [
                                motion_blocked_on_axis(&horizontal, player, MotionAxis::X),
                                motion_blocked_on_axis(&horizontal, player, MotionAxis::Y),
                                motion_blocked_on_axis(&horizontal, player, MotionAxis::Z),
                            ];
                            let retry_still_blocked = [
                                motion_blocked_on_axis(&retry, player, MotionAxis::X),
                                motion_blocked_on_axis(&retry, player, MotionAxis::Y),
                                motion_blocked_on_axis(&retry, player, MotionAxis::Z),
                            ];
                            let step_succeeded = original_blocked
                                .iter()
                                .zip(retry_still_blocked)
                                .all(|(original, retry)| !(*original && retry));
                            if !step_succeeded {
                                let after_retry = player_translation(entities, player)?;
                                entities
                                    .apply_batch(EntityCommandBatch::new([
                                        EntityCommand::SetTranslation {
                                            entity: player,
                                            translation: Vec3::new(
                                                after_retry.x,
                                                start.y,
                                                after_retry.z,
                                            ),
                                        },
                                    ]))
                                    .map_err(PlayerError::EntityBatch)?;
                            }
                            horizontal = retry;
                        } else {
                            // No usable rise: the initial sweep's partial
                            // horizontal progress remains authoritative.
                            entities
                                .apply_batch(EntityCommandBatch::new([
                                    EntityCommand::SetTranslation {
                                        entity: player,
                                        translation: horizontal_after,
                                    },
                                ]))
                                .map_err(PlayerError::EntityBatch)?;
                        }
                    }
                }
                if motion_blocked(&horizontal, player) {
                    blocked_velocity = Some(velocity);
                }
                last_motion = Some(horizontal);
            }

            if let Some(speed) = config.fall_speed_units_per_second {
                let fall_total = speed * move_delta_seconds;
                let substeps = ((fall_total / FALL_SUBSTEP_UNITS).ceil() as u32).clamp(1, 64);
                let sub_velocity = Vec3::new(
                    0.0,
                    -(fall_total / substeps as f32) / move_delta_seconds,
                    0.0,
                );
                for _ in 0..substeps {
                    let motion = run_player_motion(
                        entities,
                        scene,
                        player,
                        sub_velocity,
                        move_delta_seconds,
                    )?;
                    if motion.facts.iter().any(|fact| {
                        matches!(fact, MotionFact::Blocked { entity, axis: MotionAxis::Y, .. } if *entity == player)
                    }) {
                        break;
                    }
                }
            }

            let end = player_translation(entities, player)?;
            let mut facts = Vec::new();
            if end != start {
                facts.push(PlayerControlFact::Moved {
                    entity: player,
                    before: start,
                    after: end,
                });
            }
            if let Some(velocity) = blocked_velocity {
                facts.push(PlayerControlFact::Blocked {
                    entity: player,
                    attempted_velocity: velocity,
                });
            }
            Ok(PlayerControlReceipt {
                action,
                facts,
                motion: last_motion,
            })
        }
    }
}

#[derive(Debug)]
pub enum PlayerError {
    InvalidAction(ResolvedPlayerAction),
    UnknownPlayer { player: EntityId },
    MissingKinematicBody { player: EntityId },
    EntityBatch(entity_state::BatchRejection),
    Motion(MotionPhaseError),
}

impl std::fmt::Display for PlayerError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::InvalidAction(action) => write!(formatter, "invalid player action: {action:?}"),
            Self::UnknownPlayer { player } => {
                write!(formatter, "unknown player entity {}", player.raw())
            }
            Self::MissingKinematicBody { player } => {
                write!(
                    formatter,
                    "player entity {} has no kinematic body",
                    player.raw()
                )
            }
            Self::EntityBatch(error) => write!(formatter, "entity batch rejected: {error}"),
            Self::Motion(error) => write!(formatter, "motion phase failed: {error}"),
        }
    }
}

impl std::error::Error for PlayerError {}

fn player_action_is_valid(action: ResolvedPlayerAction) -> bool {
    match action {
        ResolvedPlayerAction::Move { forward, right } => {
            forward.is_finite()
                && right.is_finite()
                && (-1.0..=1.0).contains(&forward)
                && (-1.0..=1.0).contains(&right)
        }
        ResolvedPlayerAction::Look {
            yaw_delta,
            pitch_delta,
        } => {
            yaw_delta.is_finite()
                && pitch_delta.is_finite()
                && (-1.0..=1.0).contains(&yaw_delta)
                && (-1.0..=1.0).contains(&pitch_delta)
        }
    }
}

fn normalize_yaw(yaw_degrees: f32) -> f32 {
    (yaw_degrees + 180.0).rem_euclid(360.0) - 180.0
}

fn move_velocity(
    config: &PlayerControllerConfig,
    yaw_degrees: f32,
    forward: f32,
    right: f32,
    input_length: f32,
) -> Vec3 {
    let scale = 1.0 / input_length.max(1.0);
    let yaw = yaw_degrees.to_radians();
    let forward_basis = Vec3::new(-yaw.sin(), 0.0, -yaw.cos());
    let right_basis = Vec3::new(yaw.cos(), 0.0, -yaw.sin());
    (forward_basis * (forward * scale) + right_basis * (right * scale))
        * config.move_speed_units_per_second
}

fn run_player_motion(
    entities: &mut EntityState,
    scene: &VoxelCollisionScene,
    player: EntityId,
    velocity: Vec3,
    move_delta_seconds: f32,
) -> Result<MotionPhaseReceipt, PlayerError> {
    entities
        .apply_batch(EntityCommandBatch::new([
            EntityCommand::SetKinematicVelocity {
                entity: player,
                velocity,
            },
        ]))
        .map_err(PlayerError::EntityBatch)?;
    let result = KinematicMotionSystem::run_selected(
        entities,
        scene,
        move_delta_seconds,
        &BTreeSet::from([player]),
    );
    entities
        .apply_batch(EntityCommandBatch::new([
            EntityCommand::SetKinematicVelocity {
                entity: player,
                velocity: Vec3::ZERO,
            },
        ]))
        .map_err(PlayerError::EntityBatch)?;
    result.map_err(PlayerError::Motion)
}

fn player_translation(entities: &EntityState, player: EntityId) -> Result<Vec3, PlayerError> {
    entities
        .view(player)
        .map_err(|_| PlayerError::UnknownPlayer { player })?
        .transform
        .map(|transform| transform.translation)
        .ok_or(PlayerError::MissingKinematicBody { player })
}

fn motion_moved(motion: &MotionPhaseReceipt, player: EntityId) -> bool {
    motion.facts.iter().any(|fact| {
        matches!(fact, MotionFact::Moved { entity, before, after } if *entity == player && before != after)
    })
}

fn motion_blocked(motion: &MotionPhaseReceipt, player: EntityId) -> bool {
    motion
        .facts
        .iter()
        .any(|fact| matches!(fact, MotionFact::Blocked { entity, .. } if *entity == player))
}

fn motion_blocked_on_axis(motion: &MotionPhaseReceipt, player: EntityId, axis: MotionAxis) -> bool {
    motion.facts.iter().any(|fact| {
        matches!(fact, MotionFact::Blocked { entity, axis: blocked_axis, .. } if *entity == player && *blocked_axis == axis)
    })
}

pub(crate) fn player_view(
    entities: &EntityState,
    player: EntityId,
) -> Result<EntityView, PlayerError> {
    entities
        .view(player)
        .map_err(|_| PlayerError::UnknownPlayer { player })
}
