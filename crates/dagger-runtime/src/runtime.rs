use std::collections::{BTreeMap, BTreeSet, VecDeque};

use dagger_rpg::{
    AdmittedActorValues, AdmittedEnemyValues, AdmittedExperiment, CalculationRecord,
    ExperimentDocument, ExperimentError, MeleeResolutionInput, MeleeResolutionRecord,
};
use rusty_engine::core_ids::EntityId;
use rusty_engine::core_math::Vec3;
use rusty_engine::engine_spatial::{SpatialCollisionHit, VoxelCollisionScene};
use rusty_engine::entity_state::EntityState;
use rusty_engine::svc_collision::{
    StaticMeshColliderInstance, StaticMeshInstanceId, StaticMeshTransform,
};
use serde::Serialize;

use crate::patrol::{EnemyAiMode, PatrolService, PositionUpdate};
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
    player_state: PlayerControllerState,
    player_start: Vec3,
    player_start_state: PlayerControllerState,
    experiment: AdmittedExperiment,
    player_resources: LiveActorResources,
    enemy_resources: BTreeMap<u64, LiveActorResources>,
    calculation_sequence: u64,
    calculation_history: VecDeque<SessionCalculationRecord>,
    combat_sequence: u64,
    combat_history: VecDeque<CombatRecord>,
    combat_attempt_sequence: u64,
    combat_attempt_history: VecDeque<CombatAttemptRecord>,
    player_attack_cooldown_remaining: f32,
    melee_presentation: Option<ActiveMeleePresentation>,
    encounter_sequence: u64,
    encounter_history: VecDeque<EncounterDecisionRecord>,
    dungeon_bounds: Option<([f64; 3], [f64; 3])>,
    content_entities: Vec<ContentEntity>,
    content_live_positions: BTreeMap<u64, [f32; 3]>,
    focused_content_id: Option<u64>,
    patrol: Option<PatrolService>,
}

pub const STARTER_EXPERIMENT_JSON: &str =
    include_str!("../../../data/experiments/privateers-hold-starter.json");
pub const CALCULATION_HISTORY_LIMIT: usize = 16;
pub const COMBAT_HISTORY_LIMIT: usize = 32;
pub const ENCOUNTER_HISTORY_LIMIT: usize = 32;
pub const MELEE_ANTICIPATION_SECONDS: f32 = 0.22;
pub const MELEE_CONTACT_SECONDS: f32 = 0.32;
pub const MELEE_RECOVERY_SECONDS: f32 = 0.72;
pub const MELEE_REJECTION_SECONDS: f32 = 0.72;
const MELEE_AIM_MIN_DOT: f32 = 0.64;

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExperimentReadout {
    pub document: ExperimentDocument,
    pub move_speed_units_per_second: f32,
    pub max_health: f32,
    pub current_health: f32,
    pub player_stats: ActorGameplayReadout,
    pub enemy_stats: Vec<EnemyStatsReadout>,
    pub player_position: [f32; 3],
    pub player_yaw_degrees: f32,
    pub calculations: Vec<SessionCalculationRecord>,
    pub combat: Vec<CombatRecord>,
    pub combat_attempts: Vec<CombatAttemptRecord>,
    pub player_attack_cooldown_remaining: f32,
    pub melee_presentation: Option<MeleePresentationReadout>,
    pub encounter_decisions: Vec<EncounterDecisionRecord>,
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

impl LiveActorResources {
    fn full(stats: &AdmittedActorValues) -> Self {
        Self {
            current_health: stats.max_health,
            current_stamina: stats.max_stamina,
            current_magicka: stats.max_magicka,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ActorGameplayReadout {
    pub attributes: dagger_rpg::ActorAttributes,
    pub max_health: f32,
    pub max_stamina: f32,
    pub max_magicka: f32,
    pub current_health: f32,
    pub current_stamina: f32,
    pub current_magicka: f32,
    pub calculations: Vec<CalculationRecord>,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EnemyStatsReadout {
    pub mobile_id: u8,
    pub stats: AdmittedActorValues,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CombatRecord {
    pub sequence: u64,
    pub target_id: u64,
    pub range: f32,
    pub attack_range: f32,
    pub line_of_sight_clear: bool,
    #[serde(flatten)]
    pub resolution: MeleeResolutionRecord,
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

/// A side-effect-free evaluation through the same admission and calculation
/// authority used when an experiment is applied to play.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExperimentEvaluation {
    pub document: ExperimentDocument,
    pub move_speed_units_per_second: f32,
    pub max_health: f32,
    pub calculation: CalculationRecord,
    pub player_stats: AdmittedActorValues,
    pub enemy_stats: Vec<AdmittedEnemyValues>,
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
        validate_enemy_definitions(&admitted.content_entities, &experiment)?;
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
            calculation: player_health_calculation(&experiment).clone(),
        };
        let player_start = admitted.player_start;
        let player_start_state = admitted.player_state;
        let player_resources = LiveActorResources::full(&experiment.player.stats);
        let enemy_resources = reset_enemy_resources(&admitted.content_entities, &experiment);
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
            player_resources,
            enemy_resources,
            calculation_sequence: 1,
            calculation_history: VecDeque::from([initial_calculation]),
            combat_sequence: 0,
            combat_history: VecDeque::new(),
            combat_attempt_sequence: 0,
            combat_attempt_history: VecDeque::new(),
            player_attack_cooldown_remaining: 0.0,
            melee_presentation: None,
            encounter_sequence: 0,
            encounter_history: VecDeque::new(),
            dungeon_bounds: admitted.dungeon_bounds,
            content_entities: admitted.content_entities,
            content_live_positions,
            focused_content_id: None,
            patrol: None,
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
        let spawns = self
            .content_entities
            .iter()
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
            .collect();
        self.patrol = Some(patrol);
        Ok(())
    }

    pub fn tick_play_session(&mut self, dt: f32) -> Result<Vec<PositionUpdate>, RuntimeError> {
        if !dt.is_finite() || !(0.0..=0.25).contains(&dt) {
            return Err(RuntimeError::Encounter(
                "play-session tick must be finite and bounded to 0.25 seconds".to_string(),
            ));
        }
        self.player_attack_cooldown_remaining =
            (self.player_attack_cooldown_remaining - dt).max(0.0);
        if let Some(presentation) = &mut self.melee_presentation {
            presentation.elapsed += dt;
            if presentation.is_complete() {
                self.melee_presentation = None;
            }
        }
        let player = self.player_position()?;
        let behaviors = self
            .content_entities
            .iter()
            .filter_map(|entity| {
                self.experiment
                    .enemies
                    .iter()
                    .find(|enemy| enemy.mobile_id == entity.mobile_id)
                    .and_then(|enemy| {
                        u32::try_from(entity.id)
                            .ok()
                            .map(|handle| (handle, enemy.behavior.clone()))
                    })
            })
            .collect::<BTreeMap<_, _>>();
        let dead = self
            .enemy_resources
            .iter()
            .filter_map(|(id, resources)| {
                (resources.current_health <= 0.0)
                    .then(|| u32::try_from(*id).ok())
                    .flatten()
            })
            .collect::<std::collections::BTreeSet<_>>();
        let Some(patrol) = self.patrol.as_mut() else {
            return Ok(Vec::new());
        };
        let evaluation =
            patrol.evaluate_encounters(dt, [player.x, player.y, player.z], &behaviors, &dead);
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
            let before = self.player_resources.current_health;
            let line_of_sight_clear = self.enemy_line_of_sight_clear(id, player);
            let damage = if before > 0.0 && line_of_sight_clear {
                attack.damage
            } else {
                0.0
            };
            let after = (before - damage).max(0.0);
            self.player_resources.current_health = after;
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
                damage: Some(damage),
                line_of_sight_clear: Some(line_of_sight_clear),
                player_health_before: Some(before),
                player_health_after: Some(after),
                player_died: before > 0.0 && after <= 0.0,
            });
        }
        Ok(evaluation.positions)
    }

    fn enemy_name(&self, id: u64) -> String {
        self.content_entities
            .iter()
            .find(|entity| entity.id == id)
            .map_or_else(
                || format!("Enemy {id}"),
                |entity| entity.mobile_name.clone(),
            )
    }

    fn enemy_line_of_sight_clear(&self, id: u64, player: Vec3) -> bool {
        let Some(enemy) = self.content_live_positions.get(&id) else {
            return false;
        };
        let delta = [player.x - enemy[0], player.z - enemy[2]];
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
            .raycast_world(
                [
                    f64::from(enemy[0]),
                    f64::from(player.y),
                    f64::from(enemy[2]),
                ],
                direction,
                clear_distance,
            )
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

    pub fn player_stamina(&self) -> (f32, f32) {
        (
            self.player_resources.current_stamina,
            self.experiment.player.stats.max_stamina,
        )
    }

    pub fn dead_encounter_ids(&self) -> BTreeSet<u32> {
        self.enemy_resources
            .iter()
            .filter_map(|(&id, resources)| {
                (resources.current_health <= 0.0)
                    .then(|| u32::try_from(id).ok())
                    .flatten()
            })
            .collect()
    }

    /// Admit and apply one complete authoring document. Admission and all
    /// calculations happen before live state changes, so a rejected edit
    /// cannot partially mutate the running experiment.
    pub fn apply_experiment_json(
        &mut self,
        document: &str,
    ) -> Result<ExperimentReadout, RuntimeError> {
        let admitted = dagger_rpg::admit_json(document).map_err(RuntimeError::Experiment)?;
        validate_enemy_definitions(&self.content_entities, &admitted)?;
        let enemy_resources = reset_enemy_resources(&self.content_entities, &admitted);
        self.calculation_sequence = self.calculation_sequence.saturating_add(1);
        self.player_controller.move_speed_units_per_second =
            admitted.player.move_speed_units_per_second;
        self.player_resources = LiveActorResources::full(&admitted.player.stats);
        self.enemy_resources = enemy_resources;
        self.combat_sequence = 0;
        self.combat_history.clear();
        self.combat_attempt_sequence = 0;
        self.combat_attempt_history.clear();
        self.player_attack_cooldown_remaining = 0.0;
        self.melee_presentation = None;
        self.encounter_sequence = 0;
        self.encounter_history.clear();
        self.calculation_history
            .push_back(SessionCalculationRecord {
                sequence: self.calculation_sequence,
                calculation: player_health_calculation(&admitted).clone(),
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
        validate_enemy_definitions(&self.content_entities, &admitted)?;
        Ok(ExperimentEvaluation {
            document: admitted.document.clone(),
            move_speed_units_per_second: admitted.player.move_speed_units_per_second,
            max_health: admitted.player.stats.max_health,
            calculation: player_health_calculation(&admitted).clone(),
            player_stats: admitted.player.stats,
            enemy_stats: admitted.enemies,
        })
    }

    /// Reset the playable run to the committed Privateer's Hold start while
    /// retaining the currently applied experiment document.
    pub fn reset_play_session(&mut self) -> Result<ExperimentReadout, RuntimeError> {
        self.set_player_position(self.player_start)?;
        self.player_state = self.player_start_state;
        self.player_resources = LiveActorResources::full(&self.experiment.player.stats);
        self.enemy_resources = reset_enemy_resources(&self.content_entities, &self.experiment);
        self.combat_sequence = 0;
        self.combat_history.clear();
        self.combat_attempt_sequence = 0;
        self.combat_attempt_history.clear();
        self.player_attack_cooldown_remaining = 0.0;
        self.melee_presentation = None;
        self.encounter_sequence = 0;
        self.encounter_history.clear();
        if let Some(patrol) = self.patrol.as_mut() {
            patrol.reset();
            self.content_live_positions = patrol
                .positions()
                .into_iter()
                .map(|(handle, position, _, _)| (u64::from(handle), position))
                .collect();
        }
        self.focused_content_id = None;
        self.experiment_readout()
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
                self.player_resources = LiveActorResources::full(&self.experiment.player.stats);
                self.enemy_resources =
                    reset_enemy_resources(&self.content_entities, &self.experiment);
                self.combat_sequence = 0;
                self.combat_history.clear();
                self.combat_attempt_sequence = 0;
                self.combat_attempt_history.clear();
                self.player_attack_cooldown_remaining = 0.0;
                self.melee_presentation = None;
                self.encounter_sequence = 0;
                self.encounter_history.clear();
                self.focused_content_id = Some(id);
                return self.experiment_readout();
            }
        }
        self.set_player_position(original_position)?;
        self.player_state = original_state;
        self.focused_content_id = original_focus;
        Err(RuntimeError::Content(ContentError::NoGroundedApproach(id)))
    }

    /// Attack the focused live enemy through Dagger's authoritative gameplay
    /// and collision state. The native renderer supplies only the physical
    /// input edge; it does not choose a target or resolve combat.
    pub fn attack_focused_target(&mut self) -> Result<ExperimentReadout, RuntimeError> {
        let target_id = self.focused_content_id;
        let cooldown_before = self.player_attack_cooldown_remaining;
        let stamina_before = self.player_resources.current_stamina;
        let cooldown_duration = self.experiment.player.combat.attack_cooldown_seconds;
        let stamina_cost = self.experiment.player.combat.stamina_cost;
        if cooldown_before > 0.0 {
            let sequence = self.push_combat_attempt(CombatAttemptRecord {
                sequence: 0,
                target_id,
                accepted: false,
                outcome: "cooldown".to_string(),
                cooldown_before,
                cooldown_after: cooldown_before,
                cooldown_duration,
                stamina_before,
                stamina_cost,
                stamina_after: stamina_before,
            });
            self.melee_presentation = Some(ActiveMeleePresentation {
                attempt_sequence: sequence,
                elapsed: 0.0,
                accepted: false,
                outcome: "cooldown".to_string(),
                target_id,
                stamina_before,
                stamina_after: stamina_before,
                target_health_before: None,
                target_health_after: None,
                target_max_health: None,
                final_damage: None,
                died: false,
            });
            return self.experiment_readout();
        }
        if stamina_before < stamina_cost {
            let sequence = self.push_combat_attempt(CombatAttemptRecord {
                sequence: 0,
                target_id,
                accepted: false,
                outcome: "insufficient stamina".to_string(),
                cooldown_before,
                cooldown_after: cooldown_before,
                cooldown_duration,
                stamina_before,
                stamina_cost,
                stamina_after: stamina_before,
            });
            self.melee_presentation = Some(ActiveMeleePresentation {
                attempt_sequence: sequence,
                elapsed: 0.0,
                accepted: false,
                outcome: "insufficient stamina".to_string(),
                target_id,
                stamina_before,
                stamina_after: stamina_before,
                target_health_before: None,
                target_health_after: None,
                target_max_health: None,
                final_damage: None,
                died: false,
            });
            return self.experiment_readout();
        }
        let attack_range = self.experiment.player.combat.attack_range;
        let player_position = self.player_position()?;
        let aimed_target = select_aimed_melee_target(
            player_position,
            self.player_state.yaw_degrees,
            attack_range,
            &self.content_entities,
            &self.content_live_positions,
            &self.enemy_resources,
        );
        let id = aimed_target
            .or(self.focused_content_id)
            .ok_or(RuntimeError::Content(ContentError::NoFocusedTarget))?;
        self.focused_content_id = Some(id);
        let entity = self
            .content_entities
            .iter()
            .find(|entity| entity.id == id)
            .ok_or(RuntimeError::Content(ContentError::UnknownEntity(id)))?;
        let enemy = self
            .experiment
            .enemies
            .iter()
            .find(|enemy| enemy.mobile_id == entity.mobile_id)
            .ok_or(RuntimeError::Content(ContentError::NoCombatDefinition(id)))?;
        let target_max_health = enemy.stats.max_health;
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
        // Melee reach is planar: imported mobile anchors sit at floor-relative
        // heights that are not comparable to the player's collider center.
        let distance = delta[0].hypot(delta[2]);
        if distance > attack_range {
            return Err(RuntimeError::Content(ContentError::OutOfRange {
                id,
                distance,
                maximum: attack_range,
            }));
        }
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
                return Err(RuntimeError::Content(ContentError::Occluded(id)));
            }
        }
        let health_before = self
            .enemy_resources
            .get(&id)
            .ok_or(RuntimeError::Content(ContentError::NoCombatDefinition(id)))?
            .current_health;
        if health_before <= 0.0 {
            return Err(RuntimeError::Content(ContentError::TargetDead(id)));
        }
        let raw_roll = next_combat_roll(self.combat_sequence, id);
        let resolution = dagger_rpg::resolve_melee_attack(MeleeResolutionInput {
            actor: "Player",
            target: &format!("{} {id}", entity.mobile_name),
            raw_roll,
            player: &self.experiment.player,
            enemy,
            target_health_before: health_before,
        });
        self.enemy_resources
            .get_mut(&id)
            .expect("combat definition checked above")
            .current_health = resolution.health_after;
        self.player_resources.current_stamina = (stamina_before - stamina_cost).max(0.0);
        self.player_attack_cooldown_remaining = cooldown_duration;
        let outcome = if resolution.died {
            "killed"
        } else if resolution.hit {
            "hit"
        } else {
            "miss"
        };
        let stamina_after = self.player_resources.current_stamina;
        let attempt_sequence = self.push_combat_attempt(CombatAttemptRecord {
            sequence: 0,
            target_id: Some(id),
            accepted: true,
            outcome: outcome.to_string(),
            cooldown_before,
            cooldown_after: cooldown_duration,
            cooldown_duration,
            stamina_before,
            stamina_cost,
            stamina_after,
        });
        self.melee_presentation = Some(ActiveMeleePresentation {
            attempt_sequence,
            elapsed: 0.0,
            accepted: true,
            outcome: outcome.to_string(),
            target_id: Some(id),
            stamina_before,
            stamina_after,
            target_health_before: Some(resolution.health_before),
            target_health_after: Some(resolution.health_after),
            target_max_health: Some(target_max_health),
            final_damage: Some(resolution.final_damage),
            died: resolution.died,
        });
        self.combat_sequence = self.combat_sequence.saturating_add(1);
        self.combat_history.push_back(CombatRecord {
            sequence: self.combat_sequence,
            target_id: id,
            range: distance,
            attack_range,
            line_of_sight_clear: true,
            resolution,
        });
        while self.combat_history.len() > COMBAT_HISTORY_LIMIT {
            self.combat_history.pop_front();
        }
        self.experiment_readout()
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

    pub fn experiment_readout(&self) -> Result<ExperimentReadout, RuntimeError> {
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
                        resources: self.enemy_resources.get(&entity.id).copied(),
                        ai_state: encounter_states.get(&entity.id).copied(),
                    },
                }
            })
            .collect();
        Ok(ExperimentReadout {
            document: self.experiment.document.clone(),
            move_speed_units_per_second: self.player_controller.move_speed_units_per_second,
            max_health: self.experiment.player.stats.max_health,
            current_health: self.player_resources.current_health,
            player_stats: actor_readout(&self.experiment.player.stats, self.player_resources),
            enemy_stats: self
                .experiment
                .enemies
                .iter()
                .map(|enemy| EnemyStatsReadout {
                    mobile_id: enemy.mobile_id,
                    stats: enemy.stats.clone(),
                })
                .collect(),
            player_position: [position.x, position.y, position.z],
            player_yaw_degrees: self.player_state.yaw_degrees,
            calculations: self.calculation_history.iter().cloned().collect(),
            combat: self.combat_history.iter().cloned().collect(),
            combat_attempts: self.combat_attempt_history.iter().cloned().collect(),
            player_attack_cooldown_remaining: self.player_attack_cooldown_remaining,
            melee_presentation: self.melee_presentation(),
            encounter_decisions: self.encounter_history.iter().cloned().collect(),
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

fn select_aimed_melee_target(
    player_position: Vec3,
    yaw_degrees: f32,
    attack_range: f32,
    entities: &[ContentEntity],
    live_positions: &BTreeMap<u64, [f32; 3]>,
    resources: &BTreeMap<u64, LiveActorResources>,
) -> Option<u64> {
    let yaw = yaw_degrees.to_radians();
    let forward = [-yaw.sin(), -yaw.cos()];
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

fn player_health_calculation(experiment: &AdmittedExperiment) -> &CalculationRecord {
    experiment
        .player
        .stats
        .calculations
        .first()
        .expect("admitted player stats always include max-health calculation")
}

fn validate_enemy_definitions(
    content_entities: &[ContentEntity],
    experiment: &AdmittedExperiment,
) -> Result<(), RuntimeError> {
    for definition in &experiment.enemies {
        if !content_entities
            .iter()
            .any(|entity| entity.mobile_id == definition.mobile_id)
        {
            return Err(RuntimeError::Experiment(ExperimentError::InvalidValue {
                path: format!("enemies[mobileId={}].mobileId", definition.mobile_id),
                reason: "does not identify an enemy in the admitted project".to_string(),
            }));
        }
    }
    Ok(())
}

fn reset_enemy_resources(
    content_entities: &[ContentEntity],
    experiment: &AdmittedExperiment,
) -> BTreeMap<u64, LiveActorResources> {
    content_entities
        .iter()
        .filter_map(|entity| {
            experiment
                .enemies
                .iter()
                .find(|enemy| enemy.mobile_id == entity.mobile_id)
                .map(|enemy| (entity.id, LiveActorResources::full(&enemy.stats)))
        })
        .collect()
}

fn actor_readout(stats: &AdmittedActorValues, current: LiveActorResources) -> ActorGameplayReadout {
    ActorGameplayReadout {
        attributes: stats.attributes.clone(),
        max_health: stats.max_health,
        max_stamina: stats.max_stamina,
        max_magicka: stats.max_magicka,
        current_health: current.current_health,
        current_stamina: current.current_stamina,
        current_magicka: current.current_magicka,
        calculations: stats.calculations.clone(),
    }
}

fn collision_hit_distance(hit: SpatialCollisionHit) -> f64 {
    match hit {
        SpatialCollisionHit::Voxel(hit) => hit.distance,
        SpatialCollisionHit::StaticMesh(hit) => hit.distance,
    }
}

fn ai_mode_name(mode: EnemyAiMode) -> &'static str {
    match mode {
        EnemyAiMode::Patrol => "patrol",
        EnemyAiMode::Chase => "chase",
        EnemyAiMode::Attack => "attack",
        EnemyAiMode::Dead => "dead",
    }
}

fn next_combat_roll(sequence: u64, target_id: u64) -> u8 {
    use std::time::{SystemTime, UNIX_EPOCH};
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_or(0, |duration| u64::from(duration.subsec_nanos()));
    let mut value = nanos ^ sequence.rotate_left(17) ^ target_id.rotate_left(31);
    value ^= value >> 12;
    value ^= value << 25;
    value ^= value >> 27;
    ((value.wrapping_mul(0x2545_F491_4F6C_DD1D) % 100) + 1) as u8
}

#[cfg(test)]
mod aimed_melee_tests {
    use super::*;

    fn entity(id: u64) -> ContentEntity {
        ContentEntity {
            id,
            name: format!("enemy-{id}"),
            mobile_id: 0,
            mobile_name: "Rat".to_string(),
            texture_archive: 0,
            flying: false,
            sprite_asset: "rat".to_string(),
            authored_position: [0.0; 3],
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
