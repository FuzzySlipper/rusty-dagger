use std::collections::{BTreeMap, VecDeque};

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
use crate::project::{AdmittedProject, ContentEntity, ProjectAdmissionError, PLAYER_HALF_EXTENTS};

#[derive(Debug)]
pub enum RuntimeError {
    Admission(ProjectAdmissionError),
    Experiment(ExperimentError),
    Player(PlayerError),
    Content(ContentError),
}

#[derive(Debug, Clone, PartialEq)]
pub enum ContentError {
    UnknownEntity(u64),
    NoGroundedApproach(u64),
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
    content_entities: Vec<ContentEntity>,
    content_live_positions: BTreeMap<u64, [f32; 3]>,
    focused_content_id: Option<u64>,
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
    pub content: Vec<ContentEntityReadout>,
    pub focused_content_id: Option<u64>,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ContentEntityReadout {
    pub id: u64,
    pub kind: &'static str,
    pub name: String,
    pub reference: EnemyReferenceReadout,
    pub live: ContentLiveReadout,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EnemyReferenceReadout {
    pub mobile_id: u8,
    pub mobile_name: String,
    pub texture_archive: u16,
    pub flying: bool,
    pub sprite_asset: String,
    pub authored_position: [f32; 3],
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ContentLiveReadout {
    pub position: [f32; 3],
    pub distance_from_player: f32,
}

/// A side-effect-free evaluation through the same admission and calculation
/// authority used when an experiment is applied to play.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExperimentEvaluation {
    pub document: ExperimentDocument,
    pub move_speed_units_per_second: f32,
    pub max_health: f32,
    pub calculation: CalculationRecord,
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
        let content_live_positions = admitted
            .content_entities
            .iter()
            .map(|entity| (entity.id, entity.authored_position))
            .collect();
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
            content_entities: admitted.content_entities,
            content_live_positions,
            focused_content_id: None,
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

    /// Evaluate one complete authoring document without changing the active
    /// experiment, player, or calculation history.
    pub fn evaluate_experiment_json(
        &self,
        document: &str,
    ) -> Result<ExperimentEvaluation, RuntimeError> {
        let admitted = dagger_rpg::admit_json(document).map_err(RuntimeError::Experiment)?;
        Ok(ExperimentEvaluation {
            document: admitted.document,
            move_speed_units_per_second: admitted.player.move_speed_units_per_second,
            max_health: admitted.player.max_health,
            calculation: admitted.calculation,
        })
    }

    /// Reset the playable run to the committed Privateer's Hold start while
    /// retaining the currently applied experiment document.
    pub fn reset_play_session(&mut self) -> Result<ExperimentReadout, RuntimeError> {
        self.set_player_position(self.player_start)?;
        self.player_state = self.player_start_state;
        self.player_health = self.experiment.player.max_health;
        self.focused_content_id = None;
        self.experiment_readout()
    }

    /// Publish Dagger-owned live entity positions from the native patrol
    /// authority. Unknown renderer handles are ignored rather than becoming
    /// content identities through this synchronization seam.
    pub fn sync_content_live_positions(
        &mut self,
        positions: impl IntoIterator<Item = (u64, [f32; 3])>,
    ) {
        for (id, position) in positions {
            if self.content_live_positions.contains_key(&id)
                && position.iter().all(|component| component.is_finite())
            {
                self.content_live_positions.insert(id, position);
            }
        }
    }

    /// Reset the player beside one admitted live content entity and face it.
    /// The collision scene chooses the floor; Angular never supplies a raw
    /// teleport coordinate.
    pub fn jump_to_content(&mut self, id: u64) -> Result<ExperimentReadout, RuntimeError> {
        let target = self
            .content_live_positions
            .get(&id)
            .copied()
            .ok_or(RuntimeError::Content(ContentError::UnknownEntity(id)))?;
        let original_position = self.player_position()?;
        let original_state = self.player_state;
        let original_focus = self.focused_content_id;
        for [offset_x, offset_z] in [[0.0, 1.5], [1.5, 0.0], [0.0, -1.5], [-1.5, 0.0]] {
            let approach_probe = [target[0] + offset_x, target[1] + 2.0, target[2] + offset_z];
            let Some(grounding) =
                crate::navgrid::ground_spawn(&self.collision_scene, approach_probe, 6.0)
            else {
                continue;
            };
            let position = Vec3::new(
                approach_probe[0],
                grounding.support_y + PLAYER_HALF_EXTENTS.y + 0.3,
                approach_probe[2],
            );
            self.set_player_position(position)?;
            let mut facing_state = self.player_start_state;
            let delta_x = target[0] - position.x;
            let delta_z = target[2] - position.z;
            facing_state.yaw_degrees = (-delta_x).atan2(-delta_z).to_degrees();
            self.player_state = facing_state;

            let navigable = [
                ResolvedPlayerAction::Move {
                    forward: 0.0,
                    right: 1.0,
                },
                ResolvedPlayerAction::Move {
                    forward: -1.0,
                    right: 0.0,
                },
            ]
            .into_iter()
            .any(|action| {
                let _ = self.set_player_position(position);
                self.player_state = facing_state;
                self.apply_player_action(action).is_ok_and(|_| {
                    self.player_position().is_ok_and(|after| {
                        (after.x - position.x).hypot(after.z - position.z) > 0.01
                    })
                })
            });
            if navigable {
                self.set_player_position(position)?;
                self.player_state = facing_state;
                self.player_health = self.experiment.player.max_health;
                self.focused_content_id = Some(id);
                return self.experiment_readout();
            }
        }
        self.set_player_position(original_position)?;
        self.player_state = original_state;
        self.focused_content_id = original_focus;
        Err(RuntimeError::Content(ContentError::NoGroundedApproach(id)))
    }

    pub fn experiment_readout(&self) -> Result<ExperimentReadout, RuntimeError> {
        let position = self.player_position()?;
        let content = self
            .content_entities
            .iter()
            .map(|entity| {
                let live_position = self
                    .content_live_positions
                    .get(&entity.id)
                    .copied()
                    .unwrap_or(entity.authored_position);
                ContentEntityReadout {
                    id: entity.id,
                    kind: "enemy",
                    name: entity.name.clone(),
                    reference: EnemyReferenceReadout {
                        mobile_id: entity.mobile_id,
                        mobile_name: entity.mobile_name.clone(),
                        texture_archive: entity.texture_archive,
                        flying: entity.flying,
                        sprite_asset: entity.sprite_asset.clone(),
                        authored_position: entity.authored_position,
                    },
                    live: ContentLiveReadout {
                        position: live_position,
                        distance_from_player: (live_position[0] - position.x)
                            .hypot(live_position[1] - position.y)
                            .hypot(live_position[2] - position.z),
                    },
                }
            })
            .collect();
        Ok(ExperimentReadout {
            document: self.experiment.document.clone(),
            move_speed_units_per_second: self.player_controller.move_speed_units_per_second,
            max_health: self.experiment.player.max_health,
            current_health: self.player_health,
            player_position: [position.x, position.y, position.z],
            player_yaw_degrees: self.player_state.yaw_degrees,
            calculations: self.calculation_history.iter().cloned().collect(),
            content,
            focused_content_id: self.focused_content_id,
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
