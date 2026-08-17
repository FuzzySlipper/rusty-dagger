/**
 * Action definitions: everything an actor can attempt, expressed as
 * resolution programs. Player, AI, and diagnostics all resolve these through
 * the same Rust policy path.
 *
 * Hit checks follow the classic shape (donor: FormulaHelper
 * CalculateSuccessfulHit — adapted): d100 against skill + target armor
 * vulnerability + the classic -50 adjustment, clamped to 3..97. All rolls
 * are bounded named evidence supplied by the caller; nothing here rolls its
 * own dice.
 */

import {
  action,
  add,
  armor,
  clampedChance,
  cmp,
  constant,
  damage,
  dice,
  evidence,
  operation,
  sequence,
  skill,
  spendTrack,
  statModifier,
  weaponDice,
  when,
  type ActionDefinition,
  type Predicate,
} from "../authoring/mod.js";

/** Classic melee hit check: roll ≤ clamp(skill + target armor − 50, 3, 97). */
const meleeHit = (rollEvidence: string, attackSkill: string): Predicate =>
  cmp(
    "lte",
    evidence(rollEvidence),
    clampedChance(skill("actor", attackSkill), armor("target"), constant(-50)),
  );

export const actions: readonly ActionDefinition[] = [
  action(
    "melee-attack",
    ["attack", "melee"],
    sequence(
      operation(spendTrack("stamina", constant(10))),
      when(
        meleeHit("melee-attack.d100", "long-blade"),
        operation(
          damage(
            add(
              weaponDice("iron-longsword"),
              statModifier("actor", "strength", 5),
            ),
          ),
        ),
      ),
    ),
  ),

  action(
    "rat-bite",
    ["attack", "melee"],
    when(
      meleeHit("rat-bite.d100", "hand-to-hand"),
      operation(damage(dice("rat-bite.damage", 1, 4))),
    ),
  ),

  action(
    "skeleton-strike",
    ["attack", "melee"],
    when(
      meleeHit("skeleton-strike.d100", "long-blade"),
      operation(damage(dice("skeleton-strike.damage", 5, 15))),
    ),
  ),

  // A slower, heavier swing for pacing experiments: higher stamina cost and
  // a flat damage bonus over the standard melee attack.
  action(
    "power-attack",
    ["attack", "melee"],
    sequence(
      operation(spendTrack("stamina", constant(25))),
      when(
        meleeHit("power-attack.d100", "long-blade"),
        operation(
          damage(
            add(
              weaponDice("iron-longsword"),
              statModifier("actor", "strength", 5),
              constant(4),
            ),
          ),
        ),
      ),
    ),
  ),
];
