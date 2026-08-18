/**
 * Action definitions: everything an actor can attempt, expressed as
 * resolution programs. Player, AI, and diagnostics all resolve these through
 * the same Rust policy path.
 *
 * Hit checks follow the fuller classic shape (donor: FormulaHelper
 * CalculateSuccessfulHit / CalculateAttackDamage — adapted): d100 against
 * skill + target armor vulnerability + the classic -50 adjustment + the
 * general CalculateStatsToHit terms (attacker/target luck and agility
 * differentials, /10 each) + the CalculateSkillsToHit dodging penalty,
 * clamped to 3..97. Player melee additionally carries the player-only
 * classic terms: swing state, expert-proficiency and racial weapon bonuses,
 * and the adrenaline rush. Career facts (proficiency, racial, swing state)
 * arrive as bounded named evidence — 0 until careers and swing states are
 * modeled. Nothing here rolls its own dice.
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
  divFloor,
  divTrunc,
  evidence,
  maxOf,
  minOf,
  mul,
  operation,
  sequence,
  skill,
  spendTrack,
  stat,
  statModifier,
  sub,
  trackCurrent,
  trackMax,
  weaponDice,
  when,
  type ActionDefinition,
  type Expr,
  type Predicate,
} from "../authoring/mod.js";

/**
 * Shared classic melee hit check: roll ≤ clamp(skill + target armor − 50 +
 * general terms, 3, 97). General terms (CalculateStatsToHit /
 * CalculateSkillsToHit): luck and agility differentials /10 with the
 * donor's C# truncating integer division, and
 * − target dodging / 4 (donor comment: classic was bugged to read the
 * attacker's dodging; the corrected shape reads the target's).
 */
const meleeHit = (
  rollEvidence: string,
  attackSkill: string,
  ...extraTerms: readonly Expr[]
): Predicate =>
  cmp(
    "lte",
    evidence(rollEvidence),
    clampedChance(
      skill("actor", attackSkill),
      armor("target"),
      constant(-50),
      divTrunc(sub(stat("actor", "luck"), stat("target", "luck")), constant(10)),
      divTrunc(sub(stat("actor", "agility"), stat("target", "agility")), constant(10)),
      mul(divFloor(skill("target", "dodging"), constant(4)), constant(-1)),
      ...extraTerms,
    ),
  );

/**
 * 1 while the subject's current health is below max/8, else 0 (the
 * classic adrenaline-rush condition).
 */
const lowHealth = (subject: "actor" | "target"): Expr =>
  minOf(
    constant(1),
    maxOf(
      constant(0),
      sub(
        divFloor(trackMax(subject, "health"), constant(8)),
        trackCurrent(subject, "health"),
      ),
    ),
  );

/**
 * Player-only classic to-hit terms, all bounded named evidence except the
 * low-health condition: swing state (CalculateSwingModifiers: StrikeUp +10
 * … StrikeDown −10; 0 until swing states exist), expert-proficiency
 * careers adding attacker level (CalculateProficiencyModifiers; 0 until
 * careers exist), racial weapon bonuses (CalculateRacialModifiers: Dark
 * Elf +level/4, Wood Elf archery +level/3, Redguard non-bow +level/3; 0
 * until careers exist), and the adrenaline rush
 * (CalculateAdrenalineRushToHit): +5 for the attacker / −5 for the target
 * while that side's current health is below max/8, each gated on that
 * side's career flag as bounded 0/1 evidence — 0 until careers exist. The
 * improved +8 magnitude is not modeled (recorded deviation).
 */
const playerToHitTerms = (actionId: string): readonly Expr[] => [
  dice(`${actionId}.swing-to-hit`, -10, 10),
  dice(`${actionId}.proficiency-to-hit`, 0, 30),
  dice(`${actionId}.racial-to-hit`, 0, 30),
  mul(
    constant(5),
    dice(`${actionId}.adrenaline-rush`, 0, 1),
    lowHealth("actor"),
  ),
  mul(
    constant(-5),
    dice(`${actionId}.target-adrenaline-rush`, 0, 1),
    lowHealth("target"),
  ),
];

/**
 * Player-only classic damage terms (same donor functions as the to-hit
 * side): expert-proficiency and racial damage bonuses as bounded named
 * evidence, 0 until careers exist.
 */
const playerDamageTerms = (actionId: string): readonly Expr[] => [
  dice(`${actionId}.proficiency-damage`, 0, 30),
  dice(`${actionId}.racial-damage`, 0, 30),
];

export const actions: readonly ActionDefinition[] = [
  action(
    "melee-attack",
    ["attack", "melee"],
    sequence(
      operation(spendTrack("stamina", constant(5))),
      when(
        meleeHit("melee-attack.d100", "long-blade", ...playerToHitTerms("melee-attack")),
        operation(
          damage(
            add(
              weaponDice("iron-longsword"),
              statModifier("actor", "strength", 5),
              ...playerDamageTerms("melee-attack"),
            ),
          ),
        ),
      ),
    ),
    { reach: 2.25, cooldownSeconds: 0.75 },
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

  // Approximate pending equipment modeling: the class thief fights with a
  // low-tier blade rather than its classic equipment roll.
  action(
    "thief-strike",
    ["attack", "melee"],
    when(
      meleeHit("thief-strike.d100", "short-blade"),
      operation(damage(dice("thief-strike.damage", 2, 8))),
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
        meleeHit("power-attack.d100", "long-blade", ...playerToHitTerms("power-attack")),
        operation(
          damage(
            add(
              weaponDice("iron-longsword"),
              statModifier("actor", "strength", 5),
              constant(4),
              ...playerDamageTerms("power-attack"),
            ),
          ),
        ),
      ),
    ),
    { reach: 2.25, cooldownSeconds: 1.2 },
  ),
];
