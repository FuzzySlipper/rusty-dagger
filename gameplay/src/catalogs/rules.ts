/**
 * Rule definitions: global constraints evaluated during action checks.
 * Conditions are applied by future gameplay (spell effects); rules reference
 * them by id ahead of time so constrained actions are authored once.
 */

import { rule, type RuleDefinition } from "../authoring/mod.js";

export const rules: readonly RuleDefinition[] = [
  // A silenced actor cannot attempt spell-tagged actions.
  rule("silence", "spell", "silenced"),
];
