//! Dagger-owned Studio and native presentation boundaries.

#![forbid(unsafe_code)]

mod presentation;
mod project_access;
mod protocol;
mod readout;

pub use presentation::{build_render_bundle, DaggerRenderBundle, DaggerRenderResource};
pub use protocol::run_stdio;
