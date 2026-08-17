/**
 * The single import surface for gameplay catalogs. Catalog files must import
 * from this module only — extending the grammar means editing `authoring/`
 * and the Rust compiler in `crates/dagger-rpg` in the same change.
 */

export * from "./expressions.js";
export * from "./programs.js";
export * from "./definitions.js";
export * from "./envelope.js";
