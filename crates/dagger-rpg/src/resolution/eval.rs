//! The single evaluator for derived-value expressions, plus the actor spawn
//! authority. Action resolution (policy.rs) and diagnostics both evaluate
//! through this module — there is no second arithmetic path. Spawning
//! attaches the Engine's mechanics stat/track components; resolution reads
//! and mutates them through mechanics services.

use std::collections::BTreeMap;

use rusty_engine::entity_state::{EntityAuthoringService, EntityDefinition, RelationshipCommand};
use rusty_engine::gameplay_mechanics::{
    CapacityMetricId, EquipmentComponent, EquipmentSlotId, InventoryCapacityLimit,
    InventoryComponent, InventoryService, ItemComponent, ItemDefinitionId, MechanicsScalar,
    OperationId, SourceInstanceId, SourceInstanceIdentity, StatId, StatValue, StatsComponent,
    TrackId, TrackValue, TracksComponent,
};
use rusty_engine::gameplay_standard::{
    CapabilityRoleBinding, CapabilityRoleBindings, CapabilityRoleId, ComposedExactComparison,
    ComposedExactExpr, ExactEvaluator, ExactExpr, ExactExprLimits, ExactInputBundle,
    ExactInputReference, StandardExactFactReference, StandardMechanicsReceipt, StandardOperation,
    StandardOperationContext,
};

use super::compile::{LEFT_HAND_SLOT, RIGHT_HAND_SLOT, WEIGHT_CAPACITY_METRIC};
use super::mechanics::{mechanics_catalog_version, track_max_stat_id};
use super::{
    armor_part_stat_id, struck_body_part_name, DaggerActorDefinition, DaggerActorState,
    DaggerEvidence, DaggerExactLeaf, DaggerExpr, DaggerGameplayCatalog, DaggerGameplayError,
    DaggerGameplayState, DaggerItemDefinition, DaggerOperation, DaggerPredicate, DaggerProgram,
    DaggerRejection, DaggerSubject,
};

/// Materialized stat values (attributes and skills) for one subject. The
/// caller materializes them: definition bases at spawn, live evaluated
/// values during resolution. `tracks` carries live current track values;
/// it is `None` where reading them would be circular (spawn, derived-rule
/// evaluation of a track maximum), so track reads there reject honestly.
/// `equipment` carries live equipment facts (equipped weapon, unarmed damage
/// range); it is `None` where no live entity exists (spawn, derived-rule
/// evaluation), so the equipped-weapon nodes reject honestly there.
pub struct ActorExprValues<'a> {
    pub definition: &'a DaggerActorDefinition,
    pub stats: &'a BTreeMap<String, i64>,
    pub tracks: Option<&'a BTreeMap<String, i64>>,
    pub equipment: Option<ActorEquipment<'a>>,
}

/// Live equipment facts for one subject: the equipped weapon item (right
/// hand first, then left — the donor's primary-hand order; `None` means
/// unarmed) and the subject's unarmed damage range, evaluated once by the
/// caller from the derived `hand-to-hand-min/max-damage` rules.
#[derive(Debug, Clone, Copy)]
pub struct ActorEquipment<'a> {
    pub weapon: Option<&'a DaggerItemDefinition>,
    pub unarmed_damage: (i64, i64),
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
    let mut inputs = Vec::new();
    let expression = materialize_exact(expr, context, &mut inputs)?;
    let inputs = ExactInputBundle::new(inputs).map_err(|error| {
        DaggerRejection::InvalidExpression(format!(
            "standard exact input evidence: {error:?}"
        ))
    })?;
    ExactEvaluator::evaluate(
        &expression,
        &inputs,
        ExactExprLimits::default(),
    )
    .map(|value| value.get())
    .map_err(|error| {
        DaggerRejection::InvalidExpression(format!("standard exact evaluator: {error:?}"))
    })
}

/// Lowers Dagger's closed product leaves to values, retaining the provider's
/// exact tree and typed input references unchanged. This adapter never owns
/// arithmetic, comparison, quotas, or overflow behavior.
fn materialize_exact(
    expr: &DaggerExpr,
    context: &ExprContext,
    inputs: &mut Vec<(ExactInputReference, MechanicsScalar)>,
) -> Result<ExactExpr, DaggerRejection> {
    match expr {
        ComposedExactExpr::Literal(value) => Ok(ExactExpr::Literal(*value)),
        ComposedExactExpr::Input(input) => {
            let value = materialize_input(input, context)?;
            inputs.push((input.clone(), scalar_rejection(value)?));
            Ok(ExactExpr::Input(input.clone()))
        }
        ComposedExactExpr::Add(left, right) => Ok(ExactExpr::Add(
            Box::new(materialize_exact(left, context, inputs)?),
            Box::new(materialize_exact(right, context, inputs)?),
        )),
        ComposedExactExpr::Subtract(left, right) => Ok(ExactExpr::Subtract(
            Box::new(materialize_exact(left, context, inputs)?),
            Box::new(materialize_exact(right, context, inputs)?),
        )),
        ComposedExactExpr::Multiply(left, right) => Ok(ExactExpr::Multiply(
            Box::new(materialize_exact(left, context, inputs)?),
            Box::new(materialize_exact(right, context, inputs)?),
        )),
        ComposedExactExpr::FloorDivide(left, right) => Ok(ExactExpr::FloorDivide(
            Box::new(materialize_exact(left, context, inputs)?),
            Box::new(materialize_exact(right, context, inputs)?),
        )),
        ComposedExactExpr::TruncatingDivide(left, right) => Ok(ExactExpr::TruncatingDivide(
            Box::new(materialize_exact(left, context, inputs)?),
            Box::new(materialize_exact(right, context, inputs)?),
        )),
        ComposedExactExpr::FixedPower {
            base,
            exponent,
            scale,
        } => Ok(ExactExpr::fixed_power(
            materialize_exact(base, context, inputs)?,
            materialize_exact(exponent, context, inputs)?,
            **scale,
        )),
        ComposedExactExpr::Min(values) => values
            .iter()
            .map(|value| materialize_exact(value, context, inputs))
            .collect::<Result<Vec<_>, _>>()
            .map(ExactExpr::Min),
        ComposedExactExpr::Max(values) => values
            .iter()
            .map(|value| materialize_exact(value, context, inputs))
            .collect::<Result<Vec<_>, _>>()
            .map(ExactExpr::Max),
        ComposedExactExpr::Product(leaf) => Ok(ExactExpr::Literal(scalar_rejection(
            evaluate_product_leaf(leaf.value(), context)?,
        )?)),
    }
}

fn materialize_input(
    input: &ExactInputReference,
    context: &ExprContext,
) -> Result<i64, DaggerRejection> {
    match input {
        ExactInputReference::Roll { id, .. } => evidence_value(context.evidence, id.as_str()),
        ExactInputReference::StandardFact(StandardExactFactReference::Stat { role, stat }) => {
            let values = context.subject_values(role_subject(role.as_str())?)?;
            values.stats.get(stat.as_str()).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!(
                    "stat.{}@{}",
                    stat.as_str(),
                    values.definition.id
                ))
            })
        }
        ExactInputReference::StandardFact(StandardExactFactReference::TrackCurrent {
            role,
            track,
        }) => {
            let values = context.subject_values(role_subject(role.as_str())?)?;
            values
                .tracks
                .and_then(|tracks| tracks.get(track.as_str()))
                .copied()
                .ok_or_else(|| {
                    DaggerRejection::MissingValue(format!(
                        "track.{}@{}",
                        track.as_str(),
                        values.definition.id
                    ))
                })
        }
        ExactInputReference::StandardFact(StandardExactFactReference::TrackMaximum {
            role,
            track,
        }) => {
            let values = context.subject_values(role_subject(role.as_str())?)?;
            let stat_id = track_max_stat_id(track.as_str());
            values.stats.get(&stat_id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("stat.{stat_id}@{}", values.definition.id))
            })
        }
        _ => Err(DaggerRejection::InvalidExpression(
            "unsupported non-Dagger exact input".to_string(),
        )),
    }
}

fn role_subject(role: &str) -> Result<DaggerSubject, DaggerRejection> {
    match role {
        "actor" => Ok(DaggerSubject::Actor),
        "target" => Ok(DaggerSubject::Target),
        _ => Err(DaggerRejection::InvalidExpression(format!(
            "unsupported Dagger role {role}"
        ))),
    }
}

fn evaluate_product_leaf(
    leaf: &DaggerExactLeaf,
    context: &ExprContext,
) -> Result<i64, DaggerRejection> {
    match leaf {
        DaggerExactLeaf::EquippedWeaponSkill { subject } => {
            let values = context.subject_values(*subject)?;
            let equipment = values.equipment.ok_or_else(|| {
                DaggerRejection::MissingValue(format!("equipment@{}", values.definition.id))
            })?;
            let skill_id = equipment
                .weapon
                .and_then(|item| item.weapon.as_ref())
                .map_or("hand-to-hand", |weapon| weapon.skill.as_str());
            values.stats.get(skill_id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("skill.{skill_id}@{}", values.definition.id))
            })
        }
        DaggerExactLeaf::Dice { id, min, max } => bounded_roll(context.evidence, id, *min, *max),
        DaggerExactLeaf::EquippedWeaponDice { subject, id } => {
            let values = context.subject_values(*subject)?;
            let equipment = values.equipment.ok_or_else(|| {
                DaggerRejection::MissingValue(format!("equipment@{}", values.definition.id))
            })?;
            let (min, max) = equipment
                .weapon
                .and_then(|item| item.weapon.as_ref())
                .map_or(equipment.unarmed_damage, |weapon| {
                    (weapon.damage_min, weapon.damage_max)
                });
            bounded_roll(context.evidence, id, min, max)
        }
        DaggerExactLeaf::StruckArmor { subject, id } => {
            let values = context.subject_values(*subject)?;
            let part = struck_body_part_name(bounded_roll(context.evidence, id, 0, 19)?)
                .expect("bounded body-part roll maps");
            let stat_id = armor_part_stat_id(part);
            values.stats.get(&stat_id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("stat.{stat_id}@{}", values.definition.id))
            })
        }
        DaggerExactLeaf::PowMilli {
            base,
            exponent_roll,
            ..
        } => {
            let base = *base;
            let exponent = evidence_value(context.evidence, exponent_roll)?;
            if base < 0 || !(0..=64).contains(&exponent) {
                return Err(DaggerRejection::InvalidExpression(format!("powMilli requires non-negative base and exponent 0..=64, got {base}^{exponent}")));
            }
            let mut result = 1_000_i64;
            for _ in 0..exponent {
                result = result
                    .checked_mul(base)
                    .ok_or_else(|| {
                        DaggerRejection::InvalidExpression("powMilli overflow".to_string())
                    })?
                    .div_euclid(1_000);
            }
            Ok(result)
        }
    }
}

fn bounded_roll(
    evidence: &[DaggerEvidence],
    id: &str,
    min: i64,
    max: i64,
) -> Result<i64, DaggerRejection> {
    let value = evidence_value(evidence, id)?;
    if (min..=max).contains(&value) {
        Ok(value)
    } else {
        Err(DaggerRejection::RollOutOfBounds {
            id: id.to_string(),
            value,
            min,
            max,
        })
    }
}

fn scalar_rejection(value: i64) -> Result<MechanicsScalar, DaggerRejection> {
    MechanicsScalar::new(value).map_err(|error| {
        DaggerRejection::InvalidExpression(format!("standard scalar rejected {value}: {error:?}"))
    })
}

pub(crate) fn evidence_value(
    evidence: &[DaggerEvidence],
    id: &str,
) -> Result<i64, DaggerRejection> {
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

/// Roll evidence an action's program requires: every `dice` node as
/// (evidence id, min, max). Callers supply values and pass them to
/// `resolve_dagger_action` alongside the action's hit-roll evidence
/// (convention: `{action}.d100`, an unbounded d100 read). Equipment-driven
/// nodes are NOT statically bound here — see `action_dynamic_roll_evidence`.
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
    collect_program_dice(&action.program, &mut rolls);
    Ok(rolls)
}

/// Evidence an action's program reads with bounds that depend on live
/// equipment state: `equippedWeaponDice` (bounded by the subject's CURRENTLY
/// equipped weapon's damage range, or its unarmed range) and `struckArmor`
/// (fixed 0..=19 struck-body-part roll). Callers roll these per attempt and
/// supply them alongside `action_roll_evidence` values.
pub fn action_dynamic_roll_evidence(
    catalog: &DaggerGameplayCatalog,
    action_id: &str,
) -> Result<Vec<(String, DaggerDynamicRoll)>, DaggerGameplayError> {
    let action =
        catalog
            .actions()
            .get(action_id)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actions[{action_id}]"),
                reason: "unknown action definition".to_string(),
            })?;
    let mut rolls = Vec::new();
    collect_program_dynamic_rolls(&action.program, &mut rolls);
    Ok(rolls)
}

/// The equipment-dependent evidence kinds an action program can read.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DaggerDynamicRoll {
    /// `equippedWeaponDice`: bounds are the acting subject's live weapon range.
    EquippedWeaponDamage,
    /// `struckArmor`: fixed 0..=19 struck-body-part roll.
    StruckBodyPart,
}

fn collect_program_dynamic_rolls(
    program: &DaggerProgram,
    rolls: &mut Vec<(String, DaggerDynamicRoll)>,
) {
    use rusty_engine::gameplay_resolution::Program;
    match program {
        Program::Sequence { steps } => {
            for step in steps {
                collect_program_dynamic_rolls(step, rolls);
            }
        }
        Program::When {
            predicate,
            then_program,
            otherwise_program,
        } => {
            collect_comparison_dynamic_rolls(predicate, rolls);
            collect_program_dynamic_rolls(then_program, rolls);
            if let Some(otherwise) = otherwise_program {
                collect_program_dynamic_rolls(otherwise, rolls);
            }
        }
        Program::Operation(operation) => match operation {
            DaggerOperation::SpendTrack { amount, .. } => collect_dynamic_rolls(amount, rolls),
            DaggerOperation::Damage { amount, .. } => collect_dynamic_rolls(amount, rolls),
        },
    }
}

fn collect_comparison_dynamic_rolls(
    predicate: &DaggerPredicate,
    rolls: &mut Vec<(String, DaggerDynamicRoll)>,
) {
    match predicate {
        ComposedExactComparison::Equal(left, right)
        | ComposedExactComparison::LessThan(left, right)
        | ComposedExactComparison::LessOrEqual(left, right)
        | ComposedExactComparison::GreaterThan(left, right)
        | ComposedExactComparison::GreaterOrEqual(left, right) => {
            collect_dynamic_rolls(left, rolls);
            collect_dynamic_rolls(right, rolls);
        }
    }
}

fn collect_dynamic_rolls(expr: &DaggerExpr, rolls: &mut Vec<(String, DaggerDynamicRoll)>) {
    match expr {
        ComposedExactExpr::Product(leaf) => match leaf.value() {
            DaggerExactLeaf::EquippedWeaponDice { id, .. } => {
                rolls.push((id.clone(), DaggerDynamicRoll::EquippedWeaponDamage))
            }
            DaggerExactLeaf::StruckArmor { id, .. } => {
                rolls.push((id.clone(), DaggerDynamicRoll::StruckBodyPart))
            }
            _ => {}
        },
        ComposedExactExpr::Add(left, right)
        | ComposedExactExpr::Subtract(left, right)
        | ComposedExactExpr::Multiply(left, right)
        | ComposedExactExpr::FloorDivide(left, right)
        | ComposedExactExpr::TruncatingDivide(left, right) => {
            collect_dynamic_rolls(left, rolls);
            collect_dynamic_rolls(right, rolls);
        }
        ComposedExactExpr::FixedPower { base, exponent, .. } => {
            collect_dynamic_rolls(base, rolls);
            collect_dynamic_rolls(exponent, rolls);
        }
        ComposedExactExpr::Min(values) | ComposedExactExpr::Max(values) => {
            for value in values {
                collect_dynamic_rolls(value, rolls);
            }
        }
        ComposedExactExpr::Literal(_) | ComposedExactExpr::Input(_) => {}
    }
}

fn collect_program_dice(program: &DaggerProgram, rolls: &mut Vec<(String, i64, i64)>) {
    use rusty_engine::gameplay_resolution::Program;
    match program {
        Program::Sequence { steps } => {
            for step in steps {
                collect_program_dice(step, rolls);
            }
        }
        Program::When {
            predicate,
            then_program,
            otherwise_program,
        } => {
            collect_comparison_dice(predicate, rolls);
            collect_program_dice(then_program, rolls);
            if let Some(otherwise) = otherwise_program {
                collect_program_dice(otherwise, rolls);
            }
        }
        Program::Operation(operation) => match operation {
            DaggerOperation::SpendTrack { amount, .. } => collect_dice(amount, rolls),
            DaggerOperation::Damage { amount, .. } => collect_dice(amount, rolls),
        },
    }
}

fn collect_comparison_dice(predicate: &DaggerPredicate, rolls: &mut Vec<(String, i64, i64)>) {
    match predicate {
        ComposedExactComparison::Equal(left, right)
        | ComposedExactComparison::LessThan(left, right)
        | ComposedExactComparison::LessOrEqual(left, right)
        | ComposedExactComparison::GreaterThan(left, right)
        | ComposedExactComparison::GreaterOrEqual(left, right) => {
            collect_dice(left, rolls);
            collect_dice(right, rolls);
        }
    }
}

fn collect_dice(expr: &DaggerExpr, rolls: &mut Vec<(String, i64, i64)>) {
    match expr {
        ComposedExactExpr::Product(leaf) => {
            if let DaggerExactLeaf::Dice { id, min, max } = leaf.value() {
                rolls.push((id.clone(), *min, *max));
            }
        }
        ComposedExactExpr::Add(left, right)
        | ComposedExactExpr::Subtract(left, right)
        | ComposedExactExpr::Multiply(left, right)
        | ComposedExactExpr::FloorDivide(left, right)
        | ComposedExactExpr::TruncatingDivide(left, right) => {
            collect_dice(left, rolls);
            collect_dice(right, rolls);
        }
        ComposedExactExpr::FixedPower { base, exponent, .. } => {
            collect_dice(base, rolls);
            collect_dice(exponent, rolls);
        }
        ComposedExactExpr::Min(values) | ComposedExactExpr::Max(values) => {
            for value in values {
                collect_dice(value, rolls);
            }
        }
        ComposedExactExpr::Literal(_) | ComposedExactExpr::Input(_) => {}
    }
}

/// One actor's equipped weapon item: the right-hand assignment first, then
/// the left hand (the donor's primary-hand order). A non-weapon assignment
/// (a shield in the left hand) is not a weapon; `None` means unarmed.
pub fn equipped_weapon<'a>(
    state: &DaggerGameplayState,
    catalog: &'a DaggerGameplayCatalog,
    actor_id: &str,
) -> Result<Option<&'a DaggerItemDefinition>, DaggerGameplayError> {
    let binding = state
        .actor(actor_id)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor_id}")))?;
    let equipment = state
        .entities()
        .component::<EquipmentComponent>(binding.entity())
        .map_err(|error| {
            DaggerGameplayError::InvalidState(format!("equipment component: {error}"))
        })?
        .ok_or_else(|| {
            DaggerGameplayError::InvalidState(format!("missing equipment component: {actor_id}"))
        })?;
    for slot in [RIGHT_HAND_SLOT, LEFT_HAND_SLOT] {
        let Some(assignment) = equipment
            .assignments()
            .iter()
            .find(|assignment| assignment.slot.as_str() == slot)
        else {
            continue;
        };
        let item = state
            .entities()
            .component::<ItemComponent>(assignment.item)
            .map_err(|error| DaggerGameplayError::InvalidState(format!("item component: {error}")))?
            .ok_or_else(|| {
                DaggerGameplayError::InvalidState(format!(
                    "equipped item entity missing ItemComponent: {actor_id}"
                ))
            })?;
        let definition = catalog
            .items()
            .get(item.definition().as_str())
            .ok_or_else(|| {
                DaggerGameplayError::InvalidState(format!(
                    "equipped item {} not in the catalog",
                    item.definition().as_str()
                ))
            })?;
        if definition.weapon.is_some() {
            return Ok(Some(definition));
        }
    }
    Ok(None)
}

/// One actor definition's base stats+skills map: the spawn/derived-rule
/// evaluation shape, also used by callers that need a definition-base
/// unarmed damage range for evidence bounds.
pub fn definition_base_stats(definition: &DaggerActorDefinition) -> BTreeMap<String, i64> {
    definition
        .stats
        .iter()
        .chain(definition.skills.iter())
        .map(|(id, value)| (id.clone(), *value))
        .collect()
}

/// One subject's unarmed damage range, evaluated from the derived
/// `hand-to-hand-min-damage`/`hand-to-hand-max-damage` rules against the
/// given stat map — the rules are the formula authority; nothing here
/// reimplements them. A package without those rules yields (0, 0), so an
/// unarmed `equippedWeaponDice` there rejects honestly out of bounds.
pub fn unarmed_damage_range(
    catalog: &DaggerGameplayCatalog,
    definition: &DaggerActorDefinition,
    stats: &BTreeMap<String, i64>,
) -> Result<(i64, i64), DaggerGameplayError> {
    let evaluate = |rule_id: &str| -> Result<Option<i64>, DaggerGameplayError> {
        let Some(rule) = catalog.derived().get(rule_id) else {
            return Ok(None);
        };
        let context = ExprContext {
            catalog,
            actor: ActorExprValues {
                definition,
                stats,
                // Unarmed damage is a pure stat formula; live tracks and
                // equipment do not exist in this nested evaluation.
                tracks: None,
                equipment: None,
            },
            target: None,
            evidence: &[],
        };
        evaluate_expr(&rule.expr, &context)
            .map(Some)
            .map_err(|rejection| DaggerGameplayError::InvalidValue {
                path: format!("derived[{rule_id}]"),
                reason: format!("unarmed damage rule rejected: {rejection:?}"),
            })
    };
    let Some(min) = evaluate("hand-to-hand-min-damage")? else {
        return Ok((0, 0));
    };
    let Some(max) = evaluate("hand-to-hand-max-damage")? else {
        return Ok((0, 0));
    };
    Ok((min, max.max(min)))
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
/// the bounded direct track-mutation helper; action attempts use
/// `resolve_dagger_action` for transactional resolution.
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
    let base_stats = definition_base_stats(definition);
    let context = ExprContext {
        catalog,
        actor: ActorExprValues {
            definition,
            stats: &base_stats,
            // Derived rules evaluate against definition bases; live track
            // currents and equipment do not exist in this context.
            tracks: None,
            equipment: None,
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
/// maximums (roll evidence supplied by the caller, so evaluation is
/// repeatable for the same inputs), create the entity, attach the mechanics
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
    // Track-current and equipment reads here would be circular or undefined,
    // so they reject honestly.
    let base_stats = definition_base_stats(definition);
    let context = ExprContext {
        catalog,
        actor: ActorExprValues {
            definition,
            stats: &base_stats,
            tracks: None,
            equipment: None,
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
    let mut stat_values = Vec::with_capacity(
        base_stats.len() + track_maxima.len() + catalog.stats().armor_parts.len(),
    );
    for (id, value) in &base_stats {
        stat_values.push(StatValue::new(
            StatId::parse(id.clone()).map_err(|error| DaggerGameplayError::InvalidId {
                path: format!("actors[{definition_id}].stats"),
                value: format!("{id}: {error:?}"),
            })?,
            scalar(*value, "stats")?,
        ));
    }
    // Every actor's armor parts start at the definition's flat armor value
    // (behavior-preserving: the flat value replicates per part). Equipped
    // armor and shield sources subtract from these bases at evaluation.
    for part in &catalog.stats().armor_parts {
        stat_values.push(StatValue::new(
            StatId::parse(armor_part_stat_id(part)).expect("validated armor part id"),
            scalar(definition.armor_value, "stats.armor")?,
        ));
    }
    // Player-kind actors carry the progression stat bases (xp 0, level 1).
    // Monsters do not — their classic `level` is definition data, unrelated
    // to the progression stats, and actor stat maps are never used for them.
    if definition.kind == super::DaggerActorKind::Player {
        for id in &catalog.stats().progression {
            let base = super::progression::progression_spawn_base(id).ok_or_else(|| {
                DaggerGameplayError::InvalidValue {
                    path: "stats.progression".to_string(),
                    reason: format!("no spawn base for progression stat {id}"),
                }
            })?;
            stat_values.push(StatValue::new(
                StatId::parse(id.clone()).expect("validated progression stat id"),
                scalar(base, "stats.progression")?,
            ));
        }
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

    // Capacity is spawn law: a loadout over the actor's capacity limits
    // rejects (the containment path binds unique items, so the check runs
    // against the post-bind inventory view rather than inside it; the view
    // itself rejects over-limit state).
    let view =
        InventoryService::view(state.entities(), catalog.mechanics(), entity).map_err(|error| {
            DaggerGameplayError::InvalidValue {
                path: format!("actors[{definition_id}].inventory"),
                reason: format!("loadout rejected by the inventory view: {error:?}"),
            }
        })?;
    for usage in view.capacity() {
        if let Some(maximum) = usage.maximum {
            if usage.used > maximum {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("actors[{definition_id}].inventory"),
                    reason: format!(
                        "loadout exceeds the {} capacity limit: {} > {maximum}",
                        usage.metric.as_str(),
                        usage.used
                    ),
                });
            }
        }
    }

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
            apply_standard_mechanics_operation(
                state,
                catalog,
                StandardOperation::GrantStack {
                    role: mechanics_role("loadout-owner"),
                    item: item_id,
                    quantity: entry.quantity,
                },
                vec![(mechanics_role("loadout-owner"), owner)],
                operation.clone(),
                source.clone(),
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("standard loadout grant rejected: {error:?}"),
            })?;
            continue;
        }
        // Unique items are entities: allocate, attach the ItemComponent, and
        // contain into the owner.
        let item_entity = bind_unique_item(
            state,
            owner,
            format!("{instance}:{}", entry.item),
            item_id,
            version.clone(),
            &path(),
        )?;
        if let Some(slot) = &entry.equip_slot {
            let slot_id = EquipmentSlotId::parse(slot.clone()).map_err(|error| {
                DaggerGameplayError::InvalidId {
                    path: path(),
                    value: format!("{slot}: {error:?}"),
                }
            })?;
            apply_standard_mechanics_operation(
                state,
                catalog,
                StandardOperation::EquipUniqueItem {
                    role: mechanics_role("loadout-owner"),
                    item: item_entity,
                    slots: vec![slot_id],
                },
                vec![(mechanics_role("loadout-owner"), owner)],
                operation.clone(),
                source.clone(),
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path(),
                reason: format!("standard loadout equip rejected: {error:?}"),
            })?;
        }
    }
    Ok(())
}

/// Plans and applies one explicitly selected standard mechanics leaf through
/// the Engine surface. Dagger supplies the named roles, correlation, and
/// source identity after it has made the product-specific selection; this
/// helper owns no item, loot, equipment, or transaction policy.
///
/// The Engine effect may only run against a private product candidate. Source
/// validation, candidate application, and the one state publication therefore
/// stay together here, so a rejected standard leaf cannot partially mutate a
/// spawned actor or generated loot container.
pub fn apply_standard_mechanics_operation(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    operation: StandardOperation,
    roles: Vec<(CapabilityRoleId, rusty_engine::core_ids::EntityId)>,
    correlation: OperationId,
    source: SourceInstanceIdentity,
) -> Result<StandardMechanicsReceipt, DaggerStandardMechanicsError> {
    let requirements = operation.requirements();
    let bindings = CapabilityRoleBindings::admit(
        &requirements,
        roles
            .into_iter()
            .map(|(role, entity)| {
                let capabilities = requirements
                    .iter()
                    .find(|requirement| requirement.role() == &role)
                    .map(|requirement| requirement.capabilities().to_vec())
                    .unwrap_or_default();
                CapabilityRoleBinding::new(role, entity, capabilities)
                    .expect("standard operation requirements fit one role")
            })
            .collect(),
    )
    .map_err(|error| {
        DaggerStandardMechanicsError::Planning(format!(
            "standard mechanics role bindings: {error:?}"
        ))
    })?;
    let context = StandardOperationContext::new(correlation, source).map_err(|error| {
        DaggerStandardMechanicsError::Planning(format!("standard mechanics context: {error:?}"))
    })?;
    let plan = operation
        .plan(
            &bindings,
            &ExactInputBundle::empty(),
            state.entities(),
            catalog.mechanics(),
            &context,
        )
        .map_err(|error| {
            DaggerStandardMechanicsError::Planning(format!("standard mechanics plan: {error:?}"))
        })?;
    plan.validate_source_state(state.entities(), catalog.mechanics())
        .map_err(|error| {
            DaggerStandardMechanicsError::Planning(format!(
                "standard mechanics source validation: {error:?}"
            ))
        })?;
    let mut candidate = state.clone();
    let receipt = plan
        .effect()
        .apply_to_candidate(candidate.entities_mut(), catalog.mechanics())
        .map_err(DaggerStandardMechanicsError::Mechanics)?;
    *state = candidate;
    Ok(receipt)
}

/// One Dagger product-boundary failure while applying an Engine standard
/// mechanics leaf. Candidate mechanics errors retain their upstream identity
/// so the runtime can still distinguish a capacity rejection in its own UI.
#[derive(Debug)]
pub enum DaggerStandardMechanicsError {
    Planning(String),
    Mechanics(rusty_engine::gameplay_mechanics::MechanicsError),
}

impl DaggerStandardMechanicsError {
    pub fn mechanics_error(&self) -> Option<&rusty_engine::gameplay_mechanics::MechanicsError> {
        match self {
            Self::Planning(_) => None,
            Self::Mechanics(error) => Some(error),
        }
    }
}

impl std::fmt::Display for DaggerStandardMechanicsError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Planning(reason) => formatter.write_str(reason),
            Self::Mechanics(error) => write!(formatter, "standard mechanics: {error:?}"),
        }
    }
}

impl std::error::Error for DaggerStandardMechanicsError {}

pub(crate) fn mechanics_role(value: &str) -> CapabilityRoleId {
    CapabilityRoleId::parse(value.to_string()).expect("fixed mechanics role identity")
}

/// Allocate one unique-item entity with an ItemComponent and contain it into
/// the owner — the shared binding step of spawn loadouts and loot
/// generation. `name` is the entity's authoring name; `path` reports errors.
pub(crate) fn bind_unique_item(
    state: &mut DaggerGameplayState,
    owner: rusty_engine::core_ids::EntityId,
    name: String,
    item_id: ItemDefinitionId,
    version: rusty_engine::gameplay_mechanics::CatalogVersion,
    path: &str,
) -> Result<rusty_engine::core_ids::EntityId, DaggerGameplayError> {
    let item_entity = state.allocate_entity();
    let state_revision = state.entities().revision();
    EntityAuthoringService
        .admit(
            state.entities_mut(),
            state_revision,
            [EntityDefinition::new(item_entity, name)],
        )
        .map_err(|error| DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: format!("item entity admission: {error}"),
        })?;
    let item_revision = state
        .entities()
        .component_revision::<ItemComponent>(item_entity)
        .map_err(|error| DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: format!("item component revision: {error}"),
        })?;
    EntityAuthoringService
        .attach_component(
            state.entities_mut(),
            item_revision,
            item_entity,
            ItemComponent::new(version, item_id),
        )
        .map_err(|error| DaggerGameplayError::InvalidValue {
            path: path.to_string(),
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
            path: path.to_string(),
            reason: format!("item containment: {error:?}"),
        })?;
    Ok(item_entity)
}
