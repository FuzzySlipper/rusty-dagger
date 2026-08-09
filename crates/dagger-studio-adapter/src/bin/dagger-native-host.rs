use std::{
    collections::BTreeSet,
    env,
    io::{self, Write},
    path::Path,
    time::{Duration, Instant},
};

use anyhow::{bail, Context, Result};
use dagger_runtime::{DaggerRuntime, ResolvedPlayerAction};
use dagger_studio_adapter::{build_render_bundle, DaggerRenderBundle};
use rusty_engine::{
    render_host_contracts::{
        RendererCameraPose, RendererCameraProjection, RendererCompositionCamera,
        RendererCompositionTarget, RendererPhysicalInputReadout, RendererPickFilter,
        RendererPickRay, RendererPickRequest, RendererTargetColor, RendererTargetDepth,
        RendererTargetSampling, RendererViewComposition, RendererViewTarget, RendererViewport,
        RENDERER_VIEW_COMPOSITION_SCHEMA_VERSION,
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

const PROJECT: &str = include_str!("../../../../content/projects/privateers-hold.project.json");
const DUNGEON_HANDLE: RenderHandle = RenderHandle::new(2);

#[derive(Debug, Clone, Copy)]
struct Options {
    proof: bool,
    corrupt_resource: bool,
}

impl Options {
    fn parse() -> Result<Self> {
        let mut proof = false;
        let mut corrupt_resource = false;
        for argument in env::args().skip(1) {
            match argument.as_str() {
                "--proof" => proof = true,
                "--proof-corrupt-resource" => {
                    proof = true;
                    corrupt_resource = true;
                }
                _ => bail!("unknown argument {argument}"),
            }
        }
        Ok(Self {
            proof,
            corrupt_resource,
        })
    }
}

#[derive(Debug, Default)]
struct Proof {
    frame: bool,
    views: bool,
    camera: bool,
    resize: bool,
    resources: bool,
    input_authority: bool,
    input_noop: bool,
    pick_authority: bool,
    pick_miss: bool,
    state: bool,
    render: bool,
}

impl Proof {
    fn complete(&self) -> bool {
        self.frame
            && self.views
            && self.camera
            && self.resize
            && self.resources
            && self.input_authority
            && self.input_noop
            && self.pick_authority
            && self.pick_miss
            && self.state
            && self.render
    }
}

#[derive(Debug, Clone, Copy)]
enum PickKind {
    Miss,
    Dungeon,
}

#[derive(Debug, Clone, Copy)]
struct PendingPick {
    request_id: u64,
    kind: PickKind,
    state_before: dagger_runtime::PlayerControllerState,
}

struct NativeApplication {
    options: Options,
    runtime: DaggerRuntime,
    bundle: Option<DaggerRenderBundle>,
    window: Option<Window>,
    renderer: Option<RendererWebviewAdapter>,
    pressed_codes: BTreeSet<String>,
    pending_input: Option<u64>,
    pending_pick: Option<PendingPick>,
    dispose_request: Option<u64>,
    next_input_poll: Instant,
    started_at: Instant,
    ready: bool,
    proof: Proof,
    failure: Option<String>,
}

impl NativeApplication {
    fn new(options: Options) -> Result<Self> {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .and_then(Path::parent)
            .context("resolve Rusty Dagger workspace root")?;
        let runtime = DaggerRuntime::from_project_json(PROJECT)
            .context("admit checked Privateer's Hold project")?;
        let bundle = build_render_bundle(root, PROJECT).map_err(anyhow::Error::msg)?;
        Ok(Self {
            options,
            runtime,
            bundle: Some(bundle),
            window: None,
            renderer: None,
            pressed_codes: BTreeSet::new(),
            pending_input: None,
            pending_pick: None,
            dispose_request: None,
            next_input_poll: Instant::now(),
            started_at: Instant::now(),
            ready: false,
            proof: Proof::default(),
            failure: None,
        })
    }

    fn mount(&mut self, event_loop: &ActiveEventLoop) -> Result<()> {
        let window = event_loop
            .create_window(
                Window::default_attributes()
                    .with_title("Privateer's Hold — Rust-native Engine renderer")
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
        renderer.submit_frame(&frame)?;
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

    fn apply_input(&mut self, input: &RendererPhysicalInputReadout) -> Result<()> {
        let pressed = input.pressed_codes.iter().cloned().collect::<BTreeSet<_>>();
        let state_before = self.runtime.player_state();
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
        if pressed.contains("Enter") && !self.pressed_codes.contains("Enter") {
            self.runtime
                .apply_player_action(ResolvedPlayerAction::Look {
                    yaw_delta: 0.25,
                    pitch_delta: 0.0,
                })?;
            self.proof.input_authority = self.runtime.player_state() != state_before;
            self.update_camera()?;
            if self.pending_pick.is_none() {
                self.request_pick(PickKind::Miss)?;
            }
        }
        self.pressed_codes = pressed;
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
                self.ready = true;
                self.initialize_renderer()?;
                if self.options.proof {
                    println!("DAGGER_NATIVE_READY_FOR_INPUT");
                    io::stdout().flush()?;
                }
            }
            RendererWebviewObservation::FrameApplied { receipt, .. } => {
                if !receipt.applied {
                    bail!("renderer rejected Dagger frame: {:?}", receipt.diagnostics);
                }
                self.proof.frame = true;
                self.proof.resources = self
                    .bundle
                    .as_ref()
                    .is_some_and(|bundle| !bundle.resources.is_empty());
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
                    "DAGGER_NATIVE_PROOF_OK frame={} views={} camera={} resize={} resources={} resource_count={} resource_bytes={} source_entities={} input_authority={} input_noop={} pick_authority={} pick_miss={} state={} render={} lifecycle=disposed boundary=rust_facade",
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
        if self.options.proof && self.started_at.elapsed() > Duration::from_secs(180) {
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
        if self.failure.is_some() || self.dispose_request.is_some() {
            return;
        }
        if self.ready && self.renderer.is_some() && Instant::now() >= self.next_input_poll {
            if let Err(error) = self.request_input() {
                self.fail(event_loop, error);
                return;
            }
            self.next_input_poll = Instant::now() + Duration::from_millis(40);
        }
        if self.options.proof && self.proof.complete() {
            match self.renderer.as_mut().map(RendererWebviewAdapter::dispose) {
                Some(Ok(request_id)) => self.dispose_request = Some(request_id),
                Some(Err(error)) => self.fail(event_loop, error),
                None => self.fail(event_loop, "renderer disappeared before disposal"),
            }
        }
    }
}

fn dagger_views(pose: RendererCameraPose, target_revision: u64) -> RendererViewComposition {
    RendererViewComposition {
        schema_version: RENDERER_VIEW_COMPOSITION_SCHEMA_VERSION,
        cameras: vec![RendererCompositionCamera {
            id: "camera.privateers-hold".to_owned(),
            pose,
            projection: RendererCameraProjection::Perspective {
                fov_y_degrees: 65.0,
                near: 0.05,
                far: 512.0,
            },
        }],
        targets: vec![RendererCompositionTarget {
            id: "target.privateers-hold".to_owned(),
            revision: target_revision,
            width: 512,
            height: 384,
            color: RendererTargetColor::Rgba8Srgb,
            depth: RendererTargetDepth::Depth24,
            sampling: RendererTargetSampling::Linear,
        }],
        views: vec![
            rusty_engine::render_host_contracts::RendererCompositionView {
                id: "view.privateers-hold".to_owned(),
                camera_id: "camera.privateers-hold".to_owned(),
                target: RendererViewTarget::Offscreen {
                    target_id: "target.privateers-hold".to_owned(),
                    target_revision,
                },
                viewport: RendererViewport {
                    x: 0.0,
                    y: 0.0,
                    width: 1.0,
                    height: 1.0,
                },
                order: 10,
            },
        ],
        presentations: Vec::new(),
    }
}

fn window_bounds(window: &Window) -> RendererWebviewBounds {
    let size = window.inner_size();
    let scale = window.scale_factor();
    RendererWebviewBounds {
        x: 0,
        y: 0,
        width: ((f64::from(size.width) / scale).round() as u32).max(1),
        height: ((f64::from(size.height) / scale).round() as u32).max(1),
    }
}

fn main() -> Result<()> {
    #[cfg(target_os = "linux")]
    gtk::init().context("initialize GTK for native renderer host")?;
    let options = Options::parse()?;
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
