//! The single evaluator for derived-value expressions, plus the actor spawn
//! authority. Action resolution (policy.rs) and diagnostics both evaluate
//! through this module — there is no second arithmetic path. Spawning
//! attaches the Engine's mechanics stat/track components; resolution reads
//! and mutates them through mechanics services.

use std::collections::BTreeMap;

use rusty_engine::entity_state::{EntityAuthoringService, EntityDefinition};
use rusty_engine::gameplay_mechanics::{
    MechanicsScalar, StatId, StatValue, StatsComponent, TrackId, TrackValue, TracksComponent,
};

use super::mechanics::{mechanics_catalog_version, track_max_stat_id};
use super::{
    DaggerActorDefinition, DaggerActorState, DaggerEvidence, DaggerExpr, DaggerGameplayCatalog,
    DaggerGameplayError, DaggerGameplayState, DaggerRejection, DaggerSubject,
};

/// Materialized stat values (attributes and skills) for one subject. The
/// caller materializes them: definition bases at spawn, live evaluated
/// values during resolution.
pub struct ActorExprValues<'a> {
    pub definition: &'a DaggerActorDefinition,
    pub stats: &'a BTreeMap<String, i64>,
}

/// Everything expression evaluation may read: the catalog (items, actor
/// definitions), materialized subject values, and caller-supplied evidence
/// (rolls and world facts).
pub struct ExprContext<'a> {
    pub catalog: &'a DaggerGameplayCatalog,
    pub actor: ActorExprValues<'a>,
    pub target: Option<ActorExprValues<'a>>,
    pub evidence: &'a [DaggerEvidence],
}

impl ExprContext<'_> {
    fn subject_values(
        &self,
        subject: DaggerSubject,
    ) -> Result<&ActorExprValues<'_>, DaggerRejection> {
        match subject {
            DaggerSubject::Actor => Ok(&self.actor),
            DaggerSubject::Target => self
                .target
                .as_ref()
                .ok_or_else(|| DaggerRejection::MissingValue("target".to_string())),
        }
    }
}

pub fn evaluate_expr(expr: &DaggerExpr, context: &ExprContext) -> Result<i64, DaggerRejection> {
    match expr {
        DaggerExpr::Const { value } => Ok(*value),
        DaggerExpr::Stat { subject, id } => {
            let values = context.subject_values(*subject)?;
            values.stats.get(id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("stat.{id}@{}", values.definition.id))
            })
        }
        DaggerExpr::Skill { subject, id } => {
            let values = context.subject_values(*subject)?;
            values.stats.get(id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("skill.{id}@{}", values.definition.id))
            })
        }
        DaggerExpr::Armor { subject } => {
            Ok(context.subject_values(*subject)?.definition.armor_value)
        }
        DaggerExpr::Evidence { id } => evidence_value(context.evidence, id),
        DaggerExpr::Dice { id, min, max } => {
            let value = evidence_value(context.evidence, id)?;
            if value < *min || value > *max {
                return Err(DaggerRejection::RollOutOfBounds {
                    id: id.clone(),
                    value,
                    min: *min,
                    max: *max,
                });
            }
            Ok(value)
        }
        DaggerExpr::WeaponDice { item } => {
            let weapon = context
                .catalog
                .items()
                .get(item)
                .and_then(|definition| definition.weapon.as_ref())
                .ok_or_else(|| DaggerRejection::MissingValue(format!("item.{item}.weapon")))?;
            let id = format!("weapon-damage.{item}");
            let value = evidence_value(context.evidence, &id)?;
            if value < weapon.damage_min || value > weapon.damage_max {
                return Err(DaggerRejection::RollOutOfBounds {
                    id,
                    value,
                    min: weapon.damage_min,
                    max: weapon.damage_max,
                });
            }
            Ok(value)
        }
        DaggerExpr::Add { terms } => terms.iter().try_fold(0_i64, |total, term| {
            let value = evaluate_expr(term, context)?;
            total
                .checked_add(value)
                .ok_or_else(|| DaggerRejection::InvalidExpression("add overflow".to_string()))
        }),
        DaggerExpr::Sub { left, right } => {
            let left = evaluate_expr(left, context)?;
            let right = evaluate_expr(right, context)?;
            left.checked_sub(right)
                .ok_or_else(|| DaggerRejection::InvalidExpression("sub overflow".to_string()))
        }
        DaggerExpr::Mul { terms } => terms.iter().try_fold(1_i64, |total, term| {
            let value = evaluate_expr(term, context)?;
            total
                .checked_mul(value)
                .ok_or_else(|| DaggerRejection::InvalidExpression("mul overflow".to_string()))
        }),
        DaggerExpr::DivFloor { left, right } => {
            let left = evaluate_expr(left, context)?;
            let right = evaluate_expr(right, context)?;
            if right == 0 {
                return Err(DaggerRejection::InvalidExpression(
                    "division by zero".to_string(),
                ));
            }
            Ok(left.div_euclid(right))
        }
        DaggerExpr::Min { terms } => terms
            .iter()
            .map(|term| evaluate_expr(term, context))
            .try_fold(None::<i64>, |best, value| {
                value.map(|value| Some(best.map_or(value, |best: i64| best.min(value))))
            })?
            .ok_or_else(|| DaggerRejection::InvalidExpression("min of no terms".to_string())),
        DaggerExpr::Max { terms } => terms
            .iter()
            .map(|term| evaluate_expr(term, context))
            .try_fold(None::<i64>, |best, value| {
                value.map(|value| Some(best.map_or(value, |best: i64| best.max(value))))
            })?
            .ok_or_else(|| DaggerRejection::InvalidExpression("max of no terms".to_string())),
    }
}

fn evidence_value(evidence: &[DaggerEvidence], id: &str) -> Result<i64, DaggerRejection> {
    evidence
        .iter()
        .find(|candidate| candidate.id == id)
        .map(|entry| entry.value)
        .ok_or_else(|| DaggerRejection::MissingEvidence(id.to_string()))
}

fn scalar(value: i64, path: &str) -> Result<MechanicsScalar, DaggerGameplayError> {
    MechanicsScalar::new(value).map_err(|error| DaggerGameplayError::InvalidValue {
        path: path.to_string(),
        reason: format!("mechanics scalar rejected: {error:?}"),
    })
}

/// Roll evidence a definition's derived track rules require: every dice
/// node as (evidence id, min, max). Callers supply values (deterministic or
/// random) and pass them to `spawn_actor`.
pub fn required_roll_evidence(
    catalog: &DaggerGameplayCatalog,
    definition_id: &str,
) -> Result<Vec<(String, i64, i64)>, DaggerGameplayError> {
    let definition =
        catalog
            .actors()
            .get(definition_id)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actors[{definition_id}]"),
                reason: "unknown actor definition".to_string(),
            })?;
    let mut rolls = Vec::new();
    for track in &definition.tracks {
        collect_dice(&track.max, &mut rolls);
    }
    Ok(rolls)
}

fn collect_dice(expr: &DaggerExpr, rolls: &mut Vec<(String, i64, i64)>) {
    match expr {
        DaggerExpr::Dice { id, min, max } => rolls.push((id.clone(), *min, *max)),
        DaggerExpr::Add { terms }
        | DaggerExpr::Mul { terms }
        | DaggerExpr::Min { terms }
        | DaggerExpr::Max { terms } => {
            for term in terms {
                collect_dice(term, rolls);
            }
        }
        DaggerExpr::Sub { left, right } | DaggerExpr::DivFloor { left, right } => {
            collect_dice(left, rolls);
            collect_dice(right, rolls);
        }
        _ => {}
    }
}

/// Current maximum of one actor's track: the evaluated derived rule stored
/// as the entity's `{track}-max` stat base at spawn.
pub fn track_maximum(
    state: &DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    actor_id: &str,
    track: &str,
) -> Option<i64> {
    let binding = state.actor(actor_id)?;
    let component = state
        .entities()
        .component::<StatsComponent>(binding.entity())
        .ok()??;
    let stat = StatId::parse(track_max_stat_id(track)).ok()?;
    let _ = catalog;
    component.base(&stat).map(|value| value.get())
}

/// Restore all of one actor's tracks to their spawn maxima. Used by runtime
/// play-session resets; not a gameplay mutation path.
pub fn restore_actor_tracks(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    actor_id: &str,
) -> Result<(), DaggerGameplayError> {
    let binding = state
        .actor(actor_id)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor_id}")))?;
    let tracks: Vec<String> = binding_tracks(state, binding)?;
    for track in tracks {
        let maximum = track_maximum(state, catalog, actor_id, &track).ok_or_else(|| {
            DaggerGameplayError::InvalidState(format!("no maximum for {track}@{actor_id}"))
        })?;
        set_actor_track(state, catalog, actor_id, &track, maximum)?;
    }
    Ok(())
}

fn binding_tracks(
    state: &DaggerGameplayState,
    binding: &DaggerActorState,
) -> Result<Vec<String>, DaggerGameplayError> {
    let component = state
        .entities()
        .component::<TracksComponent>(binding.entity())
        .map_err(|error| DaggerGameplayError::InvalidState(format!("tracks component: {error}")))?
        .ok_or_else(|| DaggerGameplayError::InvalidState("missing tracks component".to_string()))?;
    Ok(component
        .values()
        .iter()
        .map(|value| value.track().as_str().to_string())
        .collect())
}

/// Spend from one actor's track through the mechanics track service,
/// clamped to the available amount, returning the value afterwards. This is
/// the runtime's live mutation path while 7046 moves attempts onto
/// `resolve_dagger_action`.
pub fn spend_actor_track(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    actor_id: &str,
    track: &str,
    amount: i64,
) -> Result<i64, DaggerGameplayError> {
    use rusty_engine::gameplay_mechanics::{
        OperationId, SourceInstanceId, SourceInstanceIdentity, TrackAdjustmentKind,
        TrackMutationRequest, TrackService,
    };

    let entity = state
        .actor(actor_id)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor_id}")))?
        .entity();
    let current = state.track_value(actor_id, track).unwrap_or(0);
    let amount = amount.clamp(0, current);
    let operation = OperationId::parse("dagger-runtime-spend").expect("fixed operation identity");
    TrackService::spend(
        state.entities_mut(),
        catalog.mechanics(),
        TrackMutationRequest {
            operation: operation.clone(),
            source: SourceInstanceIdentity::Request {
                operation,
                instance: SourceInstanceId::parse("dagger-runtime").expect("fixed source identity"),
            },
            entity,
            track: TrackId::parse(track).map_err(|error| DaggerGameplayError::InvalidId {
                path: "track".to_string(),
                value: format!("{track}: {error:?}"),
            })?,
            amount: scalar(amount, "tracks.spend")?,
            kind: TrackAdjustmentKind::Spend,
            expected_revision: None,
        },
    )
    .map_err(|error| {
        DaggerGameplayError::InvalidState(format!("spend {amount} {track}@{actor_id}: {error:?}"))
    })?;
    Ok(state.track_value(actor_id, track).unwrap_or(0))
}

/// Set one actor's track value under mechanics policy (clamped to bounds).
/// Used by runtime resets and test setups; ordinary gameplay mutation goes
/// through resolution effects, not this direct path.
pub fn set_actor_track(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    actor_id: &str,
    track: &str,
    value: i64,
) -> Result<(), DaggerGameplayError> {
    use rusty_engine::gameplay_mechanics::{
        OperationId, SourceInstanceId, SourceInstanceIdentity, TrackService, TrackSetPolicy,
        TrackSetRequest,
    };

    let entity = state
        .actor(actor_id)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor_id}")))?
        .entity();
    let operation = OperationId::parse("dagger-track-set").expect("fixed operation identity");
    TrackService::set_under_policy(
        state.entities_mut(),
        catalog.mechanics(),
        TrackSetRequest {
            operation: operation.clone(),
            source: SourceInstanceIdentity::Request {
                operation,
                instance: SourceInstanceId::parse("dagger-track-set")
                    .expect("fixed source identity"),
            },
            entity,
            track: TrackId::parse(track).map_err(|error| DaggerGameplayError::InvalidId {
                path: "track".to_string(),
                value: format!("{track}: {error:?}"),
            })?,
            value: scalar(value, "tracks.set")?,
            policy: TrackSetPolicy::ClampToBounds,
            expected_revision: None,
        },
    )
    .map_err(|error| {
        DaggerGameplayError::InvalidState(format!("set {track} for {actor_id}: {error:?}"))
    })?;
    Ok(())
}

/// Spawn one actor instance: evaluate the definition's derived track
/// maximums (roll evidence supplied by the caller, so spawns are
/// deterministic and replayable), create the entity, and attach the
/// mechanics stat/track components with live values. `instance` is the
/// scenario-owned actor key (many instances may share one definition).
/// This is the authority every consumer (diagnostics, runtime) uses to
/// spawn an actor.
pub fn spawn_actor(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    definition_id: &str,
    instance: &str,
    evidence: &[DaggerEvidence],
) -> Result<(), DaggerGameplayError> {
    let definition =
        catalog
            .actors()
            .get(definition_id)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actors[{definition_id}]"),
                reason: "unknown actor definition".to_string(),
            })?;
    // Spawn evaluation reads definition base stats; the components that
    // would carry live values are exactly what this spawn is constructing.
    let base_stats: BTreeMap<String, i64> = definition
        .stats
        .iter()
        .chain(definition.skills.iter())
        .map(|(id, value)| (id.clone(), *value))
        .collect();
    let context = ExprContext {
        catalog,
        actor: ActorExprValues {
            definition,
            stats: &base_stats,
        },
        target: None,
        evidence,
    };
    let mut track_maxima = Vec::with_capacity(definition.tracks.len());
    for track in &definition.tracks {
        let value = evaluate_expr(&track.max, &context).map_err(|rejection| {
            DaggerGameplayError::InvalidValue {
                path: format!("actors[{definition_id}].tracks[{}]", track.id),
                reason: format!("derived track maximum rejected: {rejection:?}"),
            }
        })?;
        if value < 0 {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("actors[{definition_id}].tracks[{}]", track.id),
                reason: format!("derived track maximum must be non-negative, got {value}"),
            });
        }
        track_maxima.push((track.id.clone(), value));
    }

    let entity = state.allocate_entity();
    let state_revision = state.entities().revision();
    EntityAuthoringService
        .admit(
            state.entities_mut(),
            state_revision,
            [EntityDefinition::new(entity, instance)],
        )
        .map_err(|error| DaggerGameplayError::InvalidState(format!("entity admission: {error}")))?;

    let version = mechanics_catalog_version();
    let mut stat_values = Vec::with_capacity(base_stats.len() + track_maxima.len());
    for (id, value) in &base_stats {
        stat_values.push(StatValue::new(
            StatId::parse(id.clone()).map_err(|error| DaggerGameplayError::InvalidId {
                path: format!("actors[{definition_id}].stats"),
                value: format!("{id}: {error:?}"),
            })?,
            scalar(*value, "stats")?,
        ));
    }
    for (track, maximum) in &track_maxima {
        stat_values.push(StatValue::new(
            StatId::parse(track_max_stat_id(track)).expect("compiled track id"),
            scalar(*maximum, "tracks.max")?,
        ));
    }
    let stats_component = StatsComponent::new(version.clone(), stat_values).map_err(|error| {
        DaggerGameplayError::InvalidState(format!("stats component: {error:?}"))
    })?;
    let track_values = track_maxima
        .iter()
        .map(|(track, maximum)| {
            Ok(TrackValue::new(
                TrackId::parse(track.clone()).expect("compiled track id"),
                scalar(*maximum, "tracks.current")?,
            ))
        })
        .collect::<Result<Vec<_>, DaggerGameplayError>>()?;
    let tracks_component = TracksComponent::new(version, track_values).map_err(|error| {
        DaggerGameplayError::InvalidState(format!("tracks component: {error:?}"))
    })?;

    let stats_revision = state
        .entities()
        .component_revision::<StatsComponent>(entity)
        .map_err(|error| DaggerGameplayError::InvalidState(format!("stats revision: {error}")))?;
    EntityAuthoringService
        .attach_component(
            state.entities_mut(),
            stats_revision,
            entity,
            stats_component,
        )
        .map_err(|error| DaggerGameplayError::InvalidState(format!("attach stats: {error}")))?;
    let tracks_revision = state
        .entities()
        .component_revision::<TracksComponent>(entity)
        .map_err(|error| DaggerGameplayError::InvalidState(format!("tracks revision: {error}")))?;
    EntityAuthoringService
        .attach_component(
            state.entities_mut(),
            tracks_revision,
            entity,
            tracks_component,
        )
        .map_err(|error| DaggerGameplayError::InvalidState(format!("attach tracks: {error}")))?;

    state.insert_actor(instance, DaggerActorState::new(entity, definition_id));
    Ok(())
}
