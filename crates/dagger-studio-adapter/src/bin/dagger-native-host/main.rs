mod application;
mod connected_application;
mod diagnostics;
mod lab_server;
mod proof;
mod view;

use anyhow::{Context, Result};

fn main() -> Result<()> {
    let options = proof::Options::parse()?;
    if options.browser_product {
        return connected_application::run(options);
    }
    #[cfg(target_os = "linux")]
    gtk::init().context("initialize GTK for native renderer host")?;
    application::run(options)
}
