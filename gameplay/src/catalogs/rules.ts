/**
 * Rule definitions: global constraints evaluated during action checks
 * (tag + condition rejection). Empty until gameplay introduces conditions —
 * the grammar is admitted and exercised by the Rust resolution tests, which
 * inject rules into the package rather than carrying inert content here.
 */

import type { RuleDefinition } from "../authoring/mod.js";

export const rules: readonly RuleDefinition[] = [];
