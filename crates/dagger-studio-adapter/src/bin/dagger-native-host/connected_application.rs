use std::{
    collections::{BTreeMap, BTreeSet},
    env,
    io::{self, Write},
    path::Path,
    sync::mpsc::Sender,
    thread,
    time::{Duration, Instant},
};

use anyhow::{bail, Context, Result};
use base64::{engine::general_purpose::STANDARD as BASE64, Engine as _};
use dagger_runtime::{DaggerRuntime, ResolvedPlayerAction};
use dagger_studio_adapter::{build_render_bundle, DaggerRenderBundle};
use rusty_engine::render_host_contracts::RendererCameraPose;
use rusty_engine::render_model::{RenderDiff, RenderFrameDiff, RenderHandle};
use serde::Serialize;

use crate::{
    lab_server::{LabCommand, LabReply, LabServer, ProductInput},
    live_presentation::LivePresentation,
    proof::Options,
};

const PROJECT: &str = include_str!("../../../../../content/projects/privateers-hold.project.json");
const NAVGRID: &str = include_str!("../../../../../content/projects/privateers-hold.navgrid.json");

pub(crate) fn run(options: Options) -> Result<()> {
    if options.proof || options.corrupt_resource {
        bail!("native renderer proof flags cannot be combined with --browser-product");
    }
    let port = options
        .lab_port
        .context("--browser-product requires the product HTTP service")?;
    let root = Path::new(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .and_then(Path::parent)
        .context("resolve Rusty Dagger workspace root")?;
    let mut runtime = DaggerRuntime::from_project_json(PROJECT)
        .context("admit checked Privateer's Hold project")?;
    runtime
        .install_encounter_navigation_json(NAVGRID)
        .context("install committed encounter navigation")?;
    let mut presentation = LivePresentation::from_project(PROJECT)?;
    let mut pending_presentation = PendingPresentation::default();
    pending_presentation.merge(tick_presentation(&mut runtime, &mut presentation, 0.0)?)?;
    let bundle = build_render_bundle(root, PROJECT).map_err(anyhow::Error::msg)?;
    let server = LabServer::start(
        options.lab_host,
        port,
        root.join("dist/apps/dagger-lab/browser"),
    )?;
    println!(
        "DAGGER_PRODUCT_READY api=http://127.0.0.1:{}/api/dagger-product/bootstrap ui=http://127.0.0.1:{} resources={} source_entities={}",
        server.port(),
        server.port(),
        bundle.resources.len(),
        bundle.source_entity_count,
    );
    io::stdout().flush()?;

    let mut pressed_codes = BTreeSet::new();
    let mut pressed_buttons = 0_u16;
    let mut last_tick = Instant::now();
    loop {
        let command = match server.try_recv() {
            Ok(command) => Some(command),
            Err(std::sync::mpsc::TryRecvError::Empty) => None,
            Err(std::sync::mpsc::TryRecvError::Disconnected) => {
                bail!("Dagger product HTTP service disconnected")
            }
        };
        if let Some(command) = command {
            handle_command(
                command,
                &mut runtime,
                &presentation,
                &mut pending_presentation,
                &bundle,
                &mut pressed_codes,
                &mut pressed_buttons,
            )?;
        }
        let now = Instant::now();
        let elapsed = now.saturating_duration_since(last_tick);
        if elapsed >= Duration::from_millis(50) {
            let frame = tick_presentation(
                &mut runtime,
                &mut presentation,
                elapsed.as_secs_f32().min(0.25),
            )?;
            pending_presentation.merge(frame)?;
            last_tick = now;
        }
        thread::sleep(Duration::from_millis(4));
    }
}

fn handle_command(
    command: LabCommand,
    runtime: &mut DaggerRuntime,
    presentation: &LivePresentation,
    pending_presentation: &mut PendingPresentation,
    bundle: &DaggerRenderBundle,
    pressed_codes: &mut BTreeSet<String>,
    pressed_buttons: &mut u16,
) -> Result<()> {
    match command {
        LabCommand::ProductBootstrap { reply } => {
            pending_presentation.replace(presentation.snapshot()?)?;
            send_json(reply, 200, &ProductBootstrap::new(runtime, bundle)?)
        }
        LabCommand::ProductState { reply } => send_json(
            reply,
            200,
            &product_state(runtime, pending_presentation.take()?)?,
        ),
        LabCommand::ProductInput { input, reply } => {
            let result = apply_product_input(runtime, pressed_codes, pressed_buttons, input)
                .and_then(|()| product_input_state(runtime));
            send_runtime_result(reply, result)
        }
        LabCommand::Read { reply } => send_runtime_result(reply, runtime.experiment_readout()),
        LabCommand::Apply { document, reply } => {
            send_runtime_result(reply, runtime.apply_experiment_json(&document))
        }
        LabCommand::Evaluate { document, reply } => {
            send_runtime_result(reply, runtime.evaluate_experiment_json(&document))
        }
        LabCommand::Reset { reply } | LabCommand::Play { reply } => {
            send_runtime_result(reply, runtime.reset_play_session())
        }
        LabCommand::Jump { id, reply } => send_runtime_result(reply, runtime.jump_to_content(id)),
    }
}

fn apply_product_input(
    runtime: &mut DaggerRuntime,
    previous_codes: &mut BTreeSet<String>,
    previous_buttons: &mut u16,
    input: ProductInput,
) -> Result<(), dagger_runtime::RuntimeError> {
    let pressed = input.pressed_codes.into_iter().collect::<BTreeSet<_>>();
    let attack_pressed = pressed.contains("Space") && !previous_codes.contains("Space")
        || input.buttons & 1 != 0 && *previous_buttons & 1 == 0;
    let reset_pressed = pressed.contains("KeyR") && !previous_codes.contains("KeyR");
    *previous_codes = pressed.clone();
    *previous_buttons = input.buttons;
    let forward = f32::from(pressed.contains("KeyW")) - f32::from(pressed.contains("KeyS"));
    let right = f32::from(pressed.contains("KeyD")) - f32::from(pressed.contains("KeyA"));
    if forward != 0.0 || right != 0.0 {
        runtime.apply_player_action(ResolvedPlayerAction::Move { forward, right })?;
    }
    if input.pointer_delta != [0.0, 0.0] {
        runtime.apply_player_action(resolve_pointer_look(input.pointer_delta))?;
    }
    if attack_pressed {
        let _ = runtime.attack_focused_target()?;
    }
    if reset_pressed {
        runtime.reset_play_session()?;
    }
    Ok(())
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProductBootstrap<'a> {
    schema_version: u8,
    camera: RendererCameraPose,
    frame: &'a rusty_engine::render_model::RenderFrameDiff,
    resources: Vec<ProductResource<'a>>,
    source_entity_count: usize,
}

impl<'a> ProductBootstrap<'a> {
    fn new(runtime: &DaggerRuntime, bundle: &'a DaggerRenderBundle) -> Result<Self> {
        Ok(Self {
            schema_version: 1,
            camera: camera_pose(runtime)?,
            frame: &bundle.frame,
            resources: bundle
                .resources
                .iter()
                .map(|resource| ProductResource {
                    identity: &resource.identity,
                    content_hash: &resource.content_hash,
                    media_type: &resource.media_type,
                    bytes_base64: BASE64.encode(&resource.bytes),
                })
                .collect(),
            source_entity_count: bundle.source_entity_count,
        })
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProductResource<'a> {
    identity: &'a str,
    content_hash: &'a str,
    media_type: &'a str,
    bytes_base64: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProductState {
    camera: RendererCameraPose,
    player_position: [f32; 3],
    frame: rusty_engine::render_model::RenderFrameDiff,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProductInputState {
    camera: RendererCameraPose,
    player_position: [f32; 3],
}

fn product_state(
    runtime: &DaggerRuntime,
    frame: RenderFrameDiff,
) -> Result<ProductState, dagger_runtime::RuntimeError> {
    let position = runtime.player_position()?;
    let state = runtime.player_state();
    Ok(ProductState {
        camera: RendererCameraPose {
            position: [
                f64::from(position.x),
                f64::from(position.y) + 0.75,
                f64::from(position.z),
            ],
            pitch_degrees: f64::from(state.pitch_degrees),
            yaw_degrees: f64::from(state.yaw_degrees),
        },
        player_position: [position.x, position.y, position.z],
        frame,
    })
}

fn product_input_state(
    runtime: &DaggerRuntime,
) -> Result<ProductInputState, dagger_runtime::RuntimeError> {
    let position = runtime.player_position()?;
    let camera = camera_pose(runtime)?;
    Ok(ProductInputState {
        camera,
        player_position: [position.x, position.y, position.z],
    })
}

fn tick_presentation(
    runtime: &mut DaggerRuntime,
    presentation: &mut LivePresentation,
    dt: f32,
) -> Result<RenderFrameDiff> {
    let encounter_updates = runtime.tick_play_session(dt)?;
    let positions = runtime.encounter_positions();
    let camera = camera_pose(runtime)?;
    let frame = presentation.tick(
        dt,
        [
            camera.position[0] as f32,
            camera.position[1] as f32,
            camera.position[2] as f32,
        ],
        &positions,
        &encounter_updates,
    )?;
    Ok(frame.frame)
}

#[derive(Default)]
struct PendingPresentation {
    transforms: BTreeMap<RenderHandle, RenderDiff>,
    sprites: BTreeMap<RenderHandle, RenderDiff>,
}

impl PendingPresentation {
    fn merge(&mut self, frame: RenderFrameDiff) -> Result<()> {
        for op in frame.ops {
            match &op {
                RenderDiff::Update { handle, .. } => {
                    self.transforms.insert(*handle, op);
                }
                RenderDiff::UpdateSprite { handle, .. } => {
                    self.sprites.insert(*handle, op);
                }
                _ => bail!("live presentation emitted a non-dynamic retained operation"),
            }
        }
        Ok(())
    }

    fn replace(&mut self, frame: RenderFrameDiff) -> Result<()> {
        self.transforms.clear();
        self.sprites.clear();
        self.merge(frame)
    }

    fn take(&mut self) -> Result<RenderFrameDiff, dagger_runtime::RuntimeError> {
        let ops = std::mem::take(&mut self.transforms)
            .into_values()
            .chain(std::mem::take(&mut self.sprites).into_values())
            .collect();
        RenderFrameDiff::try_from_ops(ops).map_err(|error| {
            dagger_runtime::RuntimeError::Encounter(format!("build pending live frame: {error:?}"))
        })
    }
}

fn resolve_pointer_look(pointer_delta: [f32; 2]) -> ResolvedPlayerAction {
    ResolvedPlayerAction::Look {
        yaw_delta: -pointer_delta[0] * 0.05,
        pitch_delta: -pointer_delta[1] * 0.05,
    }
}

fn camera_pose(
    runtime: &DaggerRuntime,
) -> Result<RendererCameraPose, dagger_runtime::RuntimeError> {
    let position = runtime.player_position()?;
    let state = runtime.player_state();
    Ok(RendererCameraPose {
        position: [
            f64::from(position.x),
            f64::from(position.y) + 0.75,
            f64::from(position.z),
        ],
        pitch_degrees: f64::from(state.pitch_degrees),
        yaw_degrees: f64::from(state.yaw_degrees),
    })
}

fn send_runtime_result<T: Serialize>(
    reply: Sender<LabReply>,
    result: Result<T, dagger_runtime::RuntimeError>,
) -> Result<()> {
    match result {
        Ok(value) => send_json(reply, 200, &value),
        Err(error) => send_json(
            reply,
            400,
            &serde_json::json!({ "error": error.to_string() }),
        ),
    }
}

fn send_json(reply: Sender<LabReply>, status: u16, value: &impl Serialize) -> Result<()> {
    let response = LabReply {
        status,
        body: serde_json::to_string(value).context("serialize connected Dagger response")?,
    };
    let _ = reply.send(response);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn look(delta: [f32; 2]) -> (f32, f32) {
        match resolve_pointer_look(delta) {
            ResolvedPlayerAction::Look {
                yaw_delta,
                pitch_delta,
            } => (yaw_delta, pitch_delta),
            _ => unreachable!("pointer input always resolves to look"),
        }
    }

    #[test]
    fn pointer_directions_follow_fps_look_convention() {
        assert!(look([-10.0, 0.0]).0 > 0.0, "mouse-left must turn left");
        assert!(look([10.0, 0.0]).0 < 0.0, "mouse-right must turn right");
        assert!(look([0.0, -10.0]).1 > 0.0, "mouse-up must look up");
        assert!(look([0.0, 10.0]).1 < 0.0, "mouse-down must look down");
    }

    #[test]
    fn pending_presentation_coalesces_and_drains_dynamic_updates() {
        let mut pending = PendingPresentation::default();
        let update = |frame| RenderDiff::UpdateSprite {
            handle: RenderHandle::new(1001),
            frame: Some(frame),
            tint: None,
            render_order: None,
            visible: None,
        };
        pending
            .merge(RenderFrameDiff::try_from_ops(vec![update(1)]).expect("first frame"))
            .expect("merge first frame");
        pending
            .merge(RenderFrameDiff::try_from_ops(vec![update(2)]).expect("second frame"))
            .expect("merge second frame");
        let frame = pending.take().expect("take coalesced frame");
        assert_eq!(frame.ops, vec![update(2)]);
        assert!(pending.take().expect("take drained frame").ops.is_empty());
    }
}
