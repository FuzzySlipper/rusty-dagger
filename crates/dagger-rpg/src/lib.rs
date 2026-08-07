//! dagger-rpg — data-driven Daggerfall RPG authority for Privateer's Hold.
//!
//! Phase 0 (6683) is shape only: no gameplay logic. This crate will own
//! committed tables (JSON under `data/`) and pure formulas in one place so
//! later phases (6684 stats, 6685 combat, 6686 inventory, 6687 enemies,
//! 6688 progression) can be tweaked/ported without inline magic numbers.
//!
//! Design constraints (see docs/design.md + docs/companion-reuse.md):
//! - `arena2` stays pure parsing; this crate adds meaning (formulas, tables).
//! - `dagger-runtime` will *use* this crate for live session state; it does
//!   not own the numbers.
//! - Tables are committed JSON under `data/` (not generated `content/`).
//! - Companion reuse is copy-don't-import: patterns from demo/d20/roguelike
//!   may be copied with provenance notes, never as path/git deps.

/// Hello-world probe for the empty-crate gate (6683). Replace with real tables.
pub fn hello() -> &'static str {
    "dagger-rpg"
}

/// Placeholder version marker; real tables will expose typed structs + pure fns.
pub const DATA_SCHEMA_VERSION: u32 = 0;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hello_world() {
        assert_eq!(hello(), "dagger-rpg");
        assert_eq!(DATA_SCHEMA_VERSION, 0);
    }

    #[test]
    fn data_dir_convention() {
        // Phase 0: no data files yet, but the convention is documented.
        // When `data/monsters.json` etc. land, this crate will load them via
        // serde_json at test time and validate against DFU-known values.
        assert!(hello().starts_with("dagger"));
    }
}
