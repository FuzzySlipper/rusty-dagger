//! The single evaluator for derived-value expressions, plus the actor spawn
//! authority. Action resolution (policy.rs) and diagnostics both evaluate
//! through this module — there is no second arithmetic path. Spawning
//! attaches the Engine's mechanics stat/track components; resolution reads
//! and mutates them through mechanics services.

use std::collections::BTreeMap;

use rusty_engine::entity_state::{EntityAuthoringService, EntityDefinition, RelationshipCommand};
use rusty_engine::gameplay_mechanics::{
    CapacityMetricId, EquipmentComponent, EquipmentEquipRequest, EquipmentService, EquipmentSlotId,
    InventoryCapacityLimit, InventoryComponent, InventoryMutationRequest, InventoryService,
    ItemComponent, ItemDefinitionId, MechanicsScalar, OperationId, SourceInstanceId,
    SourceInstanceIdentity, StatId, StatValue, StatsComponent, TrackId, TrackValue,
    TracksComponent,
};

use super::compile::WEIGHT_CAPACITY_METRIC;
use super::mechanics::{mechanics_catalog_version, track_max_stat_id};
use super::{
    DaggerActorDefinition, DaggerActorState, DaggerEvidence, DaggerExpr, DaggerGameplayCatalog,
    DaggerGameplayError, DaggerGameplayState, DaggerOperation, DaggerPredicate, DaggerProgram,
    DaggerRejection, DaggerSubject,
};

/// Materialized stat values (attributes and skills) for one subject. The
/// caller materializes them: definition bases at spawn, live evaluated
/// values during resolution. `tracks` carries live current track values;
/// it is `None` where reading them would be circular (spawn, derived-rule
/// evaluation of a track maximum), so track reads there reject honestly.
pub struct ActorExprValues<'a> {
    pub definition: &'a DaggerActorDefinition,
    pub stats: &'a BTreeMap<String, i64>,
    pub tracks: Option<&'a BTreeMap<String, i64>>,
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
        DaggerExpr::Track { subject, id } => {
            let values = context.subject_values(*subject)?;
            values
                .tracks
                .and_then(|tracks| tracks.get(id))
                .copied()
                .ok_or_else(|| {
                    DaggerRejection::MissingValue(format!("track.{id}@{}", values.definition.id))
                })
        }
        DaggerExpr::TrackMax { subject, id } => {
            let values = context.subject_values(*subject)?;
            let stat_id = track_max_stat_id(id);
            values.stats.get(&stat_id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("stat.{stat_id}@{}", values.definition.id))
            })
        }
        DaggerExpr::PowMilli { base, exponent } => {
            let base = evaluate_expr(base, context)?;
            let exponent = evaluate_expr(exponent, context)?;
            if base < 0 {
                return Err(DaggerRejection::InvalidExpression(format!(
                    "powMilli base must be non-negative, got {base}"
                )));
            }
            if !(0..=64).contains(&exponent) {
                return Err(DaggerRejection::InvalidExpression(format!(
                    "powMilli exponent must be 0..=64, got {exponent}"
                )));
            }
            // Fixed-point power scaled by 1000: floor division at each step
            // is the documented integer approximation of the donor's f64 pow.
            let mut accumulator = 1_000_i64;
            for _ in 0..exponent {
                accumulator = accumulator
                    .checked_mul(base)
                    .ok_or_else(|| {
                        DaggerRejection::InvalidExpression("powMilli overflow".to_string())
                    })?
                    .div_euclid(1_000);
            }
            Ok(accumulator)
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
        collect_dice(catalog, &track.max, &mut rolls);
    }
    Ok(rolls)
}

/// Roll evidence an action's program requires: every dice node as
/// (evidence id, min, max), with weapon dice bounded by the item's declared
/// damage range. Callers supply values and pass them to
/// `resolve_dagger_action` alongside the action's hit-roll evidence
/// (convention: `{action}.d100`, an unbounded d100 read).
pub fn action_roll_evidence(
    catalog: &DaggerGameplayCatalog,
    action_id: &str,
) -> Result<Vec<(String, i64, i64)>, DaggerGameplayError> {
    let action =
        catalog
            .actions()
            .get(action_id)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actions[{action_id}]"),
                reason: "unknown action definition".to_string(),
            })?;
    let mut rolls = Vec::new();
    collect_program_dice(catalog, &action.program, &mut rolls);
    Ok(rolls)
}

fn collect_program_dice(
    catalog: &DaggerGameplayCatalog,
    program: &DaggerProgram,
    rolls: &mut Vec<(String, i64, i64)>,
) {
    use rusty_engine::gameplay_resolution::Program;
    match program {
        Program::Sequence { steps } => {
            for step in steps {
                collect_program_dice(catalog, step, rolls);
            }
        }
        Program::When {
            predicate,
            then_program,
            otherwise_program,
        } => {
            let DaggerPredicate::Cmp { left, right, .. } = predicate;
            collect_dice(catalog, left, rolls);
            collect_dice(catalog, right, rolls);
            collect_program_dice(catalog, then_program, rolls);
            if let Some(otherwise) = otherwise_program {
                collect_program_dice(catalog, otherwise, rolls);
            }
        }
        Program::Operation(operation) => match operation {
            DaggerOperation::SpendTrack { amount, .. } => collect_dice(catalog, amount, rolls),
            DaggerOperation::Damage { amount, .. } => collect_dice(catalog, amount, rolls),
        },
    }
}

fn collect_dice(
    catalog: &DaggerGameplayCatalog,
    expr: &DaggerExpr,
    rolls: &mut Vec<(String, i64, i64)>,
) {
    match expr {
        DaggerExpr::Dice { id, min, max } => rolls.push((id.clone(), *min, *max)),
        DaggerExpr::WeaponDice { item } => {
            if let Some(weapon) = catalog
                .items()
                .get(item)
                .and_then(|definition| definition.weapon.as_ref())
            {
                rolls.push((
                    format!("weapon-damage.{item}"),
                    weapon.damage_min,
                    weapon.damage_max,
                ));
            }
        }
        DaggerExpr::Add { terms }
        | DaggerExpr::Mul { terms }
        | DaggerExpr::Min { terms }
        | DaggerExpr::Max { terms } => {
            for term in terms {
                collect_dice(catalog, term, rolls);
            }
        }
        DaggerExpr::Sub { left, right } | DaggerExpr::DivFloor { left, right } => {
            collect_dice(catalog, left, rolls);
            collect_dice(catalog, right, rolls);
        }
        DaggerExpr::PowMilli { base, exponent } => {
            collect_dice(catalog, base, rolls);
            collect_dice(catalog, exponent, rolls);
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

/// Evaluate one named derived rule against an actor definition's base
/// stats. Derived rules are the classic formula catalog; action resolution
/// and diagnostics evaluate them through this one authority.
pub fn evaluate_derived_rule(
    catalog: &DaggerGameplayCatalog,
    rule_id: &str,
    actor_id: &str,
    evidence: &[DaggerEvidence],
) -> Result<i64, DaggerGameplayError> {
    let rule = catalog
        .derived()
        .get(rule_id)
        .ok_or_else(|| DaggerGameplayError::InvalidValue {
            path: format!("derived[{rule_id}]"),
            reason: "unknown derived rule".to_string(),
        })?;
    let definition =
        catalog
            .actors()
            .get(actor_id)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actors[{actor_id}]"),
                reason: "unknown actor definition".to_string(),
            })?;
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
            // Derived rules evaluate against definition bases; live track
            // currents do not exist in this context.
            tracks: None,
        },
        target: None,
        evidence,
    };
    evaluate_expr(&rule.expr, &context).map_err(|rejection| DaggerGameplayError::InvalidValue {
        path: format!("derived[{rule_id}]"),
        reason: format!("evaluation rejected: {rejection:?}"),
    })
}

/// Spawn one actor instance: evaluate the definition's derived track
/// maximums (roll evidence supplied by the caller, so spawns are
/// deterministic and replayable), create the entity, attach the mechanics
/// stat/track components with live values, attach upstream
/// inventory/equipment components, and bind the authored loadout through
/// the upstream inventory/equipment services. `instance` is the
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
    // Track-current reads here would be circular (a track-max expression
    // reading the current value it defines), so they reject honestly.
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
            tracks: None,
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

    // Encumbrance convention: the actor's weight capacity limit is its
    // derived `max-encumbrance` rule (kg, classic floor(STR x 1.5)) converted
    // to the quarter-kg units the catalog weighs in, when both the rule and
    // the `weight` capacity metric exist in the package. Actors in a package
    // without either carry no limit.
    let mut capacity_limits = Vec::new();
    let declares_weight_metric = catalog
        .equipment()
        .capacity_metrics
        .iter()
        .any(|metric| metric == WEIGHT_CAPACITY_METRIC);
    if declares_weight_metric {
        if let Some(rule) = catalog.derived().get("max-encumbrance") {
            let kilograms = evaluate_expr(&rule.expr, &context).map_err(|rejection| {
                DaggerGameplayError::InvalidValue {
                    path: format!("actors[{definition_id}].derived[max-encumbrance]"),
                    reason: format!("encumbrance rule rejected: {rejection:?}"),
                }
            })?;
            let units = u64::try_from(kilograms)
                .ok()
                .and_then(|value| value.checked_mul(4))
                .ok_or_else(|| DaggerGameplayError::InvalidValue {
                    path: format!("actors[{definition_id}].derived[max-encumbrance]"),
                    reason: format!("encumbrance must be non-negative, got {kilograms}"),
                })?;
            capacity_limits.push(InventoryCapacityLimit::new(
                CapacityMetricId::parse(WEIGHT_CAPACITY_METRIC).expect("fixed metric identity"),
                units,
            ));
        }
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
    let tracks_component =
        TracksComponent::new(version.clone(), track_values).map_err(|error| {
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

    // Every actor carries upstream inventory and equipment components; the
    // authored loadout binds through the upstream services below.
    let inventory_component =
        InventoryComponent::with_capacity_limits(version.clone(), Vec::new(), capacity_limits)
            .map_err(|error| {
                DaggerGameplayError::InvalidState(format!("inventory component: {error:?}"))
            })?;
    let inventory_revision = state
        .entities()
        .component_revision::<InventoryComponent>(entity)
        .map_err(|error| {
            DaggerGameplayError::InvalidState(format!("inventory revision: {error}"))
        })?;
    EntityAuthoringService
        .attach_component(
            state.entities_mut(),
            inventory_revision,
            entity,
            inventory_component,
        )
        .map_err(|error| DaggerGameplayError::InvalidState(format!("attach inventory: {error}")))?;
    let equipment_component =
        EquipmentComponent::new(version.clone(), Vec::new()).map_err(|error| {
            DaggerGameplayError::InvalidState(format!("equipment component: {error:?}"))
        })?;
    let equipment_revision = state
        .entities()
        .component_revision::<EquipmentComponent>(entity)
        .map_err(|error| {
            DaggerGameplayError::InvalidState(format!("equipment revision: {error}"))
        })?;
    EntityAuthoringService
        .attach_component(
            state.entities_mut(),
            equipment_revision,
            entity,
            equipment_component,
        )
        .map_err(|error| DaggerGameplayError::InvalidState(format!("attach equipment: {error}")))?;

    bind_loadout(
        state,
        catalog,
        definition_id,
        definition,
        entity,
        instance,
        version,
    )?;

    state.insert_actor(instance, DaggerActorState::new(entity, definition_id));
    Ok(())
}

/// Bind one spawned actor's authored loadout through the upstream mechanics
/// services: fungible entries grant stacks, unique entries allocate an item
/// entity with an ItemComponent contained into the owner, and `equipSlot`
/// entries equip through the equipment service. Upstream rejections surface
/// with the item id in the error path.
fn bind_loadout(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    definition_id: &str,
    definition: &DaggerActorDefinition,
    owner: rusty_engine::core_ids::EntityId,
    instance: &str,
    version: rusty_engine::gameplay_mechanics::CatalogVersion,
) -> Result<(), DaggerGameplayError> {
    let operation = OperationId::parse("dagger-spawn-loadout").expect("fixed operation identity");
    let source = SourceInstanceIdentity::Request {
        operation: operation.clone(),
        instance: SourceInstanceId::parse("dagger-spawn").expect("fixed source identity"),
    };
    for (index, entry) in definition.inventory.iter().enumerate() {
        let path = || format!("actors[{definition_id}].inventory[{index}].{}", entry.item);
        let definition_ref =
            catalog
                .items()
                .get(&entry.item)
                .ok_or_else(|| DaggerGameplayError::InvalidValue {
                    path: path(),
                    reason: "unknown item".to_string(),
                })?;
        let item_id = ItemDefinitionId::parse(entry.item.clone()).map_err(|error| {
            DaggerGameplayError::InvalidId {
                path: path(),
                value: format!("{}: {error:?}", entry.item),
            }
        })?;
        if definition_ref.fungible {
            InventoryService::grant(
                state.entities_mut(),
                catalog.mechanics(),
                InventoryMutationRequest {
                    operation: operation.clone(),
                    source: source.clone(),
                    owner,
                    item: item_id,
                    quantity: entry.quantity,
                    expected_revision: None,
                },
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("loadout grant rejected: {error:?}"),
            })?;
            continue;
        }
        // Unique items are entities: allocate, attach the ItemComponent, and
        // contain into the owner.
        let item_entity = state.allocate_entity();
        let state_revision = state.entities().revision();
        EntityAuthoringService
            .admit(
                state.entities_mut(),
                state_revision,
                [EntityDefinition::new(
                    item_entity,
                    format!("{instance}:{}", entry.item),
                )],
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("item entity admission: {error}"),
            })?;
        let item_revision = state
            .entities()
            .component_revision::<ItemComponent>(item_entity)
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("item component revision: {error}"),
            })?;
        EntityAuthoringService
            .attach_component(
                state.entities_mut(),
                item_revision,
                item_entity,
                ItemComponent::new(version.clone(), item_id),
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("attach item component: {error}"),
            })?;
        let state_revision = state.entities().revision();
        state
            .entities_mut()
            .apply_relationship(
                state_revision,
                RelationshipCommand::SetContainment {
                    child: item_entity,
                    container: owner,
                },
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("item containment: {error:?}"),
            })?;
        if let Some(slot) = &entry.equip_slot {
            let slot_id = EquipmentSlotId::parse(slot.clone()).map_err(|error| {
                DaggerGameplayError::InvalidId {
                    path: path(),
                    value: format!("{slot}: {error:?}"),
                }
            })?;
            let state_revision = state.entities().revision();
            EquipmentService::equip(
                state.entities_mut(),
                catalog.mechanics(),
                EquipmentEquipRequest {
                    operation: operation.clone(),
                    source: source.clone(),
                    owner,
                    item: item_entity,
                    slots: vec![slot_id],
                    expected_equipment_revision: None,
                    expected_state_revision: state_revision,
                },
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("equip rejected: {error:?}"),
            })?;
        }
    }
    Ok(())
}
