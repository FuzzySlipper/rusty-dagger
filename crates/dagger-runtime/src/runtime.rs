use std::collections::VecDeque;

use dagger_rpg::{AdmittedExperiment, CalculationRecord, ExperimentDocument, ExperimentError};
use rusty_engine::core_ids::EntityId;
use rusty_engine::core_math::Vec3;
use rusty_engine::engine_spatial::VoxelCollisionScene;
use rusty_engine::entity_state::EntityState;
use rusty_engine::svc_collision::{
    StaticMeshColliderInstance, StaticMeshInstanceId, StaticMeshTransform,
};
use serde::Serialize;

use crate::player::{
    apply_player_action, player_view, PlayerControlReceipt, PlayerControllerConfig,
    PlayerControllerState, PlayerError, ResolvedPlayerAction,
};
use crate::project::{AdmittedProject, ProjectAdmissionError};

#[derive(Debug)]
pub enum RuntimeError {
    Admission(ProjectAdmissionError),
    Experiment(ExperimentError),
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
    player_start: Vec3,
    player_start_state: PlayerControllerState,
    experiment: AdmittedExperiment,
    player_health: f32,
    calculation_sequence: u64,
    calculation_history: VecDeque<SessionCalculationRecord>,
    dungeon_bounds: Option<([f64; 3], [f64; 3])>,
}

pub const STARTER_EXPERIMENT_JSON: &str =
    include_str!("../../../data/experiments/privateers-hold-starter.json");
pub const CALCULATION_HISTORY_LIMIT: usize = 16;

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExperimentReadout {
    pub document: ExperimentDocument,
    pub move_speed_units_per_second: f32,
    pub max_health: f32,
    pub current_health: f32,
    pub player_position: [f32; 3],
    pub player_yaw_degrees: f32,
    pub calculations: Vec<SessionCalculationRecord>,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SessionCalculationRecord {
    pub sequence: u64,
    #[serde(flatten)]
    pub calculation: CalculationRecord,
}

impl DaggerRuntime {
    pub fn from_project_json(document: &str) -> Result<Self, RuntimeError> {
        Self::from_project_and_experiment_json(document, STARTER_EXPERIMENT_JSON)
    }

    pub fn from_project_and_experiment_json(
        project_document: &str,
        experiment_document: &str,
    ) -> Result<Self, RuntimeError> {
        let admitted =
            AdmittedProject::from_json(project_document).map_err(RuntimeError::Admission)?;
        let experiment =
            dagger_rpg::admit_json(experiment_document).map_err(RuntimeError::Experiment)?;
        Self::from_admitted_project_and_experiment(admitted, experiment)
    }

    pub fn from_admitted_project(admitted: AdmittedProject) -> Result<Self, RuntimeError> {
        let experiment =
            dagger_rpg::admit_json(STARTER_EXPERIMENT_JSON).map_err(RuntimeError::Experiment)?;
        Self::from_admitted_project_and_experiment(admitted, experiment)
    }

    fn from_admitted_project_and_experiment(
        admitted: AdmittedProject,
        experiment: AdmittedExperiment,
    ) -> Result<Self, RuntimeError> {
        let mut collision_scene = admitted.collision_scene;
        // Register the dungeon trimesh collider so the kinematic motion sweep
        // blocks on the full dungeon geometry (floors, walls, ramps) with no
        // controller changes. One instance at identity over the whole mesh.
        if let Some(collider) = admitted.dungeon_collider {
            let geometry_hash = collider.geometry_hash;
            let instance = StaticMeshColliderInstance {
                id: StaticMeshInstanceId(1),
                asset: collider.id,
                expected_geometry_hash: geometry_hash,
                transform: StaticMeshTransform::IDENTITY,
            };
            let revision = collision_scene.static_mesh_collision_revision();
            collision_scene
                .replace_static_mesh_colliders(revision, [collider], [instance])
                .map_err(|error| {
                    RuntimeError::Admission(ProjectAdmissionError::CollisionRegistration(format!(
                        "{error:?}"
                    )))
                })?;
        }
        let mut player_controller = admitted.player_controller;
        player_controller.move_speed_units_per_second =
            experiment.player.move_speed_units_per_second;
        let initial_calculation = SessionCalculationRecord {
            sequence: 1,
            calculation: experiment.calculation.clone(),
        };
        let player_start = admitted.player_start;
        let player_start_state = admitted.player_state;
        let player_health = experiment.player.max_health;
        Ok(Self {
            entities: admitted.entities,
            collision_scene,
            player: admitted.player,
            player_controller,
            player_state: admitted.player_state,
            player_start,
            player_start_state,
            experiment,
            player_health,
            calculation_sequence: 1,
            calculation_history: VecDeque::from([initial_calculation]),
            dungeon_bounds: admitted.dungeon_bounds,
        })
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

    /// Admit and apply one complete authoring document. Admission and all
    /// calculations happen before live state changes, so a rejected edit
    /// cannot partially mutate the running experiment.
    pub fn apply_experiment_json(
        &mut self,
        document: &str,
    ) -> Result<ExperimentReadout, RuntimeError> {
        let admitted = dagger_rpg::admit_json(document).map_err(RuntimeError::Experiment)?;
        self.calculation_sequence = self.calculation_sequence.saturating_add(1);
        self.player_controller.move_speed_units_per_second =
            admitted.player.move_speed_units_per_second;
        self.player_health = admitted.player.max_health;
        self.calculation_history
            .push_back(SessionCalculationRecord {
                sequence: self.calculation_sequence,
                calculation: admitted.calculation.clone(),
            });
        while self.calculation_history.len() > CALCULATION_HISTORY_LIMIT {
            self.calculation_history.pop_front();
        }
        self.experiment = admitted;
        self.experiment_readout()
    }

    /// Reset the playable run to the committed Privateer's Hold start while
    /// retaining the currently applied experiment document.
    pub fn reset_play_session(&mut self) -> Result<ExperimentReadout, RuntimeError> {
        self.set_player_position(self.player_start)?;
        self.player_state = self.player_start_state;
        self.player_health = self.experiment.player.max_health;
        self.experiment_readout()
    }

    pub fn experiment_readout(&self) -> Result<ExperimentReadout, RuntimeError> {
        let position = self.player_position()?;
        Ok(ExperimentReadout {
            document: self.experiment.document.clone(),
            move_speed_units_per_second: self.player_controller.move_speed_units_per_second,
            max_health: self.experiment.player.max_health,
            current_health: self.player_health,
            player_position: [position.x, position.y, position.z],
            player_yaw_degrees: self.player_state.yaw_degrees,
            calculations: self.calculation_history.iter().cloned().collect(),
        })
    }

    /// Authoritatively reposition the player (route derivation / probing).
    /// Sets the translation and clears vertical velocity so a subsequent
    /// settle starts cleanly; collision is re-evaluated by the next action.
    pub fn set_player_position(&mut self, translation: Vec3) -> Result<(), RuntimeError> {
        use rusty_engine::entity_state::{EntityCommand, EntityCommandBatch};
        self.entities
            .apply_batch(EntityCommandBatch::new([
                EntityCommand::SetTranslation {
                    entity: self.player,
                    translation,
                },
                EntityCommand::SetKinematicVelocity {
                    entity: self.player,
                    velocity: Vec3::ZERO,
                },
            ]))
            .map_err(|error| {
                RuntimeError::Player(crate::player::PlayerError::EntityBatch(error))
            })?;
        Ok(())
    }

    pub fn entities(&self) -> &EntityState {
        &self.entities
    }

    pub fn collision_scene(&self) -> &VoxelCollisionScene {
        &self.collision_scene
    }

    /// World-space AABB (min, max) of the dungeon trimesh, when admitted.
    pub fn dungeon_bounds(&self) -> Option<([f64; 3], [f64; 3])> {
        self.dungeon_bounds
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
