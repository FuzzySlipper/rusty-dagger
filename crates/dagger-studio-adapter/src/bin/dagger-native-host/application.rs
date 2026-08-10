use std::{
    collections::{BTreeMap, BTreeSet},
    env,
    io::{self, Write},
    path::Path,
    process::Command,
    time::{Duration, Instant},
};

use anyhow::{bail, Context, Result};
use dagger_runtime::{DaggerRuntime, ResolvedPlayerAction};
use dagger_studio_adapter::{build_render_bundle, DaggerRenderBundle};
use rusty_engine::{
    render_host_contracts::{
        RendererCameraPose, RendererPhysicalInputReadout, RendererPickFilter, RendererPickRay,
        RendererPickRequest,
    },
    render_model::{RenderHandle, RenderLayer},
    renderer_webview_host::{
        RendererResource, RendererWebviewAdapter, RendererWebviewBounds,
        RendererWebviewObservation, RendererWebviewOptions,
    },
};
use winit::{
    application::ApplicationHandler,
    event::WindowEvent,
    event_loop::{ActiveEventLoop, ControlFlow, EventLoop},
    window::{Window, WindowId},
};

use crate::{
    diagnostics::{DiagnosticFrameReadout, NativeDiagnostics},
    lab_server::{LabCommand, LabReply, LabServer},
    proof::{Options, PendingPick, PickKind, Proof},
    view::{dagger_views, window_bounds},
};

const PROJECT: &str = include_str!("../../../../../content/projects/privateers-hold.project.json");
const NAVGRID: &str = include_str!("../../../../../content/projects/privateers-hold.navgrid.json");
const DUNGEON_HANDLE: RenderHandle = RenderHandle::new(2);

struct NativeApplication {
    options: Options,
    runtime: DaggerRuntime,
    diagnostics: NativeDiagnostics,
    bundle: Option<DaggerRenderBundle>,
    window: Option<Window>,
    renderer: Option<RendererWebviewAdapter>,
    pressed_codes: BTreeSet<String>,
    pending_input: Option<u64>,
    pending_pick: Option<PendingPick>,
    pending_diagnostic_frames: BTreeMap<u64, DiagnosticFrameReadout>,
    base_frame_request: Option<u64>,
    diagnostic_dispose_request: Option<u64>,
    dispose_request: Option<u64>,
    next_input_poll: Instant,
    next_diagnostic_tick: Instant,
    last_diagnostic_tick: Instant,
    started_at: Instant,
    ready: bool,
    proof: Proof,
    failure: Option<String>,
    lab_server: Option<LabServer>,
}

impl NativeApplication {
    fn new(options: Options) -> Result<Self> {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .and_then(Path::parent)
            .context("resolve Rusty Dagger workspace root")?;
        let mut runtime = DaggerRuntime::from_project_json(PROJECT)
            .context("admit checked Privateer's Hold project")?;
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .context("install committed encounter navigation")?;
        let diagnostics = NativeDiagnostics::from_documents(PROJECT, NAVGRID)?;
        let bundle = build_render_bundle(root, PROJECT).map_err(anyhow::Error::msg)?;
        Ok(Self {
            options,
            runtime,
            diagnostics,
            bundle: Some(bundle),
            window: None,
            renderer: None,
            pressed_codes: BTreeSet::new(),
            pending_input: None,
            pending_pick: None,
            pending_diagnostic_frames: BTreeMap::new(),
            base_frame_request: None,
            diagnostic_dispose_request: None,
            dispose_request: None,
            next_input_poll: Instant::now(),
            next_diagnostic_tick: Instant::now(),
            last_diagnostic_tick: Instant::now(),
            started_at: Instant::now(),
            ready: false,
            proof: Proof::default(),
            failure: None,
            lab_server: None,
        })
    }

    fn start_lab_server(&mut self) -> Result<()> {
        let Some(port) = self.options.lab_port else {
            return Ok(());
        };
        if !self.ready {
            bail!("refuse to advertise Dagger Lab before renderer readiness");
        }
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .and_then(Path::parent)
            .context("resolve Rusty Dagger workspace root")?;
        let server = LabServer::start(
            self.options.lab_host,
            port,
            root.join("dist/apps/dagger-lab/browser"),
        )?;
        println!(
            "DAGGER_LAB_READY api=http://127.0.0.1:{}/api/dagger-lab ui=http://127.0.0.1:{}",
            server.port(),
            server.port()
        );
        io::stdout().flush()?;
        self.lab_server = Some(server);
        Ok(())
    }

    fn drain_lab_commands(&mut self) -> Result<()> {
        loop {
            let command = match self.lab_server.as_ref().map(LabServer::try_recv) {
                Some(Ok(command)) => command,
                Some(Err(std::sync::mpsc::TryRecvError::Empty)) | None => break,
                Some(Err(std::sync::mpsc::TryRecvError::Disconnected)) => {
                    bail!("Dagger Lab bridge disconnected")
                }
            };
            let focus_game = match command {
                LabCommand::Read { reply } => {
                    let result = self.runtime.experiment_readout();
                    send_lab_result(reply, result)?;
                    false
                }
                LabCommand::Apply { document, reply } => {
                    let result = self.runtime.apply_experiment_json(&document);
                    send_lab_result(reply, result)?;
                    false
                }
                LabCommand::Evaluate { document, reply } => {
                    let result = self.runtime.evaluate_experiment_json(&document);
                    send_lab_result(reply, result)?;
                    false
                }
                LabCommand::Reset { reply } => {
                    let result = self.runtime.reset_play_session();
                    complete_camera_synced_lab_result(reply, result, || self.update_camera())?;
                    false
                }
                LabCommand::Play { reply } => {
                    let result = self.runtime.reset_play_session();
                    complete_camera_synced_lab_result(reply, result, || self.update_camera())?
                }
                LabCommand::Jump { id, reply } => {
                    let result = self.runtime.jump_to_content(id);
                    complete_camera_synced_lab_result(reply, result, || self.update_camera())?
                }
            };
            self.update_window_title()?;
            if focus_game {
                self.window
                    .as_ref()
                    .context("native game window unavailable")?
                    .focus_window();
            }
        }
        Ok(())
    }

    fn mount(&mut self, event_loop: &ActiveEventLoop) -> Result<()> {
        let title = self.native_window_title()?;
        let window = event_loop
            .create_window(
                Window::default_attributes()
                    .with_title(title)
                    .with_inner_size(winit::dpi::LogicalSize::new(1100, 720)),
            )
            .context("create Privateer's Hold product window")?;
        let bundle = self.bundle.as_mut().context("render bundle unavailable")?;
        if self.options.corrupt_resource {
            let resource = bundle
                .resources
                .first_mut()
                .context("no texture to corrupt")?;
            *resource
                .bytes
                .last_mut()
                .context("texture resource is empty")? ^= 0xff;
        }
        let resources = bundle
            .resources
            .iter()
            .map(|resource| RendererResource {
                identity: resource.identity.clone(),
                content_hash: resource.content_hash.clone(),
                media_type: resource.media_type.clone(),
                bytes: resource.bytes.clone(),
            })
            .collect();
        let renderer = RendererWebviewAdapter::mount(
            &window,
            RendererWebviewOptions {
                auto_start: true,
                bounds: window_bounds(&window),
                clear_color: Some(0x080a0d),
                pixel_ratio: window.scale_factor(),
                resources,
            },
        )
        .map_err(|error| anyhow::anyhow!("mount Engine-owned renderer: {error:?}"))?;
        self.window = Some(window);
        self.renderer = Some(renderer);
        self.update_window_title()?;
        Ok(())
    }

    fn native_window_title(&self) -> Result<String> {
        let readout = self.runtime.experiment_readout()?;
        let player = &readout.player_stats;
        let mut title = format!(
            "Privateer's Hold — Player H {:.0} S {:.0} M {:.0}",
            player.current_health, player.current_stamina, player.current_magicka
        );
        if let Some(focused_id) = readout.focused_content_id {
            if let Some(entity) = readout
                .content
                .iter()
                .find(|entity| entity.id == focused_id)
            {
                if let Some(resources) = entity.live.resources {
                    title.push_str(&format!(
                        " — {} H {:.0} S {:.0} M {:.0}",
                        entity.reference.mobile_name,
                        resources.current_health,
                        resources.current_stamina,
                        resources.current_magicka
                    ));
                }
            }
        }
        if let Some(combat) = readout.combat.last() {
            title.push_str(&format!(
                " — {} {:.0}",
                if combat.resolution.hit { "HIT" } else { "MISS" },
                combat.resolution.final_damage
            ));
            if combat.resolution.died {
                title.push_str(" DEAD");
            }
        }
        if let Some(attempt) = readout.combat_attempts.last() {
            if !attempt.accepted {
                if attempt.outcome == "cooldown" {
                    if readout.player_attack_cooldown_remaining > 0.0 {
                        title.push_str(&format!(
                            " — COOLDOWN {:.1}s — stamina {:.0}",
                            readout.player_attack_cooldown_remaining, attempt.stamina_after
                        ));
                    } else {
                        title.push_str(&format!(" — READY — stamina {:.0}", attempt.stamina_after));
                    }
                } else {
                    title.push_str(&format!(" — REJECTED {}", attempt.outcome));
                }
            }
        }
        if let Some(decision) = readout.encounter_decisions.last() {
            if let Some(damage) = decision.damage {
                title.push_str(&format!(" — {} attacks {:.0}", decision.enemy_name, damage));
            } else if let Some(state) = &decision.to {
                title.push_str(&format!(" — {} {state}", decision.enemy_name));
            }
        }
        title.push_str(" — Space attack — R reset — L Lab — G patrol — N navgrid");
        Ok(title)
    }

    fn update_window_title(&self) -> Result<()> {
        let title = self.native_window_title()?;
        if let Some(window) = &self.window {
            window.set_title(&title);
        }
        if self.options.proof {
            println!("DAGGER_NATIVE_STATS title={title}");
            io::stdout().flush()?;
        }
        Ok(())
    }

    fn camera_pose(&self) -> Result<RendererCameraPose> {
        let position = self.runtime.player_position()?;
        let state = self.runtime.player_state();
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

    fn update_camera(&mut self) -> Result<()> {
        let pose = self.camera_pose()?;
        self.renderer
            .as_mut()
            .context("renderer unavailable")?
            .set_camera_pose(pose, None)?;
        Ok(())
    }

    fn initialize_renderer(&mut self) -> Result<()> {
        let frame = self
            .bundle
            .as_ref()
            .context("render bundle unavailable")?
            .frame
            .clone();
        let pose = self.camera_pose()?;
        let renderer = self.renderer.as_mut().context("renderer unavailable")?;
        self.base_frame_request = Some(renderer.submit_frame(&frame)?);
        renderer.configure_views(&dagger_views(pose, 1))?;
        renderer.set_camera_pose(pose, None)?;
        renderer.read_state()?;
        renderer.render_once(None)?;
        let bounds = window_bounds(self.window.as_ref().context("window unavailable")?);
        renderer.resize(
            RendererWebviewBounds {
                width: bounds.width.saturating_sub(48).max(1),
                height: bounds.height.saturating_sub(32).max(1),
                ..bounds
            },
            self.window
                .as_ref()
                .context("window unavailable")?
                .scale_factor(),
        )?;
        self.request_input()?;
        Ok(())
    }

    fn request_input(&mut self) -> Result<()> {
        if self.pending_input.is_none() {
            self.pending_input = Some(
                self.renderer
                    .as_mut()
                    .context("renderer unavailable")?
                    .read_physical_input()?,
            );
        }
        Ok(())
    }

    fn input_control_held(&self) -> bool {
        // Input readbacks and retained updates share the Engine-owned webview.
        // Keep a control's falling edge ahead of the next diagnostic batch so
        // constrained hosts cannot latch the Rust semantic edge or dispose
        // the renderer before the physical release is observed.
        self.pressed_codes.iter().any(|code| {
            matches!(
                code.as_str(),
                "KeyG" | "KeyN" | "KeyL" | "KeyR" | "Enter" | "Space"
            )
        })
    }

    fn open_lab(&mut self) -> Result<()> {
        let url = match self.lab_server.as_ref() {
            Some(server) => server.local_url(),
            None => {
                println!("DAGGER_LAB_OPEN_UNAVAILABLE reason=disabled");
                io::stdout().flush()?;
                return Ok(());
            }
        };
        if self.options.proof {
            self.proof.lab_opened = true;
            println!("DAGGER_LAB_OPENED url={url} launcher=proof");
            io::stdout().flush()?;
            return Ok(());
        }
        match launch_external_url(&url) {
            Ok(()) => println!("DAGGER_LAB_OPENED url={url} launcher=system"),
            Err(error) => println!("DAGGER_LAB_OPEN_FAILED url={url} error={error:#}"),
        }
        io::stdout().flush()?;
        Ok(())
    }

    fn apply_input(&mut self, input: &RendererPhysicalInputReadout) -> Result<()> {
        let pressed = input.pressed_codes.iter().cloned().collect::<BTreeSet<_>>();
        let state_before = self.runtime.player_state();
        if self.options.proof {
            let released = self.pressed_codes.difference(&pressed).collect::<Vec<_>>();
            for code in &released {
                println!("DAGGER_NATIVE_INPUT_RELEASED code={code}");
            }
            if !released.is_empty() {
                io::stdout().flush()?;
            }
        }
        if pressed.is_empty() && input.pointer.buttons == 0 {
            let first_noop = !self.proof.input_noop;
            self.proof.input_noop |= self.runtime.player_state() == state_before;
            if first_noop && self.proof.input_noop && self.options.proof {
                println!("DAGGER_NATIVE_INPUT_ARMED");
                io::stdout().flush()?;
            }
        }
        if pressed.contains("KeyW") {
            self.runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 1.0,
                    right: 0.0,
                })?;
            self.update_camera()?;
        }
        if pressed.contains("KeyS") {
            self.runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: -1.0,
                    right: 0.0,
                })?;
            self.update_camera()?;
        }
        if pressed.contains("KeyA") {
            self.runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 0.0,
                    right: -1.0,
                })?;
            self.update_camera()?;
        }
        if pressed.contains("KeyD") {
            self.runtime
                .apply_player_action(ResolvedPlayerAction::Move {
                    forward: 0.0,
                    right: 1.0,
                })?;
            self.update_camera()?;
        }
        if pressed.contains("KeyG") && !self.pressed_codes.contains("KeyG") {
            let enabled = self.diagnostics.toggle_sprite_overlay();
            if self.options.proof {
                println!("DAGGER_DIAGNOSTIC_CONTROL kind=patrol enabled={enabled}");
                io::stdout().flush()?;
            }
        }
        if pressed.contains("KeyN") && !self.pressed_codes.contains("KeyN") {
            let enabled = self.diagnostics.toggle_nav_overlay();
            if self.options.proof {
                println!("DAGGER_DIAGNOSTIC_CONTROL kind=navgrid enabled={enabled}");
                io::stdout().flush()?;
            }
        }
        if pressed.contains("KeyL") && !self.pressed_codes.contains("KeyL") {
            self.open_lab()?;
        }
        if pressed.contains("KeyR") && !self.pressed_codes.contains("KeyR") {
            self.runtime.reset_play_session()?;
            self.update_camera()?;
            println!("DAGGER_COMBAT_RESET source=physical-KeyR");
            io::stdout().flush()?;
            self.update_window_title()?;
        }
        if pressed.contains("Space") && !self.pressed_codes.contains("Space") {
            match self.runtime.attack_focused_target() {
                Ok(readout) => {
                    let attempt = readout.combat_attempts.last();
                    if let Some(attempt) = attempt.filter(|attempt| !attempt.accepted) {
                        println!(
                            "DAGGER_COMBAT_REJECTED sequence={} reason={} cooldown={:.2} stamina={:.1}",
                            attempt.sequence,
                            attempt.outcome,
                            attempt.cooldown_before,
                            attempt.stamina_before,
                        );
                    } else if let Some(combat) = readout.combat.last() {
                        println!(
                            "DAGGER_COMBAT_APPLIED sequence={} target={} roll={} total={:.1} defense={:.1} hit={} damage={:.1} health={:.1}->{:.1} died={}",
                            combat.sequence,
                            combat.target_id,
                            combat.resolution.raw_roll,
                            combat.resolution.attack_total,
                            combat.resolution.target_defense,
                            combat.resolution.hit,
                            combat.resolution.final_damage,
                            combat.resolution.health_before,
                            combat.resolution.health_after,
                            combat.resolution.died,
                        );
                    }
                }
                Err(error) => println!("DAGGER_COMBAT_REJECTED error={error}"),
            }
            io::stdout().flush()?;
            self.update_window_title()?;
        }
        if pressed.contains("Enter") && !self.pressed_codes.contains("Enter") {
            self.runtime
                .apply_player_action(ResolvedPlayerAction::Look {
                    yaw_delta: 0.25,
                    pitch_delta: 0.0,
                })?;
            self.proof.input_authority = self.runtime.player_state() != state_before;
            if self.proof.input_authority && self.options.proof {
                println!("DAGGER_NATIVE_ACTION_APPLIED kind=look");
                io::stdout().flush()?;
            }
            self.update_camera()?;
            if self.pending_pick.is_none() {
                self.request_pick(PickKind::Miss)?;
            }
        }
        self.pressed_codes = pressed;
        Ok(())
    }

    fn submit_diagnostic_tick(&mut self, now: Instant) -> Result<()> {
        let dt = now
            .saturating_duration_since(self.last_diagnostic_tick)
            .as_secs_f32()
            .min(0.25);
        self.last_diagnostic_tick = now;
        let pose = self.camera_pose()?;
        let encounter_sequence = self.runtime.encounter_sequence();
        let attack_cooldown_before = self.runtime.player_attack_cooldown_remaining();
        let encounter_updates = self.runtime.tick_play_session(dt)?;
        let attack_cooldown_after = self.runtime.player_attack_cooldown_remaining();
        if self.runtime.encounter_sequence() != encounter_sequence
            || (attack_cooldown_before > 0.0 && attack_cooldown_after == 0.0)
        {
            self.update_window_title()?;
        }
        let encounter_positions = self.runtime.encounter_positions();
        let diagnostic = self.diagnostics.tick(
            dt,
            [
                pose.position[0] as f32,
                pose.position[1] as f32,
                pose.position[2] as f32,
            ],
            &encounter_positions,
            &encounter_updates,
        )?;
        if diagnostic.frame.ops.is_empty() {
            return Ok(());
        }
        let renderer = self.renderer.as_mut().context("renderer unavailable")?;
        let request_id = renderer.submit_frame(&diagnostic.frame)?;
        renderer.render_once(None)?;
        self.pending_diagnostic_frames
            .insert(request_id, diagnostic.readout);
        Ok(())
    }

    fn accept_diagnostic_readout(&mut self, readout: DiagnosticFrameReadout) {
        let before = (
            self.proof.diagnostics_enabled,
            self.proof.diagnostics_disabled,
            self.proof.animation_advanced,
            self.proof.patrol_moved,
            self.proof.stale_handle_replaced,
        );
        self.proof.animation_advanced |= readout.animation_advanced;
        self.proof.patrol_moved |= readout.patrol_moved;
        self.proof.diagnostics_enabled |= readout.overlays_enabled;
        self.proof.diagnostics_disabled |= readout.overlays_disabled;
        self.proof.stale_handle_replaced |= readout.stale_handle_replaced;
        self.proof.max_animation_updates = self
            .proof
            .max_animation_updates
            .max(readout.animation_updates);
        self.proof.max_retained_overlays = self
            .proof
            .max_retained_overlays
            .max(readout.retained_overlays);
        let after = (
            self.proof.diagnostics_enabled,
            self.proof.diagnostics_disabled,
            self.proof.animation_advanced,
            self.proof.patrol_moved,
            self.proof.stale_handle_replaced,
        );
        if self.options.proof && before != after {
            println!(
                "DAGGER_DIAGNOSTIC_APPLIED enabled={} disabled={} animation={} patrol={} replacement={} retained={}",
                after.0,
                after.1,
                after.2,
                after.3,
                after.4,
                self.proof.max_retained_overlays,
            );
        }
    }

    fn begin_proof_disposal(&mut self) -> Result<()> {
        if self.diagnostic_dispose_request.is_some() || self.dispose_request.is_some() {
            return Ok(());
        }
        let frame = self.diagnostics.dispose()?;
        let renderer = self.renderer.as_mut().context("renderer unavailable")?;
        if frame.ops.is_empty() {
            self.proof.diagnostics_disposed = true;
            self.dispose_request = Some(renderer.dispose()?);
        } else {
            self.diagnostic_dispose_request = Some(renderer.submit_frame(&frame)?);
        }
        Ok(())
    }

    fn request_pick(&mut self, kind: PickKind) -> Result<()> {
        let ray = match kind {
            PickKind::Miss => RendererPickRay::WorldRay {
                origin: [10_000.0, 10_000.0, 10_000.0],
                direction: [0.0, -1.0, 0.0],
            },
            PickKind::Dungeon => {
                let position = self.runtime.player_position()?;
                RendererPickRay::WorldRay {
                    origin: [
                        f64::from(position.x),
                        f64::from(position.y) + 4.0,
                        f64::from(position.z),
                    ],
                    direction: [0.0, -1.0, 0.0],
                }
            }
        };
        let filter = matches!(kind, PickKind::Dungeon).then(|| RendererPickFilter {
            handles: vec![DUNGEON_HANDLE],
            layers: vec![RenderLayer::Scene],
            ..RendererPickFilter::default()
        });
        let request_id = self
            .renderer
            .as_mut()
            .context("renderer unavailable")?
            .pick(&RendererPickRequest {
                filter,
                max_distance: Some(256.0),
                ray,
            })?;
        self.pending_pick = Some(PendingPick {
            request_id,
            kind,
            state_before: self.runtime.player_state(),
        });
        Ok(())
    }

    fn apply_pick(
        &mut self,
        request_id: u64,
        receipt: rusty_engine::render_host_contracts::RendererPickReceipt,
    ) -> Result<()> {
        let pending = self
            .pending_pick
            .take()
            .context("unexpected pick receipt")?;
        if pending.request_id != request_id {
            bail!(
                "pick request mismatch: received {request_id}, expected {}",
                pending.request_id
            );
        }
        match pending.kind {
            PickKind::Miss => {
                if receipt.hint.is_some() || self.runtime.player_state() != pending.state_before {
                    bail!("miss pick changed Dagger authority");
                }
                self.proof.pick_miss = true;
                self.request_pick(PickKind::Dungeon)?;
            }
            PickKind::Dungeon => {
                let hint = receipt.hint.context("dungeon pick returned no hit")?;
                let entity = hint
                    .source_trace
                    .map(|trace| trace.entity)
                    .context("dungeon pick returned no entity trace")?;
                if hint.handle != DUNGEON_HANDLE || entity != DUNGEON_HANDLE.raw() {
                    bail!(
                        "dungeon pick returned handle {} entity {entity}",
                        hint.handle.raw()
                    );
                }
                // This diagnostic's click-to-turn route is downstream Rust
                // orchestration: the renderer only reports the hit, while the
                // Dagger runtime authoritatively changes player look state.
                self.runtime
                    .apply_player_action(ResolvedPlayerAction::Look {
                        yaw_delta: -0.25,
                        pitch_delta: 0.0,
                    })?;
                self.proof.pick_authority = self.runtime.player_state() != pending.state_before;
                self.update_camera()?;
            }
        }
        Ok(())
    }

    fn handle_observation(
        &mut self,
        observation: RendererWebviewObservation,
        event_loop: &ActiveEventLoop,
    ) -> Result<()> {
        match observation {
            RendererWebviewObservation::Ready(_) => {
                if self.options.corrupt_resource {
                    bail!("corrupt resource unexpectedly reached ready state");
                }
                self.initialize_renderer()?;
                self.ready = true;
                self.start_lab_server()?;
                if self.options.proof {
                    println!("DAGGER_NATIVE_READY_FOR_INPUT");
                    io::stdout().flush()?;
                }
            }
            RendererWebviewObservation::FrameApplied {
                request_id,
                receipt,
            } => {
                if !receipt.applied {
                    bail!("renderer rejected Dagger frame: {:?}", receipt.diagnostics);
                }
                if self.base_frame_request == Some(request_id) {
                    self.proof.frame = true;
                    self.proof.resources = self
                        .bundle
                        .as_ref()
                        .is_some_and(|bundle| !bundle.resources.is_empty());
                } else if let Some(readout) = self.pending_diagnostic_frames.remove(&request_id) {
                    self.accept_diagnostic_readout(readout);
                } else if self.diagnostic_dispose_request == Some(request_id) {
                    self.diagnostic_dispose_request = None;
                    self.proof.diagnostics_disposed = true;
                    self.dispose_request = Some(
                        self.renderer
                            .as_mut()
                            .context("renderer unavailable")?
                            .dispose()?,
                    );
                }
            }
            RendererWebviewObservation::ViewsConfigured { receipt, .. } => {
                if !receipt.applied {
                    bail!("renderer rejected Dagger views: {:?}", receipt.diagnostics);
                }
                self.proof.views = true;
            }
            RendererWebviewObservation::CameraUpdated { .. } => self.proof.camera = true,
            RendererWebviewObservation::PhysicalInputRead {
                request_id,
                readout,
            } if self.pending_input == Some(request_id) => {
                self.pending_input = None;
                self.apply_input(&readout)?;
            }
            RendererWebviewObservation::PickCompleted {
                request_id,
                receipt,
            } => self.apply_pick(request_id, receipt)?,
            RendererWebviewObservation::StateRead { .. } => self.proof.state = true,
            RendererWebviewObservation::FrameRendered { .. } => self.proof.render = true,
            RendererWebviewObservation::Resized { .. } => self.proof.resize = true,
            RendererWebviewObservation::Disposed { request_id }
                if self.dispose_request == Some(request_id) =>
            {
                let bundle = self.bundle.as_ref().context("render bundle unavailable")?;
                let resource_bytes = bundle
                    .resources
                    .iter()
                    .map(|resource| resource.bytes.len())
                    .sum::<usize>();
                println!(
                    "DAGGER_NATIVE_PROOF_OK frame={} views={} camera={} resize={} resources={} resource_count={} resource_bytes={} source_entities={} input_authority={} input_noop={} pick_authority={} pick_miss={} state={} render={} lab_opened={} diagnostics_enabled={} diagnostics_disabled={} animation_advanced={} patrol_moved={} stale_handle_replaced={} diagnostics_disposed={} max_animation_updates={} max_retained_overlays={} lifecycle=disposed boundary=rust_facade",
                    self.proof.frame,
                    self.proof.views,
                    self.proof.camera,
                    self.proof.resize,
                    self.proof.resources,
                    bundle.resources.len(),
                    resource_bytes,
                    bundle.source_entity_count,
                    self.proof.input_authority,
                    self.proof.input_noop,
                    self.proof.pick_authority,
                    self.proof.pick_miss,
                    self.proof.state,
                    self.proof.render,
                    self.proof.lab_opened,
                    self.proof.diagnostics_enabled,
                    self.proof.diagnostics_disabled,
                    self.proof.animation_advanced,
                    self.proof.patrol_moved,
                    self.proof.stale_handle_replaced,
                    self.proof.diagnostics_disposed,
                    self.proof.max_animation_updates,
                    self.proof.max_retained_overlays,
                );
                event_loop.exit();
            }
            RendererWebviewObservation::MountFailed { message } => {
                self.renderer = None;
                if self.options.corrupt_resource && message.contains("content hash mismatch") {
                    println!(
                        "DAGGER_RESOURCE_REJECTION_OK lifecycle=transactional message={message}"
                    );
                    event_loop.exit();
                } else {
                    bail!("renderer mount failed transactionally: {message}");
                }
            }
            RendererWebviewObservation::OperationFailed {
                request_id,
                operation,
                message,
            } => bail!("renderer operation {operation:?} request {request_id} failed: {message}"),
            _ => {}
        }
        Ok(())
    }

    fn fail(&mut self, event_loop: &ActiveEventLoop, error: impl std::fmt::Display) {
        self.renderer = None;
        self.failure = Some(error.to_string());
        event_loop.exit();
    }
}

fn launch_external_url(url: &str) -> Result<()> {
    if let Some(command) = env::var_os("DAGGER_LAB_OPEN_COMMAND") {
        let status = Command::new(command)
            .arg(url)
            .status()
            .context("run configured Dagger Lab browser command")?;
        if !status.success() {
            bail!("configured Dagger Lab browser command exited with {status}");
        }
        return Ok(());
    }
    #[cfg(target_os = "linux")]
    let mut command = Command::new("xdg-open");
    #[cfg(target_os = "macos")]
    let mut command = Command::new("open");
    #[cfg(target_os = "windows")]
    let mut command = {
        let mut command = Command::new("cmd");
        command.args(["/C", "start", ""]);
        command
    };
    #[cfg(not(any(target_os = "linux", target_os = "macos", target_os = "windows")))]
    bail!("opening Dagger Lab is unsupported on this operating system");
    let status = command
        .arg(url)
        .status()
        .context("open Dagger Lab in the system browser")?;
    if !status.success() {
        bail!("system browser command exited with {status}");
    }
    Ok(())
}

impl ApplicationHandler for NativeApplication {
    fn resumed(&mut self, event_loop: &ActiveEventLoop) {
        if self.window.is_none() {
            if let Err(error) = self.mount(event_loop) {
                if self.options.corrupt_resource
                    && error
                        .to_string()
                        .contains("resource bytes do not match the declared SHA-256 identity")
                {
                    println!(
                        "DAGGER_RESOURCE_REJECTION_OK lifecycle=transactional message={error}"
                    );
                    event_loop.exit();
                } else {
                    self.fail(event_loop, error);
                }
            }
        }
    }

    fn window_event(
        &mut self,
        event_loop: &ActiveEventLoop,
        _window_id: WindowId,
        event: WindowEvent,
    ) {
        if matches!(event, WindowEvent::CloseRequested) && self.dispose_request.is_none() {
            let _ = self.diagnostics.dispose();
            match self.renderer.as_mut().map(RendererWebviewAdapter::dispose) {
                Some(Ok(request_id)) => self.dispose_request = Some(request_id),
                Some(Err(error)) => self.fail(event_loop, error),
                None => event_loop.exit(),
            }
        }
    }

    fn about_to_wait(&mut self, event_loop: &ActiveEventLoop) {
        #[cfg(target_os = "linux")]
        while gtk::events_pending() {
            gtk::main_iteration_do(false);
        }
        if let Err(error) = self.drain_lab_commands() {
            self.fail(event_loop, error);
            return;
        }
        if self.options.proof && self.started_at.elapsed() > Duration::from_secs(300) {
            self.fail(
                event_loop,
                format!("native renderer proof timed out: {:?}", self.proof),
            );
            return;
        }
        let observations = self
            .renderer
            .as_mut()
            .map(RendererWebviewAdapter::drain_observations)
            .unwrap_or_default();
        for observation in observations {
            let result = observation
                .map_err(anyhow::Error::from)
                .and_then(|observation| self.handle_observation(observation, event_loop));
            if let Err(error) = result {
                self.fail(event_loop, error);
                return;
            }
        }
        if self.failure.is_some()
            || self.dispose_request.is_some()
            || self.diagnostic_dispose_request.is_some()
        {
            return;
        }
        if self.ready && self.renderer.is_some() && Instant::now() >= self.next_input_poll {
            if let Err(error) = self.request_input() {
                self.fail(event_loop, error);
                return;
            }
            self.next_input_poll = Instant::now() + Duration::from_millis(40);
        }
        let now = Instant::now();
        if self.ready
            && self.proof.frame
            && self.renderer.is_some()
            && !self.input_control_held()
            && self.pending_diagnostic_frames.is_empty()
            && now >= self.next_diagnostic_tick
        {
            if let Err(error) = self.submit_diagnostic_tick(now) {
                self.fail(event_loop, error);
                return;
            }
            self.next_diagnostic_tick = now + Duration::from_millis(100);
        }
        if self.options.proof && self.proof.complete() && !self.input_control_held() {
            if let Err(error) = self.begin_proof_disposal() {
                self.fail(event_loop, error);
            }
        }
    }
}

fn send_lab_result<T: serde::Serialize>(
    reply: std::sync::mpsc::Sender<LabReply>,
    result: Result<T, dagger_runtime::RuntimeError>,
) -> Result<()> {
    let response = match result {
        Ok(value) => LabReply {
            status: 200,
            body: serde_json::to_string(&value).context("serialize Dagger Lab response")?,
        },
        Err(error) => LabReply {
            status: 400,
            body: serde_json::to_string(&serde_json::json!({ "error": error.to_string() }))
                .context("serialize Dagger Lab error")?,
        },
    };
    // A closed browser tab may abandon its one-shot reply channel. That is a
    // transport disconnect, not a reason to stop the authoritative game.
    let _ = reply.send(response);
    Ok(())
}

fn complete_camera_synced_lab_result<T: serde::Serialize>(
    reply: std::sync::mpsc::Sender<LabReply>,
    result: Result<T, dagger_runtime::RuntimeError>,
    submit_camera: impl FnOnce() -> Result<()>,
) -> Result<bool> {
    let value = match result {
        Ok(value) => value,
        Err(error) => {
            send_lab_result::<T>(reply, Err(error))?;
            return Ok(false);
        }
    };
    if let Err(error) = submit_camera() {
        let response = LabReply {
            status: 500,
            body: serde_json::to_string(&serde_json::json!({
                "error": format!("camera synchronization failed: {error}")
            }))
            .context("serialize Dagger Lab camera error")?,
        };
        let _ = reply.send(response);
        return Err(error);
    }
    send_lab_result(reply, Ok(value))?;
    Ok(true)
}

pub(crate) fn run(options: Options) -> Result<()> {
    let event_loop = EventLoop::new().context("create Privateer's Hold event loop")?;
    event_loop.set_control_flow(ControlFlow::Poll);
    let mut application = NativeApplication::new(options)?;
    event_loop
        .run_app(&mut application)
        .context("run Privateer's Hold native product")?;
    if let Some(failure) = application.failure {
        bail!(failure);
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use std::{
        net::{SocketAddr, TcpListener, TcpStream},
        sync::mpsc,
        time::Duration,
    };

    use anyhow::anyhow;

    use super::*;

    fn unused_local_port() -> u16 {
        TcpListener::bind(("127.0.0.1", 0))
            .expect("reserve local port")
            .local_addr()
            .expect("read local port")
            .port()
    }

    #[test]
    fn lab_endpoint_is_unavailable_until_native_renderer_is_ready() {
        let port = unused_local_port();
        let mut application = NativeApplication::new(Options {
            proof: false,
            corrupt_resource: false,
            lab_host: std::net::IpAddr::V4(std::net::Ipv4Addr::LOCALHOST),
            lab_port: Some(port),
        })
        .expect("construct native host");

        let address = SocketAddr::from(([127, 0, 0, 1], port));
        assert!(TcpStream::connect_timeout(&address, Duration::from_millis(100)).is_err());
        let error = application
            .start_lab_server()
            .expect_err("pre-ready Lab activation must fail closed");
        assert!(error.to_string().contains("before renderer readiness"));
        assert!(application.lab_server.is_none());
        assert!(TcpStream::connect_timeout(&address, Duration::from_millis(100)).is_err());
    }

    #[test]
    fn camera_synced_reply_never_reports_success_before_camera_acceptance() {
        let (send_failure, receive_failure) = mpsc::channel();
        let error = complete_camera_synced_lab_result(
            send_failure,
            Ok(serde_json::json!({ "position": [1, 2, 3] })),
            || {
                assert!(receive_failure.try_recv().is_err());
                Err(anyhow!("renderer rejected camera pose"))
            },
        )
        .expect_err("camera rejection must fail the host command");
        assert!(error.to_string().contains("renderer rejected camera pose"));
        let failure = receive_failure.recv().expect("camera failure reply");
        assert_eq!(failure.status, 500);
        assert!(failure.body.contains("camera synchronization failed"));

        let (send_success, receive_success) = mpsc::channel();
        let focused = complete_camera_synced_lab_result(
            send_success,
            Ok(serde_json::json!({ "position": [4, 5, 6] })),
            || {
                assert!(receive_success.try_recv().is_err());
                Ok(())
            },
        )
        .expect("accepted camera pose");
        assert!(focused);
        let success = receive_success.recv().expect("successful readback reply");
        assert_eq!(success.status, 200);
        assert!(success.body.contains("[4,5,6]"));
    }
}
