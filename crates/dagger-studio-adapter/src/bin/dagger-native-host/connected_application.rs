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
use dagger_runtime::{DaggerRuntime, MeleePresentationReadout, ResolvedPlayerAction};
use dagger_studio_adapter::{build_render_bundle, DaggerRenderBundle};
use rusty_engine::render_host_contracts::RendererCameraPose;
use rusty_engine::render_model::{RenderDiff, RenderFrameDiff, RenderHandle};
use rusty_engine::render_presentation::{
    PresentationFrameDiff, PresentationOp, PresentationOpMeta,
};
use serde::Serialize;

use crate::{
    diagnostics::NativeDiagnostics,
    lab_server::{LabCommand, LabReply, LabServer, ProductInput},
    proof::Options,
};

const PROJECT: &str = include_str!("../../../../../content/projects/privateers-hold.project.json");
const NAVGRID: &str = include_str!("../../../../../content/projects/privateers-hold.navgrid.json");
const ENCOUNTER_GALLERY_PROJECT: &str =
    include_str!("../../../../../content/projects/encounter-gallery.project.json");
const ENCOUNTER_GALLERY_NAVGRID: &str =
    include_str!("../../../../../content/projects/encounter-gallery.navgrid.json");

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
    let (project, navgrid, product_name) = if options.encounter_gallery {
        (
            ENCOUNTER_GALLERY_PROJECT,
            ENCOUNTER_GALLERY_NAVGRID,
            "encounter-gallery",
        )
    } else {
        (PROJECT, NAVGRID, "privateers-hold")
    };
    let mut runtime = DaggerRuntime::from_project_json(project)
        .with_context(|| format!("admit checked {product_name} project"))?;
    runtime
        .install_encounter_navigation_json(navgrid)
        .context("install committed encounter navigation")?;
    let mut presentation = NativeDiagnostics::from_documents(project, navgrid)?;
    let mut pending_presentation = PendingPresentation::default();
    let mut pending_audio = PendingAudioPresentation::default();
    let initial = tick_presentation(&mut runtime, &mut presentation, 0.0)?;
    pending_presentation.merge(initial.frame)?;
    pending_audio.merge(initial.presentation);
    let bundle = build_render_bundle(root, project).map_err(anyhow::Error::msg)?;
    let server = LabServer::start(
        options.lab_host,
        port,
        root.join("dist/apps/dagger-lab/browser"),
    )?;
    println!(
        "DAGGER_PRODUCT_READY product={} api=http://127.0.0.1:{}/api/dagger-product/bootstrap ui=http://127.0.0.1:{} resources={} source_entities={}",
        product_name,
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
                &mut presentation,
                &mut pending_presentation,
                &mut pending_audio,
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
            pending_presentation.merge(frame.frame)?;
            pending_audio.merge(frame.presentation);
            last_tick = now;
        }
        thread::sleep(Duration::from_millis(4));
    }
}

#[allow(clippy::too_many_arguments)]
fn handle_command(
    command: LabCommand,
    runtime: &mut DaggerRuntime,
    presentation: &mut NativeDiagnostics,
    pending_presentation: &mut PendingPresentation,
    pending_audio: &mut PendingAudioPresentation,
    bundle: &DaggerRenderBundle,
    pressed_codes: &mut BTreeSet<String>,
    pressed_buttons: &mut u16,
) -> Result<()> {
    match command {
        LabCommand::ProductBootstrap { reply } => {
            pending_presentation.replace(presentation.snapshot()?)?;
            let _ = pending_audio.take()?;
            send_json(reply, 200, &ProductBootstrap::new(runtime, bundle)?)
        }
        LabCommand::ProductState { reply } => send_json(
            reply,
            200,
            &product_state(
                runtime,
                presentation,
                pending_presentation.take()?,
                pending_audio.take()?,
            )?,
        ),
        LabCommand::ProductInput { input, reply } => {
            let result =
                apply_product_input(runtime, presentation, pressed_codes, pressed_buttons, input)
                    .and_then(|()| product_input_state(runtime, presentation));
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
    presentation: &mut NativeDiagnostics,
    previous_codes: &mut BTreeSet<String>,
    previous_buttons: &mut u16,
    input: ProductInput,
) -> Result<(), dagger_runtime::RuntimeError> {
    let pressed = input.pressed_codes.into_iter().collect::<BTreeSet<_>>();
    let attack_pressed = pressed.contains("Space") && !previous_codes.contains("Space")
        || input.buttons & 1 != 0 && *previous_buttons & 1 == 0;
    let reset_pressed = pressed.contains("KeyR") && !previous_codes.contains("KeyR");
    let patrol_debug_pressed = pressed.contains("KeyG") && !previous_codes.contains("KeyG");
    let nav_debug_pressed = pressed.contains("KeyN") && !previous_codes.contains("KeyN");
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
    if patrol_debug_pressed {
        presentation.toggle_sprite_overlay();
    }
    if nav_debug_pressed {
        presentation.toggle_nav_overlay();
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
    presentation: PresentationFrameDiff,
    patrol_debug_enabled: bool,
    nav_debug_enabled: bool,
    melee_presentation: Option<MeleePresentationReadout>,
    player_stamina: f32,
    player_max_stamina: f32,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProductInputState {
    camera: RendererCameraPose,
    player_position: [f32; 3],
    patrol_debug_enabled: bool,
    nav_debug_enabled: bool,
    melee_presentation: Option<MeleePresentationReadout>,
    player_stamina: f32,
    player_max_stamina: f32,
}

fn product_state(
    runtime: &DaggerRuntime,
    presentation: &NativeDiagnostics,
    frame: RenderFrameDiff,
    audio: PresentationFrameDiff,
) -> Result<ProductState, dagger_runtime::RuntimeError> {
    let position = runtime.player_position()?;
    let state = runtime.player_state();
    let stamina = runtime.player_stamina();
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
        presentation: audio,
        patrol_debug_enabled: presentation.sprite_overlay_enabled(),
        nav_debug_enabled: presentation.nav_overlay_enabled(),
        melee_presentation: runtime.melee_presentation(),
        player_stamina: stamina.0,
        player_max_stamina: stamina.1,
    })
}

fn product_input_state(
    runtime: &DaggerRuntime,
    presentation: &NativeDiagnostics,
) -> Result<ProductInputState, dagger_runtime::RuntimeError> {
    let position = runtime.player_position()?;
    let camera = camera_pose(runtime)?;
    let stamina = runtime.player_stamina();
    Ok(ProductInputState {
        camera,
        player_position: [position.x, position.y, position.z],
        patrol_debug_enabled: presentation.sprite_overlay_enabled(),
        nav_debug_enabled: presentation.nav_overlay_enabled(),
        melee_presentation: runtime.melee_presentation(),
        player_stamina: stamina.0,
        player_max_stamina: stamina.1,
    })
}

fn tick_presentation(
    runtime: &mut DaggerRuntime,
    presentation: &mut NativeDiagnostics,
    dt: f32,
) -> Result<crate::diagnostics::DiagnosticFrame> {
    let encounter_updates = runtime.tick_play_session(dt)?;
    let positions = runtime.encounter_positions();
    let dead_encounters = runtime.dead_encounter_ids();
    let melee_action = runtime.melee_presentation();
    let stamina = runtime.player_stamina();
    let camera = camera_pose(runtime)?;
    presentation.tick(
        dt,
        [
            camera.position[0] as f32,
            camera.position[1] as f32,
            camera.position[2] as f32,
        ],
        &positions,
        &encounter_updates,
        &dead_encounters,
        melee_action.as_ref(),
        stamina,
    )
}

#[derive(Default)]
struct PendingPresentation {
    transforms: BTreeMap<RenderHandle, RenderDiff>,
    sprites: BTreeMap<RenderHandle, RenderDiff>,
    lifecycle: Vec<RenderDiff>,
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
                RenderDiff::Destroy { handle } => {
                    self.transforms.remove(handle);
                    self.sprites.remove(handle);
                    self.lifecycle.push(op);
                }
                _ => self.lifecycle.push(op),
            }
        }
        Ok(())
    }

    fn replace(&mut self, frame: RenderFrameDiff) -> Result<()> {
        self.transforms.clear();
        self.sprites.clear();
        self.lifecycle.clear();
        self.merge(frame)
    }

    fn take(&mut self) -> Result<RenderFrameDiff, dagger_runtime::RuntimeError> {
        let ops = std::mem::take(&mut self.lifecycle)
            .into_iter()
            .chain(std::mem::take(&mut self.transforms).into_values())
            .chain(std::mem::take(&mut self.sprites).into_values())
            .collect();
        RenderFrameDiff::try_from_ops(ops).map_err(|error| {
            dagger_runtime::RuntimeError::Encounter(format!("build pending live frame: {error:?}"))
        })
    }
}

#[derive(Default)]
struct PendingAudioPresentation {
    ops: Vec<PresentationOp>,
}

impl PendingAudioPresentation {
    fn merge(&mut self, frame: PresentationFrameDiff) {
        self.ops.extend(frame.ops);
    }

    fn take(&mut self) -> Result<PresentationFrameDiff, dagger_runtime::RuntimeError> {
        let ops = std::mem::take(&mut self.ops)
            .into_iter()
            .enumerate()
            .map(|(sequence, op)| resequence_presentation(op, sequence as u32))
            .collect();
        PresentationFrameDiff::try_from_ops(ops).map_err(|error| {
            dagger_runtime::RuntimeError::Encounter(format!(
                "build pending audio presentation: {error:?}"
            ))
        })
    }
}

fn resequence_presentation(op: PresentationOp, sequence: u32) -> PresentationOp {
    let meta = PresentationOpMeta::new(sequence);
    match op {
        PresentationOp::Audio { op, .. } => PresentationOp::Audio { meta, op },
        PresentationOp::Billboard { op, .. } => PresentationOp::Billboard { meta, op },
        PresentationOp::Particle { op, .. } => PresentationOp::Particle { meta, op },
        PresentationOp::TelemetryOverlay { op, .. } => {
            PresentationOp::TelemetryOverlay { meta, op }
        }
        PresentationOp::Animation { op, .. } => PresentationOp::Animation { meta, op },
    }
}

fn resolve_pointer_look(pointer_delta: [f32; 2]) -> ResolvedPlayerAction {
    ResolvedPlayerAction::Look {
        yaw_delta: pointer_delta[0] * 0.05,
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
        assert!(
            look([-10.0, 0.0]).0 < 0.0,
            "mouse-left feeds canonical negative yaw"
        );
        assert!(
            look([10.0, 0.0]).0 > 0.0,
            "mouse-right feeds canonical positive yaw"
        );
        assert!(
            look([0.0, -10.0]).1 > 0.0,
            "mouse-up feeds canonical positive pitch"
        );
        assert!(
            look([0.0, 10.0]).1 < 0.0,
            "mouse-down feeds canonical negative pitch"
        );
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

    #[test]
    fn pending_audio_preserves_one_shots_and_resequences_poll_batches() {
        use rusty_engine::render_presentation::{
            AudioBus, AudioClipRef, AudioEmitter, AudioProjectionOp, AudioSourceDescriptor,
        };

        let emit = |signal: &str| PresentationOp::Audio {
            meta: PresentationOpMeta::new(0),
            op: AudioProjectionOp::Emit {
                signal_id: signal.to_owned(),
                descriptor: AudioSourceDescriptor {
                    clip: AudioClipRef {
                        asset: "audio-resource/example".to_owned(),
                        content_hash: format!("sha256:{}", "0".repeat(64)),
                    },
                    bus: AudioBus::Sfx,
                    volume: 1.0,
                    pitch: 1.0,
                    looping: false,
                    spatial_blend: 0.0,
                    attenuation: 1.0,
                    pan: 0.0,
                    emitter: AudioEmitter::Global2d,
                },
            },
        };
        let mut pending = PendingAudioPresentation::default();
        pending.merge(PresentationFrameDiff::try_from_ops(vec![emit("swing")]).unwrap());
        pending.merge(PresentationFrameDiff::try_from_ops(vec![emit("hit")]).unwrap());
        let frame = pending.take().expect("batched audio");
        assert_eq!(frame.ops.len(), 2);
        assert_eq!(frame.ops[0].meta().sequence, 0);
        assert_eq!(frame.ops[1].meta().sequence, 1);
        assert!(pending.take().expect("drained audio").is_empty());
    }

    #[test]
    fn encounter_gallery_floor_supports_bounded_player_movement() {
        let mut runtime =
            DaggerRuntime::from_project_json(ENCOUNTER_GALLERY_PROJECT).expect("gallery runtime");
        runtime
            .install_encounter_navigation_json(ENCOUNTER_GALLERY_NAVGRID)
            .expect("gallery navgrid");
        let mut diagnostics =
            NativeDiagnostics::from_documents(ENCOUNTER_GALLERY_PROJECT, ENCOUNTER_GALLERY_NAVGRID)
                .expect("gallery diagnostics");
        assert_eq!(
            runtime.player_position().expect("gallery spawn").y,
            0.35,
            "gallery must start at its stable grounded height"
        );
        let mut codes = BTreeSet::new();
        let mut buttons = 0;
        runtime
            .set_player_position(rusty_engine::core_math::Vec3::new(-5.0, 0.35, -5.75))
            .expect("place player in ordinary melee range for input proof");
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut codes,
            &mut buttons,
            ProductInput {
                pressed_codes: vec!["Space".to_owned()],
                pointer_delta: [0.0, 0.0],
                buttons: 0,
            },
        )
        .expect("ordinary gallery attack without Lab focus");
        assert_eq!(
            runtime
                .experiment_readout()
                .expect("gallery readout")
                .combat_attempts
                .len(),
            1
        );
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut codes,
            &mut buttons,
            ProductInput {
                pressed_codes: Vec::new(),
                pointer_delta: [0.0, 0.0],
                buttons: 0,
            },
        )
        .expect("release gallery attack");
        runtime
            .reset_play_session()
            .expect("reset gallery after attack");
        for code in ["KeyW"; 30].into_iter().chain(["KeyA"; 30]) {
            apply_product_input(
                &mut runtime,
                &mut diagnostics,
                &mut codes,
                &mut buttons,
                ProductInput {
                    pressed_codes: vec![code.to_owned()],
                    pointer_delta: [0.0, 0.0],
                    buttons: 0,
                },
            )
            .expect("bounded gallery movement");
        }
        let position = runtime.player_position().expect("gallery player");
        assert!(position.x < -10.0 && position.z < -6.0);
        assert!(
            position.y > 0.2,
            "gallery floor must prevent falling: {position:?}"
        );
    }

    #[test]
    fn connected_product_ports_native_debug_keys() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real runtime");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("real navgrid");
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        let mut codes = BTreeSet::new();
        let mut buttons = 0;

        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut codes,
            &mut buttons,
            ProductInput {
                pressed_codes: vec!["KeyG".into(), "KeyN".into()],
                pointer_delta: [0.0, 0.0],
                buttons: 0,
            },
        )
        .expect("toggle diagnostics");
        assert!(diagnostics.sprite_overlay_enabled());
        assert!(diagnostics.nav_overlay_enabled());

        // Held keys are edge-triggered and must not toggle repeatedly.
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut codes,
            &mut buttons,
            ProductInput {
                pressed_codes: vec!["KeyG".into(), "KeyN".into()],
                pointer_delta: [0.0, 0.0],
                buttons: 0,
            },
        )
        .expect("hold diagnostics");
        assert!(diagnostics.sprite_overlay_enabled());
        assert!(diagnostics.nav_overlay_enabled());
    }
}
