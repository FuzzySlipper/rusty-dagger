//! Host-neutral Dagger gameplay semantics.
//!
//! TypeScript authors definitions in `gameplay/src`; this crate admits the
//! deterministic gameplay package, compiles its authoring grammar into the
//! Engine's structural resolution programs, and is the only evaluator of
//! Dagger expressions and the only authority for actor spawns, live stat
//! reads, and effect commits through the Engine's mechanics services.

#![forbid(unsafe_code)]

mod resolution;

pub use resolution::*;
