use std::collections::BTreeMap;

use rusty_engine::gameplay_mechanics::{
    DamageKindId, MechanicsScalar, OperationId, SourceInstanceId, SourceInstanceIdentity, StatId,
    StatService, TrackId, TracksComponent,
};
use rusty_engine::gameplay_resolution::{
    PolicyFailure, PolicyResult, ResolutionIdentity, ResolutionMode, ResolutionPlan,
    ResolutionPolicy, ResolutionRequest, ResolutionTraceSink, ResolutionTransaction,
    StandardResolver,
};
use rusty_engine::gameplay_standard::{
    CapabilityRequirementId, CapabilityRoleBinding, CapabilityRoleBindings, CapabilityRoleId,
    ComposedExactComparison, ExactComparison, ExactEvaluator, ExactExpr, ExactExprLimits,
    ExactInputBundle, StandardExactOperand, StandardOperation, StandardOperationContext,
    StandardOperationPlan, STANDARD_DAMAGE_CAPABILITY, STANDARD_EFFECT_CAPABILITY,
    STANDARD_TRACK_CAPABILITY,
};

use super::eval::{
    equipped_weapon, evaluate_expr, unarmed_damage_range, ActorEquipment, ActorExprValues,
    ExprContext,
};
use super::mechanics::track_max_stat_id;
use super::{
    armor_part_stat_id, weapon_material_rank, DaggerActorDefinition, DaggerActorFacts,
    DaggerActorState, DaggerAdmittedIntent, DaggerEffect, DaggerEvent, DaggerEvidence, DaggerFacts,
    DaggerFault, DaggerGameplayCatalog, DaggerGameplayState, DaggerIntent, DaggerOperation,
    DaggerPlannedEffect, DaggerPredicate, DaggerRejection, DaggerResolutionReadout,
    DaggerResolutionReceipt, DaggerRuleDefinition, DaggerSelector, DaggerSuspension,
    DaggerTraceDetail, DaggerTransactionError,
};

pub struct DaggerResolutionPolicy<'a> {
    catalog: &'a DaggerGameplayCatalog,
    snapshot: DaggerGameplayState,
    operation: OperationId,
}

impl<'a> DaggerResolutionPolicy<'a> {
    pub fn new(
        catalog: &'a DaggerGameplayCatalog,
        snapshot: DaggerGameplayState,
        operation: OperationId,
    ) -> Self {
        Self {
            catalog,
            snapshot,
            operation,
        }
    }

    fn binding(
        &self,
        actor_id: &str,
    ) -> PolicyResult<&DaggerActorState, DaggerRejection, DaggerFault, DaggerSuspension> {
        self.snapshot.actor(actor_id).ok_or_else(|| {
            PolicyFailure::Rejected(DaggerRejection::UnknownActor(actor_id.to_string()))
        })
    }

    fn definition(
        &self,
        actor_id: &str,
    ) -> PolicyResult<&DaggerActorDefinition, DaggerRejection, DaggerFault, DaggerSuspension> {
        let binding = self.binding(actor_id)?;
        self.catalog
            .actors()
            .get(binding.definition())
            .ok_or_else(|| {
                PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                    "actor {actor_id} references unknown definition {}",
                    binding.definition()
                )))
            })
    }

    /// Materialize live stat values for one actor through the mechanics
    /// stat service: base values plus any active attributed sources. The
    /// spawn-stored `{track}-max` stats are included so `trackMax`
    /// expressions read the same map `stat` reads, and the `armor-<part>`
    /// stats are included so `struckArmor` reads equipped-armor effects.
    /// Classic actors possess every declared attribute and skill at some
    /// value, so ids the definition omits (untrained skills, monster
    /// reflexes) read as 0.
    fn live_stats(
        &self,
        actor_id: &str,
    ) -> PolicyResult<BTreeMap<String, i64>, DaggerRejection, DaggerFault, DaggerSuspension> {
        let binding = self.binding(actor_id)?;
        let definition = self.definition(actor_id)?;
        let operation = eval_operation_id();
        let ids = definition
            .stats
            .keys()
            .chain(definition.skills.keys())
            .cloned()
            .chain(
                definition
                    .tracks
                    .iter()
                    .map(|track| track_max_stat_id(&track.id)),
            )
            .chain(
                self.catalog
                    .stats()
                    .armor_parts
                    .iter()
                    .map(|part| armor_part_stat_id(part)),
            );
        let mut values = BTreeMap::new();
        for id in ids {
            let stat = StatId::parse(id.clone()).map_err(|error| {
                PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                    "stat id {id}: {error:?}"
                )))
            })?;
            let evaluation = StatService::evaluate(
                self.snapshot.entities(),
                self.catalog.mechanics(),
                binding.entity(),
                &stat,
                &operation,
                &[],
            )
            .map_err(|error| {
                PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                    "stat evaluation {id}@{actor_id}: {error:?}"
                )))
            })?;
            values.insert(id.clone(), evaluation.value.get());
        }
        for declared in self
            .catalog
            .stats()
            .attributes
            .iter()
            .chain(self.catalog.stats().skills.iter())
        {
            values.entry(declared.clone()).or_insert(0);
        }
        Ok(values)
    }

    /// Materialize live current track values for one actor from the
    /// mechanics track components.
    fn live_tracks(
        &self,
        actor_id: &str,
    ) -> PolicyResult<BTreeMap<String, i64>, DaggerRejection, DaggerFault, DaggerSuspension> {
        let definition = self.definition(actor_id)?;
        let mut values = BTreeMap::new();
        for track in &definition.tracks {
            if let Some(value) = self.snapshot.track_value(actor_id, &track.id) {
                values.insert(track.id.clone(), value);
            }
        }
        Ok(values)
    }

    /// Materialize one subject's live equipment facts for expression
    /// evaluation: the equipped weapon from the entity's EquipmentComponent
    /// (right hand first) resolved against catalog items, and the unarmed
    /// damage range evaluated once from the derived hand-to-hand rules.
    fn actor_equipment<'b>(
        &'b self,
        actor_id: &str,
        stats: &'b BTreeMap<String, i64>,
    ) -> PolicyResult<ActorEquipment<'b>, DaggerRejection, DaggerFault, DaggerSuspension> {
        let weapon = equipped_weapon(&self.snapshot, self.catalog, actor_id).map_err(|error| {
            PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                "equipment read {actor_id}: {error}"
            )))
        })?;
        let unarmed_damage = unarmed_damage_range(self.catalog, self.definition(actor_id)?, stats)
            .map_err(|error| {
                PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                    "unarmed damage {actor_id}: {error}"
                )))
            })?;
        Ok(ActorEquipment {
            weapon,
            unarmed_damage,
        })
    }

    #[allow(clippy::too_many_arguments)]
    fn expr_context<'b>(
        &'b self,
        actor_id: &str,
        target_id: &str,
        actor_stats: &'b BTreeMap<String, i64>,
        actor_tracks: &'b BTreeMap<String, i64>,
        target_stats: &'b BTreeMap<String, i64>,
        target_tracks: &'b BTreeMap<String, i64>,
        evidence: &'b [DaggerEvidence],
    ) -> PolicyResult<ExprContext<'b>, DaggerRejection, DaggerFault, DaggerSuspension> {
        Ok(ExprContext {
            catalog: self.catalog,
            actor: ActorExprValues {
                definition: self.definition(actor_id)?,
                stats: actor_stats,
                tracks: Some(actor_tracks),
                equipment: Some(self.actor_equipment(actor_id, actor_stats)?),
            },
            target: Some(ActorExprValues {
                definition: self.definition(target_id)?,
                stats: target_stats,
                tracks: Some(target_tracks),
                equipment: Some(self.actor_equipment(target_id, target_stats)?),
            }),
            evidence,
        })
    }

    /// Builds the Engine-owned mechanics mutation plan before Dagger wraps it
    /// in its product receipt/event envelope. The plan is intentionally not a
    /// second transaction authority: Dagger's aggregate transaction remains
    /// responsible for one product publication, while Engine validates the
    /// exact operand, role capability bindings, source snapshot, and mechanics
    /// mutation shape.
    fn standard_plan(
        &self,
        operation: StandardOperation,
        actor: &str,
        target: &str,
    ) -> PolicyResult<StandardOperationPlan, DaggerRejection, DaggerFault, DaggerSuspension> {
        let actor_entity = self.binding(actor)?.entity();
        let target_entity = self.binding(target)?.entity();
        let capability = |value| {
            CapabilityRequirementId::parse(value).expect("fixed standard capability identity")
        };
        let all_capabilities = vec![
            capability(STANDARD_TRACK_CAPABILITY),
            capability(STANDARD_DAMAGE_CAPABILITY),
            capability(STANDARD_EFFECT_CAPABILITY),
        ];
        let bindings = CapabilityRoleBindings::admit(
            &operation.requirements(),
            vec![
                CapabilityRoleBinding::new(
                    CapabilityRoleId::parse("actor").expect("fixed role"),
                    actor_entity,
                    all_capabilities.clone(),
                )
                .expect("fixed role capabilities fit"),
                CapabilityRoleBinding::new(
                    CapabilityRoleId::parse("target").expect("fixed role"),
                    target_entity,
                    all_capabilities,
                )
                .expect("fixed role capabilities fit"),
            ],
        )
        .map_err(|error| {
            PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                "standard role bindings: {error:?}"
            )))
        })?;
        let context = StandardOperationContext::new(
            self.operation.clone(),
            SourceInstanceIdentity::Request {
                operation: self.operation.clone(),
                instance: SourceInstanceId::parse("dagger-standard-plan")
                    .expect("fixed source identity"),
            },
        )
        .expect("matching fixed standard operation/source");
        operation
            .plan(
                &bindings,
                &ExactInputBundle::empty(),
                self.snapshot.entities(),
                self.catalog.mechanics(),
                &context,
            )
            .map_err(|error| {
                PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                    "standard operation plan: {error:?}"
                )))
            })
    }
}

fn eval_operation_id() -> OperationId {
    OperationId::parse("dagger-policy-eval").expect("fixed operation identity")
}

fn actor_facts(
    state: &DaggerGameplayState,
    binding: &DaggerActorState,
) -> Result<DaggerActorFacts, DaggerRejection> {
    let mut tracks = BTreeMap::new();
    if let Some(component) = state
        .entities()
        .component::<TracksComponent>(binding.entity())
        .ok()
        .flatten()
    {
        for value in component.values() {
            tracks.insert(value.track().as_str().to_string(), value.current().get());
        }
    }
    Ok(DaggerActorFacts {
        definition: binding.definition().to_string(),
        tracks,
        conditions: binding.conditions().clone(),
    })
}

impl ResolutionPolicy for DaggerResolutionPolicy<'_> {
    type RawIntent = DaggerIntent;
    type Intent = DaggerAdmittedIntent;
    type Facts = DaggerFacts;
    type Predicate = DaggerPredicate;
    type Operation = DaggerOperation;
    type Effect = DaggerPlannedEffect;
    type Event = DaggerEvent;
    type Evidence = DaggerEvidence;
    // Item-borne interception is gone with the dead items-set path; equipped
    // armor/shield items now act through attributed stat sources, and damage
    // responses on equipped items remain future work.
    type Interceptor = std::convert::Infallible;
    type TraceDetail = DaggerTraceDetail;
    type Rejection = DaggerRejection;
    type Fault = DaggerFault;
    type Suspension = DaggerSuspension;

    fn admit(
        &mut self,
        intent: &DaggerIntent,
        _evidence: &[DaggerEvidence],
        trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<DaggerAdmittedIntent, DaggerRejection, DaggerFault, DaggerSuspension> {
        let action = self
            .catalog
            .actions()
            .get(&intent.action)
            .cloned()
            .ok_or_else(|| {
                PolicyFailure::Rejected(DaggerRejection::UnknownAction(intent.action.clone()))
            })?;
        if self.snapshot.actor(&intent.actor).is_none() {
            return Err(PolicyFailure::Rejected(DaggerRejection::UnknownActor(
                intent.actor.clone(),
            )));
        }
        if self.snapshot.actor(&intent.target).is_none() {
            return Err(PolicyFailure::Rejected(DaggerRejection::UnknownTarget(
                intent.target.clone(),
            )));
        }
        trace.record(DaggerTraceDetail::Definition {
            id: action.id.clone(),
        });
        Ok(DaggerAdmittedIntent {
            action,
            actor: intent.actor.clone(),
            target: intent.target.clone(),
            origin: intent.origin,
        })
    }

    fn gather(
        &mut self,
        intent: &DaggerAdmittedIntent,
        _evidence: &[DaggerEvidence],
        trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<DaggerFacts, DaggerRejection, DaggerFault, DaggerSuspension> {
        let actor = actor_facts(&self.snapshot, self.binding(&intent.actor)?)
            .map_err(PolicyFailure::Rejected)?;
        let target = actor_facts(&self.snapshot, self.binding(&intent.target)?)
            .map_err(PolicyFailure::Rejected)?;
        trace.record(DaggerTraceDetail::Facts {
            actor: intent.actor.clone(),
            target: intent.target.clone(),
        });
        Ok(DaggerFacts { actor, target })
    }

    fn check(
        &mut self,
        intent: &DaggerAdmittedIntent,
        facts: &DaggerFacts,
        _evidence: &[DaggerEvidence],
        trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<(), DaggerRejection, DaggerFault, DaggerSuspension> {
        for rule in self.catalog.rules() {
            let DaggerRuleDefinition::RejectTagWhileCondition { id, tag, condition } = rule;
            if intent.action.tags.contains(tag) && facts.actor.conditions.contains(condition) {
                trace.record(DaggerTraceDetail::Decision {
                    reason: format!("{id} rejected tag {tag} while {condition}"),
                });
                return Err(PolicyFailure::Rejected(DaggerRejection::Rule {
                    rule: id.clone(),
                    reason: format!("actor has condition {condition}"),
                }));
            }
        }
        Ok(())
    }

    fn plan(
        &mut self,
        intent: &DaggerAdmittedIntent,
        _facts: &DaggerFacts,
        _evidence: &[DaggerEvidence],
        _trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<
        rusty_engine::gameplay_resolution::PolicyProgram<Self>,
        DaggerRejection,
        DaggerFault,
        DaggerSuspension,
    > {
        Ok(intent.action.program.clone())
    }

    fn evaluate_predicate(
        &mut self,
        predicate: &DaggerPredicate,
        intent: &DaggerAdmittedIntent,
        _facts: &DaggerFacts,
        evidence: &[DaggerEvidence],
        trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<bool, DaggerRejection, DaggerFault, DaggerSuspension> {
        match predicate {
            ComposedExactComparison::Equal(left, right)
            | ComposedExactComparison::LessThan(left, right)
            | ComposedExactComparison::LessOrEqual(left, right)
            | ComposedExactComparison::GreaterThan(left, right)
            | ComposedExactComparison::GreaterOrEqual(left, right) => {
                let actor_stats = self.live_stats(&intent.actor)?;
                let target_stats = self.live_stats(&intent.target)?;
                let actor_tracks = self.live_tracks(&intent.actor)?;
                let target_tracks = self.live_tracks(&intent.target)?;
                let context = self.expr_context(
                    &intent.actor,
                    &intent.target,
                    &actor_stats,
                    &actor_tracks,
                    &target_stats,
                    &target_tracks,
                    evidence,
                )?;
                let left_value = evaluate_expr(left, &context).map_err(PolicyFailure::Rejected)?;
                let right_value =
                    evaluate_expr(right, &context).map_err(PolicyFailure::Rejected)?;
                let left =
                    ExactExpr::Literal(MechanicsScalar::new(left_value).map_err(|error| {
                        PolicyFailure::Rejected(DaggerRejection::InvalidExpression(format!(
                            "comparison left: {error:?}"
                        )))
                    })?);
                let right =
                    ExactExpr::Literal(MechanicsScalar::new(right_value).map_err(|error| {
                        PolicyFailure::Rejected(DaggerRejection::InvalidExpression(format!(
                            "comparison right: {error:?}"
                        )))
                    })?);
                let (comparison, operator) = match predicate {
                    ComposedExactComparison::Equal(_, _) => {
                        (ExactComparison::Equal(left, right), "Eq")
                    }
                    ComposedExactComparison::LessThan(_, _) => {
                        (ExactComparison::LessThan(left, right), "Lt")
                    }
                    ComposedExactComparison::LessOrEqual(_, _) => {
                        (ExactComparison::LessOrEqual(left, right), "Lte")
                    }
                    ComposedExactComparison::GreaterThan(_, _) => {
                        (ExactComparison::GreaterThan(left, right), "Gt")
                    }
                    ComposedExactComparison::GreaterOrEqual(_, _) => {
                        (ExactComparison::GreaterOrEqual(left, right), "Gte")
                    }
                };
                let result = ExactEvaluator::evaluate_predicate(
                    &comparison,
                    &ExactInputBundle::empty(),
                    ExactExprLimits::default(),
                )
                .map_err(|error| {
                    PolicyFailure::Rejected(DaggerRejection::InvalidExpression(format!(
                        "comparison evaluation: {error:?}"
                    )))
                })?;
                trace.record(DaggerTraceDetail::Decision {
                    reason: format!("{left_value} {operator} {right_value} = {result}"),
                });
                Ok(result)
            }
        }
    }

    fn plan_operation(
        &mut self,
        operation: &DaggerOperation,
        intent: &DaggerAdmittedIntent,
        facts: &DaggerFacts,
        evidence: &[DaggerEvidence],
        trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<
        ResolutionPlan<DaggerPlannedEffect, DaggerEvent, DaggerIntent, DaggerEvidence>,
        DaggerRejection,
        DaggerFault,
        DaggerSuspension,
    > {
        let mut plan = ResolutionPlan::new();
        let actor_stats = self.live_stats(&intent.actor)?;
        let target_stats = self.live_stats(&intent.target)?;
        let actor_tracks = self.live_tracks(&intent.actor)?;
        let target_tracks = self.live_tracks(&intent.target)?;
        let context = self.expr_context(
            &intent.actor,
            &intent.target,
            &actor_stats,
            &actor_tracks,
            &target_stats,
            &target_tracks,
            evidence,
        )?;
        match operation {
            DaggerOperation::SpendTrack { track, amount } => {
                let amount = evaluate_expr(amount, &context).map_err(PolicyFailure::Rejected)?;
                let available = facts.actor.track(track).unwrap_or(0);
                if available < amount {
                    return Err(PolicyFailure::Rejected(
                        DaggerRejection::InsufficientTrack {
                            track: track.clone(),
                            available,
                            required: amount,
                        },
                    ));
                }
                let standard = StandardOperation::SpendTrack {
                    role: CapabilityRoleId::parse("actor").expect("fixed role"),
                    track: TrackId::parse(track.clone()).map_err(|error| {
                        PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                            "standard track {track}: {error:?}"
                        )))
                    })?,
                    amount: StandardExactOperand::from(ExactExpr::Literal(
                        MechanicsScalar::new(amount).map_err(|error| {
                            PolicyFailure::Rejected(DaggerRejection::InvalidExpression(format!(
                                "standard spend amount {amount}: {error:?}"
                            )))
                        })?,
                    )),
                };
                let standard_plan = self.standard_plan(standard, &intent.actor, &intent.target)?;
                let observed_components = standard_plan.observed_revisions().len();
                plan.push_effect(DaggerPlannedEffect::new(
                    DaggerEffect::SpendTrack {
                        actor: intent.actor.clone(),
                        track: track.clone(),
                        amount,
                    },
                    standard_plan,
                ));
                plan.push_event(DaggerEvent::TrackSpent {
                    actor: intent.actor.clone(),
                    track: track.clone(),
                    amount,
                });
                trace.record(DaggerTraceDetail::Decision {
                    reason: format!(
                        "spend {amount} {track} of {available} (standard plan observed {} components)",
                        observed_components
                    ),
                });
            }
            DaggerOperation::Damage { target, amount } => {
                let target = match target {
                    DaggerSelector::IntentTarget => &intent.target,
                };
                let amount = evaluate_expr(amount, &context).map_err(PolicyFailure::Rejected)?;
                // Health floors at zero: damage beyond the target's current
                // health is wasted, so the plan clamps to what can apply.
                let target_health = facts.target.track("health").unwrap_or(0);
                let mut applied = amount.clamp(0, target_health);
                // Classic material gate (donor FormulaHelper.CalculateWeapon-
                // Damage: `target.MinMetalToHit > weapon material` means 0
                // damage): Dagger combat law in the Rust authority, like the
                // remaining-health clamp — not an authored expression.
                // Unarmed attacks are always effective: classic has no
                // bare-hand material to compare.
                let target_definition = self.definition(target)?;
                if let Some(required) = &target_definition.min_metal_to_hit {
                    if let Some(weapon) = context.actor.equipment.and_then(|gear| gear.weapon) {
                        let weapon_material = &weapon
                            .weapon
                            .as_ref()
                            .expect("equipped_weapon returns weapon items")
                            .material;
                        let required_rank = weapon_material_rank(required).ok_or_else(|| {
                            PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                                "actor {} minMetalToHit {required} is not a weapon material",
                                target_definition.id
                            )))
                        })?;
                        let weapon_rank =
                            weapon_material_rank(weapon_material).ok_or_else(|| {
                                PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                                    "weapon {} material {weapon_material} is not a weapon material",
                                    weapon.id
                                )))
                            })?;
                        if required_rank > weapon_rank {
                            applied = 0;
                            trace.record(DaggerTraceDetail::MaterialIneffective {
                                required_material: required.clone(),
                                weapon_material: weapon_material.clone(),
                            });
                        }
                    }
                }
                let standard = StandardOperation::SubmitDamage {
                    actor: Some(CapabilityRoleId::parse("actor").expect("fixed role")),
                    target: CapabilityRoleId::parse("target").expect("fixed role"),
                    target_track: TrackId::parse("health").expect("fixed health track"),
                    parts: vec![(
                        StandardExactOperand::from(ExactExpr::Literal(
                            MechanicsScalar::new(applied).map_err(|error| {
                                PolicyFailure::Rejected(DaggerRejection::InvalidExpression(
                                    format!("standard damage amount {applied}: {error:?}"),
                                ))
                            })?,
                        )),
                        DamageKindId::parse("impact").expect("fixed damage kind"),
                    )],
                    request_sources: vec![],
                };
                let standard_plan = self.standard_plan(standard, &intent.actor, target)?;
                let observed_components = standard_plan.observed_revisions().len();
                plan.push_effect(DaggerPlannedEffect::new(
                    DaggerEffect::Damage {
                        target: target.clone(),
                        amount: applied,
                    },
                    standard_plan,
                ));
                plan.push_event(DaggerEvent::DamageApplied {
                    target: target.clone(),
                    amount: applied,
                });
                trace.record(DaggerTraceDetail::Decision {
                    reason: format!(
                        "damage {applied} to {target} (rolled {amount}; standard plan observed {} components)",
                        observed_components
                    ),
                });
            }
        }
        Ok(plan)
    }
}

pub struct DaggerTransaction<'a> {
    state: &'a mut DaggerGameplayState,
    mechanics: &'a rusty_engine::gameplay_mechanics::MechanicsCatalog,
    staged: Vec<DaggerPlannedEffect>,
}

impl<'a> DaggerTransaction<'a> {
    pub fn new(
        state: &'a mut DaggerGameplayState,
        mechanics: &'a rusty_engine::gameplay_mechanics::MechanicsCatalog,
    ) -> Self {
        Self {
            state,
            mechanics,
            staged: Vec::new(),
        }
    }
}

impl ResolutionTransaction for DaggerTransaction<'_> {
    type Effect = DaggerPlannedEffect;
    type Error = DaggerTransactionError;

    fn stage(&mut self, effect: &DaggerPlannedEffect) -> Result<(), DaggerTransactionError> {
        self.staged.push(effect.clone());
        Ok(())
    }

    fn commit(&mut self) -> Result<(), DaggerTransactionError> {
        for effect in &self.staged {
            effect
                .standard()
                .validate_source_state(self.state.entities(), self.mechanics)
                .map_err(|error| {
                    DaggerTransactionError::Mechanics(format!(
                        "standard plan source validation: {error:?}"
                    ))
                })?;
        }
        let mut candidate = self.state.clone();
        for effect in &self.staged {
            effect
                .standard()
                .effect()
                .apply_to_candidate(candidate.entities_mut(), self.mechanics)
                .map_err(|error| {
                    DaggerTransactionError::Mechanics(format!(
                        "standard plan candidate application: {error:?}"
                    ))
                })?;
        }
        *self.state = candidate;
        self.staged.clear();
        Ok(())
    }

    fn abort(&mut self) {
        self.staged.clear();
    }
}

pub fn resolve_dagger_action(
    catalog: &DaggerGameplayCatalog,
    state: &mut DaggerGameplayState,
    identity: ResolutionIdentity,
    mode: ResolutionMode,
    intent: DaggerIntent,
    evidence: Vec<DaggerEvidence>,
) -> (DaggerResolutionReceipt, DaggerResolutionReadout) {
    let snapshot = state.clone();
    let operation = OperationId::parse(format!(
        "resolution-{}-{}",
        identity.correlation().get(),
        identity.resolution().get()
    ))
    .expect("resolution operation identity fits identity limits");
    let mut policy = DaggerResolutionPolicy::new(catalog, snapshot, operation);
    let mut transaction = DaggerTransaction::new(state, catalog.mechanics());
    let receipt = StandardResolver::default().resolve(
        &mut policy,
        &mut transaction,
        ResolutionRequest::new(identity, mode, intent, evidence),
    );
    let readout = DaggerResolutionReadout::from_receipt(catalog.fingerprint(), &receipt);
    (receipt, readout)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{compile_gameplay_package, set_actor_track, spawn_actor};

    use rusty_engine::gameplay_resolution::ResolutionTransaction;

    const PACKAGE: &[u8] = include_bytes!("../../../../data/gameplay/dagger-core.package.json");

    #[test]
    fn stale_standard_plan_rejects_before_candidate_publication() {
        let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
        let mut state = DaggerGameplayState::default();
        spawn_actor(&mut state, &catalog, "player", "player", &[]).expect("spawn player");
        spawn_actor(
            &mut state,
            &catalog,
            "rat",
            "rat",
            &[DaggerEvidence {
                id: "rat.health".to_string(),
                value: 12,
            }],
        )
        .expect("spawn rat");

        let operation = OperationId::parse("resolution-stale-standard-plan").expect("fixed id");
        let policy = DaggerResolutionPolicy::new(&catalog, state.clone(), operation);
        let plan = policy
            .standard_plan(
                StandardOperation::SpendTrack {
                    role: CapabilityRoleId::parse("actor").expect("fixed role"),
                    track: TrackId::parse("stamina").expect("fixed track"),
                    amount: StandardExactOperand::from(ExactExpr::Literal(
                        MechanicsScalar::new(5).expect("fixed amount"),
                    )),
                },
                "player",
                "rat",
            )
            .expect("plan standard spend");
        let effect = DaggerPlannedEffect::new(
            DaggerEffect::SpendTrack {
                actor: "player".to_string(),
                track: "stamina".to_string(),
                amount: 5,
            },
            plan,
        );

        let mut transaction = DaggerTransaction::new(&mut state, catalog.mechanics());
        transaction.stage(&effect).expect("stage plan");
        set_actor_track(transaction.state, &catalog, "player", "stamina", 89)
            .expect("mutate source after planning");

        let error = transaction.commit().expect_err("stale plan must reject");
        assert!(matches!(
            error,
            DaggerTransactionError::Mechanics(message)
                if message.contains("standard plan source validation")
        ));
        // The transaction never cloned/applied the staged spend, so the
        // externally changed 89 remains rather than a second 84 mutation.
        assert_eq!(transaction.state.track_value("player", "stamina"), Some(89));
    }
}
