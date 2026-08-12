use rusty_engine::core_ids::EntityId;
use rusty_engine::core_math::{Vec2, Vec3};
use rusty_engine::engine_spatial::{
    CharacterControllerCommand, CharacterControllerConfig as EngineConfig,
    CharacterControllerError, CharacterControllerReceipt, CharacterControllerService,
    FirstPersonLookCommand, FirstPersonLookConfig, FirstPersonLookError, FirstPersonLookService,
    FirstPersonLookState,
};
use rusty_engine::entity_state::{EntityState, EntityView};
use serde::{Deserialize, Serialize};

pub const MAX_PLAYER_SPEED_UNITS_PER_SECOND: f32 = 1_000.0;
pub const MAX_PLAYER_LOOK_DEGREES_PER_UNIT: f32 = 180.0;
pub const MAX_PLAYER_STEP_UP_UNITS: f32 = 4.0;
pub const MAX_INPUT_CONTROL_LENGTH: usize = 64;

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

/// Dagger policy translated into the canonical Engine controller config.
///
/// The legacy fields remain public because they are part of Dagger's admitted
/// project/readout contract. `engine` is the single movement implementation.
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
    pub(crate) engine: EngineConfig,
    pub(crate) look: FirstPersonLookConfig,
}

impl PlayerControllerConfig {
    pub(crate) fn configure_engine(&mut self) {
        let mut engine = EngineConfig::responsive_fps();
        // Dagger's authored transform is a capsule center. A full-height
        // canonical body plus the host's +0.75m eye offset preserves the
        // established camera height while replacing the old 0.5m cube.
        engine.shape.standing_height = 1.8;
        engine.shape.crouched_height = 1.1;
        engine.shape.radius = 0.25;
        // Existing Dagger project markers were authored for the retired 0.5m
        // cube. Permit one bounded canonical recovery of up to a metre so the
        // full-height capsule settles those unchanged spawn choices safely.
        engine.recovery.maximum_distance = 1.0;
        engine.recovery.maximum_speed = 60.0;
        engine.ground.forward_speed = self.move_speed_units_per_second;
        engine.ground.backward_speed = self.move_speed_units_per_second;
        engine.ground.strafe_speed = self.move_speed_units_per_second;
        engine.air.maximum_speed = self.move_speed_units_per_second;
        engine.air.wish_speed_cap = self.move_speed_units_per_second;
        if let Some(speed) = self.fall_speed_units_per_second {
            // Preserve the Dagger document's terminal fall-speed policy while
            // Engine owns gravity, grounding, and continuation state.
            engine.vertical.terminal_fall_speed = speed;
        }
        if let Some(height) = self.step_up_units {
            engine.surface.maximum_step_height = height;
        }
        self.engine = engine;

        let radians_per_unit = self.look_degrees_per_unit.to_radians();
        let mut look = FirstPersonLookConfig::default();
        look.horizontal_radians_per_unit = radians_per_unit;
        look.vertical_radians_per_unit = radians_per_unit;
        look.minimum_pitch_radians = -89.0_f32.to_radians();
        look.maximum_pitch_radians = 89.0_f32.to_radians();
        self.look = look;
    }

    pub(crate) fn is_valid(&self) -> bool {
        self.move_speed_units_per_second.is_finite()
            && self.move_speed_units_per_second > 0.0
            && self.move_speed_units_per_second <= MAX_PLAYER_SPEED_UNITS_PER_SECOND
            && self.move_step_seconds.is_finite()
            && self.move_step_seconds > 0.0
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
            && self.engine.validate().is_ok()
    }
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PlayerControllerState {
    pub yaw_degrees: f32,
    pub pitch_degrees: f32,
}

impl PlayerControllerState {
    pub(crate) fn from_degrees(yaw_degrees: f32, pitch_degrees: f32) -> Self {
        Self {
            yaw_degrees,
            pitch_degrees,
        }
    }

    pub(crate) fn set_yaw_degrees(&mut self, yaw_degrees: f32) {
        self.yaw_degrees = yaw_degrees;
    }

    pub(crate) fn engine_look_state(self) -> FirstPersonLookState {
        FirstPersonLookState {
            yaw_radians: -self.yaw_degrees.to_radians(),
            pitch_radians: -self.pitch_degrees.to_radians(),
        }
    }
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
    pub motion: Option<CharacterControllerReceipt>,
}

#[allow(clippy::too_many_arguments)]
pub(crate) fn apply_player_action(
    entities: &mut EntityState,
    scene: &rusty_engine::engine_spatial::VoxelCollisionScene,
    player: EntityId,
    state: &mut PlayerControllerState,
    look_state: &mut FirstPersonLookState,
    service: &mut CharacterControllerService,
    command_sequence: &mut u64,
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
    if entity_view.character_motion.is_none() || entity_view.transform.is_none() {
        return Err(PlayerError::MissingCharacterMotion { player });
    }

    match action {
        ResolvedPlayerAction::Look {
            yaw_delta,
            pitch_delta,
        } => {
            let before = *state;
            // Public degree fields remain a stable Dagger readout. Rebuild the
            // explicit Engine state first because Dagger diagnostics may set a
            // facing directly before issuing their next semantic command.
            *look_state = state.engine_look_state();
            let receipt = FirstPersonLookService.integrate(
                &config.look,
                *look_state,
                FirstPersonLookCommand {
                    delta: Vec2::new(yaw_delta, pitch_delta),
                },
            )?;
            *look_state = receipt.after;
            // RendererCameraPose and Daggerfall aim use the historical camera
            // degree convention, opposite Engine's canonical basis signs.
            state.yaw_degrees = -receipt.after.yaw_radians.to_degrees();
            state.pitch_degrees = -receipt.after.pitch_radians.to_degrees();
            Ok(PlayerControlReceipt {
                action,
                facts: vec![PlayerControlFact::LookChanged {
                    entity: player,
                    before_yaw_degrees: before.yaw_degrees,
                    after_yaw_degrees: state.yaw_degrees,
                    before_pitch_degrees: before.pitch_degrees,
                    after_pitch_degrees: state.pitch_degrees,
                }],
                motion: None,
            })
        }
        ResolvedPlayerAction::Move { forward, right } => {
            // The project contract predates Engine's bounded fixed-step
            // command envelope. Preserve the authored action duration by
            // splitting it into deterministic canonical ticks.
            let substeps = (move_delta_seconds / (1.0 / 60.0)).ceil().max(1.0) as u32;
            let step_seconds = move_delta_seconds / substeps as f32;
            let action_start = entities
                .transform(player)
                .ok_or(PlayerError::MissingCharacterMotion { player })?
                .translation;
            let mut blocked = false;
            let mut last_receipt = None;
            for _ in 0..substeps {
                *command_sequence = command_sequence
                    .checked_add(1)
                    .ok_or(PlayerError::CommandSequenceExhausted)?;
                let receipt = service.step(
                    entities,
                    scene,
                    player,
                    &config.engine,
                    CharacterControllerCommand {
                        planar_intent: Vec2::new(right, forward),
                        heading_yaw_radians: look_state.yaw_radians,
                        step_seconds,
                        sequence: *command_sequence,
                        ..CharacterControllerCommand::idle(step_seconds, *command_sequence)
                    },
                )?;
                blocked |= !receipt.blocks.is_empty()
                    || receipt.contacts.iter().any(|contact| {
                        matches!(
                            contact.kind,
                            rusty_engine::engine_spatial::CharacterContactKind::Wall
                        )
                    });
                last_receipt = Some(receipt);
            }
            let receipt = last_receipt.expect("at least one canonical controller substep");
            let mut facts = Vec::new();
            let before = action_start;
            let after = receipt.transform_after.translation;
            if before != after {
                facts.push(PlayerControlFact::Moved {
                    entity: player,
                    before,
                    after,
                });
            }
            if blocked {
                facts.push(PlayerControlFact::Blocked {
                    entity: player,
                    attempted_velocity: receipt.wish_velocity,
                });
            }
            Ok(PlayerControlReceipt {
                action,
                facts,
                motion: Some(receipt),
            })
        }
    }
}

#[derive(Debug)]
pub enum PlayerError {
    InvalidAction(ResolvedPlayerAction),
    UnknownPlayer { player: EntityId },
    MissingCharacterMotion { player: EntityId },
    CommandSequenceExhausted,
    EntityBatch(rusty_engine::entity_state::BatchRejection),
    MotionPublication(rusty_engine::entity_state::CharacterMotionPublicationError),
    Controller(CharacterControllerError),
    Look(FirstPersonLookError),
}

impl std::fmt::Display for PlayerError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(formatter, "{self:?}")
    }
}

impl std::error::Error for PlayerError {}

impl From<CharacterControllerError> for PlayerError {
    fn from(value: CharacterControllerError) -> Self {
        Self::Controller(value)
    }
}

impl From<FirstPersonLookError> for PlayerError {
    fn from(value: FirstPersonLookError) -> Self {
        Self::Look(value)
    }
}

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

pub(crate) fn player_view(
    entities: &EntityState,
    player: EntityId,
) -> Result<EntityView, PlayerError> {
    entities
        .view(player)
        .map_err(|_| PlayerError::UnknownPlayer { player })
}
