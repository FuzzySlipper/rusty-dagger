//! The single evaluator for derived-value expressions. Action resolution
//! (policy.rs) and diagnostics state construction both evaluate through this
//! module — there is no second arithmetic path.

use super::{
    DaggerActorDefinition, DaggerActorState, DaggerEvidence, DaggerExpr, DaggerGameplayCatalog,
    DaggerGameplayError, DaggerRejection, DaggerSubject,
};

/// Everything expression evaluation may read: the catalog (items, actor
/// definitions), the acting definition, an optional target definition, and
/// caller-supplied evidence (rolls and world facts).
pub struct ExprContext<'a> {
    pub catalog: &'a DaggerGameplayCatalog,
    pub actor: &'a DaggerActorDefinition,
    pub target: Option<&'a DaggerActorDefinition>,
    pub evidence: &'a [DaggerEvidence],
}

impl ExprContext<'_> {
    fn subject_definition(
        &self,
        subject: DaggerSubject,
    ) -> Result<&DaggerActorDefinition, DaggerRejection> {
        match subject {
            DaggerSubject::Actor => Ok(self.actor),
            DaggerSubject::Target => self
                .target
                .ok_or_else(|| DaggerRejection::MissingValue("target".to_string())),
        }
    }
}

pub fn evaluate_expr(expr: &DaggerExpr, context: &ExprContext) -> Result<i64, DaggerRejection> {
    match expr {
        DaggerExpr::Const { value } => Ok(*value),
        DaggerExpr::Stat { subject, id } => {
            let definition = context.subject_definition(*subject)?;
            definition.stats.get(id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("stat.{id}@{}", definition.id))
            })
        }
        DaggerExpr::Skill { subject, id } => {
            let definition = context.subject_definition(*subject)?;
            definition.skills.get(id).copied().ok_or_else(|| {
                DaggerRejection::MissingValue(format!("skill.{id}@{}", definition.id))
            })
        }
        DaggerExpr::Armor { subject } => Ok(context.subject_definition(*subject)?.armor_value),
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

/// Evaluate one actor definition's derived track maximums into a live state.
/// This is the authority every consumer (diagnostics now, runtime later)
/// uses to spawn an actor; roll evidence is supplied by the caller so spawns
/// are deterministic and replayable.
pub fn initial_actor_state(
    catalog: &DaggerGameplayCatalog,
    actor_id: &str,
    evidence: &[DaggerEvidence],
) -> Result<DaggerActorState, DaggerGameplayError> {
    let definition =
        catalog
            .actors()
            .get(actor_id)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actors[{actor_id}]"),
                reason: "unknown actor definition".to_string(),
            })?;
    let context = ExprContext {
        catalog,
        actor: definition,
        target: None,
        evidence,
    };
    let mut state = DaggerActorState::new(actor_id);
    for track in &definition.tracks {
        let value = evaluate_expr(&track.max, &context).map_err(|rejection| {
            DaggerGameplayError::InvalidValue {
                path: format!("actors[{actor_id}].tracks[{}]", track.id),
                reason: format!("derived track maximum rejected: {rejection:?}"),
            }
        })?;
        if value < 0 {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("actors[{actor_id}].tracks[{}]", track.id),
                reason: format!("derived track maximum must be non-negative, got {value}"),
            });
        }
        state = state.with_track(track.id.clone(), value);
    }
    Ok(state)
}
