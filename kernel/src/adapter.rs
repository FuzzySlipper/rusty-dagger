use std::fmt;

use rusty_engine::product_kernel::serde_json::{json, Value};
use rusty_engine::{
    product_kernel::ProductRuntimeResources,
    runtime_composition::{ProductRuntimeAdapter, ProductRuntimeOutputs},
    runtime_input::{InputFrame, RuntimeIntentEnvelope, RuntimeIntentValue},
    runtime_lifecycle::{RuntimeLifecycle, RuntimePhaseToken, SimulationStep},
    runtime_mutation::{
        MutationAuthority, MutationBatch, MutationBatchId, MutationCausation, MutationOperation,
        MutationOperationId, MutationProvenance,
    },
    runtime_schedule::ScheduleSystemInvocation,
    runtime_standard_capabilities::{
        ObservePairsBatchIdentity, ObservePairsPlan, OBSERVE_PAIRS_TARGET,
    },
    runtime_timeline::TimelineRelease,
};

use crate::{
    model::DaggerProductAuthority, planner::DaggerProductPlanner, projection::dagger_ui_projection,
    resources::DaggerRuntimeResources,
};

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum KernelError {
    MissingResource(String),
    InvalidResource { resource: String, detail: String },
    InvalidIntent { intent: String, detail: String },
    PendingOverflow,
    Mutation(String),
    Schedule(String),
    Projection(String),
}

impl fmt::Display for KernelError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "Dagger Product Kernel error: {self:?}")
    }
}
impl std::error::Error for KernelError {}

const MAX_PENDING_OPERATIONS: usize = 64;
/// Privateer's Hold runs its one authored realtime system at 60 Hz.  The
/// adapter owns this closed value so schedule data cannot be smuggled through
/// an input operation or supplied by a host clock.
const SIMULATION_STEP_SECONDS: f64 = 1.0 / 60.0;

#[derive(Debug, Clone)]
struct PendingOperation {
    binding: String,
    target: String,
    payload: Value,
    causation: String,
}

/// Thin lifecycle adapter. It has no loop, clock, callback, filesystem, or
/// registry: each hook only queues closed data for the one mutation lane.
pub struct DaggerProductAdapter {
    authority: DaggerProductAuthority,
    planner: DaggerProductPlanner,
    pending: Vec<PendingOperation>,
    initial_render_offset: usize,
    initial_render_complete: bool,
    pub(crate) observe_pairs: Vec<ObservePairsPlan>,
}

impl DaggerProductAdapter {
    pub fn from_resources(resources: ProductRuntimeResources<'_>) -> Result<Self, KernelError> {
        let static_scene_ops = crate::projection::static_scene_ops(resources)?;
        let resources = DaggerRuntimeResources::decode(resources)?;
        let runtime = dagger_runtime::DaggerRuntime::from_product_resources_with_mesh_resource(
            resources.project,
            resources.navgrid,
            resources.encounters,
            resources.gameplay_package,
            resources.dungeon_mesh,
        )
        .map_err(|error| KernelError::InvalidResource {
            resource: "Dagger runtime resources".to_owned(),
            detail: error.to_string(),
        })?;
        Ok(Self {
            authority: DaggerProductAuthority::new(runtime, static_scene_ops),
            planner: DaggerProductPlanner,
            pending: Vec::new(),
            initial_render_offset: 0,
            initial_render_complete: false,
            observe_pairs: Vec::new(),
        })
    }

    fn queue(
        &mut self,
        binding: impl Into<String>,
        target: impl Into<String>,
        payload: Value,
        causation: impl Into<String>,
    ) -> Result<(), KernelError> {
        if self.pending.len() == MAX_PENDING_OPERATIONS {
            return Err(KernelError::PendingOverflow);
        }
        self.pending.push(PendingOperation {
            binding: binding.into(),
            target: target.into(),
            payload,
            causation: causation.into(),
        });
        Ok(())
    }

    fn queue_intent(&mut self, intent: &RuntimeIntentEnvelope) -> Result<(), KernelError> {
        let active = matches!(intent.value(), RuntimeIntentValue::Digital { active: true });
        let axis = match intent.value() {
            RuntimeIntentValue::Axis { value } => Some(value.value()),
            _ => None,
        };
        let payload = match intent.value() {
            RuntimeIntentValue::ProductPayload { payload } => Some(payload),
            _ => None,
        };
        match intent.intent() {
            "dagger.move.forward" if active => self.queue(
                "dagger.move",
                "kernel.dagger-move",
                json!({"forward": 1.0, "right": 0.0}),
                "input.move",
            ),
            "dagger.move.backward" if active => self.queue(
                "dagger.move",
                "kernel.dagger-move",
                json!({"forward": -1.0, "right": 0.0}),
                "input.move",
            ),
            "dagger.move.left" if active => self.queue(
                "dagger.move",
                "kernel.dagger-move",
                json!({"forward": 0.0, "right": -1.0}),
                "input.move",
            ),
            "dagger.move.right" if active => self.queue(
                "dagger.move",
                "kernel.dagger-move",
                json!({"forward": 0.0, "right": 1.0}),
                "input.move",
            ),
            "dagger.look.yaw" => self.queue(
                "dagger.look",
                "kernel.dagger-look",
                json!({"yaw": axis.unwrap_or(0.0), "pitch": 0.0}),
                "input.look",
            ),
            "dagger.look.pitch" => self.queue(
                "dagger.look",
                "kernel.dagger-look",
                json!({"yaw": 0.0, "pitch": axis.unwrap_or(0.0)}),
                "input.look",
            ),
            "dagger.attack" if active => self.queue(
                "dagger.attack",
                "kernel.dagger-attack",
                json!({}),
                "input.attack",
            ),
            "dagger.session.reset" if active => self.queue(
                "dagger.session",
                "kernel.dagger-session",
                json!({"action": "reset"}),
                "intent.reset",
            ),
            "dagger.loot.open" if active => self.queue(
                "dagger.loot",
                "kernel.dagger-loot",
                json!({"action": "open"}),
                "intent.loot-open",
            ),
            "dagger.loot.close" if active => self.queue(
                "dagger.loot",
                "kernel.dagger-loot",
                json!({"action": "close"}),
                "intent.loot-close",
            ),
            "dagger.debug.nav" if active => self.queue(
                "dagger.debug",
                "kernel.dagger-debug",
                json!({"toggle": "nav"}),
                "input.debug-nav",
            ),
            "dagger.content.jump" => self.queue_payload(
                intent,
                payload.as_ref(),
                "dagger.content.jump.v1",
                "dagger.content",
                "kernel.dagger-content",
                "intent.jump",
            ),
            "dagger.equipment.equip" => self.queue_payload(
                intent,
                payload.as_ref(),
                "dagger.equipment.equip.v1",
                "dagger.equipment",
                "kernel.dagger-equipment",
                "intent.equip",
            ),
            "dagger.equipment.unequip" => self.queue_payload(
                intent,
                payload.as_ref(),
                "dagger.equipment.unequip.v1",
                "dagger.equipment",
                "kernel.dagger-equipment",
                "intent.unequip",
            ),
            "dagger.inventory.move" => self.queue_payload(
                intent,
                payload.as_ref(),
                "dagger.inventory.move.v1",
                "dagger.inventory",
                "kernel.dagger-inventory",
                "intent.inventory-move",
            ),
            "dagger.loot.transfer-stack" => self.queue_payload(
                intent,
                payload.as_ref(),
                "dagger.loot.transfer-stack.v1",
                "dagger.loot",
                "kernel.dagger-loot",
                "intent.loot-stack",
            ),
            "dagger.loot.transfer-item" => self.queue_payload(
                intent,
                payload.as_ref(),
                "dagger.loot.transfer-item.v1",
                "dagger.loot",
                "kernel.dagger-loot",
                "intent.loot-item",
            ),
            "dagger.settings.update" => self.queue_payload(
                intent,
                payload.as_ref(),
                "dagger.settings.update.v1",
                "dagger.settings",
                "kernel.dagger-settings",
                "intent.settings",
            ),
            _ => Ok(()),
        }
    }

    fn queue_payload(
        &mut self,
        intent: &RuntimeIntentEnvelope,
        payload: Option<&rusty_engine::runtime_input::RuntimeProductPayload>,
        contract: &str,
        binding: &'static str,
        target: &'static str,
        causation: &'static str,
    ) -> Result<(), KernelError> {
        let payload = payload.ok_or_else(|| KernelError::InvalidIntent {
            intent: intent.intent().to_owned(),
            detail: "requires product payload".to_owned(),
        })?;
        if intent.descriptor().payload_contract() != Some(contract)
            || payload.contract() != contract
        {
            return Err(KernelError::InvalidIntent {
                intent: intent.intent().to_owned(),
                detail: format!("expected payload contract {contract}"),
            });
        }
        self.queue(binding, target, payload.data().clone(), causation)
    }

    #[cfg(test)]
    pub(crate) fn from_runtime_for_test(runtime: dagger_runtime::DaggerRuntime) -> Self {
        Self {
            authority: DaggerProductAuthority::new(runtime, Vec::new()),
            planner: DaggerProductPlanner,
            pending: Vec::new(),
            initial_render_offset: 0,
            initial_render_complete: true,
            observe_pairs: Vec::new(),
        }
    }

    #[cfg(test)]
    pub(crate) fn runtime_for_test(&self) -> &dagger_runtime::DaggerRuntime {
        &self.authority.runtime
    }
}

impl MutationAuthority for DaggerProductAuthority {
    type Guard = u64;
    fn guard(&self) -> Self::Guard {
        self.revision
    }
    fn publication_domain(&self) -> &str {
        "dagger.session"
    }
}

impl ProductRuntimeAdapter for DaggerProductAdapter {
    type Authority = DaggerProductAuthority;
    type Guard = u64;
    type Planner = DaggerProductPlanner;
    type Evidence = u32;
    type Error = KernelError;
    type ScheduleOutput = String;
    type UiOutput = Value;

    fn on_input(
        &mut self,
        _frame: &InputFrame,
        intents: &[RuntimeIntentEnvelope],
    ) -> Result<(), Self::Error> {
        for intent in intents {
            self.queue_intent(intent)?;
        }
        Ok(())
    }

    fn dispatch_schedule(
        &mut self,
        invocation: ScheduleSystemInvocation<'_>,
        _lifecycle: &RuntimeLifecycle,
        _token: RuntimePhaseToken,
    ) -> Result<Self::ScheduleOutput, Self::Error> {
        if invocation.system().capability().target() == OBSERVE_PAIRS_TARGET {
            let plan = self
                .observe_pairs
                .iter()
                .find(|plan| plan.matches_system(invocation.system()))
                .ok_or_else(|| {
                    KernelError::Schedule(format!(
                        "missing retained observe-pairs plan for {}",
                        invocation.system_id()
                    ))
                })?;
            let emission = self
                .authority
                .runtime
                .evaluate_observe_pairs_and_batch(
                    plan,
                    ObservePairsBatchIdentity {
                        batch_id: MutationBatchId::new(format!(
                            "dagger-observe-{}",
                            invocation.step().value()
                        ))
                        .map_err(|error| KernelError::Mutation(error.to_string()))?,
                        causation: MutationCausation::new("schedule.observe-pairs")
                            .map_err(|error| KernelError::Mutation(error.to_string()))?,
                        provenance: MutationProvenance::new("rusty-dagger.kernel.observe-pairs")
                            .map_err(|error| KernelError::Mutation(error.to_string()))?,
                        operation_id: MutationOperationId::new(
                            invocation
                                .step()
                                .value()
                                .saturating_mul(MAX_PENDING_OPERATIONS as u64),
                        ),
                    },
                )
                .map_err(|error| KernelError::Schedule(error.to_string()))?;
            for operation in emission.batch.operations() {
                self.queue(
                    operation.binding_id(),
                    operation.target(),
                    operation.payload().clone(),
                    "schedule.observe-pairs",
                )?;
            }
            return Ok(format!(
                "observe-pairs {} aggregates",
                emission.readout.aggregates.len()
            ));
        }
        match invocation.system_id() {
            "dagger.simulation" => {
                let payload = invocation.system().payload();
                let fields = payload.as_object().ok_or_else(|| {
                    KernelError::Schedule("dagger.simulation payload must be an object".to_owned())
                })?;
                if fields.len() != 3
                    || fields.get("kind").and_then(Value::as_str) != Some("dagger.simulation.v1")
                    || fields.get("operationBinding").and_then(Value::as_str)
                        != Some("dagger.simulation-result")
                    || fields.get("operationType").and_then(Value::as_str)
                        != Some("dagger.simulation.result.v1")
                {
                    return Err(KernelError::Schedule(
                        "invalid dagger.simulation schedule payload".to_owned(),
                    ));
                }
                self.queue(
                    "dagger.simulation-result",
                    "kernel.dagger-simulation-result",
                    json!({
                        "kind": "dagger.simulation.result.v1",
                        "operationBinding": "dagger.simulation-result",
                        "operationType": "dagger.simulation.result.v1",
                        "stepSeconds": SIMULATION_STEP_SECONDS,
                    }),
                    "schedule.simulation",
                )?;
                Ok("dagger simulation queued".to_owned())
            }
            other => Err(KernelError::Schedule(format!(
                "unsupported Dagger schedule system {other}"
            ))),
        }
    }

    fn on_timeline_releases(&mut self, _releases: &TimelineRelease) -> Result<(), Self::Error> {
        // Privateer's Hold declares no timeline work.  This required lifecycle
        // hook is intentionally observational and must not invent a second
        // consequence authority.
        Ok(())
    }

    fn prepare_mutation(
        &mut self,
        step: SimulationStep,
    ) -> Result<Option<MutationBatch>, Self::Error> {
        if self.pending.is_empty() {
            return Ok(None);
        }
        let pending = std::mem::take(&mut self.pending);
        let causation = pending.first().expect("nonempty checked").causation.clone();
        let operations = pending
            .into_iter()
            .enumerate()
            .map(|(index, operation)| {
                MutationOperation::new(
                    MutationOperationId::new(
                        step.value()
                            .saturating_mul(MAX_PENDING_OPERATIONS as u64)
                            .saturating_add(index as u64),
                    ),
                    operation.binding,
                    operation.target,
                    operation.payload,
                )
                .map_err(|error| KernelError::Mutation(error.to_string()))
            })
            .collect::<Result<Vec<_>, _>>()?;
        MutationBatch::new(
            MutationBatchId::new(format!("dagger-step-{}", step.value()))
                .map_err(|error| KernelError::Mutation(error.to_string()))?,
            MutationCausation::new(causation)
                .map_err(|error| KernelError::Mutation(error.to_string()))?,
            MutationProvenance::new("rusty-dagger.kernel")
                .map_err(|error| KernelError::Mutation(error.to_string()))?,
            operations,
        )
        .map(Some)
        .map_err(|error| KernelError::Mutation(error.to_string()))
    }

    fn mutation_parts(&mut self) -> (&mut Self::Authority, &mut Self::Planner) {
        (&mut self.authority, &mut self.planner)
    }

    fn project(
        &mut self,
        _lifecycle: &RuntimeLifecycle,
        _token: RuntimePhaseToken,
    ) -> Result<ProductRuntimeOutputs<Self::UiOutput>, Self::Error> {
        let (output, next_offset, complete) = dagger_ui_projection(
            &self.authority,
            !self.initial_render_complete,
            self.initial_render_offset,
        )
        .map_err(KernelError::Projection)?;
        self.initial_render_offset = next_offset;
        self.initial_render_complete = complete;
        Ok(output)
    }
}
