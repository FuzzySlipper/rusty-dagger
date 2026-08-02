use core_ids::EntityId;
use core_math::Vec3;
use engine_spatial::{MaterialVoxel, VoxelCollisionScene};
use entity_state::{EntityDefinition, EntityState};
use serde::Deserialize;

use crate::player::{PlayerControllerConfig, PlayerControllerState, PlayerInputBindings};

pub const SUPPORTED_PROJECT_SCHEMA_VERSION: u32 = 24;
pub const PLAYER_ENTITY_ID: EntityId = EntityId::new(1);
pub const PLAYER_HALF_EXTENTS: Vec3 = Vec3::new(0.25, 0.25, 0.25);

#[derive(Debug)]
pub enum ProjectAdmissionError {
    Json(String),
    UnsupportedSchema { actual: u32, expected: u32 },
    MissingScene,
    MissingVoxelEnvironment,
    EmptyVoxelEnvironment,
    InvalidVoxelEnvironment(String),
    MissingPlayer,
    InvalidPlayer(String),
    InvalidEntityState(String),
}

impl std::fmt::Display for ProjectAdmissionError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(formatter, "{self:?}")
    }
}

impl std::error::Error for ProjectAdmissionError {}

#[derive(Debug)]
pub struct AdmittedProject {
    pub schema_version: u32,
    pub scene_id: String,
    pub player: EntityId,
    pub player_start: Vec3,
    pub player_controller: PlayerControllerConfig,
    pub player_state: PlayerControllerState,
    pub material_voxel_count: usize,
    pub collision_scene: VoxelCollisionScene,
    pub entities: EntityState,
}

impl AdmittedProject {
    /// Admit the committed project document through the same material voxel
    /// data used by the imported presentation. Unknown authored fields remain
    /// opaque; only the runtime-owned scene, player, and controller fields are
    /// interpreted here.
    pub fn from_json(document: &str) -> Result<Self, ProjectAdmissionError> {
        let project: ProjectDocument = serde_json::from_str(document)
            .map_err(|error| ProjectAdmissionError::Json(error.to_string()))?;
        if project.schema_version != SUPPORTED_PROJECT_SCHEMA_VERSION {
            return Err(ProjectAdmissionError::UnsupportedSchema {
                actual: project.schema_version,
                expected: SUPPORTED_PROJECT_SCHEMA_VERSION,
            });
        }

        let scene = project
            .scenes
            .iter()
            .find(|scene| project.entry_scene.as_deref() == Some(scene.id.as_str()))
            .or_else(|| project.scenes.first())
            .ok_or(ProjectAdmissionError::MissingScene)?;
        let voxel_environment = scene
            .voxel_environment
            .as_ref()
            .ok_or(ProjectAdmissionError::MissingVoxelEnvironment)?;
        if voxel_environment.material_voxels.is_empty() {
            return Err(ProjectAdmissionError::EmptyVoxelEnvironment);
        }
        let material_voxels = voxel_environment
            .material_voxels
            .iter()
            .map(|voxel| MaterialVoxel {
                address: voxel.address,
                material_slot: voxel.material_slot,
            })
            .collect::<Vec<_>>();
        let material_voxel_count = material_voxels.len();
        let collision_scene = VoxelCollisionScene::from_material_voxels(
            voxel_environment.voxel_size,
            voxel_environment.chunk_size,
            material_voxels,
        )
        .map_err(|error| ProjectAdmissionError::InvalidVoxelEnvironment(error.to_string()))?;

        let player = scene
            .entities
            .iter()
            .find(|entity| entity.player_controller.is_some())
            .ok_or(ProjectAdmissionError::MissingPlayer)?;
        let player_id = EntityId::new(player.id);
        if player_id != PLAYER_ENTITY_ID {
            return Err(ProjectAdmissionError::InvalidPlayer(format!(
                "player controller must be entity {}, got {}",
                PLAYER_ENTITY_ID.raw(),
                player_id.raw()
            )));
        }
        let translation = Vec3::new(
            player.translation[0],
            player.translation[1],
            player.translation[2],
        );
        let controller_document = player
            .player_controller
            .as_ref()
            .expect("player controller was selected");
        let player_controller = controller_document.to_runtime_config()?;
        let player_state = PlayerControllerState {
            yaw_degrees: player_controller.initial_yaw_degrees,
            pitch_degrees: player_controller.initial_pitch_degrees,
        };
        let entities = EntityState::from_definitions([EntityDefinition::new(
            player_id,
            player.name.as_deref().unwrap_or("player"),
        )
        .with_transform(translation)
        .with_collision(true, false)
        .with_kinematic(PLAYER_HALF_EXTENTS, Vec3::ZERO)])
        .map_err(|error| ProjectAdmissionError::InvalidEntityState(error.to_string()))?;

        Ok(Self {
            schema_version: project.schema_version,
            scene_id: scene.id.clone(),
            player: player_id,
            player_start: translation,
            player_controller,
            player_state,
            material_voxel_count,
            collision_scene,
            entities,
        })
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ProjectDocument {
    schema_version: u32,
    entry_scene: Option<String>,
    scenes: Vec<SceneDocument>,
}

#[derive(Debug, Deserialize)]
struct SceneDocument {
    id: String,
    #[serde(rename = "voxelEnvironment")]
    voxel_environment: Option<VoxelEnvironmentDocument>,
    #[serde(default)]
    entities: Vec<EntityDocument>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct VoxelEnvironmentDocument {
    voxel_size: f64,
    chunk_size: u32,
    #[serde(default)]
    material_voxels: Vec<MaterialVoxelDocument>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct MaterialVoxelDocument {
    address: [i64; 3],
    material_slot: u16,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct EntityDocument {
    id: u64,
    name: Option<String>,
    translation: [f32; 3],
    player_controller: Option<PlayerControllerDocument>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct PlayerControllerDocument {
    move_speed_units_per_second: f32,
    move_step_seconds: f32,
    look_degrees_per_unit: f32,
    initial_yaw_degrees: f32,
    initial_pitch_degrees: f32,
    fall_speed_units_per_second: Option<f32>,
    step_up_units: Option<f32>,
    bindings: PlayerBindingsDocument,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct PlayerBindingsDocument {
    move_forward: String,
    move_backward: String,
    move_left: String,
    move_right: String,
    mouse_look: String,
    primary_fire: String,
    #[serde(default)]
    select_weapon: Vec<String>,
}

impl PlayerControllerDocument {
    fn to_runtime_config(&self) -> Result<PlayerControllerConfig, ProjectAdmissionError> {
        let config = PlayerControllerConfig {
            move_speed_units_per_second: self.move_speed_units_per_second,
            move_step_seconds: self.move_step_seconds,
            look_degrees_per_unit: self.look_degrees_per_unit,
            initial_yaw_degrees: self.initial_yaw_degrees,
            initial_pitch_degrees: self.initial_pitch_degrees,
            fall_speed_units_per_second: self.fall_speed_units_per_second,
            step_up_units: self.step_up_units,
            bindings: PlayerInputBindings::new(
                self.bindings.move_forward.clone(),
                self.bindings.move_backward.clone(),
                self.bindings.move_left.clone(),
                self.bindings.move_right.clone(),
                self.bindings.mouse_look.clone(),
                self.bindings.primary_fire.clone(),
                self.bindings.select_weapon.clone(),
            ),
        };
        if config.is_valid() {
            Ok(config)
        } else {
            Err(ProjectAdmissionError::InvalidPlayer(
                "playerController contains invalid bounds or duplicate controls".to_string(),
            ))
        }
    }
}
