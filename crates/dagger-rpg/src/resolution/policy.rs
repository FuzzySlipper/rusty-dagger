use rusty_engine::gameplay_resolution::{
    PolicyFailure, PolicyResult, ResolutionIdentity, ResolutionMode, ResolutionPlan,
    ResolutionPolicy, ResolutionRequest, ResolutionTraceSink, ResolutionTransaction,
    StandardResolver,
};

use super::eval::{evaluate_expr, ExprContext};
use super::{
    DaggerActorDefinition, DaggerAdmittedIntent, DaggerEffect, DaggerEvent, DaggerEvidence,
    DaggerFacts, DaggerFault, DaggerGameplayCatalog, DaggerGameplayState, DaggerIntent,
    DaggerInterceptor, DaggerInterceptorKind, DaggerOperation, DaggerPredicate, DaggerRejection,
    DaggerResolutionReadout, DaggerResolutionReceipt, DaggerRuleDefinition, DaggerSelector,
    DaggerSuspension, DaggerTraceDetail, DaggerTransactionError,
};

pub struct DaggerResolutionPolicy<'a> {
    catalog: &'a DaggerGameplayCatalog,
    snapshot: DaggerGameplayState,
}

impl<'a> DaggerResolutionPolicy<'a> {
    pub fn new(catalog: &'a DaggerGameplayCatalog, snapshot: DaggerGameplayState) -> Self {
        Self { catalog, snapshot }
    }

    fn definition(
        &self,
        actor_id: &str,
    ) -> PolicyResult<&DaggerActorDefinition, DaggerRejection, DaggerFault, DaggerSuspension> {
        let state = self.snapshot.actor(actor_id).ok_or_else(|| {
            PolicyFailure::Rejected(DaggerRejection::UnknownActor(actor_id.to_string()))
        })?;
        self.catalog
            .actors()
            .get(state.definition())
            .ok_or_else(|| {
                PolicyFailure::Fault(DaggerFault::InvalidProgram(format!(
                    "actor {actor_id} references unknown definition {}",
                    state.definition()
                )))
            })
    }

    fn expr_context<'b>(
        &'b self,
        actor_definition: &'b DaggerActorDefinition,
        target_definition: Option<&'b DaggerActorDefinition>,
        evidence: &'b [DaggerEvidence],
    ) -> ExprContext<'b> {
        ExprContext {
            catalog: self.catalog,
            actor: actor_definition,
            target: target_definition,
            evidence,
        }
    }
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
        let actor = self.snapshot.actor(&intent.actor).cloned().ok_or_else(|| {
            PolicyFailure::Rejected(DaggerRejection::UnknownActor(intent.actor.clone()))
        })?;
        let target = self
            .snapshot
            .actor(&intent.target)
            .cloned()
            .ok_or_else(|| {
                PolicyFailure::Rejected(DaggerRejection::UnknownTarget(intent.target.clone()))
            })?;
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
            if intent.action.tags.contains(tag) && facts.actor.conditions().contains(condition) {
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
        for item in facts.target.items() {
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
                let actor_definition = self.definition(&intent.actor)?;
                let target_definition = self.definition(&intent.target)?;
                let context =
                    self.expr_context(actor_definition, Some(target_definition), evidence);
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
        let actor_definition = self.definition(&intent.actor)?;
        let target_definition = self.definition(&intent.target)?;
        let context = self.expr_context(actor_definition, Some(target_definition), evidence);
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
    staged: Vec<DaggerEffect>,
}

impl<'a> DaggerTransaction<'a> {
    pub fn new(state: &'a mut DaggerGameplayState) -> Self {
        Self {
            state,
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
            match effect {
                DaggerEffect::SpendTrack {
                    actor,
                    track,
                    amount,
                } => candidate
                    .actor_mut(actor)
                    .ok_or_else(|| DaggerTransactionError::UnknownActor(actor.clone()))?
                    .spend_track(track, *amount)?,
                DaggerEffect::Damage { target, amount } => candidate
                    .actor_mut(target)
                    .ok_or_else(|| DaggerTransactionError::UnknownActor(target.clone()))?
                    .apply_damage(*amount)?,
            }
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
    let mut transaction = DaggerTransaction::new(state);
    let receipt = StandardResolver::default().resolve(
        &mut policy,
        &mut transaction,
        ResolutionRequest::new(identity, mode, intent, evidence),
    );
    let readout = DaggerResolutionReadout::from_receipt(catalog.fingerprint(), &receipt);
    (receipt, readout)
}
