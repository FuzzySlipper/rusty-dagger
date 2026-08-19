use std::collections::{BTreeMap, BTreeSet, VecDeque};

use dagger_rpg::{
    action_dynamic_roll_evidence, action_roll_evidence, bind_actor_loot, compile_gameplay_package,
    definition_base_stats, equipped_weapon, loot_roll_evidence, restore_actor_tracks,
    spawn_container, struck_body_part_name, track_maximum, unarmed_damage_range,
    AuthoredGameplayPayload, DaggerContainerState, DaggerDynamicRoll, DaggerEvent, DaggerEvidence,
    DaggerGameplayCatalog, DaggerGameplayError, DaggerGameplayState, DaggerIntent,
    DaggerIntentOrigin, DaggerLootGeneration, DaggerProgressionRecord,
};
use rusty_engine::core_ids::EntityId;
use rusty_engine::core_math::Vec3;
use rusty_engine::engine_spatial::{
    CharacterControllerService, FirstPersonLookState, SpatialCollisionHit, VoxelCollisionScene,
};
use rusty_engine::entity_state::{
    replace_character_motion_state, CharacterMotionComponent, CharacterMotionStateReplacement,
    EntityState, TransformComponent,
};
use rusty_engine::gameplay_resolution::{
    CorrelationId, ResolutionId, ResolutionIdentity, ResolutionMode,
};
use rusty_engine::svc_collision::{
    StaticMeshColliderInstance, StaticMeshInstanceId, StaticMeshTransform,
};
use serde::{Deserialize, Serialize};

use crate::patrol::{EnemyAiMode, PatrolService, PositionUpdate};
use crate::player::{
    apply_player_action, player_view, PlayerControlReceipt, PlayerControllerConfig,
    PlayerControllerState, PlayerError, PlayerFrameReceipt, ResolvedPlayerAction,
    ResolvedPlayerFrame,
};
use crate::project::{
    AdmittedProject, ContentEntity, ContentEntityKind, ProjectAdmissionError, PLAYER_HALF_EXTENTS,
};

#[derive(Debug)]
pub enum RuntimeError {
    Admission(ProjectAdmissionError),
    Gameplay(DaggerGameplayError),
    Player(PlayerError),
    Content(ContentError),
    Encounter(String),
}

#[derive(Debug, Clone, PartialEq)]
pub enum ContentError {
    UnknownEntity(u64),
    NoGroundedApproach(u64),
    NoFocusedTarget,
    NoCombatDefinition(u64),
    TargetDead(u64),
    OutOfRange {
        id: u64,
        distance: f32,
        maximum: f32,
    },
    Occluded(u64),
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
    player_look_state: FirstPersonLookState,
    player_controller_service: CharacterControllerService,
    player_command_sequence: u64,
    player_start: Vec3,
    player_start_look_state: FirstPersonLookState,
    gameplay_catalog: DaggerGameplayCatalog,
    gameplay_payload: AuthoredGameplayPayload,
    gameplay: DaggerGameplayState,
    combat_sequence: u64,
    combat_history: VecDeque<CombatRecord>,
    combat_attempt_sequence: u64,
    combat_attempt_history: VecDeque<CombatAttemptRecord>,
    player_attack_cooldown_remaining: f32,
    player_action_sequence: u64,
    melee_presentation: Option<ActiveMeleePresentation>,
    encounter_sequence: u64,
    encounter_history: VecDeque<EncounterDecisionRecord>,
    enemy_attack_sequences: BTreeMap<u64, u64>,
    enemy_hurt_sequences: BTreeMap<u64, u64>,
    dungeon_bounds: Option<([f64; 3], [f64; 3])>,
    content_entities: Vec<ContentEntity>,
    content_live_positions: BTreeMap<u64, [f32; 3]>,
    focused_content_id: Option<u64>,
    patrol: Option<PatrolService>,
    named_encounters: Vec<NamedEncounter>,
    active_encounter_id: Option<String>,
    active_encounter_outcome: NamedEncounterOutcome,
    active_encounter_engaged: bool,
    equipment_log: VecDeque<EquipmentLogRecord>,
    equipment_log_sequence: u64,
    /// Cursor into the stable carried-equippable ordering: index of the item
    /// the last equip-cycle press applied to (`None` before the first press).
    equip_cycle_cursor: Option<usize>,
    /// Kill-XP award receipts from the progression authority (capped like
    /// combat history).
    progression_history: VecDeque<DaggerProgressionRecord>,
    /// Per-player level-up sequence feeding the salt-5 hp-roll stream; reset
    /// with the play session so hp rolls are deterministic per session.
    player_level_up_sequence: u64,
}

pub const GAMEPLAY_PACKAGE: &[u8] =
    include_bytes!("../../../data/gameplay/dagger-core.package.json");
pub const PLAYER_ACTOR_ID: &str = "player";
pub const MELEE_ACTION_ID: &str = "melee-attack";
pub const COMBAT_HISTORY_LIMIT: usize = 32;
pub const ENCOUNTER_HISTORY_LIMIT: usize = 32;
pub const MELEE_ANTICIPATION_SECONDS: f32 = 0.22;
pub const MELEE_CONTACT_SECONDS: f32 = 0.32;
pub const MELEE_RECOVERY_SECONDS: f32 = 0.72;
pub const MELEE_REJECTION_SECONDS: f32 = 0.72;
const MELEE_AIM_MIN_DOT: f32 = 0.64;
/// Reach of the interact/pickup verb (KeyF) in world units. Classic looting
/// is a click on the adjacent container sprite; 2.5 units covers the
/// jump-to-content standoff (1.5u) with margin for the low corpse piles.
pub const LOOT_INTERACT_REACH: f32 = 2.5;
/// Player level used for loot generation. Loot generates at spawn, when the
/// session player is always level 1 (progression persistence is
/// session-only); if later-generated containers ever appear, the level must
/// be read from the player's live `level` stat at generation time.
const LOOT_GENERATION_LEVEL: i64 = 1;

/// Read-only lab readout: catalog definitions, live state, and resolution
/// explanation. There is no editable document — the committed gameplay
/// package is the only source of gameplay truth.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LabReadout {
    pub gameplay_package: GameplayPackageReadout,
    pub move_speed_units_per_second: f32,
    pub max_health: f32,
    pub current_health: f32,
    pub player_stats: ActorGameplayReadout,
    pub player_position: [f32; 3],
    pub player_yaw_degrees: f32,
    pub combat: Vec<CombatRecord>,
    pub combat_attempts: Vec<CombatAttemptRecord>,
    pub player_attack_cooldown_remaining: f32,
    pub melee_presentation: Option<MeleePresentationReadout>,
    pub encounter_decisions: Vec<EncounterDecisionRecord>,
    pub content: Vec<ContentEntityReadout>,
    pub focused_content_id: Option<u64>,
    pub named_encounters: Vec<NamedEncounterReadout>,
    pub active_encounter: Option<NamedEncounterReadout>,
    /// The player's upstream inventory/equipment view (`InventoryService::view`
    /// plus the EquipmentComponent's slot assignments).
    pub player_inventory: PlayerInventoryReadout,
    /// Ordered equip/unequip/swap receipts from the equip-cycle verb.
    pub equipment_log: Vec<EquipmentLogRecord>,
    /// Live loot containers: treasure piles and loot-bearing enemy corpses,
    /// with contents and the generation receipt from spawn.
    pub loot_containers: Vec<LootContainerReadout>,
    /// Kill-XP progression state: the player's live xp/level, pacing to the
    /// next level, health, and the award history.
    pub progression: ProgressionReadout,
}

/// Read-only view of the kill-XP experiment profile state: live progression
/// stats, pacing against the `xp-level` curve, health, and the award
/// receipts from the progression authority.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProgressionReadout {
    pub xp: i64,
    pub level: i64,
    /// xp remaining until the next `xp-level` threshold (the next level).
    pub xp_to_next_level: i64,
    pub current_health: f32,
    pub max_health: f32,
    /// Ordered kill-XP award receipts (capped like combat history).
    pub history: Vec<DaggerProgressionRecord>,
}

/// Capacity usage for one metric in quarter-kg units (`used` / `maximum`).
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InventoryCapacityReadout {
    pub metric: String,
    pub used: u64,
    pub maximum: Option<u64>,
}

/// One fungible stack in the player's inventory.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InventoryStackReadout {
    pub item: String,
    pub quantity: u64,
}

/// One unique item entity the player carries, with its equip slot if assigned.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InventoryItemReadout {
    pub item: String,
    pub entity: u64,
    pub equip_slot: Option<String>,
}

/// Read-only view of the player's upstream inventory and equipment state.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerInventoryReadout {
    pub capacity: Vec<InventoryCapacityReadout>,
    pub stacks: Vec<InventoryStackReadout>,
    pub items: Vec<InventoryItemReadout>,
}

/// The committed gameplay package's definitions plus its admission
/// fingerprint, served read-only for the lab's definition panels.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GameplayPackageReadout {
    pub fingerprint: String,
    #[serde(flatten)]
    pub payload: AuthoredGameplayPayload,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NamedEncounterReadout {
    pub id: String,
    pub name: String,
    pub objective: String,
    pub route_code: String,
    pub member_entity_ids: Vec<u64>,
    pub status: String,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct NamedEncounterCatalog {
    schema_version: u32,
    encounters: Vec<NamedEncounter>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct NamedEncounter {
    id: String,
    name: String,
    objective: String,
    route_code: String,
    start_entity_id: u64,
    member_entity_ids: Vec<u64>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
enum NamedEncounterOutcome {
    #[default]
    Inactive,
    Active,
    Victory,
    Defeat,
}

/// Read-only view of one live loot container (treasure pile or enemy corpse).
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LootContainerReadout {
    /// Container instance id in the gameplay state (`treasure-<id>` /
    /// `enemy-<id>`).
    pub id: String,
    /// treasure | corpse
    pub kind: &'static str,
    /// The scene content entity this container anchors to.
    pub content_entity_id: u64,
    pub loot_key: String,
    /// Current contents (`InventoryService::view` on the container entity).
    pub contents: PlayerInventoryReadout,
    /// The spawn-time generation receipt, including the per-category
    /// unsupported-coverage notes from the loot-table authority.
    pub generation: DaggerLootGeneration,
    /// True once nothing remains to take.
    pub emptied: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ContentEntityReadout {
    pub id: u64,
    /// enemy | treasure
    pub kind: &'static str,
    pub name: String,
    /// Decoded mobile reference for enemies; `None` for treasure containers.
    pub reference: Option<EnemyReferenceReadout>,
    /// Classic loot table key for treasure containers; `None` for enemies.
    pub loot_key: Option<String>,
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
    pub resources: Option<LiveActorResources>,
    pub ai_state: Option<&'static str>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LiveActorResources {
    pub current_health: f32,
    pub current_stamina: f32,
    pub current_magicka: f32,
}

/// Scenario id of one enemy instance in the live gameplay state.
fn enemy_actor_id(id: u64) -> String {
    format!("enemy-{id}")
}

/// Deterministic spawn roll for one entity's bounded roll evidence: stable
/// across resets so spawned actors are reproducible.
fn spawn_roll(entity_id: u64, evidence_id: &str, min: i64, max: i64) -> i64 {
    let mut value = entity_id
        .wrapping_mul(0x9E37_79B9_7F4A_7C15)
        .wrapping_add(u64::from(u32::from_ne_bytes(
            evidence_id.as_bytes()[..4].try_into().unwrap_or([0; 4]),
        )));
    value ^= value >> 29;
    let span = (max - min + 1) as u64;
    min + (value % span) as i64
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ActorGameplayReadout {
    pub attributes: ActorAttributeReadout,
    pub max_health: f32,
    pub max_stamina: f32,
    pub max_magicka: f32,
    pub current_health: f32,
    pub current_stamina: f32,
    pub current_magicka: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ActorAttributeReadout {
    pub strength: f32,
    pub endurance: f32,
    pub intelligence: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CombatRecord {
    pub sequence: u64,
    pub target_id: u64,
    pub range: f32,
    pub attack_range: f32,
    pub line_of_sight_clear: bool,
    pub action: String,
    pub status: String,
    pub roll: i64,
    pub hit: bool,
    pub damage: i64,
    pub died: bool,
    pub health_before: f32,
    pub health_after: f32,
    pub target_max_health: f32,
    /// Weapon item id the swing resolved with, or "unarmed".
    pub weapon: String,
    /// The body part the struck-part roll selected, when the action reads one.
    pub struck_part: Option<String>,
    /// True when the target's minMetalToHit gated the damage to 0.
    pub material_ineffective: bool,
    pub decisions: Vec<String>,
    pub events: Vec<String>,
}

/// One equipment mutation receipt summary (equip verb history). Both
/// successful mutations and upstream rejections append records; a rejection
/// carries `accepted: false` and the upstream error reason.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EquipmentLogRecord {
    pub sequence: u64,
    /// equip | unequip | swap | grant
    pub operation: String,
    /// Item definition id the mutation applied to.
    pub item: String,
    pub slots: Vec<String>,
    /// For a swap, the item definition id it replaced.
    pub replaced_item: Option<String>,
    /// Stack size for fungible grants.
    pub quantity: Option<u64>,
    pub accepted: bool,
    /// Upstream rejection reason when the mutation was refused.
    pub reason: Option<String>,
    /// Committed component revision after the mutation (equipment revision,
    /// or the inventory revision for grants); absent on rejection.
    pub committed_revision: Option<u64>,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CombatAttemptRecord {
    pub sequence: u64,
    pub target_id: Option<u64>,
    pub accepted: bool,
    pub outcome: String,
    pub cooldown_before: f32,
    pub cooldown_after: f32,
    pub cooldown_duration: f32,
    pub stamina_before: f32,
    pub stamina_cost: f32,
    pub stamina_after: f32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum MeleePresentationPhase {
    Anticipation,
    Contact,
    Recovery,
    Rejected,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MeleePresentationReadout {
    pub attempt_sequence: u64,
    pub phase: MeleePresentationPhase,
    pub phase_progress: f32,
    pub accepted: bool,
    pub outcome: String,
    pub target_id: Option<u64>,
    pub stamina_before: f32,
    pub stamina_after: f32,
    pub target_health_before: Option<f32>,
    pub target_health_after: Option<f32>,
    pub target_max_health: Option<f32>,
    pub final_damage: Option<f32>,
    pub died: bool,
}

#[derive(Debug, Clone, PartialEq)]
struct ActiveMeleePresentation {
    attempt_sequence: u64,
    elapsed: f32,
    accepted: bool,
    outcome: String,
    target_id: Option<u64>,
    stamina_before: f32,
    stamina_after: f32,
    target_health_before: Option<f32>,
    target_health_after: Option<f32>,
    target_max_health: Option<f32>,
    final_damage: Option<f32>,
    died: bool,
    contact_resolved: bool,
}

impl ActiveMeleePresentation {
    fn readout(&self) -> MeleePresentationReadout {
        let (phase, phase_progress) = if !self.accepted {
            (
                MeleePresentationPhase::Rejected,
                normalized_progress(self.elapsed, MELEE_REJECTION_SECONDS),
            )
        } else if self.elapsed < MELEE_ANTICIPATION_SECONDS {
            (
                MeleePresentationPhase::Anticipation,
                normalized_progress(self.elapsed, MELEE_ANTICIPATION_SECONDS),
            )
        } else if self.elapsed < MELEE_ANTICIPATION_SECONDS + MELEE_CONTACT_SECONDS {
            (
                MeleePresentationPhase::Contact,
                normalized_progress(
                    self.elapsed - MELEE_ANTICIPATION_SECONDS,
                    MELEE_CONTACT_SECONDS,
                ),
            )
        } else {
            (
                MeleePresentationPhase::Recovery,
                normalized_progress(
                    self.elapsed - MELEE_ANTICIPATION_SECONDS - MELEE_CONTACT_SECONDS,
                    MELEE_RECOVERY_SECONDS,
                ),
            )
        };
        MeleePresentationReadout {
            attempt_sequence: self.attempt_sequence,
            phase,
            phase_progress,
            accepted: self.accepted,
            outcome: self.outcome.clone(),
            target_id: self.target_id,
            stamina_before: self.stamina_before,
            stamina_after: self.stamina_after,
            target_health_before: self.target_health_before,
            target_health_after: self.target_health_after,
            target_max_health: self.target_max_health,
            final_damage: self.final_damage,
            died: self.died,
        }
    }

    fn is_complete(&self) -> bool {
        let duration = if self.accepted {
            MELEE_ANTICIPATION_SECONDS + MELEE_CONTACT_SECONDS + MELEE_RECOVERY_SECONDS
        } else {
            MELEE_REJECTION_SECONDS
        };
        self.elapsed >= duration
    }
}

fn normalized_progress(elapsed: f32, duration: f32) -> f32 {
    (elapsed / duration).clamp(0.0, 1.0)
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EncounterDecisionRecord {
    pub sequence: u64,
    pub enemy_id: u64,
    pub enemy_name: String,
    pub decision: String,
    pub from: Option<String>,
    pub to: Option<String>,
    pub distance_to_player: f32,
    pub damage: Option<f32>,
    pub line_of_sight_clear: Option<bool>,
    pub player_health_before: Option<f32>,
    pub player_health_after: Option<f32>,
    pub player_died: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct EnemyPresentationReadout {
    pub handle: u32,
    pub attack_sequence: u64,
    pub hurt_sequence: u64,
    pub dead: bool,
}

impl DaggerRuntime {
    pub fn from_project_json(document: &str) -> Result<Self, RuntimeError> {
        let admitted = AdmittedProject::from_json(document).map_err(RuntimeError::Admission)?;
        Self::from_admitted_project(admitted)
    }

    pub fn from_admitted_project(admitted: AdmittedProject) -> Result<Self, RuntimeError> {
        Self::from_admitted_project_with_gameplay_package(admitted, GAMEPLAY_PACKAGE)
    }

    /// Admission with an explicit gameplay package. The committed package is
    /// the product default; diagnostics and pacing proofs inject a mutated
    /// package through this one seam rather than forking admission.
    pub fn from_admitted_project_with_gameplay_package(
        admitted: AdmittedProject,
        gameplay_package: &[u8],
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
        let player_start = admitted.player_start;
        let player_start_look_state = admitted.player_look_state;
        let gameplay_catalog =
            compile_gameplay_package(gameplay_package).map_err(RuntimeError::Gameplay)?;
        let gameplay_payload: AuthoredGameplayPayload = {
            let package = rusty_engine::gameplay_rules::decode_rule_package(gameplay_package)
                .map_err(|error| {
                    RuntimeError::Gameplay(DaggerGameplayError::Package(error.to_string()))
                })?;
            serde_json::from_value(package.payload().clone()).map_err(|error| {
                RuntimeError::Gameplay(DaggerGameplayError::Payload(error.to_string()))
            })?
        };
        let mut player_controller = admitted.player_controller;
        player_controller.move_speed_units_per_second = gameplay_catalog
            .actors()
            .get(PLAYER_ACTOR_ID)
            .and_then(|player| player.move_speed)
            .ok_or_else(|| {
                RuntimeError::Gameplay(DaggerGameplayError::InvalidValue {
                    path: "actors[player].moveSpeed".to_string(),
                    reason: "player actor must declare a movement speed".to_string(),
                })
            })?;
        player_controller.configure_engine();
        let gameplay = spawn_live_actors(&gameplay_catalog, &admitted.content_entities)?;
        let enemy_presentation_sequences = admitted
            .content_entities
            .iter()
            .map(|entity| (entity.id, 0_u64))
            .collect::<BTreeMap<_, _>>();
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
            player_look_state: admitted.player_look_state,
            player_controller_service: CharacterControllerService::default(),
            player_command_sequence: 0,
            player_start,
            player_start_look_state,
            gameplay_catalog,
            gameplay_payload,
            gameplay,
            combat_sequence: 0,
            player_action_sequence: 0,
            combat_history: VecDeque::new(),
            combat_attempt_sequence: 0,
            combat_attempt_history: VecDeque::new(),
            player_attack_cooldown_remaining: 0.0,
            melee_presentation: None,
            encounter_sequence: 0,
            encounter_history: VecDeque::new(),
            enemy_attack_sequences: enemy_presentation_sequences.clone(),
            enemy_hurt_sequences: enemy_presentation_sequences,
            dungeon_bounds: admitted.dungeon_bounds,
            content_entities: admitted.content_entities,
            content_live_positions,
            focused_content_id: None,
            patrol: None,
            named_encounters: Vec::new(),
            active_encounter_id: None,
            active_encounter_outcome: NamedEncounterOutcome::Inactive,
            active_encounter_engaged: false,
            equipment_log: VecDeque::new(),
            equipment_log_sequence: 0,
            equip_cycle_cursor: None,
            progression_history: VecDeque::new(),
            player_level_up_sequence: 0,
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
        PlayerControllerState::from_look_state(self.player_look_state)
    }

    /// Install the committed navigation artifact as the live enemy movement
    /// authority. The runtime owns the resulting service; native diagnostics
    /// only project its positions.
    pub fn install_encounter_navigation_json(
        &mut self,
        navgrid_document: &str,
    ) -> Result<(), RuntimeError> {
        #[derive(serde::Deserialize)]
        #[serde(rename_all = "camelCase")]
        struct EncounterNavGrid {
            cells: Vec<(f64, f64, f64, f64)>,
        }
        let navgrid: EncounterNavGrid =
            serde_json::from_str(navgrid_document).map_err(|error| {
                RuntimeError::Encounter(format!("invalid encounter navgrid: {error}"))
            })?;
        // Only enemies patrol; treasure containers are static anchors. Their
        // authored positions are re-merged below so the aimed queries and the
        // lab keep seeing them.
        let spawns = self
            .content_entities
            .iter()
            .filter(|entity| entity.enemy().is_some())
            .map(|entity| {
                let handle = u32::try_from(entity.id).map_err(|_| {
                    RuntimeError::Encounter(format!(
                        "content entity {} cannot be used as a patrol handle",
                        entity.id
                    ))
                })?;
                Ok((handle, entity.authored_position))
            })
            .collect::<Result<Vec<_>, RuntimeError>>()?;
        let patrol = PatrolService::new(&navgrid.cells, &spawns);
        patrol.validate().map_err(RuntimeError::Encounter)?;
        self.content_live_positions = patrol
            .positions()
            .into_iter()
            .map(|(handle, position, _, _)| (u64::from(handle), position))
            .chain(
                self.content_entities
                    .iter()
                    .filter(|entity| entity.enemy().is_none())
                    .map(|entity| (entity.id, entity.authored_position)),
            )
            .collect();
        self.patrol = Some(patrol);
        Ok(())
    }

    pub fn install_named_encounters_json(&mut self, document: &str) -> Result<(), RuntimeError> {
        let catalog: NamedEncounterCatalog = serde_json::from_str(document).map_err(|error| {
            RuntimeError::Encounter(format!("invalid named encounters: {error}"))
        })?;
        if catalog.schema_version != 1 {
            return Err(RuntimeError::Encounter(format!(
                "unsupported named encounter schema {}",
                catalog.schema_version
            )));
        }
        let mut ids = BTreeSet::new();
        let mut routes = BTreeSet::new();
        for encounter in &catalog.encounters {
            if encounter.id.trim().is_empty()
                || encounter.name.trim().is_empty()
                || encounter.objective.trim().is_empty()
            {
                return Err(RuntimeError::Encounter(
                    "named encounter id, name, and objective must be non-empty".to_string(),
                ));
            }
            if !ids.insert(encounter.id.as_str()) || !routes.insert(encounter.route_code.as_str()) {
                return Err(RuntimeError::Encounter(format!(
                    "duplicate named encounter id or route for {}",
                    encounter.id
                )));
            }
            if encounter.member_entity_ids.is_empty()
                || !encounter
                    .member_entity_ids
                    .contains(&encounter.start_entity_id)
            {
                return Err(RuntimeError::Encounter(format!(
                    "named encounter {} must include its start entity",
                    encounter.id
                )));
            }
            for member in &encounter.member_entity_ids {
                if !self
                    .content_entities
                    .iter()
                    .any(|entity| entity.id == *member)
                {
                    return Err(RuntimeError::Encounter(format!(
                        "named encounter {} references unsupported enemy {}",
                        encounter.id, member
                    )));
                }
            }
        }
        self.named_encounters = catalog.encounters;
        self.active_encounter_id = None;
        self.active_encounter_outcome = NamedEncounterOutcome::Inactive;
        self.active_encounter_engaged = false;
        Ok(())
    }

    pub fn start_named_encounter(&mut self, id: &str) -> Result<LabReadout, RuntimeError> {
        let encounter = self
            .named_encounters
            .iter()
            .find(|encounter| encounter.id == id)
            .cloned()
            .ok_or_else(|| RuntimeError::Encounter(format!("unknown named encounter {id}")))?;
        self.jump_to_content(encounter.start_entity_id)?;
        self.active_encounter_id = Some(encounter.id);
        self.active_encounter_outcome = NamedEncounterOutcome::Active;
        self.active_encounter_engaged = false;
        self.lab_readout()
    }

    pub fn route_named_encounter(&mut self, route_code: &str) -> Result<bool, RuntimeError> {
        let id = self
            .named_encounters
            .iter()
            .find(|encounter| encounter.route_code == route_code)
            .map(|encounter| encounter.id.clone());
        let Some(id) = id else {
            return Ok(false);
        };
        self.start_named_encounter(&id)?;
        Ok(true)
    }

    pub fn tick_play_session(&mut self, dt: f32) -> Result<Vec<PositionUpdate>, RuntimeError> {
        if !dt.is_finite() || !(0.0..=0.25).contains(&dt) {
            return Err(RuntimeError::Encounter(
                "play-session tick must be finite and bounded to 0.25 seconds".to_string(),
            ));
        }
        if self.active_encounter_id.is_some() && !self.active_encounter_engaged {
            return Ok(Vec::new());
        }
        self.player_attack_cooldown_remaining =
            (self.player_attack_cooldown_remaining - dt).max(0.0);
        let resolve_contact = self
            .melee_presentation
            .as_ref()
            .is_some_and(|presentation| {
                presentation.accepted
                    && !presentation.contact_resolved
                    && presentation.elapsed < MELEE_ANTICIPATION_SECONDS
                    && presentation.elapsed + dt >= MELEE_ANTICIPATION_SECONDS
            });
        if let Some(presentation) = &mut self.melee_presentation {
            presentation.elapsed += dt;
        }
        if resolve_contact {
            self.resolve_melee_contact()?;
        }
        if self
            .melee_presentation
            .as_ref()
            .is_some_and(ActiveMeleePresentation::is_complete)
        {
            self.melee_presentation = None;
        }
        let player = self.player_position()?;
        // Behavior tuning and the attempted action both come from the
        // admitted gameplay catalog; the action resolves through the shared
        // policy.
        let behaviors = self
            .content_entities
            .iter()
            .filter_map(|entity| {
                let reference = entity.enemy()?;
                let behavior = self
                    .gameplay_catalog
                    .actors()
                    .values()
                    .find(|actor| actor.mobile_id == Some(reference.mobile_id))
                    .and_then(|actor| actor.behavior.as_ref())?;
                u32::try_from(entity.id).ok().map(|handle| {
                    (
                        handle,
                        crate::patrol::EncounterBehavior {
                            detection_range: behavior.detection_range,
                            patrol_speed: behavior.patrol_speed,
                            chase_speed: behavior.chase_speed,
                            attack_range: behavior.attack_range,
                            attack_cooldown_seconds: behavior.attack_cooldown_seconds,
                            action: behavior.action.clone(),
                        },
                    )
                })
            })
            .collect::<BTreeMap<_, _>>();
        let dead = self
            .content_entities
            .iter()
            .filter_map(|entity| {
                self.is_enemy_dead(entity.id)
                    .then(|| u32::try_from(entity.id).ok())
                    .flatten()
            })
            .collect::<std::collections::BTreeSet<_>>();
        let player_alive = self.player_health() > 0.0;
        let Some(patrol) = self.patrol.as_mut() else {
            return Ok(Vec::new());
        };
        let player_target = if player_alive {
            [player.x, player.y, player.z]
        } else {
            [f32::INFINITY; 3]
        };
        let evaluation = patrol.evaluate_encounters(dt, player_target, &behaviors, &dead);
        for update in &evaluation.positions {
            self.content_live_positions
                .insert(u64::from(update.handle), update.translation);
        }
        for decision in evaluation.decisions {
            let id = u64::from(decision.handle);
            self.push_encounter_record(EncounterDecisionRecord {
                sequence: 0,
                enemy_id: id,
                enemy_name: self.enemy_name(id),
                decision: "state changed".to_string(),
                from: Some(ai_mode_name(decision.from).to_string()),
                to: Some(ai_mode_name(decision.to).to_string()),
                distance_to_player: decision.distance_to_player,
                damage: None,
                line_of_sight_clear: None,
                player_health_before: None,
                player_health_after: None,
                player_died: false,
            });
        }
        for attack in evaluation.attacks {
            let id = u64::from(attack.handle);
            let before = self.player_health();
            if before <= 0.0 {
                continue;
            }
            let line_of_sight_clear = self.enemy_line_of_sight_clear(id, player);
            let (damage, after) = if line_of_sight_clear {
                // AI attack intents resolve through the same authored action
                // policy as the player's attempts; damage is never flat.
                // Rolls draw from this enemy's own attack stream.
                let roll_sequence = {
                    let sequence = self.enemy_attack_sequences.entry(id).or_insert(0);
                    *sequence = sequence.saturating_add(1);
                    *sequence
                };
                let attempt = self.resolve_action_attempt(
                    &attack.action,
                    &enemy_actor_id(id),
                    PLAYER_ACTOR_ID,
                    DaggerIntentOrigin::Ai,
                    id,
                    roll_sequence,
                )?;
                let after = self.player_health();
                (Some(attempt.damage as f32), Some(after))
            } else {
                (Some(0.0), Some(before))
            };
            self.push_encounter_record(EncounterDecisionRecord {
                sequence: 0,
                enemy_id: id,
                enemy_name: self.enemy_name(id),
                decision: if line_of_sight_clear {
                    "melee attack"
                } else {
                    "attack blocked"
                }
                .to_string(),
                from: None,
                to: None,
                distance_to_player: attack.distance_to_player,
                damage,
                line_of_sight_clear: Some(line_of_sight_clear),
                player_health_before: Some(before),
                player_health_after: after,
                player_died: before > 0.0 && after.is_some_and(|after| after <= 0.0),
            });
        }
        self.refresh_named_encounter_outcome();
        Ok(evaluation.positions)
    }

    fn refresh_named_encounter_outcome(&mut self) {
        if self.active_encounter_outcome != NamedEncounterOutcome::Active {
            return;
        }
        if self.player_health() <= 0.0 {
            self.active_encounter_outcome = NamedEncounterOutcome::Defeat;
            return;
        }
        let Some(encounter) = self
            .named_encounters
            .iter()
            .find(|encounter| self.active_encounter_id.as_deref() == Some(encounter.id.as_str()))
        else {
            return;
        };
        if encounter
            .member_entity_ids
            .iter()
            .all(|id| self.is_enemy_dead(*id))
        {
            self.active_encounter_outcome = NamedEncounterOutcome::Victory;
        }
    }

    fn enemy_name(&self, id: u64) -> String {
        self.content_entities
            .iter()
            .find(|entity| entity.id == id)
            .map_or_else(|| format!("Enemy {id}"), |entity| entity.name.clone())
    }

    fn enemy_line_of_sight_clear(&self, id: u64, player: Vec3) -> bool {
        let Some(enemy) = self.content_live_positions.get(&id) else {
            return false;
        };
        // Classic sprites do not provide per-mobile controller dimensions.
        // Encounter sensing has already established a shared nav level, so
        // cast between actor centers at the player's stable body height.
        let origin = [enemy[0], player.y, enemy[2]];
        let delta = [player.x - origin[0], player.z - origin[2]];
        let distance = delta[0].hypot(delta[1]);
        if distance <= 0.4 {
            return true;
        }
        let direction = [
            f64::from(delta[0] / distance),
            0.0,
            f64::from(delta[1] / distance),
        ];
        let clear_distance = f64::from((distance - 0.35).max(0.0));
        !self
            .collision_scene
            .raycast_world(origin.map(f64::from), direction, clear_distance)
            .is_some_and(|hit| collision_hit_distance(hit) + 0.05 < clear_distance)
    }

    fn push_encounter_record(&mut self, mut record: EncounterDecisionRecord) {
        self.encounter_sequence = self.encounter_sequence.saturating_add(1);
        record.sequence = self.encounter_sequence;
        self.encounter_history.push_back(record);
        while self.encounter_history.len() > ENCOUNTER_HISTORY_LIMIT {
            self.encounter_history.pop_front();
        }
    }

    pub fn encounter_positions(&self) -> Vec<(u32, [f32; 3], f32, bool)> {
        self.patrol
            .as_ref()
            .map_or_else(Vec::new, PatrolService::positions)
    }

    pub fn encounter_sequence(&self) -> u64 {
        self.encounter_sequence
    }

    pub fn player_attack_cooldown_remaining(&self) -> f32 {
        self.player_attack_cooldown_remaining
    }

    pub fn melee_presentation(&self) -> Option<MeleePresentationReadout> {
        self.melee_presentation
            .as_ref()
            .map(ActiveMeleePresentation::readout)
    }

    /// One required field of the wired melee action (reach, cooldown).
    fn melee_action_field(
        &self,
        field: impl Fn(&dagger_rpg::DaggerActionDefinition) -> Option<f32>,
    ) -> f32 {
        self.gameplay_catalog
            .actions()
            .get(MELEE_ACTION_ID)
            .and_then(field)
            .unwrap_or_else(|| panic!("{MELEE_ACTION_ID} must declare this field"))
    }

    pub fn player_stamina(&self) -> (f32, f32) {
        (
            self.live_track(PLAYER_ACTOR_ID, "stamina"),
            self.live_track_max(PLAYER_ACTOR_ID, "stamina"),
        )
    }

    fn live_track(&self, actor: &str, track: &str) -> f32 {
        self.gameplay.track_value(actor, track).unwrap_or(0) as f32
    }

    fn live_track_max(&self, actor: &str, track: &str) -> f32 {
        track_maximum(&self.gameplay, &self.gameplay_catalog, actor, track).unwrap_or(0) as f32
    }

    fn player_health(&self) -> f32 {
        self.live_track(PLAYER_ACTOR_ID, "health")
    }

    fn enemy_health(&self, id: u64) -> f32 {
        self.live_track(&enemy_actor_id(id), "health")
    }

    fn has_live_actor(&self, id: u64) -> bool {
        self.gameplay.actor(&enemy_actor_id(id)).is_some()
    }

    fn is_enemy_dead(&self, id: u64) -> bool {
        self.has_live_actor(id) && self.enemy_health(id) <= 0.0
    }

    fn live_resources(&self, actor: &str) -> LiveActorResources {
        LiveActorResources {
            current_health: self.live_track(actor, "health"),
            current_stamina: self.live_track(actor, "stamina"),
            current_magicka: self.live_track(actor, "magicka"),
        }
    }

    /// Player stats panel: live values from the mechanics binding and
    /// attribute values from the admitted player definition.
    fn player_gameplay_readout(&self) -> ActorGameplayReadout {
        let definition = self
            .gameplay_catalog
            .actors()
            .get(PLAYER_ACTOR_ID)
            .expect("player actor definition");
        let stat = |id: &str| definition.stats.get(id).copied().unwrap_or(0) as f32;
        ActorGameplayReadout {
            attributes: ActorAttributeReadout {
                strength: stat("strength"),
                endurance: stat("endurance"),
                intelligence: stat("intelligence"),
            },
            max_health: self.live_track_max(PLAYER_ACTOR_ID, "health"),
            max_stamina: self.live_track_max(PLAYER_ACTOR_ID, "stamina"),
            max_magicka: self.live_track_max(PLAYER_ACTOR_ID, "magicka"),
            current_health: self.live_track(PLAYER_ACTOR_ID, "health"),
            current_stamina: self.live_track(PLAYER_ACTOR_ID, "stamina"),
            current_magicka: self.live_track(PLAYER_ACTOR_ID, "magicka"),
        }
    }

    /// Session-scoped progression reset: spawn xp/level/health-max bases
    /// through the progression authority, plus the runtime-side history and
    /// level-up roll sequence. Called by `reset_play_session` BEFORE track
    /// restoration so health restores to the spawn maximum.
    fn reset_progression(&mut self) -> Result<(), RuntimeError> {
        dagger_rpg::reset_actor_progression(
            &mut self.gameplay,
            &self.gameplay_catalog,
            PLAYER_ACTOR_ID,
        )
        .map_err(RuntimeError::Gameplay)?;
        self.progression_history.clear();
        self.player_level_up_sequence = 0;
        Ok(())
    }

    fn restore_live_actors(&mut self) -> Result<(), RuntimeError> {
        restore_actor_tracks(&mut self.gameplay, &self.gameplay_catalog, PLAYER_ACTOR_ID)
            .map_err(RuntimeError::Gameplay)?;
        let enemy_ids = self
            .content_entities
            .iter()
            .map(|entity| entity.id)
            .filter(|id| self.has_live_actor(*id))
            .collect::<Vec<_>>();
        for id in enemy_ids {
            restore_actor_tracks(
                &mut self.gameplay,
                &self.gameplay_catalog,
                &enemy_actor_id(id),
            )
            .map_err(RuntimeError::Gameplay)?;
        }
        Ok(())
    }

    pub fn dead_encounter_ids(&self) -> BTreeSet<u32> {
        self.content_entities
            .iter()
            .filter_map(|entity| {
                self.is_enemy_dead(entity.id)
                    .then(|| u32::try_from(entity.id).ok())
                    .flatten()
            })
            .collect()
    }

    pub fn enemy_presentation(&self) -> Vec<EnemyPresentationReadout> {
        self.content_entities
            .iter()
            .filter(|entity| self.has_live_actor(entity.id))
            .filter_map(|entity| {
                let id = entity.id;
                Some(EnemyPresentationReadout {
                    handle: u32::try_from(id).ok()?,
                    attack_sequence: self.enemy_attack_sequences.get(&id).copied().unwrap_or(0),
                    hurt_sequence: self.enemy_hurt_sequences.get(&id).copied().unwrap_or(0),
                    dead: self.is_enemy_dead(id),
                })
            })
            .collect()
    }

    fn reset_enemy_presentation_sequences(&mut self) {
        for sequence in self.enemy_attack_sequences.values_mut() {
            *sequence = 0;
        }
        for sequence in self.enemy_hurt_sequences.values_mut() {
            *sequence = 0;
        }
    }

    /// Reset the playable run to the committed Privateer's Hold start,
    /// restoring catalog spawn state. Progression is session-scoped: the
    /// reset also restores the player's spawn xp/level/health-max bases
    /// (before track restoration reads them), clears the award history, and
    /// restarts the level-up roll sequence so a retried session reproduces
    /// the same hp rolls. The lab jump verb only heals — it preserves
    /// progression within a session.
    pub fn reset_play_session(&mut self) -> Result<LabReadout, RuntimeError> {
        self.reset_progression()?;
        if let Some(active) = self.active_encounter_id.clone() {
            return self.start_named_encounter(&active);
        }
        self.set_player_position(self.player_start)?;
        self.player_look_state = self.player_start_look_state;
        self.restore_live_actors()?;
        self.combat_sequence = 0;
        self.player_action_sequence = 0;
        self.combat_history.clear();
        self.combat_attempt_sequence = 0;
        self.combat_attempt_history.clear();
        self.player_attack_cooldown_remaining = 0.0;
        self.melee_presentation = None;
        self.encounter_sequence = 0;
        self.encounter_history.clear();
        self.reset_enemy_presentation_sequences();
        self.equipment_log.clear();
        self.equipment_log_sequence = 0;
        self.equip_cycle_cursor = None;
        if let Some(patrol) = self.patrol.as_mut() {
            patrol.reset();
            self.content_live_positions = patrol
                .positions()
                .into_iter()
                .map(|(handle, position, _, _)| (u64::from(handle), position))
                .chain(
                    self.content_entities
                        .iter()
                        .filter(|entity| entity.enemy().is_none())
                        .map(|entity| (entity.id, entity.authored_position)),
                )
                .collect();
        }
        self.focused_content_id = None;
        self.lab_readout()
    }

    /// Reset the player beside one admitted live content entity and face it.
    /// The collision scene chooses the floor; Angular never supplies a raw
    /// teleport coordinate.
    pub fn jump_to_content(&mut self, id: u64) -> Result<LabReadout, RuntimeError> {
        let target = self
            .content_live_positions
            .get(&id)
            .copied()
            .ok_or(RuntimeError::Content(ContentError::UnknownEntity(id)))?;
        let original_position = self.player_position()?;
        let original_look_state = self.player_look_state;
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
            let mut facing_state = self.player_start_look_state;
            let delta_x = target[0] - position.x;
            let delta_z = target[2] - position.z;
            facing_state.yaw_radians = delta_x.atan2(-delta_z);
            let horizontal_distance = delta_x.hypot(delta_z).max(0.01);
            let camera_y = position.y + 0.75;
            let target_visual_center_y = target[1] + 0.75;
            facing_state.pitch_radians =
                (target_visual_center_y - camera_y).atan2(horizontal_distance);
            self.player_look_state = facing_state;

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
                self.player_look_state = facing_state;
                self.apply_player_action(action).is_ok_and(|_| {
                    self.player_position().is_ok_and(|after| {
                        (after.x - position.x).hypot(after.z - position.z) > 0.01
                    })
                })
            });
            if navigable && self.enemy_line_of_sight_clear(id, position) {
                self.set_player_position(position)?;
                self.player_look_state = facing_state;
                self.restore_live_actors()?;
                self.combat_sequence = 0;
                self.player_action_sequence = 0;
                self.combat_history.clear();
                self.combat_attempt_sequence = 0;
                self.combat_attempt_history.clear();
                self.player_attack_cooldown_remaining = 0.0;
                self.melee_presentation = None;
                self.encounter_sequence = 0;
                self.encounter_history.clear();
                self.reset_enemy_presentation_sequences();
                self.active_encounter_id = None;
                self.active_encounter_outcome = NamedEncounterOutcome::Inactive;
                self.active_encounter_engaged = false;
                self.focused_content_id = Some(id);
                return self.lab_readout();
            }
        }
        self.set_player_position(original_position)?;
        self.player_look_state = original_look_state;
        self.focused_content_id = original_focus;
        Err(RuntimeError::Content(ContentError::NoGroundedApproach(id)))
    }

    /// Start a first-person attack from a physical input edge. A target is not
    /// required to begin the swing; Dagger resolves the aimed contact only
    /// when the Rust-owned action reaches its contact frame.
    pub fn attack_focused_target(&mut self) -> Result<LabReadout, RuntimeError> {
        if self.active_encounter_id.is_some() {
            self.active_encounter_engaged = true;
        }
        let cooldown_before = self.player_attack_cooldown_remaining;
        let stamina_before = self.live_track(PLAYER_ACTOR_ID, "stamina");
        let cooldown_duration = self.melee_action_field(|action| action.cooldown_seconds);
        if cooldown_before > 0.0 {
            self.push_combat_attempt(CombatAttemptRecord {
                sequence: 0,
                target_id: None,
                accepted: false,
                outcome: "cooldown".to_string(),
                cooldown_before,
                cooldown_after: cooldown_before,
                cooldown_duration,
                stamina_before,
                stamina_cost: 0.0,
                stamina_after: stamina_before,
            });
            return self.lab_readout();
        }
        // The authored melee action owns its stamina cost and spends it
        // during resolution; scheduling here only gates the swing cooldown.
        // The attempt record's stamina cost/after update when the contact
        // resolves with the actual spend.
        self.player_attack_cooldown_remaining = cooldown_duration;
        let attempt_sequence = self.push_combat_attempt(CombatAttemptRecord {
            sequence: 0,
            target_id: None,
            accepted: true,
            outcome: "swinging".to_string(),
            cooldown_before,
            cooldown_after: cooldown_duration,
            cooldown_duration,
            stamina_before,
            stamina_cost: 0.0,
            stamina_after: stamina_before,
        });
        self.melee_presentation = Some(ActiveMeleePresentation {
            attempt_sequence,
            elapsed: 0.0,
            accepted: true,
            outcome: "swinging".to_string(),
            target_id: None,
            stamina_before,
            stamina_after: stamina_before,
            target_health_before: None,
            target_health_after: None,
            target_max_health: None,
            final_damage: None,
            died: false,
            contact_resolved: false,
        });
        self.lab_readout()
    }

    fn resolve_melee_contact(&mut self) -> Result<(), RuntimeError> {
        let attempt_sequence = self
            .melee_presentation
            .as_ref()
            .map(|action| action.attempt_sequence)
            .ok_or_else(|| RuntimeError::Encounter("melee contact has no active action".into()))?;
        let attack_range = self.melee_action_field(|action| action.reach);
        let player_position = self.player_position()?;
        let live_enemy_resources = self
            .content_entities
            .iter()
            .filter(|entity| self.has_live_actor(entity.id))
            .map(|entity| (entity.id, self.live_resources(&enemy_actor_id(entity.id))))
            .collect::<BTreeMap<_, _>>();
        let target = select_aimed_melee_target(
            player_position,
            self.player_state().yaw_degrees,
            attack_range,
            &self.content_entities,
            &self.content_live_positions,
            &live_enemy_resources,
        );
        let Some(id) = target else {
            self.finish_melee_contact(attempt_sequence, None, "miss", None);
            return Ok(());
        };
        let target_position = self
            .content_live_positions
            .get(&id)
            .copied()
            .ok_or(RuntimeError::Content(ContentError::UnknownEntity(id)))?;
        let delta = [
            target_position[0] - player_position.x,
            target_position[1] - player_position.y,
            target_position[2] - player_position.z,
        ];
        let distance = delta[0].hypot(delta[2]);
        if distance > 0.4 {
            let direction = [
                f64::from(delta[0] / distance),
                0.0,
                f64::from(delta[2] / distance),
            ];
            let origin = [
                f64::from(player_position.x),
                f64::from(player_position.y),
                f64::from(player_position.z),
            ];
            let clear_distance = f64::from((distance - 0.35).max(0.0));
            if self
                .collision_scene
                .raycast_world(origin, direction, clear_distance)
                .is_some_and(|hit| collision_hit_distance(hit) + 0.05 < clear_distance)
            {
                self.finish_melee_contact(attempt_sequence, None, "miss", None);
                return Ok(());
            }
        }
        let health_before = self.enemy_health(id);
        let target_max_health = self.live_track_max(&enemy_actor_id(id), "health");
        // The player's melee contact resolves through the same authored
        // action policy as AI attacks; range, aim, and line of sight above
        // remain runtime world facts. Rolls draw from the player-action
        // stream so outcomes are deterministic per swing.
        self.player_action_sequence = self.player_action_sequence.saturating_add(1);
        let roll_sequence = self.player_action_sequence;
        let attempt = self.resolve_action_attempt(
            "melee-attack",
            PLAYER_ACTOR_ID,
            &enemy_actor_id(id),
            DaggerIntentOrigin::Player,
            id,
            roll_sequence,
        )?;
        let health_after = self.enemy_health(id);
        let hit = attempt.damage > 0;
        let died = health_before > 0.0 && health_after <= 0.0;
        if died {
            // A player-origin resolution left the enemy dead: award kill-XP
            // through the progression authority.
            self.award_kill_progression_for(id)?;
        }
        if hit {
            if let Some(sequence) = self.enemy_hurt_sequences.get_mut(&id) {
                *sequence = sequence.saturating_add(1);
            }
        }
        let outcome = if !attempt.succeeded {
            "rejected"
        } else if died {
            "killed"
        } else if hit {
            "hit"
        } else if attempt.material_ineffective {
            "ineffective"
        } else {
            "miss"
        };
        self.focused_content_id = Some(id);
        // Combat records sequence contiguously within the history; the
        // shared combat_sequence also advances on enemy resolutions and is
        // only a resolution identity, not a display sequence.
        let record_sequence = self
            .combat_history
            .back()
            .map_or(1, |record| record.sequence.saturating_add(1));
        self.combat_history.push_back(CombatRecord {
            sequence: record_sequence,
            target_id: id,
            range: distance,
            attack_range,
            line_of_sight_clear: true,
            action: "melee-attack".to_string(),
            status: attempt.status.clone(),
            roll: attempt.roll,
            hit,
            damage: attempt.damage,
            died,
            health_before,
            health_after,
            target_max_health,
            weapon: attempt.weapon.clone(),
            struck_part: attempt.struck_part.clone(),
            material_ineffective: attempt.material_ineffective,
            decisions: attempt.decisions.clone(),
            events: attempt.events.clone(),
        });
        while self.combat_history.len() > COMBAT_HISTORY_LIMIT {
            self.combat_history.pop_front();
        }
        self.finish_melee_contact(
            attempt_sequence,
            Some(id),
            outcome,
            Some(MeleeContactResult {
                damage: attempt.damage,
                stamina_spent: attempt.stamina_spent,
                stamina_after: self.live_track(PLAYER_ACTOR_ID, "stamina"),
                health_before,
                health_after,
                target_max_health,
                died,
            }),
        );
        Ok(())
    }

    /// Resolve one authored action attempt through the shared policy with
    /// deterministic combat rolls supplied as evidence. Used identically for
    /// player melee contacts and AI attack intents.
    fn resolve_action_attempt(
        &mut self,
        action: &str,
        actor: &str,
        target: &str,
        origin: DaggerIntentOrigin,
        target_entity: u64,
        roll_sequence: u64,
    ) -> Result<ActionAttemptOutcome, RuntimeError> {
        self.combat_sequence = self.combat_sequence.saturating_add(1);
        let sequence = self.combat_sequence;
        let roll = deterministic_roll(roll_sequence, target_entity, 1, 1, 100);
        let mut evidence = vec![DaggerEvidence {
            id: format!("{action}.d100"),
            value: roll,
        }];
        for (id, min, max) in
            action_roll_evidence(&self.gameplay_catalog, action).map_err(RuntimeError::Gameplay)?
        {
            // Career and swing facts (proficiency, racial bonuses, swing
            // state) are 0 until careers and swing states are modeled;
            // genuine rolls get a deterministic in-bounds value.
            let value = if zeroed_career_fact(&id) {
                0
            } else {
                deterministic_roll(roll_sequence, target_entity, 2, min, max)
            };
            evidence.push(DaggerEvidence { value, id });
        }
        // Equipment-driven evidence: the weapon damage roll is bounded by the
        // actor's CURRENTLY equipped weapon (unarmed: the derived
        // hand-to-hand range) so evaluation bounds never spuriously reject,
        // and the struck-body-part roll selects the armor part the hit check
        // reads.
        let weapon = equipped_weapon(&self.gameplay, &self.gameplay_catalog, actor)
            .map_err(RuntimeError::Gameplay)?;
        let weapon_label = weapon
            .map_or("unarmed", |item| item.id.as_str())
            .to_string();
        let mut struck_part = None;
        for (id, kind) in action_dynamic_roll_evidence(&self.gameplay_catalog, action)
            .map_err(RuntimeError::Gameplay)?
        {
            let value = match kind {
                DaggerDynamicRoll::StruckBodyPart => {
                    let value = deterministic_roll(roll_sequence, target_entity, 3, 0, 19);
                    struck_part = struck_body_part_name(value).map(str::to_string);
                    value
                }
                DaggerDynamicRoll::EquippedWeaponDamage => {
                    let (min, max) = match weapon {
                        Some(item) => {
                            let weapon = item.weapon.as_ref().expect("weapon item");
                            (weapon.damage_min, weapon.damage_max)
                        }
                        None => {
                            // Evidence bounds follow the definition-base
                            // skills; nothing in the current packages modifies
                            // skills at runtime, and any future divergence
                            // fails closed as a RollOutOfBounds rejection.
                            let binding = self.gameplay.actor(actor).ok_or_else(|| {
                                RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                                    "unknown actor {actor}"
                                )))
                            })?;
                            let definition = self
                                .gameplay_catalog
                                .actors()
                                .get(binding.definition())
                                .expect("bound actor definition");
                            unarmed_damage_range(
                                &self.gameplay_catalog,
                                definition,
                                &definition_base_stats(definition),
                            )
                            .map_err(RuntimeError::Gameplay)?
                        }
                    };
                    deterministic_roll(roll_sequence, target_entity, 2, min, max)
                }
            };
            evidence.push(DaggerEvidence { value, id });
        }
        let (receipt, readout) = dagger_rpg::resolve_dagger_action(
            &self.gameplay_catalog,
            &mut self.gameplay,
            ResolutionIdentity::root(
                ResolutionId::new(sequence).expect("non-zero resolution id"),
                CorrelationId::new(7046).expect("non-zero correlation id"),
            ),
            ResolutionMode::Apply,
            DaggerIntent {
                action: action.to_string(),
                actor: actor.to_string(),
                target: target.to_string(),
                origin,
            },
            evidence,
        );
        let mut damage = 0_i64;
        let mut stamina_spent = 0_i64;
        for event in receipt.events() {
            match event {
                DaggerEvent::DamageApplied { amount, .. } => damage += amount,
                DaggerEvent::TrackSpent { track, amount, .. } if track == "stamina" => {
                    stamina_spent += amount;
                }
                _ => {}
            }
        }
        let material_ineffective = readout.trace.iter().any(|record| {
            matches!(
                record.detail,
                Some(dagger_rpg::DaggerTraceDetail::MaterialIneffective { .. })
            )
        });
        Ok(ActionAttemptOutcome {
            succeeded: receipt.succeeded(),
            status: readout.status.clone(),
            roll,
            damage,
            stamina_spent,
            weapon: weapon_label,
            struck_part,
            material_ineffective,
            decisions: readout
                .trace
                .iter()
                .filter_map(|record| match &record.detail {
                    Some(dagger_rpg::DaggerTraceDetail::Decision { reason }) => {
                        Some(reason.clone())
                    }
                    _ => None,
                })
                .collect(),
            events: readout
                .events
                .iter()
                .map(|event| format!("{event:?}"))
                .collect(),
        })
    }

    /// Kill-XP hook: the enemy `id` just died to a player-origin resolution.
    /// The progression authority owns the award's state changes; the runtime
    /// supplies the per-level hp rolls from its salted stream (salt 5, the
    /// per-player level-up sequence) so level-ups are deterministic per
    /// session. Award receipts append to the capped progression history.
    fn award_kill_progression_for(&mut self, id: u64) -> Result<(), RuntimeError> {
        let victim = enemy_actor_id(id);
        let Some((level_before, level_after)) = dagger_rpg::kill_level_gains(
            &self.gameplay,
            &self.gameplay_catalog,
            PLAYER_ACTOR_ID,
            &victim,
        )
        .map_err(RuntimeError::Gameplay)?
        else {
            return Ok(());
        };
        let mut evidence = Vec::new();
        if level_after > level_before {
            let hit_points_per_level = self
                .gameplay_catalog
                .actors()
                .get(PLAYER_ACTOR_ID)
                .and_then(|player| player.hit_points_per_level)
                .ok_or_else(|| {
                    RuntimeError::Gameplay(DaggerGameplayError::InvalidState(
                        "player levels up without hitPointsPerLevel".to_string(),
                    ))
                })?;
            let player_entity = self.player_actor_entity().raw();
            for level in (level_before + 1)..=level_after {
                self.player_level_up_sequence = self.player_level_up_sequence.saturating_add(1);
                let roll = deterministic_roll(
                    self.player_level_up_sequence,
                    player_entity,
                    5,
                    hit_points_per_level / 2,
                    hit_points_per_level,
                );
                evidence.push(DaggerEvidence {
                    id: format!("{PLAYER_ACTOR_ID}.level-up.{level}.hp-roll"),
                    value: roll,
                });
            }
        }
        if let Some(record) = dagger_rpg::award_kill_progression(
            &mut self.gameplay,
            &self.gameplay_catalog,
            PLAYER_ACTOR_ID,
            &victim,
            &evidence,
        )
        .map_err(RuntimeError::Gameplay)?
        {
            self.progression_history.push_back(record);
            while self.progression_history.len() > COMBAT_HISTORY_LIMIT {
                self.progression_history.pop_front();
            }
        }
        Ok(())
    }

    /// The player's kill-XP progression readout: live progression stats,
    /// xp-to-next from the `xp-level` curve's own divisor (no duplicated
    /// pacing constant), health, and the award history.
    fn progression_readout(&self) -> Result<ProgressionReadout, RuntimeError> {
        let xp =
            dagger_rpg::live_stat_base(&self.gameplay, PLAYER_ACTOR_ID, dagger_rpg::XP_STAT_ID)
                .map_err(RuntimeError::Gameplay)?;
        let level =
            dagger_rpg::live_stat_base(&self.gameplay, PLAYER_ACTOR_ID, dagger_rpg::LEVEL_STAT_ID)
                .map_err(RuntimeError::Gameplay)?;
        let divisor =
            dagger_rpg::xp_level_divisor(&self.gameplay_catalog).map_err(RuntimeError::Gameplay)?;
        Ok(ProgressionReadout {
            xp,
            level,
            xp_to_next_level: divisor - xp.rem_euclid(divisor),
            current_health: self.player_health(),
            max_health: self.live_track_max(PLAYER_ACTOR_ID, "health"),
            history: self.progression_history.iter().cloned().collect(),
        })
    }

    fn finish_melee_contact(
        &mut self,
        attempt_sequence: u64,
        target_id: Option<u64>,
        outcome: &str,
        result: Option<MeleeContactResult>,
    ) {
        if let Some(attempt) = self
            .combat_attempt_history
            .iter_mut()
            .find(|attempt| attempt.sequence == attempt_sequence)
        {
            attempt.target_id = target_id;
            attempt.outcome = outcome.to_string();
            if let Some(result) = &result {
                attempt.stamina_cost = result.stamina_spent as f32;
                attempt.stamina_after = result.stamina_after;
            }
        }
        if let Some(action) = self.melee_presentation.as_mut() {
            action.contact_resolved = true;
            action.outcome = outcome.to_string();
            action.target_id = target_id;
            if let Some(result) = result {
                action.stamina_after = result.stamina_after;
                action.target_health_before = Some(result.health_before);
                action.target_health_after = Some(result.health_after);
                action.target_max_health = Some(result.target_max_health);
                action.final_damage = Some(result.damage as f32);
                action.died = result.died;
            }
        }
    }

    fn push_combat_attempt(&mut self, mut record: CombatAttemptRecord) -> u64 {
        self.combat_attempt_sequence = self.combat_attempt_sequence.saturating_add(1);
        record.sequence = self.combat_attempt_sequence;
        self.combat_attempt_history.push_back(record);
        while self.combat_attempt_history.len() > COMBAT_HISTORY_LIMIT {
            self.combat_attempt_history.pop_front();
        }
        self.combat_attempt_sequence
    }

    /// The player's upstream inventory/equipment view, sourced from
    /// `InventoryService::view` plus the entity's EquipmentComponent.
    fn player_inventory_readout(&self) -> Result<PlayerInventoryReadout, RuntimeError> {
        use rusty_engine::gameplay_mechanics::{EquipmentComponent, InventoryService};

        let owner = self
            .gameplay
            .actor(PLAYER_ACTOR_ID)
            .expect("player actor binding")
            .entity();
        let view = InventoryService::view(
            self.gameplay.entities(),
            self.gameplay_catalog.mechanics(),
            owner,
        )
        .map_err(|error| {
            RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                "player inventory view: {error:?}"
            )))
        })?;
        let equipment = self
            .gameplay
            .entities()
            .component::<EquipmentComponent>(owner)
            .map_err(|error| {
                RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                    "player equipment component: {error}"
                )))
            })?;
        let slot_of = |entity: rusty_engine::core_ids::EntityId| {
            equipment.and_then(|component| {
                component
                    .assignments()
                    .iter()
                    .find(|assignment| assignment.item == entity)
                    .map(|assignment| assignment.slot.as_str().to_string())
            })
        };
        Ok(PlayerInventoryReadout {
            capacity: view
                .capacity()
                .iter()
                .map(|usage| InventoryCapacityReadout {
                    metric: usage.metric.as_str().to_string(),
                    used: usage.used,
                    maximum: usage.maximum,
                })
                .collect(),
            stacks: view
                .stacks()
                .iter()
                .map(|stack| InventoryStackReadout {
                    item: stack.definition.as_str().to_string(),
                    quantity: stack.quantity,
                })
                .collect(),
            items: view
                .unique_items()
                .iter()
                .map(|item| InventoryItemReadout {
                    item: item.definition.as_str().to_string(),
                    entity: item.entity.raw(),
                    equip_slot: slot_of(item.entity),
                })
                .collect(),
        })
    }

    /// The player's carried equippable unique items in the stable ordering
    /// (item entities are allocated at spawn in loadout order, so raw entity
    /// id order is deterministic).
    fn carried_equippables(&self) -> Result<Vec<(EntityId, String)>, RuntimeError> {
        use rusty_engine::gameplay_mechanics::InventoryService;

        let owner = self.player_actor_entity();
        let view = InventoryService::view(
            self.gameplay.entities(),
            self.gameplay_catalog.mechanics(),
            owner,
        )
        .map_err(|error| {
            RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                "player inventory view: {error:?}"
            )))
        })?;
        let mut carried = view
            .unique_items()
            .iter()
            .filter(|entry| {
                self.gameplay_catalog
                    .items()
                    .get(entry.definition.as_str())
                    .is_some_and(dagger_rpg::DaggerItemDefinition::equippable)
            })
            .map(|entry| (entry.entity, entry.definition.as_str().to_string()))
            .collect::<Vec<_>>();
        carried.sort_by_key(|(entity, _)| entity.raw());
        Ok(carried)
    }

    fn player_actor_entity(&self) -> EntityId {
        self.gameplay
            .actor(PLAYER_ACTOR_ID)
            .expect("player actor binding")
            .entity()
    }

    fn player_equipment_component(
        &self,
    ) -> Result<rusty_engine::gameplay_mechanics::EquipmentComponent, RuntimeError> {
        self.gameplay
            .entities()
            .component::<rusty_engine::gameplay_mechanics::EquipmentComponent>(
                self.player_actor_entity(),
            )
            .map_err(|error| {
                RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                    "player equipment component: {error}"
                )))
            })?
            .cloned()
            .ok_or_else(|| {
                RuntimeError::Gameplay(DaggerGameplayError::InvalidState(
                    "player has no equipment component".to_string(),
                ))
            })
    }

    /// Equip verb: equip one carried item entity into its preferred slot,
    /// swapping out the occupant when the slot is taken. An upstream
    /// rejection is logged (not thrown) so the lab can show the reason.
    pub fn equip_item(&mut self, item: u64) -> Result<LabReadout, RuntimeError> {
        use rusty_engine::gameplay_mechanics::{
            EquipmentEquipRequest, EquipmentService, EquipmentSlotId, EquipmentSwapRequest,
        };

        let carried = self.carried_equippables()?;
        let entity = EntityId::new(item);
        let Some((_, item_id)) = carried.iter().find(|(candidate, _)| *candidate == entity) else {
            self.log_equipment_rejection(
                "equip",
                format!("entity-{item}"),
                Vec::new(),
                None,
                "not a carried equippable item".to_string(),
            );
            return self.lab_readout();
        };
        let item_id = item_id.clone();
        let Some(slot) = self.preferred_equip_slot(&item_id) else {
            self.log_equipment_rejection(
                "equip",
                item_id,
                Vec::new(),
                None,
                "no legal equipment slot".to_string(),
            );
            return self.lab_readout();
        };
        let slot_id = EquipmentSlotId::parse(slot.clone()).map_err(|error| {
            RuntimeError::Gameplay(DaggerGameplayError::InvalidId {
                path: "equip.slot".to_string(),
                value: format!("{slot}: {error:?}"),
            })
        })?;
        let equipment = self.player_equipment_component()?;
        let owner = self.player_actor_entity();
        let (operation, source) = equipment_operation();
        let expected_state_revision = self.gameplay.entities().revision();
        let occupant = equipment
            .assignment(&slot_id)
            .map(|assignment| assignment.item);
        let result = match occupant {
            Some(outgoing) => EquipmentService::swap(
                self.gameplay.entities_mut(),
                self.gameplay_catalog.mechanics(),
                EquipmentSwapRequest {
                    operation,
                    source,
                    owner,
                    outgoing_item: outgoing,
                    incoming_item: entity,
                    incoming_slots: vec![slot_id],
                    expected_equipment_revision: None,
                    expected_state_revision,
                },
            ),
            None => EquipmentService::equip(
                self.gameplay.entities_mut(),
                self.gameplay_catalog.mechanics(),
                EquipmentEquipRequest {
                    operation,
                    source,
                    owner,
                    item: entity,
                    slots: vec![slot_id],
                    expected_equipment_revision: None,
                    expected_state_revision,
                },
            ),
        };
        match result {
            Ok(receipt) => {
                let item_name = |entity: EntityId| {
                    carried
                        .iter()
                        .find(|(candidate, _)| *candidate == entity)
                        .map(|(_, id)| id.clone())
                        .unwrap_or_else(|| format!("entity-{}", entity.raw()))
                };
                self.push_equipment_record(EquipmentLogRecord {
                    sequence: 0,
                    operation: format!("{:?}", receipt.kind).to_lowercase(),
                    item: item_name(receipt.item),
                    slots: receipt
                        .changes
                        .iter()
                        .map(|change| change.slot.as_str().to_string())
                        .collect(),
                    replaced_item: receipt.replaced_item.map(&item_name),
                    quantity: None,
                    accepted: true,
                    reason: None,
                    committed_revision: Some(receipt.committed_equipment_revision),
                });
            }
            Err(error) => {
                self.log_equipment_rejection(
                    if occupant.is_some() { "swap" } else { "equip" },
                    item_id,
                    vec![slot],
                    None,
                    format!("{error:?}"),
                );
            }
        }
        self.lab_readout()
    }

    /// Unequip verb: strip whatever occupies one equipment slot. An empty or
    /// unknown slot is logged as a rejection, not thrown.
    pub fn unequip_slot(&mut self, slot: &str) -> Result<LabReadout, RuntimeError> {
        use rusty_engine::gameplay_mechanics::{
            EquipmentService, EquipmentSlotId, EquipmentUnequipRequest,
        };

        let slot_id = EquipmentSlotId::parse(slot).map_err(|error| {
            RuntimeError::Gameplay(DaggerGameplayError::InvalidId {
                path: "unequip.slot".to_string(),
                value: format!("{slot}: {error:?}"),
            })
        })?;
        let equipment = self.player_equipment_component()?;
        let Some(assignment) = equipment.assignment(&slot_id) else {
            self.log_equipment_rejection(
                "unequip",
                "—".to_string(),
                vec![slot.to_string()],
                None,
                "slot is empty".to_string(),
            );
            return self.lab_readout();
        };
        let item_entity = assignment.item;
        let item_name = self
            .gameplay
            .entities()
            .component::<rusty_engine::gameplay_mechanics::ItemComponent>(item_entity)
            .ok()
            .flatten()
            .map(|item| item.definition().as_str().to_string())
            .unwrap_or_else(|| format!("entity-{}", item_entity.raw()));
        let owner = self.player_actor_entity();
        let (operation, source) = equipment_operation();
        let expected_state_revision = self.gameplay.entities().revision();
        match EquipmentService::unequip(
            self.gameplay.entities_mut(),
            self.gameplay_catalog.mechanics(),
            EquipmentUnequipRequest {
                operation,
                source,
                owner,
                item: item_entity,
                expected_equipment_revision: None,
                expected_state_revision,
            },
        ) {
            Ok(receipt) => self.push_equipment_record(EquipmentLogRecord {
                sequence: 0,
                operation: "unequip".to_string(),
                item: item_name,
                slots: receipt
                    .changes
                    .iter()
                    .map(|change| change.slot.as_str().to_string())
                    .collect(),
                replaced_item: None,
                quantity: None,
                accepted: true,
                reason: None,
                committed_revision: Some(receipt.committed_equipment_revision),
            }),
            Err(error) => self.log_equipment_rejection(
                "unequip",
                item_name,
                vec![slot.to_string()],
                None,
                format!("{error:?}"),
            ),
        }
        self.lab_readout()
    }

    /// Experiment-lab grant verb: grant a fungible item stack through
    /// `InventoryService::grant`. Unique (equippable) items are out of scope
    /// — they would need entity allocation and containment, which the spawn
    /// loadout already owns. Over-capacity grants reject upstream and the
    /// rejection lands in the equipment log with the reason.
    pub fn grant_item(&mut self, item: &str, quantity: u64) -> Result<LabReadout, RuntimeError> {
        use rusty_engine::gameplay_mechanics::{
            InventoryMutationRequest, InventoryService, ItemDefinitionId,
        };

        let reject = |runtime: &mut Self, reason: String| {
            runtime.log_equipment_rejection(
                "grant",
                item.to_string(),
                Vec::new(),
                Some(quantity),
                reason,
            );
        };
        let definition = self.gameplay_catalog.items().get(item);
        let definition = match definition {
            Some(definition) if definition.fungible => definition,
            Some(_) => {
                reject(
                    self,
                    "not a fungible item (unique items equip, not stack)".to_string(),
                );
                return self.lab_readout();
            }
            None => {
                reject(self, "unknown item".to_string());
                return self.lab_readout();
            }
        };
        if quantity == 0 {
            reject(self, "quantity must be positive".to_string());
            return self.lab_readout();
        }
        let item_id = ItemDefinitionId::parse(definition.id.clone()).map_err(|error| {
            RuntimeError::Gameplay(DaggerGameplayError::InvalidId {
                path: "grant.item".to_string(),
                value: format!("{item}: {error:?}"),
            })
        })?;
        let owner = self.player_actor_entity();
        let (operation, source) = equipment_operation();
        match InventoryService::grant(
            self.gameplay.entities_mut(),
            self.gameplay_catalog.mechanics(),
            InventoryMutationRequest {
                operation,
                source,
                owner,
                item: item_id,
                quantity,
                expected_revision: None,
            },
        ) {
            Ok(receipt) => self.push_equipment_record(EquipmentLogRecord {
                sequence: 0,
                operation: "grant".to_string(),
                item: receipt.item.as_str().to_string(),
                slots: Vec::new(),
                replaced_item: None,
                quantity: Some(receipt.after_quantity - receipt.before_quantity),
                accepted: true,
                reason: None,
                committed_revision: Some(receipt.committed_inventory_revision),
            }),
            Err(error) => reject(self, format!("{error:?}")),
        }
        self.lab_readout()
    }

    /// Interact/pickup verb (KeyF in the native host): an aimed-interact
    /// query — the `select_aimed_melee_target` cone shape over a different
    /// candidate set: dead enemies (their actor entities are the corpse
    /// containers; donor EnemyDeath.cs:123 keeps the loot in the enemy's
    /// inventory) and treasure container entities (no live actor). Living
    /// enemies are NOT lootable.
    ///
    /// On a hit the verb performs a take-all transfer: fungible stacks
    /// through `InventoryService::transfer`, unique items through
    /// `EquipmentService::transfer_unique_item`, respecting the player's
    /// capacity per item and stopping at the first rejection (the donor loot
    /// window's partial-take spirit via CanCarryAmount). Deviation from the
    /// donor: no loot window UI with selective take — take-all with capacity
    /// legality. Every transfer and the stopping rejection land in the
    /// equipment log under a `loot:<container>` operation. An empty
    /// container logs the donor's "nothing to take" note.
    pub fn interact_loot(&mut self) -> Result<LabReadout, RuntimeError> {
        use rusty_engine::gameplay_mechanics::{
            EquipmentService, InventoryService, InventoryTransferRequest, ItemTransferRequest,
        };

        let player_position = self.player_position()?;
        let lootable = self
            .content_entities
            .iter()
            .filter(|entity| entity.enemy().is_none() || self.is_enemy_dead(entity.id))
            .map(|entity| entity.id)
            .collect::<BTreeSet<_>>();
        let target = select_aimed_loot_target(
            player_position,
            self.player_state().yaw_degrees,
            LOOT_INTERACT_REACH,
            &self.content_entities,
            &self.content_live_positions,
            &lootable,
        );
        let Some(id) = target else {
            self.log_equipment_rejection(
                "loot",
                "—".to_string(),
                Vec::new(),
                None,
                format!("no loot container within {LOOT_INTERACT_REACH} units of the aim"),
            );
            return self.lab_readout();
        };
        let entity = self
            .content_entities
            .iter()
            .find(|entity| entity.id == id)
            .expect("aimed target is a content entity");
        let instance = match &entity.kind {
            ContentEntityKind::Enemy(_) => enemy_actor_id(id),
            ContentEntityKind::Treasure { .. } => treasure_container_id(id),
        };
        let no_treasure_note = |runtime: &mut Self| {
            // Donor: clicking an empty corpse says "the body has no treasure".
            runtime.log_equipment_rejection(
                format!("loot:{instance}"),
                "—".to_string(),
                Vec::new(),
                None,
                "nothing to take (the body has no treasure)".to_string(),
            );
        };
        let Some(container) = self.gameplay.container(&instance) else {
            // Dead enemy whose definition carries no loot table (rat, bat):
            // classic generates no loot for it.
            no_treasure_note(self);
            return self.lab_readout();
        };
        let from_owner = container.entity();
        let view = InventoryService::view(
            self.gameplay.entities(),
            self.gameplay_catalog.mechanics(),
            from_owner,
        )
        .map_err(|error| {
            RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                "container inventory view: {error:?}"
            )))
        })?;
        let stacks = view.stacks().to_vec();
        let items = view.unique_items().to_vec();
        if stacks.is_empty() && items.is_empty() {
            no_treasure_note(self);
            return self.lab_readout();
        }
        let to_owner = self.player_actor_entity();
        let (operation, source) = equipment_operation();
        for stack in stacks {
            let quantity = stack.quantity;
            let item = stack.definition.as_str().to_string();
            match InventoryService::transfer(
                self.gameplay.entities_mut(),
                self.gameplay_catalog.mechanics(),
                InventoryTransferRequest {
                    operation: operation.clone(),
                    source: source.clone(),
                    from_owner,
                    to_owner,
                    item: stack.definition.clone(),
                    quantity,
                    expected_from_revision: None,
                    expected_to_revision: None,
                },
            ) {
                Ok(receipt) => self.push_equipment_record(EquipmentLogRecord {
                    sequence: 0,
                    operation: format!("loot:{instance}"),
                    item,
                    slots: Vec::new(),
                    replaced_item: None,
                    quantity: Some(receipt.to_after - receipt.to_before),
                    accepted: true,
                    reason: None,
                    committed_revision: Some(receipt.committed_to_revision),
                }),
                // Capacity (or any upstream) rejection: log and stop — the
                // rest of the pile stays in the container.
                Err(error) => {
                    self.log_equipment_rejection(
                        format!("loot:{instance}"),
                        item,
                        Vec::new(),
                        Some(quantity),
                        format!("{error:?}"),
                    );
                    return self.lab_readout();
                }
            }
        }
        for item in items {
            let item_name = item.definition.as_str().to_string();
            let relationship_revision = self.gameplay.entities().revision();
            match EquipmentService::transfer_unique_item(
                self.gameplay.entities_mut(),
                self.gameplay_catalog.mechanics(),
                ItemTransferRequest {
                    operation: operation.clone(),
                    source: source.clone(),
                    item: item.entity,
                    from_owner,
                    to_owner,
                    expected_relationship_revision: relationship_revision,
                    expected_from_inventory_revision: None,
                    expected_to_inventory_revision: None,
                },
            ) {
                Ok(receipt) => self.push_equipment_record(EquipmentLogRecord {
                    sequence: 0,
                    operation: format!("loot:{instance}"),
                    item: item_name,
                    slots: Vec::new(),
                    replaced_item: None,
                    quantity: None,
                    accepted: true,
                    reason: None,
                    committed_revision: Some(receipt.revision_after),
                }),
                Err(error) => {
                    self.log_equipment_rejection(
                        format!("loot:{instance}"),
                        item_name,
                        Vec::new(),
                        None,
                        format!("{error:?}"),
                    );
                    return self.lab_readout();
                }
            }
        }
        self.lab_readout()
    }

    /// Equip-cycle verb (KeyE in the native host): each press equips the next
    /// carried equippable item in the stable entity ordering, skipping
    /// already-equipped items and swapping when the item's legal slot is
    /// occupied. When every carried equippable is already equipped, the press
    /// unequips the next item after the cursor instead, so the verb can also
    /// strip gear. Every attempt lands in the equipment log, rejections
    /// included.
    pub fn equip_cycle(&mut self) -> Result<LabReadout, RuntimeError> {
        let carried = self.carried_equippables()?;
        if carried.is_empty() {
            return self.lab_readout();
        }
        let equipment = self.player_equipment_component()?;
        let equipped = |entity: EntityId| {
            equipment
                .assignments()
                .iter()
                .any(|assignment| assignment.item == entity)
        };
        let n = carried.len();
        let cursor = self
            .equip_cycle_cursor
            .unwrap_or(n - 1)
            .min(n.saturating_sub(1));
        let target = (1..=n)
            .map(|offset| (cursor + offset) % n)
            .find(|index| !equipped(carried[*index].0));
        match target {
            Some(index) => {
                self.equip_cycle_cursor = Some(index);
                self.equip_item(carried[index].0.raw())
            }
            None => {
                // Everything equippable is already equipped: strip the next
                // item after the cursor.
                let index = (cursor + 1) % n;
                self.equip_cycle_cursor = Some(index);
                let slot = equipment
                    .assignments()
                    .iter()
                    .find(|assignment| assignment.item == carried[index].0)
                    .map(|assignment| assignment.slot.as_str().to_string())
                    .ok_or_else(|| {
                        RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                            "equipped item {} has no assignment",
                            carried[index].1
                        )))
                    })?;
                self.unequip_slot(&slot)
            }
        }
    }

    /// The item's preferred equip slot: right hand for one/two-handed
    /// weapons, left hand for left-only weapons and shields, and the first
    /// authored slot accepting an armor piece's classification otherwise.
    fn preferred_equip_slot(&self, item_id: &str) -> Option<String> {
        let item = self.gameplay_catalog.items().get(item_id)?;
        if let Some(weapon) = &item.weapon {
            return Some(
                match weapon.hands {
                    dagger_rpg::DaggerWeaponHands::LeftOnly => "left-hand",
                    _ => "right-hand",
                }
                .to_string(),
            );
        }
        if item.shield.is_some() {
            return Some("left-hand".to_string());
        }
        let classification = item.classifications.first()?;
        self.gameplay_catalog
            .equipment()
            .slots
            .iter()
            .find(|slot| slot.allowed_classifications.contains(classification))
            .map(|slot| slot.id.clone())
    }

    fn push_equipment_record(&mut self, mut record: EquipmentLogRecord) {
        self.equipment_log_sequence = self.equipment_log_sequence.saturating_add(1);
        record.sequence = self.equipment_log_sequence;
        self.equipment_log.push_back(record);
        while self.equipment_log.len() > COMBAT_HISTORY_LIMIT {
            self.equipment_log.pop_front();
        }
    }

    fn log_equipment_rejection(
        &mut self,
        operation: impl Into<String>,
        item: String,
        slots: Vec<String>,
        quantity: Option<u64>,
        reason: String,
    ) {
        self.push_equipment_record(EquipmentLogRecord {
            sequence: 0,
            operation: operation.into(),
            item,
            slots,
            replaced_item: None,
            quantity,
            accepted: false,
            reason: Some(reason),
            committed_revision: None,
        });
    }

    pub fn lab_readout(&self) -> Result<LabReadout, RuntimeError> {
        let position = self.player_position()?;
        let encounter_states = self
            .patrol
            .as_ref()
            .map(|patrol| {
                patrol
                    .states()
                    .into_iter()
                    .map(|(handle, mode)| (u64::from(handle), ai_mode_name(mode)))
                    .collect::<BTreeMap<_, _>>()
            })
            .unwrap_or_default();
        let content = self
            .content_entities
            .iter()
            .map(|entity| {
                let live_position = self
                    .content_live_positions
                    .get(&entity.id)
                    .copied()
                    .unwrap_or(entity.authored_position);
                let (kind, reference, loot_key) = match entity.enemy() {
                    Some(enemy) => (
                        "enemy",
                        Some(EnemyReferenceReadout {
                            mobile_id: enemy.mobile_id,
                            mobile_name: enemy.mobile_name.clone(),
                            texture_archive: enemy.texture_archive,
                            flying: enemy.flying,
                            sprite_asset: entity.sprite_asset.clone(),
                            authored_position: entity.authored_position,
                        }),
                        None,
                    ),
                    None => match &entity.kind {
                        ContentEntityKind::Treasure { loot_key } => {
                            ("treasure", None, Some(loot_key.clone()))
                        }
                        ContentEntityKind::Enemy(_) => unreachable!("handled above"),
                    },
                };
                ContentEntityReadout {
                    id: entity.id,
                    kind,
                    name: entity.name.clone(),
                    reference,
                    loot_key,
                    live: ContentLiveReadout {
                        position: live_position,
                        distance_from_player: (live_position[0] - position.x)
                            .hypot(live_position[1] - position.y)
                            .hypot(live_position[2] - position.z),
                        resources: self
                            .has_live_actor(entity.id)
                            .then(|| self.live_resources(&enemy_actor_id(entity.id))),
                        ai_state: encounter_states.get(&entity.id).copied(),
                    },
                }
            })
            .collect();
        let named_encounters = self
            .named_encounters
            .iter()
            .map(|encounter| self.named_encounter_readout(encounter))
            .collect::<Vec<_>>();
        let active_encounter = self
            .active_encounter_id
            .as_deref()
            .and_then(|id| {
                self.named_encounters
                    .iter()
                    .find(|encounter| encounter.id == id)
            })
            .map(|encounter| self.named_encounter_readout(encounter));
        Ok(LabReadout {
            gameplay_package: GameplayPackageReadout {
                fingerprint: self.gameplay_catalog.fingerprint().to_string(),
                payload: self.gameplay_payload.clone(),
            },
            move_speed_units_per_second: self.player_controller.move_speed_units_per_second,
            max_health: self.live_track_max(PLAYER_ACTOR_ID, "health"),
            current_health: self.player_health(),
            player_stats: self.player_gameplay_readout(),
            player_position: [position.x, position.y, position.z],
            player_yaw_degrees: self.player_state().yaw_degrees,
            combat: self.combat_history.iter().cloned().collect(),
            combat_attempts: self.combat_attempt_history.iter().cloned().collect(),
            player_attack_cooldown_remaining: self.player_attack_cooldown_remaining,
            melee_presentation: self.melee_presentation(),
            encounter_decisions: self.encounter_history.iter().cloned().collect(),
            content,
            focused_content_id: self.focused_content_id,
            named_encounters,
            active_encounter,
            player_inventory: self.player_inventory_readout()?,
            equipment_log: self.equipment_log.iter().cloned().collect(),
            loot_containers: self.loot_containers_readout()?,
            progression: self.progression_readout()?,
        })
    }

    /// One readout per tracked loot container, ordered by instance id:
    /// treasure piles from spawn_container and loot-bearing enemies from
    /// bind_actor_loot. Contents are the live upstream inventory view, so
    /// looted items disappear as they transfer.
    fn loot_containers_readout(&self) -> Result<Vec<LootContainerReadout>, RuntimeError> {
        self.gameplay
            .containers()
            .iter()
            .map(|(instance, container)| {
                let (kind, content_entity_id) = if let Some(id) = instance.strip_prefix("treasure-")
                {
                    ("treasure", id.parse::<u64>().ok())
                } else if let Some(id) = instance.strip_prefix("enemy-") {
                    ("corpse", id.parse::<u64>().ok())
                } else {
                    ("corpse", None)
                };
                let view = self.inventory_readout_for(container.entity())?;
                let emptied = view.stacks.is_empty() && view.items.is_empty();
                Ok(LootContainerReadout {
                    id: instance.clone(),
                    kind,
                    content_entity_id: content_entity_id.unwrap_or(0),
                    loot_key: container.key().to_string(),
                    contents: view,
                    generation: container.generation().clone(),
                    emptied,
                })
            })
            .collect()
    }

    /// `InventoryService::view` for any inventory-carrying entity (player or
    /// container), rendered as the shared inventory readout shape.
    fn inventory_readout_for(
        &self,
        owner: EntityId,
    ) -> Result<PlayerInventoryReadout, RuntimeError> {
        use rusty_engine::gameplay_mechanics::InventoryService;

        let view = InventoryService::view(
            self.gameplay.entities(),
            self.gameplay_catalog.mechanics(),
            owner,
        )
        .map_err(|error| {
            RuntimeError::Gameplay(DaggerGameplayError::InvalidState(format!(
                "inventory view: {error:?}"
            )))
        })?;
        Ok(PlayerInventoryReadout {
            capacity: view
                .capacity()
                .iter()
                .map(|usage| InventoryCapacityReadout {
                    metric: usage.metric.as_str().to_string(),
                    used: usage.used,
                    maximum: usage.maximum,
                })
                .collect(),
            stacks: view
                .stacks()
                .iter()
                .map(|stack| InventoryStackReadout {
                    item: stack.definition.as_str().to_string(),
                    quantity: stack.quantity,
                })
                .collect(),
            items: view
                .unique_items()
                .iter()
                .map(|item| InventoryItemReadout {
                    item: item.definition.as_str().to_string(),
                    entity: item.entity.raw(),
                    equip_slot: None,
                })
                .collect(),
        })
    }

    fn named_encounter_readout(&self, encounter: &NamedEncounter) -> NamedEncounterReadout {
        let status = if self.active_encounter_id.as_deref() == Some(encounter.id.as_str()) {
            match self.active_encounter_outcome {
                NamedEncounterOutcome::Inactive => "available",
                NamedEncounterOutcome::Active => "active",
                NamedEncounterOutcome::Victory => "victory",
                NamedEncounterOutcome::Defeat => "defeat",
            }
        } else {
            "available"
        };
        NamedEncounterReadout {
            id: encounter.id.clone(),
            name: encounter.name.clone(),
            objective: encounter.objective.clone(),
            route_code: encounter.route_code.clone(),
            member_entity_ids: encounter.member_entity_ids.clone(),
            status: status.to_string(),
        }
    }

    /// Authoritatively reposition the player (route derivation / probing).
    /// Sets the translation and resets canonical continuation state so a
    /// subsequent settle starts cleanly; collision is re-evaluated next tick.
    pub fn set_player_position(&mut self, translation: Vec3) -> Result<(), RuntimeError> {
        let transform_revision = self
            .entities
            .component_revision::<TransformComponent>(self.player)
            .expect("admitted player transform revision");
        let motion_revision = self
            .entities
            .component_revision::<CharacterMotionComponent>(self.player)
            .expect("admitted player motion revision");
        let mut transform = *self
            .entities
            .transform(self.player)
            .expect("admitted player transform");
        transform.translation = translation;
        let mut motion = CharacterMotionComponent::at_rest(translation.y);
        motion.last_command_sequence = self.player_command_sequence;
        replace_character_motion_state(
            &mut self.entities,
            CharacterMotionStateReplacement {
                entity: self.player,
                expected_transform_revision: transform_revision,
                expected_motion_revision: motion_revision,
                transform,
                motion,
            },
        )
        .map_err(|error| {
            RuntimeError::Player(crate::player::PlayerError::MotionPublication(error))
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
            &mut self.player_look_state,
            &mut self.player_controller_service,
            &mut self.player_command_sequence,
            &self.player_controller,
            action,
            self.player_controller.move_step_seconds,
        )
        .map_err(RuntimeError::Player)?;
        Ok(result)
    }

    /// Apply one fixed-cadence product input frame atomically. Look is
    /// resolved first and the resulting Engine heading owns movement in this
    /// same frame; neutral frames still advance grounding and gravity.
    pub fn apply_player_frame(
        &mut self,
        frame: ResolvedPlayerFrame,
    ) -> Result<PlayerFrameReceipt, RuntimeError> {
        crate::player::apply_player_frame(
            &mut self.entities,
            &self.collision_scene,
            self.player,
            &mut self.player_look_state,
            &mut self.player_controller_service,
            &mut self.player_command_sequence,
            &self.player_controller,
            frame,
        )
        .map_err(RuntimeError::Player)
    }
}

fn select_aimed_melee_target(
    player_position: Vec3,
    yaw_degrees: f32,
    attack_range: f32,
    entities: &[ContentEntity],
    live_positions: &BTreeMap<u64, [f32; 3]>,
    resources: &BTreeMap<u64, LiveActorResources>,
) -> Option<u64> {
    let yaw = yaw_degrees.to_radians();
    let forward = [yaw.sin(), -yaw.cos()];
    entities
        .iter()
        .filter_map(|entity| {
            let position = live_positions.get(&entity.id)?;
            let resources = resources.get(&entity.id)?;
            if resources.current_health <= 0.0 {
                return None;
            }
            let delta = [
                position[0] - player_position.x,
                position[2] - player_position.z,
            ];
            let distance = delta[0].hypot(delta[1]);
            if distance <= 0.001 || distance > attack_range {
                return None;
            }
            let dot = (delta[0] * forward[0] + delta[1] * forward[1]) / distance;
            (dot >= MELEE_AIM_MIN_DOT).then_some((entity.id, dot, distance))
        })
        .max_by(|left, right| {
            left.1
                .total_cmp(&right.1)
                .then_with(|| right.2.total_cmp(&left.2))
                .then_with(|| right.0.cmp(&left.0))
        })
        .map(|candidate| candidate.0)
}

/// Aimed interact query for the loot verb: the same cone/range/ranking shape
/// as `select_aimed_melee_target`, but over an explicit candidate set (dead
/// enemies + treasure containers) instead of living actors.
fn select_aimed_loot_target(
    player_position: Vec3,
    yaw_degrees: f32,
    reach: f32,
    entities: &[ContentEntity],
    live_positions: &BTreeMap<u64, [f32; 3]>,
    candidates: &BTreeSet<u64>,
) -> Option<u64> {
    let yaw = yaw_degrees.to_radians();
    let forward = [yaw.sin(), -yaw.cos()];
    entities
        .iter()
        .filter_map(|entity| {
            if !candidates.contains(&entity.id) {
                return None;
            }
            let position = live_positions.get(&entity.id)?;
            let delta = [
                position[0] - player_position.x,
                position[2] - player_position.z,
            ];
            let distance = delta[0].hypot(delta[1]);
            if distance <= 0.001 || distance > reach {
                return None;
            }
            let dot = (delta[0] * forward[0] + delta[1] * forward[1]) / distance;
            (dot >= MELEE_AIM_MIN_DOT).then_some((entity.id, dot, distance))
        })
        .max_by(|left, right| {
            left.1
                .total_cmp(&right.1)
                .then_with(|| right.2.total_cmp(&left.2))
                .then_with(|| right.0.cmp(&left.0))
        })
        .map(|candidate| candidate.0)
}

fn collision_hit_distance(hit: SpatialCollisionHit) -> f64 {
    match hit {
        SpatialCollisionHit::Voxel(hit) => hit.distance,
        SpatialCollisionHit::StaticMesh(hit) => hit.distance,
    }
}

/// Outcome of one authored action resolution in live play.
struct ActionAttemptOutcome {
    succeeded: bool,
    status: String,
    roll: i64,
    damage: i64,
    stamina_spent: i64,
    weapon: String,
    struck_part: Option<String>,
    material_ineffective: bool,
    decisions: Vec<String>,
    events: Vec<String>,
}

/// Resolved melee contact facts for the attempt record and presentation.
struct MeleeContactResult {
    damage: i64,
    stamina_spent: i64,
    stamina_after: f32,
    health_before: f32,
    health_after: f32,
    target_max_health: f32,
    died: bool,
}

/// Fixed operation/source identity for the lab equipment verbs.
fn equipment_operation() -> (
    rusty_engine::gameplay_mechanics::OperationId,
    rusty_engine::gameplay_mechanics::SourceInstanceIdentity,
) {
    use rusty_engine::gameplay_mechanics::{OperationId, SourceInstanceId, SourceInstanceIdentity};

    let operation = OperationId::parse("dagger-equipment-verb").expect("fixed operation identity");
    let source = SourceInstanceIdentity::Request {
        operation: operation.clone(),
        instance: SourceInstanceId::parse("dagger-equipment").expect("fixed source identity"),
    };
    (operation, source)
}

/// Career/swing evidence ids (authored in `gameplay/src/catalogs/actions.ts`)
/// are supplied as 0 until careers and swing states are modeled: an actor
/// without a career gets no proficiency/racial bonus, and no swing state
/// means no swing modifier.
fn zeroed_career_fact(evidence_id: &str) -> bool {
    const SUFFIXES: [&str; 7] = [
        ".swing-to-hit",
        ".proficiency-to-hit",
        ".racial-to-hit",
        ".proficiency-damage",
        ".racial-damage",
        ".adrenaline-rush",
        ".target-adrenaline-rush",
    ];
    SUFFIXES.iter().any(|suffix| evidence_id.ends_with(suffix))
}

/// Deterministic combat roll: seeded by attempt sequence, target, and salt —
/// no time source, so encounters are replayable and diagnostics and browser
/// gates can assert exact outcomes. Salt 1 is the d100 hit roll; salt 2 is
/// the damage dice roll (bounded by the actor's live weapon); salt 3 is the
/// struck-body-part roll (0..=19); salt 5 is the progression level-up hp
/// roll (loot draws from the per-entity spawn streams instead).
fn deterministic_roll(sequence: u64, target_id: u64, salt: u64, min: i64, max: i64) -> i64 {
    let mut value = sequence
        .rotate_left(17)
        .wrapping_add(target_id.rotate_left(31))
        .wrapping_add(salt.wrapping_mul(0x9E37_79B9_7F4A_7C15));
    value ^= value >> 29;
    value = value.wrapping_mul(0x2545_F491_4F6C_DD1D);
    value ^= value >> 27;
    let span = (max - min + 1) as u64;
    min + (value % span) as i64
}

/// Instance id of the loot container anchored to one treasure content
/// entity (the 3000+ project id band).
fn treasure_container_id(id: u64) -> String {
    format!("treasure-{id}")
}

/// Spawn the live player and one actor instance per admitted content entity
/// whose mobile has an actor definition in the gameplay catalog. Entities
/// without a definition (currently the Thief, mobile 138 — an enemy-class
/// mobile the catalogs don't model yet, see task 7056) get no live gameplay
/// state, matching the previous experiment model: they patrol but are not
/// combatants. Spawn rolls are deterministic per entity so resets are
/// reproducible.
///
/// Loot (donor EnemyDeath.cs:123 model — contents are generated AT SPAWN
/// into the enemy's inventory and transferred out at loot time):
/// - enemies whose definition declares `loot_table_key` get
///   `bind_actor_loot` with evidence drawn from that enemy's spawn stream
///   (the plain `loot.<key>...` contract ids evaluated through
///   `spawn_roll(entity_id, ...)`); rats/bats have no key and carry no loot,
///   exactly as classic.
/// - each treasure content entity gets `spawn_container` with the dungeon's
///   loot key and the same per-entity stream.
///
/// Loot generates at spawn with `LOOT_GENERATION_LEVEL` (see its note):
/// the session player is level 1 at spawn; progression levels arrive only
/// later through kill awards.
fn spawn_live_actors(
    catalog: &DaggerGameplayCatalog,
    content_entities: &[ContentEntity],
) -> Result<DaggerGameplayState, RuntimeError> {
    /// Map a loot roll contract into one entity's deterministic spawn stream.
    fn loot_evidence(
        catalog: &DaggerGameplayCatalog,
        entity_id: u64,
        key: &str,
    ) -> Result<Vec<DaggerEvidence>, RuntimeError> {
        loot_roll_evidence(catalog, key)
            .map_err(RuntimeError::Gameplay)?
            .into_iter()
            .map(|(id, min, max)| {
                Ok(DaggerEvidence {
                    value: spawn_roll(entity_id, &id, min, max),
                    id,
                })
            })
            .collect()
    }

    let mut state = DaggerGameplayState::default();
    dagger_rpg::spawn_actor(&mut state, catalog, PLAYER_ACTOR_ID, PLAYER_ACTOR_ID, &[])
        .map_err(RuntimeError::Gameplay)?;
    for entity in content_entities {
        match entity.enemy() {
            Some(reference) => {
                let Some(definition) = catalog
                    .actors()
                    .values()
                    .find(|actor| actor.mobile_id == Some(reference.mobile_id))
                else {
                    continue;
                };
                let rolls = dagger_rpg::required_roll_evidence(catalog, &definition.id)
                    .map_err(RuntimeError::Gameplay)?;
                let evidence = rolls
                    .into_iter()
                    .map(|(id, min, max)| DaggerEvidence {
                        value: spawn_roll(entity.id, &id, min, max),
                        id,
                    })
                    .collect::<Vec<_>>();
                let instance = enemy_actor_id(entity.id);
                dagger_rpg::spawn_actor(
                    &mut state,
                    catalog,
                    &definition.id.clone(),
                    &instance,
                    &evidence,
                )
                .map_err(RuntimeError::Gameplay)?;
                if let Some(key) = definition.loot_table_key.clone() {
                    let evidence = loot_evidence(catalog, entity.id, &key)?;
                    let generation = bind_actor_loot(
                        &mut state,
                        catalog,
                        &instance,
                        &key,
                        LOOT_GENERATION_LEVEL,
                        &evidence,
                    )
                    .map_err(RuntimeError::Gameplay)?;
                    // Track the corpse container so the lab can enumerate it:
                    // the contents live in the actor's own inventory.
                    let container_entity =
                        state.actor(&instance).expect("just-spawned actor").entity();
                    state.insert_container(
                        instance,
                        DaggerContainerState::new(container_entity, key, generation),
                    );
                }
            }
            None => {
                let ContentEntityKind::Treasure { loot_key } = &entity.kind else {
                    continue;
                };
                let evidence = loot_evidence(catalog, entity.id, loot_key)?;
                spawn_container(
                    &mut state,
                    catalog,
                    &treasure_container_id(entity.id),
                    loot_key,
                    LOOT_GENERATION_LEVEL,
                    &evidence,
                )
                .map_err(RuntimeError::Gameplay)?;
            }
        }
    }
    Ok(state)
}

fn ai_mode_name(mode: EnemyAiMode) -> &'static str {
    match mode {
        EnemyAiMode::Patrol => "patrol",
        EnemyAiMode::Chase => "chase",
        EnemyAiMode::Attack => "attack",
        EnemyAiMode::Dead => "dead",
    }
}

#[cfg(test)]
mod aimed_melee_tests {
    use super::*;

    fn entity(id: u64) -> ContentEntity {
        ContentEntity {
            id,
            name: format!("enemy-{id}"),
            sprite_asset: "rat".to_string(),
            authored_position: [0.0; 3],
            kind: ContentEntityKind::Enemy(crate::project::EnemyContentReference {
                mobile_id: 0,
                mobile_name: "Rat".to_string(),
                texture_archive: 0,
                flying: false,
            }),
        }
    }

    #[test]
    fn ordinary_melee_aim_selects_a_live_target_in_front_without_lab_focus() {
        let entities = vec![entity(1), entity(2), entity(3)];
        let positions = BTreeMap::from([
            (1, [0.2, 0.0, -1.5]),
            (2, [1.0, 0.0, -1.2]),
            (3, [0.0, 0.0, 1.0]),
        ]);
        let resources = BTreeMap::from([
            (
                1,
                LiveActorResources {
                    current_health: 3.0,
                    current_stamina: 0.0,
                    current_magicka: 0.0,
                },
            ),
            (
                2,
                LiveActorResources {
                    current_health: 3.0,
                    current_stamina: 0.0,
                    current_magicka: 0.0,
                },
            ),
            (
                3,
                LiveActorResources {
                    current_health: 3.0,
                    current_stamina: 0.0,
                    current_magicka: 0.0,
                },
            ),
        ]);
        assert_eq!(
            select_aimed_melee_target(Vec3::ZERO, 0.0, 2.0, &entities, &positions, &resources,),
            Some(1),
        );
    }

    #[test]
    fn ordinary_melee_aim_ignores_dead_out_of_cone_and_out_of_range_targets() {
        let entities = vec![entity(1), entity(2), entity(3)];
        let positions = BTreeMap::from([
            (1, [0.0, 0.0, -1.0]),
            (2, [1.5, 0.0, 0.0]),
            (3, [0.0, 0.0, -3.0]),
        ]);
        let resources = BTreeMap::from([
            (
                1,
                LiveActorResources {
                    current_health: 0.0,
                    current_stamina: 0.0,
                    current_magicka: 0.0,
                },
            ),
            (
                2,
                LiveActorResources {
                    current_health: 3.0,
                    current_stamina: 0.0,
                    current_magicka: 0.0,
                },
            ),
            (
                3,
                LiveActorResources {
                    current_health: 3.0,
                    current_stamina: 0.0,
                    current_magicka: 0.0,
                },
            ),
        ]);
        assert_eq!(
            select_aimed_melee_target(Vec3::ZERO, 0.0, 2.0, &entities, &positions, &resources,),
            None,
        );
    }
}
