use rusty_engine::core_ids::EntityId;
use rusty_engine::core_math::Vec3;
use rusty_engine::engine_spatial::{FirstPersonLookState, MaterialVoxel, VoxelCollisionScene};
use rusty_engine::entity_state::{CharacterMotionComponent, EntityDefinition, EntityState};
use rusty_engine::render_model::{
    decode_mesh_resource_payload, MeshCollisionPolicy, MeshPayloadSource, StaticMeshAsset,
};
use rusty_engine::svc_collision::{StaticMeshAssetId, StaticMeshColliderAsset};

pub type DungeonBounds = ([f64; 3], [f64; 3]);
type DungeonColliderAdmission = (StaticMeshColliderAsset, DungeonBounds);
use rusty_engine::product_kernel::serde::Deserialize;

use crate::player::{PlayerControllerConfig, PlayerInputBindings};

pub const SUPPORTED_PROJECT_SCHEMA_VERSION: u32 = 24;
pub const PLAYER_ENTITY_ID: EntityId = EntityId::new(1);
/// Conservative AABB for Dagger diagnostics around Engine's standing capsule.
pub const PLAYER_HALF_EXTENTS: Vec3 = Vec3::new(0.25, 0.9, 0.25);
/// The dungeon static mesh is the collision authority: one trimesh collider
/// over the full dungeon geometry (floors, walls,
/// ceilings, ramps). Registered under a fixed asset/instance id at identity.
pub const DUNGEON_COLLIDER_ASSET_ID: StaticMeshAssetId = StaticMeshAssetId(1);
pub const DUNGEON_COLLIDER_MESH_ASSET: &str = "mesh/privateers-hold";

#[derive(Debug)]
pub enum ProjectAdmissionError {
    Json(String),
    UnsupportedSchema { actual: u32, expected: u32 },
    MissingScene,
    UnknownEntryScene { scene_id: String },
    MissingCollisionAuthority,
    InvalidVoxelEnvironment(String),
    MissingDungeonCollider,
    InvalidDungeonCollider(String),
    MissingPlayer,
    InvalidPlayer(String),
    InvalidEntityState(String),
    CollisionRegistration(String),
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
    pub player_look_state: FirstPersonLookState,
    pub material_voxel_count: usize,
    pub dungeon_collider: Option<StaticMeshColliderAsset>,
    /// World-space AABB of the dungeon trimesh payload (min, max), when the
    /// mesh asset is present. Consumers (nav grid derivation) use it to bound
    /// their sweeps over the collision authority.
    pub dungeon_bounds: Option<DungeonBounds>,
    pub collision_scene: VoxelCollisionScene,
    pub entities: EntityState,
    pub content_entities: Vec<ContentEntity>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ContentEntity {
    pub id: u64,
    pub name: String,
    pub sprite_asset: String,
    pub authored_position: [f32; 3],
    pub kind: ContentEntityKind,
}

/// What one admitted content entity is. Enemies carry their decoded mobile
/// reference and spawn a live actor; treasure containers (RDB
/// random-treasure markers, project entities with a `lootKey`) carry the
/// classic dungeon-treasure loot table key the runtime generates from.
#[derive(Debug, Clone, PartialEq)]
pub enum ContentEntityKind {
    Enemy(EnemyContentReference),
    Treasure {
        /// Classic loot table key (donor LootTables.cs dungeon-type array:
        /// Privateer's Hold's MAPS.BSA dungeon type is 2, Human Stronghold
        /// -> "N"). Stamped on the project entity by generate-project.py
        /// from the import sidecar.
        loot_key: String,
    },
}

#[derive(Debug, Clone, PartialEq)]
pub struct EnemyContentReference {
    pub mobile_id: u8,
    pub mobile_name: String,
    pub texture_archive: u16,
    pub flying: bool,
}

impl ContentEntity {
    pub fn enemy(&self) -> Option<&EnemyContentReference> {
        match &self.kind {
            ContentEntityKind::Enemy(reference) => Some(reference),
            ContentEntityKind::Treasure { .. } => None,
        }
    }
}

impl AdmittedProject {
    /// Admit the committed project document. The dungeon static mesh is the
    /// collision authority: its full inline triangle payload (floors, walls,
    /// ceilings, ramps) becomes one trimesh collider. The legacy hidden
    /// `gameplayProxy` material-voxel environment is accepted as an optional
    /// additive authority (used by the adversarial wall/floor probes) but is
    /// no longer required. Unknown authored fields remain opaque; only the
    /// runtime-owned scene, player, and controller fields are interpreted here.
    pub fn from_json(document: &str) -> Result<Self, ProjectAdmissionError> {
        Self::from_json_with_optional_mesh_resource(document, None)
    }

    pub fn from_json_with_mesh_resource(
        document: &str,
        mesh_resource: &[u8],
    ) -> Result<Self, ProjectAdmissionError> {
        Self::from_json_with_optional_mesh_resource(document, Some(mesh_resource))
    }

    fn from_json_with_optional_mesh_resource(
        document: &str,
        mesh_resource: Option<&[u8]>,
    ) -> Result<Self, ProjectAdmissionError> {
        let project: ProjectDocument = rusty_engine::product_kernel::serde_json::from_str(document)
            .map_err(|error| ProjectAdmissionError::Json(error.to_string()))?;
        if project.schema_version != SUPPORTED_PROJECT_SCHEMA_VERSION {
            return Err(ProjectAdmissionError::UnsupportedSchema {
                actual: project.schema_version,
                expected: SUPPORTED_PROJECT_SCHEMA_VERSION,
            });
        }

        let scene = match project.entry_scene.as_deref() {
            Some(entry_scene) => project
                .scenes
                .iter()
                .find(|scene| scene.id == entry_scene)
                .ok_or_else(|| ProjectAdmissionError::UnknownEntryScene {
                    scene_id: entry_scene.to_string(),
                })?,
            None => project
                .scenes
                .first()
                .ok_or(ProjectAdmissionError::MissingScene)?,
        };

        // Optional additive voxel authority (legacy gameplayProxy / probes).
        let (material_voxel_count, material_voxels, voxel_size, chunk_size) =
            match scene.voxel_environment.as_ref() {
                Some(environment) if !environment.material_voxels.is_empty() => {
                    let voxels = environment
                        .material_voxels
                        .iter()
                        .map(|voxel| MaterialVoxel {
                            address: voxel.address,
                            material_slot: voxel.material_slot,
                        })
                        .collect::<Vec<_>>();
                    (
                        voxels.len(),
                        voxels,
                        environment.voxel_size,
                        environment.chunk_size,
                    )
                }
                _ => (0, Vec::new(), 0.5, 16),
            };
        let collision_scene =
            VoxelCollisionScene::from_material_voxels(voxel_size, chunk_size, material_voxels)
                .map_err(|error| {
                    ProjectAdmissionError::InvalidVoxelEnvironment(error.to_string())
                })?;

        // Trimesh authority: decode the dungeon static mesh's inline payload.
        let (dungeon_collider, dungeon_bounds) = match project
            .assets
            .iter()
            .find(|asset| asset.id == DUNGEON_COLLIDER_MESH_ASSET)
            .map(|asset| dungeon_collider_asset(asset, mesh_resource))
            .transpose()?
        {
            Some((collider, bounds)) => (Some(collider), Some(bounds)),
            None => (None, None),
        };
        if dungeon_collider.is_none() && material_voxel_count == 0 {
            return Err(ProjectAdmissionError::MissingCollisionAuthority);
        }

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
        let player_look_state = FirstPersonLookState {
            yaw_radians: player_controller.initial_yaw_degrees.to_radians(),
            pitch_radians: player_controller.initial_pitch_degrees.to_radians(),
        };
        let entities = EntityState::from_definitions([EntityDefinition::new(
            player_id,
            player.name.as_deref().unwrap_or("player"),
        )
        .with_transform(translation)
        .with_collision(true, false)
        .with_character_motion(CharacterMotionComponent::at_rest(translation.y))])
        .map_err(|error| ProjectAdmissionError::InvalidEntityState(error.to_string()))?;
        let content_entities = scene
            .entities
            .iter()
            .filter_map(|entity| {
                let asset = entity.sprite.as_ref()?.asset.as_str();
                // Treasure containers carry their loot key on the entity
                // (`lootKey`, stamped by generate-project.py from the import
                // sidecar — classic MAPS.BSA dungeon type 2 (Human
                // Stronghold) -> donor dungeon-type array -> "N"); they are
                // content with a position but no actor.
                if let Some(loot_key) = entity.loot_key.as_deref() {
                    return Some(ContentEntity {
                        id: entity.id,
                        name: entity
                            .name
                            .clone()
                            .unwrap_or_else(|| format!("treasure-{}", entity.id)),
                        sprite_asset: asset.to_string(),
                        authored_position: entity.translation,
                        kind: ContentEntityKind::Treasure {
                            loot_key: loot_key.to_string(),
                        },
                    });
                }
                let mobile_id = asset
                    .strip_prefix("texture/enemy-")?
                    .strip_suffix("-atlas")?
                    .parse::<u8>()
                    .ok()?;
                let mobile = crate::mobile::mobile_type(mobile_id)?;
                Some(ContentEntity {
                    id: entity.id,
                    name: entity
                        .name
                        .clone()
                        .unwrap_or_else(|| format!("enemy-{mobile_id}-{}", entity.id)),
                    sprite_asset: asset.to_string(),
                    authored_position: entity.translation,
                    kind: ContentEntityKind::Enemy(EnemyContentReference {
                        mobile_id,
                        mobile_name: mobile.name.to_string(),
                        texture_archive: mobile.texture_archive,
                        flying: mobile.flying,
                    }),
                })
            })
            .collect();

        Ok(Self {
            schema_version: project.schema_version,
            scene_id: scene.id.clone(),
            player: player_id,
            player_start: translation,
            player_controller,
            player_look_state,
            material_voxel_count,
            dungeon_collider,
            dungeon_bounds,
            collision_scene,
            entities,
            content_entities,
        })
    }
}

/// Build the dungeon trimesh collider from a static-mesh asset's inline
/// payload (`f32` positions + `u32` indices → `f64` world-space triangles).
/// Also returns the payload's world-space AABB for bounded sweeps.
fn dungeon_collider_asset(
    asset: &AssetDocument,
    mesh_resource: Option<&[u8]>,
) -> Result<DungeonColliderAdmission, ProjectAdmissionError> {
    let mesh = asset
        .static_mesh
        .as_ref()
        .ok_or(ProjectAdmissionError::MissingDungeonCollider)?;
    if mesh.collision != MeshCollisionPolicy::Trimesh {
        return Err(ProjectAdmissionError::InvalidDungeonCollider(format!(
            "static mesh collision policy must be trimesh, got {:?}",
            mesh.collision
        )));
    }
    let payload = match &mesh.payload.source {
        MeshPayloadSource::Inline { .. } => mesh.payload.clone(),
        MeshPayloadSource::Resource { .. } => decode_mesh_resource_payload(
            &mesh.payload,
            mesh_resource.ok_or_else(|| {
                ProjectAdmissionError::InvalidDungeonCollider(
                    "resource-backed dungeon mesh bytes are missing".to_owned(),
                )
            })?,
        )
        .map_err(|error| ProjectAdmissionError::InvalidDungeonCollider(format!("{error:?}")))?,
        MeshPayloadSource::SharedBuffer { .. } => {
            return Err(ProjectAdmissionError::InvalidDungeonCollider(
                "shared-buffer dungeon meshes are not admitted".to_owned(),
            ))
        }
    };
    let MeshPayloadSource::Inline {
        positions: source_positions,
        indices: source_indices,
        ..
    } = payload.source
    else {
        return Err(ProjectAdmissionError::InvalidDungeonCollider(
            "decoded dungeon mesh is not inline".to_owned(),
        ));
    };
    if source_positions.len() % 3 != 0 || source_indices.len() % 3 != 0 {
        return Err(ProjectAdmissionError::InvalidDungeonCollider(
            "static mesh payload has ragged positions or indices".to_string(),
        ));
    }
    let positions = source_positions
        .as_chunks::<3>()
        .0
        .iter()
        .map(|vertex| [vertex[0] as f64, vertex[1] as f64, vertex[2] as f64])
        .collect::<Vec<_>>();
    let triangles = source_indices
        .as_chunks::<3>()
        .0
        .iter()
        .map(|triangle| [triangle[0], triangle[1], triangle[2]])
        .collect::<Vec<_>>();
    let mut min = [f64::INFINITY; 3];
    let mut max = [f64::NEG_INFINITY; 3];
    for vertex in &positions {
        for axis in 0..3 {
            min[axis] = min[axis].min(vertex[axis]);
            max[axis] = max[axis].max(vertex[axis]);
        }
    }
    let collider = StaticMeshColliderAsset::new(DUNGEON_COLLIDER_ASSET_ID, positions, triangles)
        .map_err(|error| ProjectAdmissionError::InvalidDungeonCollider(format!("{error:?}")))?;
    Ok((collider, (min, max)))
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
struct SceneDocument {
    id: String,
    #[serde(rename = "voxelEnvironment")]
    voxel_environment: Option<VoxelEnvironmentDocument>,
    #[serde(default)]
    entities: Vec<EntityDocument>,
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
struct VoxelEnvironmentDocument {
    voxel_size: f64,
    chunk_size: u32,
    #[serde(default)]
    material_voxels: Vec<MaterialVoxelDocument>,
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
struct MaterialVoxelDocument {
    address: [i64; 3],
    material_slot: u16,
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
struct EntityDocument {
    id: u64,
    name: Option<String>,
    translation: [f32; 3],
    player_controller: Option<PlayerControllerDocument>,
    sprite: Option<SpriteDocument>,
    /// Classic loot table key on treasure container entities (absent on
    /// every other entity kind).
    loot_key: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
struct SpriteDocument {
    asset: String,
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
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
#[serde(crate = "rusty_engine::product_kernel::serde")]
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
        let mut config = PlayerControllerConfig {
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
            engine: Default::default(),
            look: Default::default(),
        };
        config.configure_engine();
        if config.is_valid() {
            Ok(config)
        } else {
            Err(ProjectAdmissionError::InvalidPlayer(
                "playerController contains invalid bounds or duplicate controls".to_string(),
            ))
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
struct ProjectDocument {
    #[serde(rename = "schemaVersion")]
    schema_version: u32,
    #[serde(rename = "entryScene")]
    entry_scene: Option<String>,
    #[serde(default)]
    assets: Vec<AssetDocument>,
    scenes: Vec<SceneDocument>,
}

#[derive(Debug, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
struct AssetDocument {
    id: String,
    #[serde(rename = "staticMesh")]
    static_mesh: Option<StaticMeshAsset>,
}
