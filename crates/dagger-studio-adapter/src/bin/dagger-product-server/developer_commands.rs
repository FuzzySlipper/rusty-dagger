//! Dagger's thin composition of the Engine developer-command contract.
//!
//! The Engine owns the envelope, discovery, correlation preflight, history,
//! generated TypeScript client, and public shell.  This module owns only the
//! product-safe-point bindings and the explicitly namespaced Dagger scenarios.

use std::convert::Infallible;

use dagger_runtime::{DaggerRuntime, ProductReadout, RuntimeError};
use rusty_engine::core_ids::EntityId;
use rusty_engine::{
    developer_command::{
        map_command_response, CommandBindings, CommandDescriptor, CommandId, CommandLane,
        CommandProfile, CommandRequest, CommandResponse, DeveloperCommand, DispatchFacts,
        HandlerResult, HostCommandDiscovery, HostCommandOutcome, HostCommandRequest,
        HostCommandResponse, HostDecimalU64, HostErrorBody, HostErrorCode, HostErrorMessage,
        HostReceiptRefs, HostResponseContext, ParameterDescriptor, ProfileId, RuntimeInstanceId,
        TypeDescriptor,
    },
    developer_command_standard::{
        AdminSetTrack, HostEntityRequest, HostTrackSetReceipt, HostTrackSetRequest, InspectEntity,
        InspectMechanics,
    },
    gameplay_mechanics::MechanicsError,
};
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use serde_json::Value;

const PROFILE: &str = "dagger.developer";
const RUNTIME: &str = "dagger-product";
const CONTRACT: &str = "dagger.developer.v1";
const CATALOG_EPOCH: u64 = 1;
const HISTORY_CAPACITY: usize = 128;

pub(crate) type DaggerDeveloperRequest = HostCommandRequest<Value>;
pub(crate) type DaggerDeveloperResponse = HostCommandResponse<Value, Value>;

/// Engine command bindings remain private to the connected product loop.  The
/// live `DaggerRuntime` is borrowed only while a queued request reaches that
/// loop's existing safe point.
pub(crate) struct DaggerDeveloperCommands {
    bindings: CommandBindings,
}

impl DaggerDeveloperCommands {
    pub(crate) fn new() -> Result<Self, String> {
        let profile = CommandProfile::new(
            ProfileId::parse(PROFILE).expect("fixed Dagger developer profile"),
            [CommandLane::Inspect, CommandLane::Play, CommandLane::Admin],
        )
        .expect("fixed Dagger developer lanes");
        let mut bindings = CommandBindings::new(profile, developer_facts(), HISTORY_CAPACITY)
            .map_err(|error| error.to_string())?;
        bindings
            .expose_borrowed::<InspectEntity>()
            .and_then(|()| bindings.expose_borrowed::<InspectMechanics>())
            .and_then(|()| bindings.expose_borrowed::<AdminSetTrack>())
            .and_then(|()| bindings.expose_borrowed::<DaggerScenarioPrepare>())
            .and_then(|()| bindings.expose_borrowed::<DaggerScenarioMelee>())
            .and_then(|()| bindings.expose_borrowed::<DaggerScenarioAdvance>())
            .and_then(|()| bindings.expose_borrowed::<DaggerScenarioProgression>())
            .map_err(|error| error.to_string())?;
        Ok(Self { bindings })
    }

    pub(crate) fn discover(&self) -> HostCommandDiscovery {
        HostCommandDiscovery::from_bindings(
            &self.bindings,
            CommandId::parse(CONTRACT).expect("fixed Dagger command contract identity"),
        )
    }

    /// Dispatch one already-queued request at the connected application's
    /// safe point.  No HTTP handler, client, or shell can mutate the runtime
    /// directly.
    pub(crate) fn execute(
        &mut self,
        runtime: &mut DaggerRuntime,
        request: DaggerDeveloperRequest,
    ) -> DaggerDeveloperResponse {
        self.bindings.set_facts(developer_facts());
        let fallback = HostResponseContext::new(
            request.correlation.clone(),
            request.expected.profile.clone(),
        );
        let (request, response_context) = match request.into_command_parts() {
            Ok(parts) => parts,
            Err(error) => {
                return host_error(
                    fallback,
                    self.bindings.facts(),
                    "invalid-envelope",
                    error.to_string(),
                )
            }
        };
        match request.command.as_str() {
            "standard.inspect.entity" => {
                let request = match decode_entity(request) {
                    Ok(request) => request,
                    Err(error) => {
                        return host_error(
                            response_context,
                            self.bindings.facts(),
                            "invalid-payload",
                            error,
                        )
                    }
                };
                let response = self.bindings.dispatch_borrowed::<InspectEntity, _>(
                    request,
                    &mut |_context, entity| {
                        Ok::<_, Infallible>(runtime.developer_inspect_entity(entity))
                    },
                );
                erase_mapped(
                    response,
                    response_context,
                    HostReceiptRefs::empty(),
                    infallible_error,
                )
            }
            "standard.inspect.mechanics" => {
                let request = match decode_entity(request) {
                    Ok(request) => request,
                    Err(error) => {
                        return host_error(
                            response_context,
                            self.bindings.facts(),
                            "invalid-payload",
                            error,
                        )
                    }
                };
                let response = self
                    .bindings
                    .dispatch_borrowed::<InspectMechanics, _>(request, &mut |_context, entity| {
                        runtime.developer_inspect_mechanics(entity)
                    });
                erase_mapped(
                    response,
                    response_context,
                    HostReceiptRefs::empty(),
                    mechanics_error,
                )
            }
            "standard.admin.track.set" => {
                let request = match decode_payload::<HostTrackSetRequest>(request)
                    .and_then(|request| map_track_request(runtime, request))
                {
                    Ok(request) => request,
                    Err(error) => {
                        return host_error(
                            response_context,
                            self.bindings.facts(),
                            "invalid-payload",
                            error,
                        )
                    }
                };
                let response = self
                    .bindings
                    .dispatch_borrowed::<AdminSetTrack, _>(request, &mut |_context, request| {
                        runtime.developer_set_track(request)
                    });
                erase_mapped(
                    project_track_response(response),
                    response_context,
                    HostReceiptRefs::empty(),
                    mechanics_error,
                )
            }
            "dagger.scenario.prepare" => self.dispatch_prepare(runtime, request, response_context),
            "dagger.scenario.melee" => self.dispatch_melee(runtime, request, response_context),
            "dagger.scenario.advance" => self.dispatch_advance(runtime, request, response_context),
            "dagger.scenario.progression" => {
                self.dispatch_progression(runtime, request, response_context)
            }
            _ => host_error(
                response_context,
                self.bindings.facts(),
                "command-unavailable",
                "developer command is not exposed by Rusty Dagger",
            ),
        }
    }

    fn dispatch_prepare(
        &mut self,
        runtime: &mut DaggerRuntime,
        request: CommandRequest<Value>,
        context: HostResponseContext,
    ) -> DaggerDeveloperResponse {
        let request = match decode_payload::<PrepareScenarioRequest>(request) {
            Ok(request) => request,
            Err(error) => {
                return host_error(context, self.bindings.facts(), "invalid-payload", error)
            }
        };
        let response = self.bindings.dispatch_borrowed::<DaggerScenarioPrepare, _>(
            request,
            &mut |_context, request: PrepareScenarioRequest| {
                runtime
                    .reset_play_session()
                    .map_err(DaggerDeveloperError::from)?;
                let id = scenario_target(&request.target)?;
                runtime
                    .jump_to_content(id)
                    .map_err(DaggerDeveloperError::from)
                    .and_then(project_scenario_readout)
            },
        );
        erase_mapped(response, context, HostReceiptRefs::empty(), dagger_error)
    }

    fn dispatch_melee(
        &mut self,
        runtime: &mut DaggerRuntime,
        request: CommandRequest<Value>,
        context: HostResponseContext,
    ) -> DaggerDeveloperResponse {
        let request = match decode_payload::<MeleeScenarioRequest>(request) {
            Ok(request) => request,
            Err(error) => {
                return host_error(context, self.bindings.facts(), "invalid-payload", error)
            }
        };
        let response = self.bindings.dispatch_borrowed::<DaggerScenarioMelee, _>(
            request,
            &mut |_context, request: MeleeScenarioRequest| {
                if !(1..=8).contains(&request.swings) {
                    return Err(DaggerDeveloperError::new(
                        "invalid-scenario",
                        "swings must be in 1..=8",
                    ));
                }
                runtime
                    .run_developer_melee_scenario(request.swings)
                    .map_err(DaggerDeveloperError::from)
                    .and_then(project_scenario_readout)
            },
        );
        erase_mapped(response, context, HostReceiptRefs::empty(), dagger_error)
    }

    fn dispatch_advance(
        &mut self,
        runtime: &mut DaggerRuntime,
        request: CommandRequest<Value>,
        context: HostResponseContext,
    ) -> DaggerDeveloperResponse {
        let request = match decode_payload::<AdvanceScenarioRequest>(request) {
            Ok(request) => request,
            Err(error) => {
                return host_error(context, self.bindings.facts(), "invalid-payload", error)
            }
        };
        let response = self.bindings.dispatch_borrowed::<DaggerScenarioAdvance, _>(
            request,
            &mut |_context, request: AdvanceScenarioRequest| {
                if !(1..=32).contains(&request.ticks) {
                    return Err(DaggerDeveloperError::new(
                        "invalid-scenario",
                        "ticks must be in 1..=32",
                    ));
                }
                runtime
                    .run_developer_advance_scenario(request.ticks)
                    .map_err(DaggerDeveloperError::from)
                    .and_then(project_scenario_readout)
            },
        );
        erase_mapped(response, context, HostReceiptRefs::empty(), dagger_error)
    }

    fn dispatch_progression(
        &mut self,
        runtime: &mut DaggerRuntime,
        request: CommandRequest<Value>,
        context: HostResponseContext,
    ) -> DaggerDeveloperResponse {
        let request = match decode_payload::<EmptyScenarioRequest>(request) {
            Ok(request) => request,
            Err(error) => {
                return host_error(context, self.bindings.facts(), "invalid-payload", error)
            }
        };
        let response = self
            .bindings
            .dispatch_borrowed::<DaggerScenarioProgression, _>(
                request,
                &mut |_context, _request| {
                    runtime
                        .run_developer_progression_scenario()
                        .map_err(DaggerDeveloperError::from)
                        .and_then(project_scenario_readout)
                },
            );
        erase_mapped(response, context, HostReceiptRefs::empty(), dagger_error)
    }
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct PrepareScenarioRequest {
    target: String,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct MeleeScenarioRequest {
    swings: u8,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct AdvanceScenarioRequest {
    ticks: u8,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(deny_unknown_fields)]
struct EmptyScenarioRequest {}

#[derive(Debug, Clone)]
struct DaggerDeveloperError {
    code: &'static str,
    message: String,
}

impl DaggerDeveloperError {
    fn new(code: &'static str, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
        }
    }
}

impl From<RuntimeError> for DaggerDeveloperError {
    fn from(error: RuntimeError) -> Self {
        Self::new("dagger-runtime-rejected", error.to_string())
    }
}

macro_rules! scenario_command {
    ($name:ident, $request:ty, $id:literal, $lane:expr, $summary:literal) => {
        #[derive(Debug, Clone, Copy, Default)]
        struct $name;

        impl DeveloperCommand for $name {
            type Request = $request;
            type Reply = ScenarioReadout;
            type Error = DaggerDeveloperError;

            fn descriptor() -> CommandDescriptor {
                scenario_descriptor($id, $lane, $summary, <$request>::parameters())
            }
        }
    };
}

/// Bounded result projection for a product scenario command. The full Dagger
/// product readout belongs to `/readout`; returning it from the generic host
/// command would exceed the public client's strict response contract and
/// expose unrelated private catalog/detail trees.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ScenarioReadout {
    player: ScenarioPlayerReadout,
    progression: ScenarioProgressionReadout,
    #[serde(skip_serializing_if = "Option::is_none")]
    latest_combat: Option<ScenarioCombatReadout>,
    #[serde(skip_serializing_if = "Option::is_none")]
    latest_encounter: Option<ScenarioEncounterReadout>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ScenarioPlayerReadout {
    health: ScenarioTrackReadout,
    stamina: ScenarioTrackReadout,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ScenarioTrackReadout {
    current: i64,
    maximum: i64,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ScenarioProgressionReadout {
    xp: i64,
    level: i64,
    awards: usize,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ScenarioCombatReadout {
    target_id: String,
    outcome: String,
    damage: i64,
    died: bool,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ScenarioEncounterReadout {
    enemy_id: String,
    damage: i64,
    player_health_before: i64,
    player_health_after: i64,
}

fn project_scenario_readout(
    readout: ProductReadout,
) -> Result<ScenarioReadout, DaggerDeveloperError> {
    let health = ScenarioTrackReadout {
        current: scenario_integer(readout.current_health, "player health")?,
        maximum: scenario_integer(readout.max_health, "player health maximum")?,
    };
    let stamina = ScenarioTrackReadout {
        current: scenario_integer(readout.player_stats.current_stamina, "player stamina")?,
        maximum: scenario_integer(readout.player_stats.max_stamina, "player stamina maximum")?,
    };
    let latest_combat = readout.combat.last().map(|record| ScenarioCombatReadout {
        target_id: record.target_id.to_string(),
        outcome: record.status.clone(),
        damage: record.damage,
        died: record.died,
    });
    let latest_encounter = if let Some(record) =
        readout.encounter_decisions.iter().rev().find(|record| {
            record.damage.is_some()
                && record.player_health_before.is_some()
                && record.player_health_after.is_some()
        }) {
        Some(ScenarioEncounterReadout {
            enemy_id: record.enemy_id.to_string(),
            damage: scenario_integer(
                record.damage.expect("checked encounter damage"),
                "latest encounter damage",
            )?,
            player_health_before: scenario_integer(
                record
                    .player_health_before
                    .expect("checked encounter health before"),
                "latest encounter player health before",
            )?,
            player_health_after: scenario_integer(
                record
                    .player_health_after
                    .expect("checked encounter health after"),
                "latest encounter player health after",
            )?,
        })
    } else {
        None
    };
    Ok(ScenarioReadout {
        player: ScenarioPlayerReadout { health, stamina },
        progression: ScenarioProgressionReadout {
            xp: readout.progression.xp,
            level: readout.progression.level,
            awards: readout.progression.history.len(),
        },
        latest_combat,
        latest_encounter,
    })
}

fn scenario_integer(value: f32, field: &str) -> Result<i64, DaggerDeveloperError> {
    let value = f64::from(value);
    if !value.is_finite()
        || value.fract() != 0.0
        || value < i64::MIN as f64
        || value > i64::MAX as f64
    {
        return Err(DaggerDeveloperError::new(
            "scenario-readout-invalid",
            format!("{field} is not an exact i64 scenario value"),
        ));
    }
    Ok(value as i64)
}

trait ScenarioParameters {
    fn parameters() -> Vec<ParameterDescriptor>;
}

impl ScenarioParameters for PrepareScenarioRequest {
    fn parameters() -> Vec<ParameterDescriptor> {
        vec![parameter(
            "target",
            "Committed scenario target.",
            TypeDescriptor::Identifier { maximum_bytes: 32 },
        )]
    }
}
impl ScenarioParameters for MeleeScenarioRequest {
    fn parameters() -> Vec<ParameterDescriptor> {
        vec![parameter(
            "swings",
            "Bounded physical melee swings (1..=8).",
            TypeDescriptor::UnsignedInteger,
        )]
    }
}
impl ScenarioParameters for AdvanceScenarioRequest {
    fn parameters() -> Vec<ParameterDescriptor> {
        vec![parameter(
            "ticks",
            "Bounded 0.25-second production ticks (1..=32).",
            TypeDescriptor::UnsignedInteger,
        )]
    }
}
impl ScenarioParameters for EmptyScenarioRequest {
    fn parameters() -> Vec<ParameterDescriptor> {
        Vec::new()
    }
}

scenario_command!(
    DaggerScenarioPrepare,
    PrepareScenarioRequest,
    "dagger.scenario.prepare",
    CommandLane::Admin,
    "Admin setup: reset the session and place the player at one committed scenario target."
);
scenario_command!(
    DaggerScenarioMelee,
    MeleeScenarioRequest,
    "dagger.scenario.melee",
    CommandLane::Play,
    "Run bounded ordinary physical melee swings through Dagger's production combat path."
);
scenario_command!(
    DaggerScenarioAdvance,
    AdvanceScenarioRequest,
    "dagger.scenario.advance",
    CommandLane::Play,
    "Advance bounded production play-session ticks so live encounter actions can resolve."
);
scenario_command!(DaggerScenarioProgression, EmptyScenarioRequest, "dagger.scenario.progression", CommandLane::Admin, "Admin demonstration: execute the committed Orc and Giant Bat kill sequence through real melee to cross level 2.");

fn scenario_descriptor(
    id: &str,
    lane: CommandLane,
    summary: &str,
    parameters: Vec<ParameterDescriptor>,
) -> CommandDescriptor {
    CommandDescriptor::new(
        CommandId::parse(id).expect("fixed Dagger scenario command identity"),
        Vec::new(),
        lane,
        summary,
        parameters,
        TypeDescriptor::Record { fields: Vec::new() },
        TypeDescriptor::Record { fields: Vec::new() },
    )
    .expect("fixed Dagger scenario descriptor")
}

fn parameter(name: &str, summary: &str, value: TypeDescriptor) -> ParameterDescriptor {
    ParameterDescriptor::new(name, summary, true, value)
}

fn developer_facts() -> DispatchFacts {
    DispatchFacts {
        runtime: RuntimeInstanceId::parse(RUNTIME).expect("fixed Dagger runtime identity"),
        revision: 1,
        catalog_epoch: CATALOG_EPOCH,
    }
}

fn decode_payload<T: DeserializeOwned>(
    request: CommandRequest<Value>,
) -> Result<CommandRequest<T>, String> {
    let payload = serde_json::from_value(request.payload).map_err(|error| error.to_string())?;
    Ok(CommandRequest {
        protocol_version: request.protocol_version,
        command: request.command,
        correlation: request.correlation,
        runtime: request.runtime,
        expected: request.expected,
        cancelled: request.cancelled,
        timed_out: request.timed_out,
        payload,
    })
}

fn decode_entity(request: CommandRequest<Value>) -> Result<CommandRequest<EntityId>, String> {
    let request = decode_payload::<HostEntityRequest>(request)?;
    let entity = request
        .payload
        .into_entity()
        .map_err(|error| error.to_string())?;
    Ok(CommandRequest {
        protocol_version: request.protocol_version,
        command: request.command,
        correlation: request.correlation,
        runtime: request.runtime,
        expected: request.expected,
        cancelled: request.cancelled,
        timed_out: request.timed_out,
        payload: entity,
    })
}

fn map_track_request(
    runtime: &DaggerRuntime,
    request: CommandRequest<HostTrackSetRequest>,
) -> Result<CommandRequest<rusty_engine::gameplay_mechanics::TrackSetRequest>, String> {
    let payload = runtime
        .developer_map_track_set(request.payload)
        .map_err(|error| error.to_string())?;
    Ok(CommandRequest {
        protocol_version: request.protocol_version,
        command: request.command,
        correlation: request.correlation,
        runtime: request.runtime,
        expected: request.expected,
        cancelled: request.cancelled,
        timed_out: request.timed_out,
        payload,
    })
}

fn project_track_response(
    response: CommandResponse<rusty_engine::gameplay_mechanics::TrackSetReceipt, MechanicsError>,
) -> CommandResponse<HostTrackSetReceipt, MechanicsError> {
    CommandResponse {
        protocol_version: response.protocol_version,
        provenance: response.provenance,
        facts: response.facts,
        result: match response.result {
            HandlerResult::Success(receipt) => {
                HandlerResult::Success(HostTrackSetReceipt::from_owner(receipt))
            }
            HandlerResult::Rejected(error) => HandlerResult::Rejected(error),
        },
    }
}

fn scenario_target(target: &str) -> Result<u64, DaggerDeveloperError> {
    match target {
        // This committed rat's admitted combat evidence includes a bounded
        // successful bite during the 32-tick play demonstration. The alias
        // stays semantic while patrol remains free to move it normally.
        "rat" => Ok(2036),
        "orc" => Ok(2003),
        "bat-east" => Ok(2002),
        "bat-west" => Ok(2005),
        _ => Err(DaggerDeveloperError::new(
            "invalid-scenario",
            "target must be one of rat, orc, bat-east, or bat-west",
        )),
    }
}

fn erase_mapped<R: Serialize, E, M>(
    response: CommandResponse<R, E>,
    context: HostResponseContext,
    receipts: HostReceiptRefs,
    map_error: M,
) -> DaggerDeveloperResponse
where
    M: FnOnce(E) -> HostErrorBody<Value>,
{
    erase_response(map_command_response(response, context, receipts, map_error).wire)
}

fn erase_response<R: Serialize, E: Serialize>(
    response: HostCommandResponse<R, E>,
) -> DaggerDeveloperResponse {
    let outcome = match response.outcome {
        HostCommandOutcome::Success {
            value,
            receipt_refs,
        } => HostCommandOutcome::Success {
            value: serde_json::to_value(value).expect("developer command result must serialize"),
            receipt_refs,
        },
        HostCommandOutcome::Error {
            code,
            message,
            details,
        } => HostCommandOutcome::Error {
            code,
            message,
            details: details.map(|value| {
                serde_json::to_value(value).expect("developer command error must serialize")
            }),
        },
    };
    HostCommandResponse {
        correlation: response.correlation,
        runtime: response.runtime,
        profile: response.profile,
        revision: response.revision,
        catalog_epoch: response.catalog_epoch,
        outcome,
    }
}

fn host_error(
    context: HostResponseContext,
    facts: &DispatchFacts,
    code: &'static str,
    message: impl Into<String>,
) -> DaggerDeveloperResponse {
    HostCommandResponse {
        correlation: context.correlation().clone(),
        runtime: facts.runtime.clone(),
        profile: context.profile().clone(),
        revision: HostDecimalU64::new(facts.revision),
        catalog_epoch: HostDecimalU64::new(facts.catalog_epoch),
        outcome: HostCommandOutcome::Error {
            code: HostErrorCode::parse(code).expect("fixed Dagger error identity"),
            message: bounded_message(message),
            details: None,
        },
    }
}

fn infallible_error(error: Infallible) -> HostErrorBody<Value> {
    match error {}
}

fn mechanics_error(error: MechanicsError) -> HostErrorBody<Value> {
    HostErrorBody {
        code: HostErrorCode::parse("mechanics-rejected").expect("fixed Dagger error identity"),
        message: bounded_message(error.to_string()),
        details: None,
    }
}

fn dagger_error(error: DaggerDeveloperError) -> HostErrorBody<Value> {
    HostErrorBody {
        code: HostErrorCode::parse(error.code).expect("fixed Dagger error identity"),
        message: bounded_message(error.message),
        details: None,
    }
}

fn bounded_message(message: impl Into<String>) -> HostErrorMessage {
    let mut message = message.into();
    while message.len() > rusty_engine::developer_command::MAX_HOST_ERROR_MESSAGE_BYTES {
        message.pop();
    }
    HostErrorMessage::new(message).expect("message is bounded")
}

#[cfg(test)]
mod tests {
    use super::*;
    use rusty_engine::developer_command::{HostExpectedFacts, CURRENT_PROTOCOL_VERSION};

    const PROJECT: &str =
        include_str!("../../../../../content/projects/privateers-hold.project.json");
    const NAVGRID: &str =
        include_str!("../../../../../content/projects/privateers-hold.navgrid.json");

    #[test]
    fn discovery_exposes_engine_standard_and_dagger_owned_commands() {
        let commands = DaggerDeveloperCommands::new().expect("bindings");
        let ids = commands
            .discover()
            .commands
            .into_iter()
            .map(|command| command.id.to_string())
            .collect::<Vec<_>>();
        assert!(ids.contains(&"standard.inspect.entity".to_owned()));
        assert!(ids.contains(&"standard.admin.track.set".to_owned()));
        assert!(ids.contains(&"dagger.scenario.melee".to_owned()));
        assert!(ids.contains(&"dagger.scenario.progression".to_owned()));
    }

    #[test]
    fn scenario_target_vocabulary_is_closed() {
        assert_eq!(scenario_target("rat").expect("rat"), 2036);
        assert!(scenario_target("teleport-anywhere").is_err());
    }

    #[test]
    fn queued_standard_admin_and_play_scenario_borrow_the_live_runtime_once() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("admit committed project");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install committed patrol navigation");
        let mut commands = DaggerDeveloperCommands::new().expect("bindings");

        let inspect = commands.execute(
            &mut runtime,
            request(
                "standard.inspect.mechanics",
                serde_json::json!({ "entity": "1" }),
                "inspect",
            ),
        );
        assert!(matches!(
            inspect.outcome,
            HostCommandOutcome::Success { .. }
        ));

        let admin = commands.execute(
            &mut runtime,
            request(
                "standard.admin.track.set",
                serde_json::json!({
                    "operation": "dagger-admin-health",
                    "source": {
                        "kind": "request",
                        "operation": "dagger-admin-health",
                        "instance": "dagger-admin"
                    },
                    "entity": "1",
                    "track": "health",
                    "value": 1,
                    "policy": "clampToBounds"
                }),
                "admin",
            ),
        );
        assert!(matches!(admin.outcome, HostCommandOutcome::Success { .. }));
        let admin_value = success_value(&admin);
        assert_eq!(admin_value["entity"], "1");
        assert_eq!(admin_value["track"], "health");
        assert_eq!(admin_value["operation"], "dagger-admin-health");
        assert_eq!(admin_value["decision"], "applied");
        assert!(admin_value["catalogVersion"].is_string());
        assert!(admin_value["observedRevisions"].is_array());
        assert_eq!(
            runtime
                .product_readout()
                .expect("readout after standard admin mutation")
                .current_health,
            1.0,
            "the standard admin command mutates the live Dagger mechanics component"
        );

        let prepare = commands.execute(
            &mut runtime,
            request(
                "dagger.scenario.prepare",
                serde_json::json!({ "target": "rat" }),
                "prepare",
            ),
        );
        assert!(matches!(
            prepare.outcome,
            HostCommandOutcome::Success { .. }
        ));
        let prepare_value = scenario_value(&prepare);
        assert_eq!(prepare_value["player"]["health"]["current"], 85);
        assert!(
            serde_json::to_vec(prepare_value)
                .expect("scenario projection serializes")
                .len()
                < 4_096,
            "scenario commands must not return the full product readout"
        );
        let health_before_advance = runtime
            .product_readout()
            .expect("readout before production advance")
            .current_health;
        let advance = commands.execute(
            &mut runtime,
            request(
                "dagger.scenario.advance",
                serde_json::json!({ "ticks": 32 }),
                "advance",
            ),
        );
        assert!(matches!(
            advance.outcome,
            HostCommandOutcome::Success { .. }
        ));
        let after_advance = runtime
            .product_readout()
            .expect("readout after production advance");
        assert!(
            after_advance.current_health < health_before_advance,
            "the real patrol path must damage the prepared player"
        );
        assert!(
            after_advance
                .encounter_decisions
                .iter()
                .any(|record| record.damage.is_some_and(|damage| damage > 0.0)),
            "the Rust encounter history must retain the AI damage receipt"
        );
        let before = runtime.player_stamina().0;
        let melee = commands.execute(
            &mut runtime,
            request(
                "dagger.scenario.melee",
                serde_json::json!({ "swings": 1 }),
                "melee",
            ),
        );
        assert!(matches!(melee.outcome, HostCommandOutcome::Success { .. }));
        assert!(
            runtime.player_stamina().0 < before,
            "production melee spends stamina"
        );
    }

    #[test]
    fn standard_host_wire_rejects_noncanonical_stale_and_absent_tracks_without_mutation() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("admit committed project");
        let mut commands = DaggerDeveloperCommands::new().expect("bindings");
        let before = runtime
            .product_readout()
            .expect("readout before invalid standard admin requests")
            .current_health;

        for (correlation, entity, track, expected_revision) in [
            ("noncanonical", "01", "health", None),
            ("stale", "1", "health", Some("999999")),
            ("absent-track", "1", "missing-track", None),
        ] {
            let rejected = commands.execute(
                &mut runtime,
                request(
                    "standard.admin.track.set",
                    serde_json::json!({
                        "operation": "dagger-invalid-admin",
                        "source": {
                            "kind": "request",
                            "operation": "dagger-invalid-admin",
                            "instance": "dagger-invalid-admin"
                        },
                        "entity": entity,
                        "track": track,
                        "value": 1,
                        "policy": "clampToBounds",
                        "expectedRevision": expected_revision,
                    }),
                    correlation,
                ),
            );
            assert!(matches!(rejected.outcome, HostCommandOutcome::Error { .. }));
            assert_eq!(
                runtime
                    .product_readout()
                    .expect("readout after rejected standard admin request")
                    .current_health,
                before,
                "{correlation} standard host request mutated Dagger runtime"
            );
        }
    }

    #[test]
    fn queued_progression_scenario_uses_the_production_kill_hook() {
        let mut runtime =
            DaggerRuntime::from_project_json(PROJECT).expect("admit committed project");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install committed patrol navigation");
        let mut commands = DaggerDeveloperCommands::new().expect("bindings");

        let progression = commands.execute(
            &mut runtime,
            request(
                "dagger.scenario.progression",
                serde_json::json!({}),
                "progression",
            ),
        );
        assert!(matches!(
            progression.outcome,
            HostCommandOutcome::Success { .. }
        ));
        let readout = runtime.product_readout().expect("readout after kills");
        assert_eq!(readout.progression.level, 2);
        assert_eq!(readout.progression.history.len(), 3);
        assert!(
            readout
                .notices
                .iter()
                .any(|notice| matches!(notice.kind, dagger_runtime::ProductNoticeKind::LevelUp)),
            "production kill progression emits the normal level-up notice"
        );

        let repeated = commands.execute(
            &mut runtime,
            request(
                "dagger.scenario.progression",
                serde_json::json!({}),
                "progression-repeat",
            ),
        );
        assert!(matches!(
            repeated.outcome,
            HostCommandOutcome::Success { .. }
        ));
        let repeated_readout = runtime
            .product_readout()
            .expect("readout after repeated kills");
        assert_eq!(repeated_readout.progression.level, 2);
        assert_eq!(repeated_readout.progression.history.len(), 3);
    }

    fn scenario_value(response: &DaggerDeveloperResponse) -> &Value {
        success_value(response)
    }

    fn success_value(response: &DaggerDeveloperResponse) -> &Value {
        match &response.outcome {
            HostCommandOutcome::Success { value, .. } => value,
            HostCommandOutcome::Error { code, message, .. } => {
                panic!("developer command rejected unexpectedly: {code:?}: {message:?}")
            }
        }
    }

    fn request(command: &str, payload: Value, correlation: &str) -> DaggerDeveloperRequest {
        let facts = developer_facts();
        HostCommandRequest {
            protocol_version: CURRENT_PROTOCOL_VERSION,
            command: CommandId::parse(command).expect("test command"),
            correlation: rusty_engine::developer_command::CorrelationId::parse(correlation)
                .expect("test correlation"),
            runtime: facts.runtime,
            expected: HostExpectedFacts {
                profile: ProfileId::parse(PROFILE).expect("test profile"),
                revision: HostDecimalU64::new(facts.revision),
                catalog_epoch: HostDecimalU64::new(facts.catalog_epoch),
            },
            payload,
        }
    }
}
