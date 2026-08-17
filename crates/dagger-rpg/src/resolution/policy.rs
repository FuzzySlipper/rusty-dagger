use std::collections::BTreeMap;

use rusty_engine::gameplay_mechanics::{
    MechanicsScalar, OperationId, SourceInstanceId, SourceInstanceIdentity, StatId, StatService,
    TrackAdjustmentKind, TrackId, TrackMutationRequest, TrackService, TracksComponent,
};
use rusty_engine::gameplay_resolution::{
    PolicyFailure, PolicyResult, ResolutionIdentity, ResolutionMode, ResolutionPlan,
    ResolutionPolicy, ResolutionRequest, ResolutionTraceSink, ResolutionTransaction,
    StandardResolver,
};

use super::eval::{evaluate_expr, ActorExprValues, ExprContext};
use super::{
    DaggerActorDefinition, DaggerActorFacts, DaggerActorState, DaggerAdmittedIntent, DaggerEffect,
    DaggerEvent, DaggerEvidence, DaggerFacts, DaggerFault, DaggerGameplayCatalog,
    DaggerGameplayState, DaggerIntent, DaggerInterceptor, DaggerInterceptorKind, DaggerOperation,
    DaggerPredicate, DaggerRejection, DaggerResolutionReadout, DaggerResolutionReceipt,
    DaggerRuleDefinition, DaggerSelector, DaggerSuspension, DaggerTraceDetail,
    DaggerTransactionError,
};

pub struct DaggerResolutionPolicy<'a> {
    catalog: &'a DaggerGameplayCatalog,
    snapshot: DaggerGameplayState,
}

impl<'a> DaggerResolutionPolicy<'a> {
    pub fn new(catalog: &'a DaggerGameplayCatalog, snapshot: DaggerGameplayState) -> Self {
        Self { catalog, snapshot }
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
    /// stat service: base values plus any active attributed sources.
    fn live_stats(
        &self,
        actor_id: &str,
    ) -> PolicyResult<BTreeMap<String, i64>, DaggerRejection, DaggerFault, DaggerSuspension> {
        let binding = self.binding(actor_id)?;
        let definition = self.definition(actor_id)?;
        let operation = eval_operation_id();
        let mut values = BTreeMap::new();
        for id in definition.stats.keys().chain(definition.skills.keys()) {
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
        Ok(values)
    }

    fn expr_context<'b>(
        &'b self,
        actor_id: &str,
        target_id: &str,
        actor_stats: &'b BTreeMap<String, i64>,
        target_stats: &'b BTreeMap<String, i64>,
        evidence: &'b [DaggerEvidence],
    ) -> PolicyResult<ExprContext<'b>, DaggerRejection, DaggerFault, DaggerSuspension> {
        Ok(ExprContext {
            catalog: self.catalog,
            actor: ActorExprValues {
                definition: self.definition(actor_id)?,
                stats: actor_stats,
            },
            target: Some(ActorExprValues {
                definition: self.definition(target_id)?,
                stats: target_stats,
            }),
            evidence,
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
        items: binding.items().clone(),
    })
}

impl ResolutionPolicy for DaggerResolutionPolicy<'_> {
    type RawIntent = DaggerIntent;
    type Intent = DaggerAdmittedIntent;
    type Facts = DaggerFacts;
    type Predicate = DaggerPredicate;
    type Operation = DaggerOperation;
    type Effect = DaggerEffect;
    type Event = DaggerEvent;
    type Evidence = DaggerEvidence;
    type Interceptor = DaggerInterceptor;
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

    fn interceptors(
        &mut self,
        _intent: &DaggerAdmittedIntent,
        facts: &DaggerFacts,
        _evidence: &[DaggerEvidence],
        trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<Vec<DaggerInterceptor>, DaggerRejection, DaggerFault, DaggerSuspension> {
        let mut interceptors = Vec::new();
        for item in &facts.target.items {
            if let Some(definition) = self.catalog.items().get(item) {
                if let Some(kind) = &definition.interceptor {
                    trace.record(DaggerTraceDetail::Source {
                        id: definition.id.clone(),
                    });
                    interceptors.push(DaggerInterceptor {
                        source: definition.id.clone(),
                        kind: kind.clone(),
                    });
                }
            }
        }
        Ok(interceptors)
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
            DaggerPredicate::Cmp { op, left, right } => {
                let actor_stats = self.live_stats(&intent.actor)?;
                let target_stats = self.live_stats(&intent.target)?;
                let context = self.expr_context(
                    &intent.actor,
                    &intent.target,
                    &actor_stats,
                    &target_stats,
                    evidence,
                )?;
                let left_value = evaluate_expr(left, &context).map_err(PolicyFailure::Rejected)?;
                let right_value =
                    evaluate_expr(right, &context).map_err(PolicyFailure::Rejected)?;
                let result = op.compare(left_value, right_value);
                trace.record(DaggerTraceDetail::Decision {
                    reason: format!("{left_value} {op:?} {right_value} = {result}"),
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
        ResolutionPlan<DaggerEffect, DaggerEvent, DaggerIntent, DaggerEvidence>,
        DaggerRejection,
        DaggerFault,
        DaggerSuspension,
    > {
        let mut plan = ResolutionPlan::new();
        let actor_stats = self.live_stats(&intent.actor)?;
        let target_stats = self.live_stats(&intent.target)?;
        let context = self.expr_context(
            &intent.actor,
            &intent.target,
            &actor_stats,
            &target_stats,
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
                plan.push_effect(DaggerEffect::SpendTrack {
                    actor: intent.actor.clone(),
                    track: track.clone(),
                    amount,
                });
                plan.push_event(DaggerEvent::TrackSpent {
                    actor: intent.actor.clone(),
                    track: track.clone(),
                    amount,
                });
                trace.record(DaggerTraceDetail::Decision {
                    reason: format!("spend {amount} {track} of {available}"),
                });
            }
            DaggerOperation::Damage { target, amount } => {
                let target = match target {
                    DaggerSelector::IntentTarget => &intent.target,
                };
                let amount = evaluate_expr(amount, &context).map_err(PolicyFailure::Rejected)?;
                plan.push_effect(DaggerEffect::Damage {
                    target: target.clone(),
                    amount,
                });
                plan.push_event(DaggerEvent::DamageApplied {
                    target: target.clone(),
                    amount,
                });
                trace.record(DaggerTraceDetail::Decision {
                    reason: format!("damage {amount} to {target}"),
                });
            }
        }
        Ok(plan)
    }

    fn before_commit(
        &mut self,
        interceptor: &DaggerInterceptor,
        _intent: &DaggerAdmittedIntent,
        _facts: &DaggerFacts,
        _evidence: &[DaggerEvidence],
        plan: &mut ResolutionPlan<DaggerEffect, DaggerEvent, DaggerIntent, DaggerEvidence>,
        trace: &mut dyn ResolutionTraceSink<DaggerTraceDetail>,
    ) -> PolicyResult<(), DaggerRejection, DaggerFault, DaggerSuspension> {
        match interceptor.kind {
            DaggerInterceptorKind::ReduceDamage { amount } => {
                for effect in plan.effects_mut() {
                    if let DaggerEffect::Damage { amount: damage, .. } = effect {
                        *damage = damage.saturating_sub(amount).max(0);
                    }
                }
                for event in plan.events_mut() {
                    if let DaggerEvent::DamageApplied { amount: damage, .. } = event {
                        *damage = damage.saturating_sub(amount).max(0);
                    }
                }
                plan.push_event(DaggerEvent::InterceptorApplied {
                    source: interceptor.source.clone(),
                    amount,
                });
                trace.record(DaggerTraceDetail::Source {
                    id: interceptor.source.clone(),
                });
            }
        }
        Ok(())
    }
}

pub struct DaggerTransaction<'a> {
    state: &'a mut DaggerGameplayState,
    mechanics: &'a rusty_engine::gameplay_mechanics::MechanicsCatalog,
    operation: OperationId,
    staged: Vec<DaggerEffect>,
}

impl<'a> DaggerTransaction<'a> {
    pub fn new(
        state: &'a mut DaggerGameplayState,
        mechanics: &'a rusty_engine::gameplay_mechanics::MechanicsCatalog,
        operation: OperationId,
    ) -> Self {
        Self {
            state,
            mechanics,
            operation,
            staged: Vec::new(),
        }
    }
}

impl ResolutionTransaction for DaggerTransaction<'_> {
    type Effect = DaggerEffect;
    type Error = DaggerTransactionError;

    fn stage(&mut self, effect: &DaggerEffect) -> Result<(), DaggerTransactionError> {
        self.staged.push(effect.clone());
        Ok(())
    }

    fn commit(&mut self) -> Result<(), DaggerTransactionError> {
        let mut candidate = self.state.clone();
        for effect in &self.staged {
            let (actor, track, amount) = match effect {
                DaggerEffect::SpendTrack {
                    actor,
                    track,
                    amount,
                } => (actor, track.clone(), *amount),
                DaggerEffect::Damage { target, amount } => (target, "health".to_string(), *amount),
            };
            let binding = candidate
                .actors()
                .get(actor)
                .ok_or_else(|| DaggerTransactionError::UnknownActor(actor.clone()))?
                .entity();
            let source = SourceInstanceIdentity::Request {
                operation: self.operation.clone(),
                instance: SourceInstanceId::parse("dagger-policy").expect("fixed source identity"),
            };
            TrackService::spend(
                candidate.entities_mut(),
                self.mechanics,
                TrackMutationRequest {
                    operation: self.operation.clone(),
                    source,
                    entity: binding,
                    track: TrackId::parse(track.clone()).map_err(|error| {
                        DaggerTransactionError::Mechanics(format!("track id {track}: {error:?}"))
                    })?,
                    amount: MechanicsScalar::new(amount).map_err(|error| {
                        DaggerTransactionError::Mechanics(format!("amount {amount}: {error:?}"))
                    })?,
                    kind: TrackAdjustmentKind::Spend,
                    expected_revision: None,
                },
            )
            .map_err(|error| {
                DaggerTransactionError::Mechanics(format!(
                    "track spend {amount} {track} for {actor}: {error:?}"
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
    let mut policy = DaggerResolutionPolicy::new(catalog, snapshot);
    let operation = OperationId::parse(format!(
        "resolution-{}-{}",
        identity.correlation().get(),
        identity.resolution().get()
    ))
    .expect("resolution operation identity fits identity limits");
    let mut transaction = DaggerTransaction::new(state, catalog.mechanics(), operation);
    let receipt = StandardResolver::default().resolve(
        &mut policy,
        &mut transaction,
        ResolutionRequest::new(identity, mode, intent, evidence),
    );
    let readout = DaggerResolutionReadout::from_receipt(catalog.fingerprint(), &receipt);
    (receipt, readout)
}
