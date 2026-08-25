use dagger_runtime::DaggerRuntime;
use rusty_engine::render_model::RenderDiff;

/// The sole Dagger candidate/live state. `RuntimeMutation` publishes only a
/// complete clone staged through Dagger's named semantic methods.
#[derive(Debug, Clone)]
pub struct DaggerProductAuthority {
    pub runtime: DaggerRuntime,
    pub revision: u64,
    /// Immutable retained scene definitions derived once from admitted project
    /// bytes. They are presentation facts, never a second gameplay state.
    pub static_scene_ops: Vec<RenderDiff>,
}

impl DaggerProductAuthority {
    pub fn new(runtime: DaggerRuntime, static_scene_ops: Vec<RenderDiff>) -> Self {
        Self {
            runtime,
            revision: 0,
            static_scene_ops,
        }
    }
}
