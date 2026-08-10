use std::{env, net::IpAddr};

use anyhow::{bail, Result};

#[derive(Debug, Clone, Copy)]
pub(crate) struct Options {
    pub(crate) browser_product: bool,
    pub(crate) proof: bool,
    pub(crate) corrupt_resource: bool,
    pub(crate) lab_host: IpAddr,
    pub(crate) lab_port: Option<u16>,
}

impl Options {
    pub(crate) fn parse() -> Result<Self> {
        let mut proof = false;
        let mut browser_product = false;
        let mut corrupt_resource = false;
        let mut requested_lab_port = None;
        let mut lab_host = IpAddr::V4(std::net::Ipv4Addr::LOCALHOST);
        let mut no_lab = false;
        for argument in env::args().skip(1) {
            match argument.as_str() {
                "--browser-product" => browser_product = true,
                "--proof" => proof = true,
                "--proof-corrupt-resource" => {
                    proof = true;
                    corrupt_resource = true;
                }
                "--no-lab" => no_lab = true,
                value if value.starts_with("--lab-port=") => {
                    requested_lab_port = Some(
                        value["--lab-port=".len()..]
                            .parse::<u16>()
                            .map_err(|_| anyhow::anyhow!("invalid lab port in {value}"))?,
                    );
                }
                value if value.starts_with("--lab-host=") => {
                    lab_host = value["--lab-host=".len()..]
                        .parse::<IpAddr>()
                        .map_err(|_| anyhow::anyhow!("invalid Lab bind address in {value}"))?;
                }
                _ => bail!("unknown argument {argument}"),
            }
        }
        if !browser_product && requested_lab_port.is_some() {
            bail!("--lab-port requires --browser-product");
        }
        let lab_port = if no_lab {
            None
        } else if browser_product {
            Some(requested_lab_port.unwrap_or(4274))
        } else {
            None
        };
        Ok(Self {
            browser_product,
            proof,
            corrupt_resource,
            lab_host,
            lab_port,
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
