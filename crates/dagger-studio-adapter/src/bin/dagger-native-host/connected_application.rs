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
use dagger_runtime::{
    DaggerRuntime, MeleePresentationReadout, ResolvedPlayerFrame, MAX_PLAYER_FRAME_LOOK_UNITS,
};
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
const PRIVATEERS_HOLD_ENCOUNTERS: &str =
    include_str!("../../../../../data/encounters/privateers-hold.json");
const ENCOUNTER_GALLERY_ENCOUNTERS: &str =
    include_str!("../../../../../data/encounters/encounter-gallery.json");

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
    let (project, navgrid, encounters, product_name) = if options.encounter_gallery {
        (
            ENCOUNTER_GALLERY_PROJECT,
            ENCOUNTER_GALLERY_NAVGRID,
            ENCOUNTER_GALLERY_ENCOUNTERS,
            "encounter-gallery",
        )
    } else {
        (
            PROJECT,
            NAVGRID,
            PRIVATEERS_HOLD_ENCOUNTERS,
            "privateers-hold",
        )
    };
    let mut runtime = DaggerRuntime::from_project_json(project)
        .with_context(|| format!("admit checked {product_name} project"))?;
    runtime
        .install_encounter_navigation_json(navgrid)
        .context("install committed encounter navigation")?;
    runtime
        .install_named_encounters_json(encounters)
        .context("install committed named encounters")?;
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
        root.join("content"),
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

    let mut accepted_input_sequence = 0_u64;
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
                &mut accepted_input_sequence,
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
    accepted_input_sequence: &mut u64,
) -> Result<()> {
    match command {
        LabCommand::ProductBootstrap { reply } => {
            pending_presentation.replace(presentation.snapshot()?)?;
            let _ = pending_audio.take()?;
            send_json(
                reply,
                200,
                &ProductBootstrap::new(runtime, bundle, *accepted_input_sequence)?,
            )
        }
        LabCommand::ProductState { reply } => send_json(
            reply,
            200,
            &product_state(
                runtime,
                presentation,
                pending_presentation.take()?,
                pending_audio.take()?,
                *accepted_input_sequence,
            )?,
        ),
        LabCommand::ProductInput { input, reply } => {
            let result = apply_product_input(runtime, presentation, accepted_input_sequence, input)
                .and_then(|()| {
                    product_input_state(runtime, presentation, *accepted_input_sequence)
                        .map_err(anyhow::Error::from)
                });
            match result {
                Ok(state) => send_json(reply, 200, &state),
                Err(error) => send_json(
                    reply,
                    409,
                    &serde_json::json!({ "error": error.to_string() }),
                ),
            }
        }
        LabCommand::Read { reply } => send_runtime_result(reply, runtime.lab_readout()),
        LabCommand::Reset { reply } | LabCommand::Play { reply } => {
            send_runtime_result(reply, runtime.reset_play_session())
        }
        LabCommand::Jump { id, reply } => send_runtime_result(reply, runtime.jump_to_content(id)),
        LabCommand::Equip { item, reply } => send_runtime_result(reply, runtime.equip_item(item)),
        LabCommand::Unequip { slot, reply } => {
            send_runtime_result(reply, runtime.unequip_slot(&slot))
        }
        LabCommand::Grant {
            item,
            quantity,
            reply,
        } => send_runtime_result(reply, runtime.grant_item(&item, quantity)),
    }
}

fn apply_product_input(
    runtime: &mut DaggerRuntime,
    presentation: &mut NativeDiagnostics,
    accepted_sequence: &mut u64,
    input: ProductInput,
) -> Result<()> {
    if input.sequence <= *accepted_sequence {
        bail!(
            "stale product input sequence {}; latest accepted is {}",
            input.sequence,
            *accepted_sequence
        );
    }
    let pressed = input.pressed_codes.into_iter().collect::<BTreeSet<_>>();
    let _held_buttons = input.buttons;
    let pressed_edges = input.pressed_edges.into_iter().collect::<BTreeSet<_>>();
    let attack_pressed = pressed_edges.contains("Space") || input.button_pressed_edges & 1 != 0;
    let reset_pressed = pressed_edges.contains("KeyR");
    let equip_pressed = pressed_edges.contains("KeyE");
    let patrol_debug_pressed = pressed_edges.contains("KeyG");
    let nav_debug_pressed = pressed_edges.contains("KeyN");
    let active = |code| pressed.contains(code) || pressed_edges.contains(code);
    let forward = f32::from(active("KeyW")) - f32::from(active("KeyS"));
    let right = f32::from(active("KeyD")) - f32::from(active("KeyA"));
    let (yaw_delta, pitch_delta) = pointer_look_delta(input.pointer_delta);
    if forward != 0.0 || right != 0.0 || yaw_delta != 0.0 || pitch_delta != 0.0 {
        runtime.apply_player_frame(ResolvedPlayerFrame {
            forward,
            right,
            yaw_delta,
            pitch_delta,
            step_seconds: input.step_seconds,
        })?;
    }
    if attack_pressed {
        let _ = runtime.attack_focused_target()?;
    }
    if equip_pressed {
        let _ = runtime.equip_cycle()?;
    }
    for route_code in ["Digit1", "Digit2"] {
        if pressed_edges.contains(route_code) {
            runtime.route_named_encounter(route_code)?;
        }
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
    *accepted_sequence = input.sequence;
    Ok(())
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProductBootstrap<'a> {
    schema_version: u8,
    input_sequence: u64,
    camera: RendererCameraPose,
    frame: &'a rusty_engine::render_model::RenderFrameDiff,
    resources: Vec<ProductResource<'a>>,
    source_entity_count: usize,
}

impl<'a> ProductBootstrap<'a> {
    fn new(
        runtime: &DaggerRuntime,
        bundle: &'a DaggerRenderBundle,
        input_sequence: u64,
    ) -> Result<Self> {
        Ok(Self {
            schema_version: 1,
            input_sequence,
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
    input_sequence: u64,
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
    input_sequence: u64,
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
    input_sequence: u64,
) -> Result<ProductState, dagger_runtime::RuntimeError> {
    let position = runtime.player_position()?;
    let state = runtime.player_state();
    let stamina = runtime.player_stamina();
    Ok(ProductState {
        input_sequence,
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
    input_sequence: u64,
) -> Result<ProductInputState, dagger_runtime::RuntimeError> {
    let position = runtime.player_position()?;
    let camera = camera_pose(runtime)?;
    let stamina = runtime.player_stamina();
    Ok(ProductInputState {
        input_sequence,
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
    let enemy_presentation = runtime.enemy_presentation();
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
        &enemy_presentation,
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

fn pointer_look_delta(pointer_delta: [f32; 2]) -> (f32, f32) {
    (
        (pointer_delta[0] * 0.05).clamp(-MAX_PLAYER_FRAME_LOOK_UNITS, MAX_PLAYER_FRAME_LOOK_UNITS),
        (-pointer_delta[1] * 0.05).clamp(-MAX_PLAYER_FRAME_LOOK_UNITS, MAX_PLAYER_FRAME_LOOK_UNITS),
    )
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
        pointer_look_delta(delta)
    }

    fn input(
        sequence: u64,
        pressed_codes: &[&str],
        pressed_edges: &[&str],
        pointer_delta: [f32; 2],
    ) -> ProductInput {
        ProductInput {
            sequence,
            step_seconds: 0.04,
            pressed_codes: pressed_codes.iter().map(ToString::to_string).collect(),
            pressed_edges: pressed_edges.iter().map(ToString::to_string).collect(),
            pointer_delta,
            buttons: 0,
            button_pressed_edges: 0,
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
        let mut sequence = 0;
        runtime
            .set_player_position(rusty_engine::core_math::Vec3::new(-5.0, 0.35, -5.75))
            .expect("place player in ordinary melee range for input proof");
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(1, &["Space"], &["Space"], [0.0, 0.0]),
        )
        .expect("ordinary gallery attack without Lab focus");
        assert_eq!(
            runtime
                .lab_readout()
                .expect("gallery readout")
                .combat_attempts
                .len(),
            1
        );
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(2, &[], &[], [0.0, 0.0]),
        )
        .expect("release gallery attack");
        runtime
            .reset_play_session()
            .expect("reset gallery after attack");
        for (index, code) in ["KeyW"; 30].into_iter().chain(["KeyA"; 30]).enumerate() {
            apply_product_input(
                &mut runtime,
                &mut diagnostics,
                &mut sequence,
                input(index as u64 + 3, &[code], &[], [0.0, 0.0]),
            )
            .expect("bounded gallery movement");
        }
        let position = runtime.player_position().expect("gallery player");
        assert!(
            position.x < -3.5 && position.z < 0.5,
            "sampled W/A frames must move across the gallery floor: {position:?}"
        );
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
        let mut sequence = 0;

        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(1, &[], &["KeyG", "KeyN"], [0.0, 0.0]),
        )
        .expect("toggle diagnostics");
        assert!(diagnostics.sprite_overlay_enabled());
        assert!(diagnostics.nav_overlay_enabled());

        // Held state without another durable edge must not toggle repeatedly.
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(2, &["KeyG", "KeyN"], &[], [0.0, 0.0]),
        )
        .expect("hold diagnostics");
        assert!(diagnostics.sprite_overlay_enabled());
        assert!(diagnostics.nav_overlay_enabled());
    }

    #[test]
    fn ordinary_number_keys_route_named_combat_encounters() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real runtime");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("real navgrid");
        runtime
            .install_named_encounters_json(PRIVATEERS_HOLD_ENCOUNTERS)
            .expect("named encounters");
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        let mut sequence = 0;

        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(1, &[], &["Digit1"], [0.0, 0.0]),
        )
        .expect("Rat route");
        let rat = runtime.lab_readout().expect("Rat readout");
        assert_eq!(
            rat.active_encounter.expect("active Rat").id,
            "rat-introduction"
        );

        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(2, &[], &["Digit2"], [0.0, 0.0]),
        )
        .expect("Skeleton route");
        let skeleton = runtime.lab_readout().expect("Skeleton readout");
        assert_eq!(
            skeleton.active_encounter.expect("active Skeleton").id,
            "skeletal-guardroom"
        );
    }

    #[test]
    fn product_input_rejects_stale_sequences_and_bounds_large_pointer_bursts() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real runtime");
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        let mut sequence = 0;
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(4, &["KeyW"], &[], [10_000.0, -10_000.0]),
        )
        .expect("bounded pointer burst");
        assert_eq!(sequence, 4);
        assert!(runtime.player_state().pitch_degrees <= 89.0);
        let error = apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(4, &[], &[], [0.0, 0.0]),
        )
        .expect_err("duplicate input sequence");
        assert!(error.to_string().contains("stale product input sequence"));
    }

    #[test]
    fn edge_only_attack_keeps_the_authored_target_pose() {
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real runtime");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("real navigation");
        runtime.jump_to_content(2007).expect("jump beside Rat");
        let before = runtime.player_position().expect("player pose");
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        let mut sequence = 0;
        apply_product_input(
            &mut runtime,
            &mut diagnostics,
            &mut sequence,
            input(1, &[], &["Space"], [0.0, 0.0]),
        )
        .expect("attack edge");
        assert_eq!(
            runtime.player_position().expect("unchanged player pose"),
            before
        );
        assert_eq!(
            runtime
                .lab_readout()
                .expect("readout")
                .combat_attempts
                .len(),
            1
        );
    }
}
