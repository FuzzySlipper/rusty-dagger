use core_ids::EntityId;
use core_math::Vec3;
use engine_spatial::VoxelCollisionScene;
use entity_state::EntityState;

use crate::player::{
    apply_player_action, player_view, PlayerControlReceipt, PlayerControllerConfig,
    PlayerControllerState, PlayerError, ResolvedPlayerAction,
};
use crate::project::{AdmittedProject, ProjectAdmissionError};

#[derive(Debug)]
pub enum RuntimeError {
    Admission(ProjectAdmissionError),
    Player(PlayerError),
}

impl std::fmt::Display for RuntimeError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(formatter, "{self:?}")
    }
}

impl std::error::Error for RuntimeError {}

pub struct DaggerRuntime {
    entities: EntityState,
    collision_scene: VoxelCollisionScene,
    player: EntityId,
    player_controller: PlayerControllerConfig,
    player_state: PlayerControllerState,
}

impl DaggerRuntime {
    pub fn from_project_json(document: &str) -> Result<Self, RuntimeError> {
        let admitted = AdmittedProject::from_json(document).map_err(RuntimeError::Admission)?;
        Ok(Self::from_admitted_project(admitted))
    }

    pub fn from_admitted_project(admitted: AdmittedProject) -> Self {
        Self {
            entities: admitted.entities,
            collision_scene: admitted.collision_scene,
            player: admitted.player,
            player_controller: admitted.player_controller,
            player_state: admitted.player_state,
        }
    }

    pub fn player(&self) -> EntityId {
        self.player
    }

    pub fn player_position(&self) -> Result<Vec3, RuntimeError> {
        Ok(player_view(&self.entities, self.player)
            .map_err(RuntimeError::Player)?
            .transform
            .expect("admitted player has transform")
            .translation)
    }

    pub fn player_controller(&self) -> &PlayerControllerConfig {
        &self.player_controller
    }

    pub fn player_state(&self) -> PlayerControllerState {
        self.player_state
    }

    pub fn entities(&self) -> &EntityState {
        &self.entities
    }

    pub fn collision_scene(&self) -> &VoxelCollisionScene {
        &self.collision_scene
    }

    pub fn apply_player_action(
        &mut self,
        action: ResolvedPlayerAction,
    ) -> Result<PlayerControlReceipt, RuntimeError> {
        let result = apply_player_action(
            &mut self.entities,
            &self.collision_scene,
            self.player,
            &mut self.player_state,
            &self.player_controller,
            action,
            self.player_controller.move_step_seconds,
        )
        .map_err(RuntimeError::Player)?;
        Ok(result)
    }
}
