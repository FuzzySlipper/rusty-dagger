use rusty_engine::{
    render_host_contracts::{
        RendererCameraPose, RendererCameraProjection, RendererCompositionCamera,
        RendererCompositionTarget, RendererCompositionView, RendererTargetColor,
        RendererTargetDepth, RendererTargetSampling, RendererViewComposition, RendererViewTarget,
        RendererViewport, RENDERER_VIEW_COMPOSITION_SCHEMA_VERSION,
    },
    renderer_webview_host::RendererWebviewBounds,
};
use winit::window::Window;

pub(crate) fn dagger_views(
    pose: RendererCameraPose,
    target_revision: u64,
) -> RendererViewComposition {
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
        views: vec![RendererCompositionView {
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
        }],
        presentations: Vec::new(),
    }
}

pub(crate) fn window_bounds(window: &Window) -> RendererWebviewBounds {
    let size = window.inner_size();
    let scale = window.scale_factor();
    RendererWebviewBounds {
        x: 0,
        y: 0,
        width: ((f64::from(size.width) / scale).round() as u32).max(1),
        height: ((f64::from(size.height) / scale).round() as u32).max(1),
    }
}
