use std::env;

use anyhow::{bail, Result};

#[derive(Debug, Clone, Copy)]
pub(crate) struct Options {
    pub(crate) proof: bool,
    pub(crate) corrupt_resource: bool,
}

impl Options {
    pub(crate) fn parse() -> Result<Self> {
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
pub(crate) struct Proof {
    pub(crate) frame: bool,
    pub(crate) views: bool,
    pub(crate) camera: bool,
    pub(crate) resize: bool,
    pub(crate) resources: bool,
    pub(crate) input_authority: bool,
    pub(crate) input_noop: bool,
    pub(crate) pick_authority: bool,
    pub(crate) pick_miss: bool,
    pub(crate) state: bool,
    pub(crate) render: bool,
    pub(crate) diagnostics_enabled: bool,
    pub(crate) diagnostics_disabled: bool,
    pub(crate) animation_advanced: bool,
    pub(crate) patrol_moved: bool,
    pub(crate) stale_handle_replaced: bool,
    pub(crate) diagnostics_disposed: bool,
    pub(crate) max_animation_updates: usize,
    pub(crate) max_retained_overlays: usize,
}

impl Proof {
    pub(crate) fn complete(&self) -> bool {
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
            && self.diagnostics_enabled
            && self.diagnostics_disabled
            && self.animation_advanced
            && self.patrol_moved
            && self.stale_handle_replaced
    }
}

#[derive(Debug, Clone, Copy)]
pub(crate) enum PickKind {
    Miss,
    Dungeon,
}

#[derive(Debug, Clone, Copy)]
pub(crate) struct PendingPick {
    pub(crate) request_id: u64,
    pub(crate) kind: PickKind,
    pub(crate) state_before: dagger_runtime::PlayerControllerState,
}
