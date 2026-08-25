use crate::{adapter::KernelError, model::DaggerProductAuthority};
use dagger_runtime::{DaggerRuntime, ResolvedPlayerFrame};
use rusty_engine::product_kernel::serde_json::Value;
use rusty_engine::runtime_mutation::{
    MutationOwnerEvidence, MutationPlanner, MutationResolvedBatch, MutationStage,
};

const SIMULATION_STEP_SECONDS: f64 = 1.0 / 60.0;

#[derive(Debug, Default)]
pub struct DaggerProductPlanner;
impl MutationPlanner<DaggerProductAuthority, u32> for DaggerProductPlanner {
    type Error = KernelError;
    fn stage(
        &mut self,
        authority: &DaggerProductAuthority,
        batch: &MutationResolvedBatch,
    ) -> Result<MutationStage<DaggerProductAuthority, u32>, Self::Error> {
        let mut candidate = DaggerProductAuthority {
            runtime: authority
                .runtime
                .clone_for_staging()
                .map_err(runtime_error)?,
            revision: authority
                .revision
                .checked_add(1)
                .ok_or_else(|| KernelError::Mutation("revision overflow".to_owned()))?,
            static_scene_ops: authority.static_scene_ops.clone(),
        };
        for op in batch.operations() {
            apply(&mut candidate.runtime, op.target(), op.payload())?;
        }
        let evidence = batch
            .operations()
            .iter()
            .enumerate()
            .map(|(i, op)| MutationOwnerEvidence::for_operation(op, i as u32))
            .collect();
        Ok(MutationStage::new(candidate, evidence))
    }
}
fn runtime_error(error: impl std::fmt::Display) -> KernelError {
    KernelError::Mutation(error.to_string())
}
fn u(payload: &Value, key: &str) -> Result<u64, KernelError> {
    match payload.get(key) {
        Some(Value::String(v)) => v
            .parse()
            .map_err(|_| KernelError::Mutation(format!("invalid {key}"))),
        Some(v) => v
            .as_u64()
            .ok_or_else(|| KernelError::Mutation(format!("invalid {key}"))),
        None => Err(KernelError::Mutation(format!("missing {key}"))),
    }
}
fn s<'a>(payload: &'a Value, key: &str) -> Result<&'a str, KernelError> {
    payload
        .get(key)
        .and_then(Value::as_str)
        .ok_or_else(|| KernelError::Mutation(format!("missing {key}")))
}
fn apply(runtime: &mut DaggerRuntime, target: &str, p: &Value) -> Result<(), KernelError> {
    match target {
        "kernel.dagger-move" | "kernel.dagger-look" => {
            runtime
                .apply_player_frame(ResolvedPlayerFrame {
                    forward: p.get("forward").and_then(Value::as_f64).unwrap_or(0.0) as f32,
                    right: p.get("right").and_then(Value::as_f64).unwrap_or(0.0) as f32,
                    yaw_delta: p.get("yaw").and_then(Value::as_f64).unwrap_or(0.0) as f32,
                    pitch_delta: p.get("pitch").and_then(Value::as_f64).unwrap_or(0.0) as f32,
                    step_seconds: 0.05,
                })
                .map_err(runtime_error)?;
        }
        "kernel.dagger-simulation-result" => {
            let fields = p.as_object().ok_or_else(|| {
                KernelError::Mutation("simulation payload must be an object".to_owned())
            })?;
            if fields.len() != 4
                || fields.get("kind").and_then(Value::as_str) != Some("dagger.simulation.result.v1")
                || fields.get("operationBinding").and_then(Value::as_str)
                    != Some("dagger.simulation-result")
                || fields.get("operationType").and_then(Value::as_str)
                    != Some("dagger.simulation.result.v1")
            {
                return Err(KernelError::Mutation(
                    "invalid dagger.simulation.result.v1 payload".to_owned(),
                ));
            }
            let step_seconds = fields
                .get("stepSeconds")
                .and_then(Value::as_f64)
                .ok_or_else(|| {
                    KernelError::Mutation("missing simulation stepSeconds".to_owned())
                })?;
            if !step_seconds.is_finite()
                || step_seconds.to_bits() != SIMULATION_STEP_SECONDS.to_bits()
            {
                return Err(KernelError::Mutation(
                    "dagger.simulation.result.v1 requires the fixed 60 Hz step".to_owned(),
                ));
            }
            runtime
                .tick_play_session(SIMULATION_STEP_SECONDS as f32)
                .map_err(runtime_error)?;
        }
        "kernel.dagger-attack" => {
            runtime.attack_focused_target().map_err(runtime_error)?;
        }
        "kernel.dagger-session" => {
            runtime.reset_play_session().map_err(runtime_error)?;
        }
        "kernel.dagger-content" => {
            runtime
                .jump_to_content(u(p, "id")?)
                .map_err(runtime_error)?;
        }
        "kernel.dagger-equipment" => {
            if p.get("expectedItem").is_some() {
                runtime
                    .unequip_item_from_slot(
                        s(p, "slot")?,
                        u(p, "expectedItem")?,
                        u(p, "expectedEquipmentRevision")?,
                    )
                    .map_err(runtime_error)?;
            } else if p.get("slot").is_some() {
                runtime
                    .equip_item_in_slot(
                        u(p, "item")?,
                        s(p, "slot")?,
                        u(p, "expectedEquipmentRevision")?,
                    )
                    .map_err(runtime_error)?;
            } else {
                runtime.equip_cycle().map_err(runtime_error)?;
            }
        }
        "kernel.dagger-loot" => match p.get("action").and_then(Value::as_str) {
            Some("open") => {
                runtime.open_aimed_loot().map_err(runtime_error)?;
            }
            Some("close") => {
                runtime.close_loot().map_err(runtime_error)?;
            }
            _ if p.get("quantity").is_some() => {
                runtime
                    .transfer_loot_stack(
                        s(p, "containerId")?,
                        u(p, "expectedInventoryRevision")?,
                        s(p, "item")?,
                        u(p, "quantity")?,
                    )
                    .map_err(runtime_error)?;
            }
            _ => {
                runtime
                    .transfer_loot_item(
                        s(p, "containerId")?,
                        u(p, "expectedInventoryRevision")?,
                        u(p, "item")?,
                    )
                    .map_err(runtime_error)?;
            }
        },
        "kernel.dagger-inventory" => runtime.move_inventory_item(p).map_err(runtime_error)?,
        "kernel.dagger-settings" => runtime
            .apply_settings_update(p)
            .map(|_| ())
            .map_err(runtime_error)?,
        "kernel.dagger-debug" => runtime.toggle_debug_nav().map_err(runtime_error)?,
        "kernel.dagger-observe-pairs-result" => runtime
            .apply_observe_pairs_result(p)
            .map_err(runtime_error)?,
        other => return Err(KernelError::Mutation(format!("unknown target {other}"))),
    };
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::apply;
    use dagger_runtime::DaggerRuntime;
    use rusty_engine::product_kernel::serde_json::json;

    const PROJECT: &[u8] =
        include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.project.json");
    const NAVGRID: &[u8] =
        include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.navgrid.json");
    const ENCOUNTERS: &[u8] =
        include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.encounters.json");
    const GAMEPLAY: &[u8] =
        include_bytes!("../dagger-runtime/tests/fixtures/dagger-core.package.json");

    fn runtime() -> DaggerRuntime {
        DaggerRuntime::from_product_resources(PROJECT, NAVGRID, ENCOUNTERS, GAMEPLAY)
            .expect("admitted Dagger runtime fixture")
    }

    #[test]
    fn simulation_payload_is_closed_and_rejects_without_mutating_the_candidate() {
        let mut runtime = runtime();
        let before = runtime.product_readout().expect("initial readout");
        let error = apply(
            &mut runtime,
            "kernel.dagger-simulation-result",
            &json!({
                "kind": "dagger.simulation.result.v1",
                "operationBinding": "dagger.simulation-result",
                "operationType": "dagger.simulation.result.v1",
                "stepSeconds": 0.05,
            }),
        )
        .expect_err("non-60 Hz schedule payload is rejected");
        assert!(error.to_string().contains("fixed 60 Hz"));
        assert_eq!(
            runtime.product_readout().expect("unchanged readout"),
            before,
            "failed simulation payload cannot partially mutate the staged candidate"
        );
        apply(
            &mut runtime,
            "kernel.dagger-simulation-result",
            &json!({
                "kind": "dagger.simulation.result.v1",
                "operationBinding": "dagger.simulation-result",
                "operationType": "dagger.simulation.result.v1",
                "stepSeconds": 1.0 / 60.0,
            }),
        )
        .expect("fixed 60 Hz simulation invokes the real Dagger tick");
    }
}
