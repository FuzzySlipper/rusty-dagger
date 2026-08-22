//! The single evaluator for derived-value expressions, plus the actor spawn
//! authority. Action resolution (policy.rs) and diagnostics both evaluate
//! through this module — there is no second arithmetic path. Spawning
//! attaches the Engine's mechanics stat/track components; resolution reads
//! and mutates them through mechanics services.

use std::collections::BTreeMap;

use rusty_engine::entity_state::{EntityAuthoringService, EntityDefinition};
use rusty_engine::gameplay_mechanics::{
    CapacityMetricId, EquipmentComponent, EquipmentSlotId, InventoryCapacityLimit,
    InventoryComponent, InventoryService, ItemComponent, ItemDefinitionId, ItemService,
    MechanicsScalar, OperationId, SourceInstanceId, SourceInstanceIdentity, StatId, StatValue,
    StatsComponent, TrackId, TrackValue, TracksComponent, UniqueItemMaterializationReceipt,
    UniqueItemMaterializationRequest,
};
use rusty_engine::gameplay_standard::{
    BoundedSample, BoundedSampleKey, BoundedSamplePlan, BoundedSamplePlanError,
    BoundedSamplePlanIdentity, BoundedSamplePlanVersion, BoundedSampleReceipt,
    BoundedSampleRequirement, CapabilityRoleBinding, CapabilityRoleBindings, CapabilityRoleId,
    ComposedExactComparison, ComposedExactExpr, ExactEvaluationError, ExactEvaluator, ExactExpr,
    ExactExprLimits, ExactInputBundle, ExactInputReference, StandardExactFactReference,
    StandardMechanicsReceipt, StandardOperation, StandardOperationContext,
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
    let requirements = expression_bounded_requirements(expr, context)?;
    let receipt = if requirements.is_empty() {
        None
    } else {
        Some(bounded_sample_receipt(
            "dagger.expression",
            &requirements,
            context.evidence,
            false,
        )?)
    };
    evaluate_expr_with_receipt(expr, context, receipt.as_ref())
}

/// Evaluate an expression against an already validated action evidence set.
///
/// Standard resolution evaluates several subexpressions from one action
/// program. The caller validates that complete program's bounded evidence once
/// and passes the resulting Engine receipt through each subexpression so an
/// unrelated branch's evidence is not mistaken for an unknown sample.
pub(crate) fn evaluate_expr_with_receipt(
    expr: &DaggerExpr,
    context: &ExprContext,
    receipt: Option<&BoundedSampleReceipt>,
) -> Result<i64, DaggerRejection> {
    let mut inputs = Vec::new();
    let expression = materialize_exact(expr, context, receipt, &mut inputs)?;
    let inputs = ExactInputBundle::new(inputs).map_err(|error| {
        DaggerRejection::InvalidExpression(format!("standard exact input evidence: {error:?}"))
    })?;
    ExactEvaluator::evaluate(&expression, &inputs, ExactExprLimits::default())
        .map(|value| value.get())
        .map_err(standard_exact_rejection)
}

fn standard_exact_rejection(error: ExactEvaluationError) -> DaggerRejection {
    match error {
        ExactEvaluationError::BoundedRollOutOfRange {
            input: ExactInputReference::BoundedRoll { descriptor },
            value,
        } => DaggerRejection::RollOutOfBounds {
            id: descriptor.id().as_str().to_owned(),
            value: value.get(),
            min: descriptor.minimum().get(),
            max: descriptor.maximum().get(),
        },
        ExactEvaluationError::MissingBoundedRoll {
            input: ExactInputReference::BoundedRoll { descriptor },
        } => DaggerRejection::MissingEvidence(descriptor.id().as_str().to_owned()),
        error => DaggerRejection::InvalidExpression(format!("standard exact evaluator: {error:?}")),
    }
}

/// Lowers Dagger's closed product leaves to values, retaining the provider's
/// exact tree and typed input references unchanged. This adapter never owns
/// arithmetic, comparison, quotas, or overflow behavior.
fn materialize_exact(
    expr: &DaggerExpr,
    context: &ExprContext,
    receipt: Option<&BoundedSampleReceipt>,
    inputs: &mut Vec<(ExactInputReference, MechanicsScalar)>,
) -> Result<ExactExpr, DaggerRejection> {
    match expr {
        ComposedExactExpr::Literal(value) => Ok(ExactExpr::Literal(*value)),
        ComposedExactExpr::Input(input) => {
            let value = materialize_input(input, context, receipt)?;
            inputs.push((input.clone(), scalar_rejection(value)?));
            Ok(ExactExpr::Input(input.clone()))
        }
        ComposedExactExpr::Add(left, right) => Ok(ExactExpr::Add(
            Box::new(materialize_exact(left, context, receipt, inputs)?),
            Box::new(materialize_exact(right, context, receipt, inputs)?),
        )),
        ComposedExactExpr::Subtract(left, right) => Ok(ExactExpr::Subtract(
            Box::new(materialize_exact(left, context, receipt, inputs)?),
            Box::new(materialize_exact(right, context, receipt, inputs)?),
        )),
        ComposedExactExpr::Multiply(left, right) => Ok(ExactExpr::Multiply(
            Box::new(materialize_exact(left, context, receipt, inputs)?),
            Box::new(materialize_exact(right, context, receipt, inputs)?),
        )),
        ComposedExactExpr::FloorDivide(left, right) => Ok(ExactExpr::FloorDivide(
            Box::new(materialize_exact(left, context, receipt, inputs)?),
            Box::new(materialize_exact(right, context, receipt, inputs)?),
        )),
        ComposedExactExpr::TruncatingDivide(left, right) => Ok(ExactExpr::TruncatingDivide(
            Box::new(materialize_exact(left, context, receipt, inputs)?),
            Box::new(materialize_exact(right, context, receipt, inputs)?),
        )),
        ComposedExactExpr::FixedPower {
            base,
            exponent,
            scale,
        } => Ok(ExactExpr::fixed_power(
            materialize_exact(base, context, receipt, inputs)?,
            materialize_exact(exponent, context, receipt, inputs)?,
            **scale,
        )),
        ComposedExactExpr::Min(values) => values
            .iter()
            .map(|value| materialize_exact(value, context, receipt, inputs))
            .collect::<Result<Vec<_>, _>>()
            .map(ExactExpr::Min),
        ComposedExactExpr::Max(values) => values
            .iter()
            .map(|value| materialize_exact(value, context, receipt, inputs))
            .collect::<Result<Vec<_>, _>>()
            .map(ExactExpr::Max),
        ComposedExactExpr::Product(leaf) => Ok(ExactExpr::Literal(scalar_rejection(
            evaluate_product_leaf(leaf.value(), context, receipt)?,
        )?)),
    }
}

fn materialize_input(
    input: &ExactInputReference,
    context: &ExprContext,
    receipt: Option<&BoundedSampleReceipt>,
) -> Result<i64, DaggerRejection> {
    match input {
        ExactInputReference::BoundedRoll { descriptor } => {
            let receipt = receipt.ok_or_else(|| {
                DaggerRejection::InvalidExpression(
                    "bounded evidence receipt missing for expression".to_string(),
                )
            })?;
            bounded_sample_value(receipt, descriptor.id().as_str())
        }
        ExactInputReference::Roll { id, .. } => {
            unbounded_evidence_value(context.evidence, id.as_str())
        }
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
    receipt: Option<&BoundedSampleReceipt>,
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
        DaggerExactLeaf::EquippedWeaponDice { subject, id } => {
            let values = context.subject_values(*subject)?;
            let equipment = values.equipment.ok_or_else(|| {
                DaggerRejection::MissingValue(format!("equipment@{}", values.definition.id))
            })?;
            let _ = equipment;
            let receipt = receipt.ok_or_else(|| {
                DaggerRejection::InvalidExpression(
                    "bounded evidence receipt missing for product leaf".to_string(),
                )
            })?;
            bounded_sample_value(receipt, id)
        }
        DaggerExactLeaf::StruckArmor { subject, id } => {
            let values = context.subject_values(*subject)?;
            let receipt = receipt.ok_or_else(|| {
                DaggerRejection::InvalidExpression(
                    "bounded evidence receipt missing for product leaf".to_string(),
                )
            })?;
            let part = struck_body_part_name(bounded_sample_value(receipt, id)?)
                .expect("bounded body-part roll maps");
            let stat_id = armor_part_stat_id(part);
            values.stats.get(&stat_id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("stat.{stat_id}@{}", values.definition.id))
            })
        }
    }
}

fn scalar_rejection(value: i64) -> Result<MechanicsScalar, DaggerRejection> {
    MechanicsScalar::new(value).map_err(|error| {
        DaggerRejection::InvalidExpression(format!("standard scalar rejected {value}: {error:?}"))
    })
}

/// Read a caller-supplied unbounded fact. This is intentionally separate from
/// Engine's `BoundedSamplePlan`: `ExactInputReference::Roll` carries no
/// declared bounds (for example the product's hit d100 and derived facts), so
/// there is no honest plan requirement to validate here. Every bounded input
/// and bounded product leaf is admitted and read through the Engine receipt
/// above.
fn unbounded_evidence_value(evidence: &[DaggerEvidence], id: &str) -> Result<i64, DaggerRejection> {
    evidence
        .iter()
        .find(|candidate| candidate.id == id)
        .map(|entry| entry.value)
        .ok_or_else(|| DaggerRejection::MissingEvidence(id.to_string()))
}

/// Build and validate one Engine-owned bounded evidence receipt. `strict`
/// controls whether extra product evidence is rejected; action and loot
/// boundaries use strict validation, while the public expression helper keeps
/// its historical ability to evaluate one expression from a larger evidence
/// vector.
pub(crate) fn bounded_sample_receipt(
    identity: &str,
    requirements: &[(String, i64, i64)],
    evidence: &[DaggerEvidence],
    strict: bool,
) -> Result<BoundedSampleReceipt, DaggerRejection> {
    if requirements.is_empty() {
        return Err(DaggerRejection::InvalidExpression(
            "bounded evidence plan must contain at least one requirement".to_string(),
        ));
    }
    let identity = BoundedSamplePlanIdentity::parse(identity.to_string()).map_err(|error| {
        DaggerRejection::InvalidExpression(format!("bounded evidence identity: {error:?}"))
    })?;
    let version = BoundedSamplePlanVersion::new(1).map_err(|error| {
        DaggerRejection::InvalidExpression(format!("bounded evidence version: {error:?}"))
    })?;
    let mut normalized_requirements: Vec<BoundedSampleRequirement> = Vec::new();
    for (id, minimum, maximum) in requirements {
        let key = BoundedSampleKey::parse(id.to_ascii_lowercase()).map_err(|error| {
            DaggerRejection::InvalidExpression(format!("bounded evidence key: {error:?}"))
        })?;
        // A repeated reference is one sample requirement, even when the
        // expression reaches it through multiple paths. Preserve the first
        // declaration's order, but reject a contradictory second range
        // explicitly rather than letting duplicate plan entries obscure the
        // product error behind Engine plan admission.
        if let Some(existing) = normalized_requirements
            .iter()
            .find(|existing| existing.key() == &key)
        {
            if (existing.minimum(), existing.maximum()) != (*minimum, *maximum) {
                return Err(DaggerRejection::InvalidExpression(format!(
                    "conflicting bounded evidence bounds for {}: {}..={} vs {}..={}",
                    key.as_str(),
                    existing.minimum(),
                    existing.maximum(),
                    minimum,
                    maximum
                )));
            }
            continue;
        }
        let requirement =
            BoundedSampleRequirement::new(key, *minimum, *maximum).map_err(|error| {
                DaggerRejection::InvalidExpression(format!(
                    "bounded evidence requirement: {error:?}"
                ))
            })?;
        normalized_requirements.push(requirement);
    }
    let requirements = normalized_requirements;
    let plan = BoundedSamplePlan::new(identity, version, requirements).map_err(|error| {
        DaggerRejection::InvalidExpression(format!("bounded evidence plan: {error:?}"))
    })?;
    let required_keys = plan
        .requirements()
        .iter()
        .map(|requirement| requirement.key().as_str())
        .collect::<std::collections::BTreeSet<_>>();
    let samples = evidence
        .iter()
        .filter(|sample| strict || required_keys.contains(sample.id.to_ascii_lowercase().as_str()))
        .map(|sample| -> Result<BoundedSample, DaggerRejection> {
            Ok(BoundedSample::new(
                BoundedSampleKey::parse(sample.id.to_ascii_lowercase()).map_err(|error| {
                    DaggerRejection::InvalidExpression(format!("bounded evidence key: {error:?}"))
                })?,
                sample.value,
            ))
        })
        .collect::<Result<Vec<_>, _>>()?;
    plan.validate(samples).map_err(bounded_sample_error)
}

/// A product boundary with no bounded requirements may omit an Engine receipt,
/// but it must not silently accept caller-supplied samples. Callers invoke
/// this after removing any declared unbounded observations from their own
/// evidence surface.
pub(crate) fn reject_unexpected_bounded_evidence(
    evidence: &[DaggerEvidence],
) -> Result<(), DaggerRejection> {
    if let Some(sample) = evidence.first() {
        let key = BoundedSampleKey::parse(sample.id.to_ascii_lowercase()).map_err(|error| {
            DaggerRejection::InvalidExpression(format!("bounded evidence key: {error:?}"))
        })?;
        return Err(bounded_sample_error(
            BoundedSamplePlanError::UnknownSample { key },
        ));
    }
    Ok(())
}

fn bounded_sample_error(error: BoundedSamplePlanError) -> DaggerRejection {
    match error {
        BoundedSamplePlanError::MissingSample { key } => {
            DaggerRejection::MissingEvidence(key.as_str().to_string())
        }
        BoundedSamplePlanError::SampleOutOfRange {
            key,
            value,
            minimum,
            maximum,
        } => DaggerRejection::RollOutOfBounds {
            id: key.as_str().to_string(),
            value,
            min: minimum,
            max: maximum,
        },
        other => {
            DaggerRejection::InvalidExpression(format!("bounded evidence validation: {other:?}"))
        }
    }
}

pub(crate) fn bounded_sample_value(
    receipt: &BoundedSampleReceipt,
    id: &str,
) -> Result<i64, DaggerRejection> {
    receipt
        .accepted_samples()
        .iter()
        .find(|sample| sample.key().as_str() == id.to_ascii_lowercase())
        .map(BoundedSample::value)
        .ok_or_else(|| DaggerRejection::MissingEvidence(id.to_string()))
}

fn expression_bounded_requirements(
    expr: &DaggerExpr,
    context: &ExprContext,
) -> Result<Vec<(String, i64, i64)>, DaggerRejection> {
    let mut requirements = Vec::new();
    collect_bounded_rolls(expr, &mut requirements);
    collect_product_bounded_requirements(expr, context, &mut requirements)?;
    Ok(requirements)
}

fn collect_product_bounded_requirements(
    expr: &DaggerExpr,
    context: &ExprContext,
    requirements: &mut Vec<(String, i64, i64)>,
) -> Result<(), DaggerRejection> {
    match expr {
        ComposedExactExpr::Product(leaf) => match leaf.value() {
            DaggerExactLeaf::EquippedWeaponDice { subject, id } => {
                let values = context.subject_values(*subject)?;
                let equipment = values.equipment.ok_or_else(|| {
                    DaggerRejection::MissingValue(format!("equipment@{}", values.definition.id))
                })?;
                let bounds = equipment
                    .weapon
                    .and_then(|item| item.weapon.as_ref())
                    .map_or(equipment.unarmed_damage, |weapon| {
                        (weapon.damage_min, weapon.damage_max)
                    });
                requirements.push((id.clone(), bounds.0, bounds.1));
            }
            DaggerExactLeaf::StruckArmor { id, .. } => {
                requirements.push((id.clone(), 0, 19));
            }
            DaggerExactLeaf::EquippedWeaponSkill { .. } => {}
        },
        ComposedExactExpr::Add(left, right)
        | ComposedExactExpr::Subtract(left, right)
        | ComposedExactExpr::Multiply(left, right)
        | ComposedExactExpr::FloorDivide(left, right)
        | ComposedExactExpr::TruncatingDivide(left, right) => {
            collect_product_bounded_requirements(left, context, requirements)?;
            collect_product_bounded_requirements(right, context, requirements)?;
        }
        ComposedExactExpr::FixedPower { base, exponent, .. } => {
            collect_product_bounded_requirements(base, context, requirements)?;
            collect_product_bounded_requirements(exponent, context, requirements)?;
        }
        ComposedExactExpr::Min(values) | ComposedExactExpr::Max(values) => {
            for value in values {
                collect_product_bounded_requirements(value, context, requirements)?;
            }
        }
        ComposedExactExpr::Literal(_) | ComposedExactExpr::Input(_) => {}
    }
    Ok(())
}

fn scalar(value: i64, path: &str) -> Result<MechanicsScalar, DaggerGameplayError> {
    MechanicsScalar::new(value).map_err(|error| DaggerGameplayError::InvalidValue {
        path: path.to_string(),
        reason: format!("mechanics scalar rejected: {error:?}"),
    })
}

/// Roll evidence a definition's derived track rules require: every boundedRoll
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
        collect_bounded_rolls(&track.max, &mut rolls);
    }
    Ok(rolls)
}

/// Roll evidence an action's program requires: every `boundedRoll` input as
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
    collect_program_bounded_rolls(&action.program, &mut rolls);
    Ok(rolls)
}

/// Evidence an action's program reads through an unbounded
/// `ExactInputReference::Roll`. These observations have no bounded-plan
/// declaration, but they are still declared inputs of the action and must be
/// excluded from the action's strict bounded sample validation. The product
/// remains responsible for supplying and interpreting their values.
pub fn action_unbounded_roll_evidence(
    catalog: &DaggerGameplayCatalog,
    action_id: &str,
) -> Result<Vec<String>, DaggerGameplayError> {
    let action =
        catalog
            .actions()
            .get(action_id)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actions[{action_id}]"),
                reason: "unknown action definition".to_string(),
            })?;
    let mut rolls = Vec::new();
    collect_program_unbounded_rolls(&action.program, &mut rolls);
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

fn collect_program_bounded_rolls(program: &DaggerProgram, rolls: &mut Vec<(String, i64, i64)>) {
    use rusty_engine::gameplay_resolution::Program;
    match program {
        Program::Sequence { steps } => {
            for step in steps {
                collect_program_bounded_rolls(step, rolls);
            }
        }
        Program::When {
            predicate,
            then_program,
            otherwise_program,
        } => {
            collect_comparison_bounded_rolls(predicate, rolls);
            collect_program_bounded_rolls(then_program, rolls);
            if let Some(otherwise) = otherwise_program {
                collect_program_bounded_rolls(otherwise, rolls);
            }
        }
        Program::Operation(operation) => match operation {
            DaggerOperation::SpendTrack { amount, .. } => collect_bounded_rolls(amount, rolls),
            DaggerOperation::Damage { amount, .. } => collect_bounded_rolls(amount, rolls),
        },
    }
}

fn collect_comparison_bounded_rolls(
    predicate: &DaggerPredicate,
    rolls: &mut Vec<(String, i64, i64)>,
) {
    match predicate {
        ComposedExactComparison::Equal(left, right)
        | ComposedExactComparison::LessThan(left, right)
        | ComposedExactComparison::LessOrEqual(left, right)
        | ComposedExactComparison::GreaterThan(left, right)
        | ComposedExactComparison::GreaterOrEqual(left, right) => {
            collect_bounded_rolls(left, rolls);
            collect_bounded_rolls(right, rolls);
        }
    }
}

fn collect_program_unbounded_rolls(program: &DaggerProgram, rolls: &mut Vec<String>) {
    use rusty_engine::gameplay_resolution::Program;
    match program {
        Program::Sequence { steps } => {
            for step in steps {
                collect_program_unbounded_rolls(step, rolls);
            }
        }
        Program::When {
            predicate,
            then_program,
            otherwise_program,
        } => {
            collect_comparison_unbounded_rolls(predicate, rolls);
            collect_program_unbounded_rolls(then_program, rolls);
            if let Some(otherwise) = otherwise_program {
                collect_program_unbounded_rolls(otherwise, rolls);
            }
        }
        Program::Operation(operation) => match operation {
            DaggerOperation::SpendTrack { amount, .. } => collect_unbounded_rolls(amount, rolls),
            DaggerOperation::Damage { amount, .. } => collect_unbounded_rolls(amount, rolls),
        },
    }
}

fn collect_comparison_unbounded_rolls(predicate: &DaggerPredicate, rolls: &mut Vec<String>) {
    match predicate {
        ComposedExactComparison::Equal(left, right)
        | ComposedExactComparison::LessThan(left, right)
        | ComposedExactComparison::LessOrEqual(left, right)
        | ComposedExactComparison::GreaterThan(left, right)
        | ComposedExactComparison::GreaterOrEqual(left, right) => {
            collect_unbounded_rolls(left, rolls);
            collect_unbounded_rolls(right, rolls);
        }
    }
}

fn collect_unbounded_rolls(expr: &DaggerExpr, rolls: &mut Vec<String>) {
    match expr {
        ComposedExactExpr::Input(ExactInputReference::Roll { id, .. }) => {
            rolls.push(id.as_str().to_owned());
        }
        ComposedExactExpr::Add(left, right)
        | ComposedExactExpr::Subtract(left, right)
        | ComposedExactExpr::Multiply(left, right)
        | ComposedExactExpr::FloorDivide(left, right)
        | ComposedExactExpr::TruncatingDivide(left, right) => {
            collect_unbounded_rolls(left, rolls);
            collect_unbounded_rolls(right, rolls);
        }
        ComposedExactExpr::FixedPower { base, exponent, .. } => {
            collect_unbounded_rolls(base, rolls);
            collect_unbounded_rolls(exponent, rolls);
        }
        ComposedExactExpr::Min(values) | ComposedExactExpr::Max(values) => {
            for value in values {
                collect_unbounded_rolls(value, rolls);
            }
        }
        ComposedExactExpr::Literal(_)
        | ComposedExactExpr::Input(_)
        | ComposedExactExpr::Product(_) => {}
    }
}

fn collect_bounded_rolls(expr: &DaggerExpr, rolls: &mut Vec<(String, i64, i64)>) {
    match expr {
        ComposedExactExpr::Input(ExactInputReference::BoundedRoll { descriptor }) => {
            rolls.push((
                descriptor.id().as_str().to_owned(),
                descriptor.minimum().get(),
                descriptor.maximum().get(),
            ));
        }
        ComposedExactExpr::Add(left, right)
        | ComposedExactExpr::Subtract(left, right)
        | ComposedExactExpr::Multiply(left, right)
        | ComposedExactExpr::FloorDivide(left, right)
        | ComposedExactExpr::TruncatingDivide(left, right) => {
            collect_bounded_rolls(left, rolls);
            collect_bounded_rolls(right, rolls);
        }
        ComposedExactExpr::FixedPower { base, exponent, .. } => {
            collect_bounded_rolls(base, rolls);
            collect_bounded_rolls(exponent, rolls);
        }
        ComposedExactExpr::Min(values) | ComposedExactExpr::Max(values) => {
            for value in values {
                collect_bounded_rolls(value, rolls);
            }
        }
        ComposedExactExpr::Literal(_)
        | ComposedExactExpr::Input(_)
        | ComposedExactExpr::Product(_) => {}
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

    bind_loadout(state, catalog, definition_id, definition, entity, instance)?;

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
        // Unique items are entities. Dagger allocates/names them, while the
        // Engine atomically admits, attaches, and contains them.
        let materialized = bind_unique_item(
            state,
            catalog,
            owner,
            format!("{instance}:{}", entry.item),
            item_id,
            &path(),
        )?;
        let item_entity = materialized.entity;
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

/// Allocate and name one Dagger unique-item entity, then ask the Engine to
/// atomically admit, attach, and contain it. This is the shared binding step
/// for spawn loadouts and loot generation. Dagger retains the allocator,
/// authored name, container choice, and error presentation; the receipt
/// retains exact catalog provenance and containment evidence.
pub(crate) fn bind_unique_item(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    owner: rusty_engine::core_ids::EntityId,
    name: String,
    item_id: ItemDefinitionId,
    path: &str,
) -> Result<UniqueItemMaterializationReceipt, DaggerGameplayError> {
    let item_entity = state.allocate_entity();
    let expected_state_revision = state.entities().revision();
    ItemService::materialize_unique(
        state.entities_mut(),
        catalog.mechanics(),
        UniqueItemMaterializationRequest {
            entity: EntityDefinition::new(item_entity, name),
            item: item_id,
            container: owner,
            expected_state_revision,
        },
    )
    .map_err(|error| DaggerGameplayError::InvalidValue {
        path: path.to_string(),
        reason: format!("atomic unique-item materialization: {error:?}"),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    const PACKAGE: &[u8] = include_bytes!("../../../../data/gameplay/dagger-core.package.json");

    #[test]
    fn unique_item_binding_retains_catalog_and_containment_receipt_without_equipping() {
        let catalog = super::super::compile::compile_gameplay_package(PACKAGE)
            .expect("compile Dagger gameplay package");
        let mut state = DaggerGameplayState::default();
        let owner = state.allocate_entity();
        let owner_revision = state.entities().revision();
        EntityAuthoringService
            .admit(
                state.entities_mut(),
                owner_revision,
                [EntityDefinition::new(owner, "test-owner")],
            )
            .expect("admit explicit item container");
        let equipment_revision = state
            .entities()
            .component_revision::<EquipmentComponent>(owner)
            .expect("equipment component revision");
        EntityAuthoringService
            .attach_component(
                state.entities_mut(),
                equipment_revision,
                owner,
                EquipmentComponent::new(catalog.mechanics().version().clone(), Vec::new())
                    .expect("empty equipment component"),
            )
            .expect("attach explicit equipment component");

        let receipt = bind_unique_item(
            &mut state,
            &catalog,
            owner,
            "test-owner:iron-longsword".to_string(),
            ItemDefinitionId::parse("iron-longsword").expect("known item identity"),
            "test.unique-item",
        )
        .expect("materialize unique item");

        assert_eq!(receipt.catalog_version, *catalog.mechanics().version());
        assert_eq!(
            receipt.catalog_fingerprint,
            catalog.mechanics().fingerprint()
        );
        assert_eq!(receipt.containment_before, None);
        assert_eq!(receipt.containment_after, Some(owner));
        assert!(state.entities().contains(receipt.entity));
        assert_eq!(state.entities().contained_in(receipt.entity), Some(owner));
        let item = state
            .entities()
            .component::<ItemComponent>(receipt.entity)
            .expect("item component read")
            .expect("materialized item component");
        assert_eq!(item.catalog_version(), catalog.mechanics().version());
        assert_eq!(item.definition(), &receipt.item);
        assert!(state
            .entities()
            .component::<EquipmentComponent>(owner)
            .expect("equipment component read")
            .expect("equipment component")
            .assignments()
            .is_empty());
    }

    #[test]
    fn failed_unique_item_binding_does_not_publish_admission_or_attachment() {
        let catalog = super::super::compile::compile_gameplay_package(PACKAGE)
            .expect("compile Dagger gameplay package");
        let mut state = DaggerGameplayState::default();
        let before_revision = state.entities().revision();

        assert!(matches!(
            bind_unique_item(
                &mut state,
                &catalog,
                rusty_engine::core_ids::EntityId::new(99_999),
                "missing-owner:iron-longsword".to_string(),
                ItemDefinitionId::parse("iron-longsword").expect("known item identity"),
                "test.missing-owner",
            ),
            Err(DaggerGameplayError::InvalidValue { .. })
        ));

        assert_eq!(state.entities().revision(), before_revision);
        assert_eq!(state.entities().total_count(), 0);
        assert!(!state
            .entities()
            .contains(rusty_engine::core_ids::EntityId::new(1)));
    }

    #[test]
    fn bounded_receipt_delegates_complete_validation_and_preserves_plan_order() {
        let requirements = vec![
            ("Loot.A.Gold".to_string(), -5, 5),
            ("loot.pick".to_string(), i64::MIN, i64::MAX),
        ];
        let receipt = bounded_sample_receipt(
            "dagger.test",
            &requirements,
            &[
                DaggerEvidence {
                    id: "loot.pick".to_string(),
                    value: i64::MAX,
                },
                DaggerEvidence {
                    id: "Loot.A.Gold".to_string(),
                    value: -5,
                },
            ],
            true,
        )
        .expect("Engine accepts complete bounded evidence");

        assert_eq!(receipt.identity().as_str(), "dagger.test");
        assert_eq!(receipt.version().get(), 1);
        assert_eq!(
            receipt
                .requirements()
                .iter()
                .map(|requirement| requirement.key().as_str())
                .collect::<Vec<_>>(),
            vec!["loot.a.gold", "loot.pick"]
        );
        assert_eq!(
            receipt
                .accepted_samples()
                .iter()
                .map(|sample| (sample.key().as_str(), sample.value()))
                .collect::<Vec<_>>(),
            vec![("loot.a.gold", -5), ("loot.pick", i64::MAX)]
        );
    }

    #[test]
    fn bounded_receipt_rejects_missing_unknown_duplicate_and_out_of_range_samples() {
        let requirements = vec![("test.roll".to_string(), -2, 2)];
        let missing = bounded_sample_receipt("dagger.test", &requirements, &[], true);
        assert!(matches!(
            missing,
            Err(DaggerRejection::MissingEvidence(id)) if id == "test.roll"
        ));

        let unknown = bounded_sample_receipt(
            "dagger.test",
            &requirements,
            &[DaggerEvidence {
                id: "other.roll".to_string(),
                value: 0,
            }],
            true,
        );
        assert!(matches!(
            unknown,
            Err(DaggerRejection::InvalidExpression(_))
        ));

        let duplicate = bounded_sample_receipt(
            "dagger.test",
            &requirements,
            &[
                DaggerEvidence {
                    id: "test.roll".to_string(),
                    value: 0,
                },
                DaggerEvidence {
                    id: "TEST.ROLL".to_string(),
                    value: 1,
                },
            ],
            true,
        );
        assert!(matches!(
            duplicate,
            Err(DaggerRejection::InvalidExpression(_))
        ));

        let out_of_range = bounded_sample_receipt(
            "dagger.test",
            &requirements,
            &[DaggerEvidence {
                id: "test.roll".to_string(),
                value: 3,
            }],
            true,
        );
        assert!(matches!(
            out_of_range,
            Err(DaggerRejection::RollOutOfBounds {
                id,
                value: 3,
                min: -2,
                max: 2,
            }) if id == "test.roll"
        ));
    }

    #[test]
    fn bounded_receipt_deduplicates_identical_requirements_and_rejects_conflicting_bounds() {
        let repeated = bounded_sample_receipt(
            "dagger.test",
            &[
                ("Test.Roll".to_string(), -2, 2),
                ("test.roll".to_string(), -2, 2),
            ],
            &[DaggerEvidence {
                id: "test.roll".to_string(),
                value: 1,
            }],
            true,
        )
        .expect("identical references share one bounded sample");
        assert_eq!(repeated.requirements().len(), 1);
        assert_eq!(repeated.accepted_samples().len(), 1);

        let conflicting = bounded_sample_receipt(
            "dagger.test",
            &[
                ("test.roll".to_string(), -2, 2),
                ("TEST.ROLL".to_string(), -1, 2),
            ],
            &[DaggerEvidence {
                id: "test.roll".to_string(),
                value: 1,
            }],
            true,
        );
        assert!(matches!(
            conflicting,
            Err(DaggerRejection::InvalidExpression(message))
                if message.contains("conflicting bounded evidence bounds")
        ));
    }
}
