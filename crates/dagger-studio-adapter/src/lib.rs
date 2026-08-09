//! Dagger-owned Studio and native presentation boundaries.

#![forbid(unsafe_code)]

mod adapter;

pub use adapter::{build_render_bundle, run_stdio, DaggerRenderBundle, DaggerRenderResource};
