mod application;
mod diagnostics;
mod lab_server;
mod proof;
mod view;

use anyhow::{Context, Result};

fn main() -> Result<()> {
    #[cfg(target_os = "linux")]
    gtk::init().context("initialize GTK for native renderer host")?;
    application::run(proof::Options::parse()?)
}
