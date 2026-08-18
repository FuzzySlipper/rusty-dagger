/**
 * Named classic formulas, authored once as derived rules over one subject's
 * stats and evaluated on demand in Rust. Donor provenance:
 * `Formulas/FormulaHelper.cs` basic formulas (adopted shapes).
 *
 * Errata notes (documented, not recreated):
 * - Classic applies negative endurance healing modifiers as modifier + 1
 *   (donor comment in HealingRateModifier); we use the corrected shape.
 * - The Daggerfall Chronicles hand-to-hand table misstates 45-79; classic
 *   display continues (skill / 5) + 1, which is what we author.
 * - Classic fatigue uses (strength + endurance) * 64 units; our stamina
 *   track is a deliberate simplified profile, not the classic unit scale.
 * - Spell points multiplier is class-owned (0.5x to 3.0x); authored here at
 *   the common 1.5x for reference.
 * - Rapid-healing career flags (FormulaHelper.CalculateHealthRecoveryRate,
 *   FormulaHelper.cs:178-206) and the NoRegenSpellPoints career flag
 *   (FormulaHelper.cs:219-229) gate recovery in classic; both are modeled
 *   here as 0/1 evidence facts the caller supplies (the
 *   `target-facing-away` precedent), since the flags are career+world facts.
 * - DFU's monster multi-attack reflexes chance (50 − 10*(reflexes−2),
 *   FormulaHelper.cs:654) and enemy melee-timer cadence
 *   (EnemyAttack.cs:115-131) are encounter/AI-layer formulas: expressible in
 *   this grammar, but not authored here.
 */

import {
  add,
  constant,
  derivedRule,
  divFloor,
  evidence,
  maxOf,
  mul,
  powMilli,
  skill,
  stat,
  statModifier,
  sub,
  type DerivedRule,
} from "../authoring/mod.js";

const enduranceModifier = () => statModifier("actor", "endurance", 10);

/**
 * Donor `DaggerSkills.GetAdvancementMultiplier` constants
 * (DaggerfallSkills.cs:485-547): how many uses of each skill count toward
 * advancement. Caller-side data for supplying the
 * `skill-advancement-multiplier` evidence value; not itself a rule.
 */
export const SKILL_ADVANCEMENT_MULTIPLIERS: Readonly<Record<string, number>> = {
  medical: 12,
  etiquette: 1,
  streetwise: 1,
  mercantile: 1,
  swimming: 1,
  backstabbing: 1,
  destruction: 1,
  illusion: 1,
  alteration: 1,
  mysticism: 1,
  archery: 1,
  lockpicking: 2,
  pickpocket: 2,
  stealth: 2,
  climbing: 2,
  restoration: 2,
  thaumaturgy: 2,
  "short-blade": 2,
  "long-blade": 2,
  "hand-to-hand": 2,
  axe: 2,
  "blunt-weapon": 2,
  dodging: 4,
  jumping: 5,
  "critical-strike": 8,
  orcish: 15,
  harpy: 15,
  giantish: 15,
  dragonish: 15,
  nymph: 15,
  daedric: 15,
  spriggan: 15,
  centaurian: 15,
  impish: 15,
  running: 50,
};

export const derived: readonly DerivedRule[] = [
  // FormulaHelper.DamageModifier
  derivedRule("damage-modifier", statModifier("actor", "strength", 5)),
  // FormulaHelper.ToHitModifier
  derivedRule(
    "to-hit-modifier",
    sub(divFloor(stat("actor", "agility"), constant(10)), constant(5)),
  ),
  // FormulaHelper.HitPointsModifier
  derivedRule("hit-points-modifier", enduranceModifier()),
  // FormulaHelper.HealingRateModifier
  derivedRule("healing-rate-modifier", enduranceModifier()),
  // FormulaHelper.MagicResist
  derivedRule("magic-resist", divFloor(stat("actor", "willpower"), constant(10))),
  // FormulaHelper.MaxEncumbrance
  derivedRule(
    "max-encumbrance",
    divFloor(mul(stat("actor", "strength"), constant(3)), constant(2)),
  ),
  // DaggerfallEntity.MaxBreath
  derivedRule("max-breath", divFloor(stat("actor", "endurance"), constant(2))),
  // DaggerfallEntity.MaxFatigue (classic unit scale; see header note)
  derivedRule(
    "max-fatigue",
    mul(add(stat("actor", "strength"), stat("actor", "endurance")), constant(64)),
  ),
  // FormulaHelper.SpellPoints: intelligence times the career-owned
  // multiplier (0.5x-3.0x). The multiplier is caller-supplied evidence in
  // milli (1500 = 1.5x); careers own the value per class.
  derivedRule(
    "spell-points",
    divFloor(
      mul(stat("actor", "intelligence"), evidence("spell-point-multiplier-milli")),
      constant(1000),
    ),
  ),
  // FormulaHelper.CalculateHandToHandMinDamage
  derivedRule(
    "hand-to-hand-min-damage",
    add(divFloor(skill("actor", "hand-to-hand"), constant(10)), constant(1)),
  ),
  // FormulaHelper.CalculateHandToHandMaxDamage
  derivedRule(
    "hand-to-hand-max-damage",
    add(divFloor(skill("actor", "hand-to-hand"), constant(5)), constant(1)),
  ),
  // FormulaHelper.CalculateHealthRecoveryRate (per hour of rest, base form:
  // endurance modifier + (medical + 60) * maxHealth / 1000, minimum 1; a
  // RapidHealing career flag (Always/InLight/InDarkness) raises the +60 to
  // +100 — the flag crosses as 0/1 evidence since it is a career+world fact)
  derivedRule(
    "health-recovery-rate",
    maxOf(
      constant(1),
      add(
        enduranceModifier(),
        divFloor(
          mul(
            add(
              skill("actor", "medical"),
              add(constant(60), mul(constant(40), evidence("rapid-healing-active"))),
            ),
            evidence("max-health"),
          ),
          constant(1000),
        ),
      ),
    ),
  ),
  // FormulaHelper.CalculateFatigueRecoveryRate (per hour of rest)
  derivedRule(
    "fatigue-recovery-rate",
    maxOf(constant(1), divFloor(evidence("max-fatigue"), constant(8))),
  ),
  // FormulaHelper.CalculateSpellPointRecoveryRate (per hour of rest); the
  // NoRegenSpellPoints career flag zeroes recovery, so the whole rate scales
  // by (1 - flag), the flag crossing as 0/1 evidence
  derivedRule(
    "spell-point-recovery-rate",
    mul(
      maxOf(constant(1), divFloor(evidence("max-magicka"), constant(8))),
      sub(constant(1), evidence("no-regen-spell-points")),
    ),
  ),
  // FormulaHelper.CalculateBackstabChance (full value only when the target
  // faces away; the caller supplies that world fact as 0/1 evidence)
  derivedRule(
    "backstab-chance",
    mul(skill("actor", "backstabbing"), evidence("target-facing-away")),
  ),
  // FormulaHelper.CalculatePlayerLevel: the classic skill-sum progression
  // curve (top-2 level-up skills) — floor((current - starting + 28) / 15).
  derivedRule(
    "player-level",
    divFloor(
      add(
        sub(
          evidence("current-level-up-skills-sum"),
          evidence("starting-level-up-skills-sum"),
        ),
        constant(28),
      ),
      constant(15),
    ),
  ),
  // FormulaHelper.CalculateHitPointsPerLevelUp: roll in
  // [hitPointsPerLevel/2, hitPointsPerLevel] (career-owned bounds, supplied
  // as bounded evidence) plus the endurance modifier, minimum 1.
  derivedRule(
    "hit-points-per-level-up",
    maxOf(constant(1), add(evidence("hp-level-up-roll"), enduranceModifier())),
  ),
  // PlayerEntity.RaiseSkills (PlayerEntity.cs:1377-1378): skill uses scale
  // by 1 − (reflexes−2)/8, authored in milli (1000 = 1.0x).
  derivedRule(
    "reflexes-skill-use-scale-milli",
    sub(
      constant(1000),
      mul(sub(stat("actor", "reflexes"), constant(2)), constant(125)),
    ),
  ),
  // FormulaHelper.CalculateSkillUsesForAdvancement:
  // floor(skillValue * skillMult * careerMult * 1.04^level * 2 / 5 + 1).
  // Integer form: the career multiplier crosses in centi (2-decimal) and the
  // 1.04^level factor in milli via powMilli, so the combined denominator is
  // 5 * 100 * 1000 = 500000. Golden: skill 30, mult 2, career 130, level 1
  // yields 33.
  derivedRule(
    "skill-uses-for-advancement",
    add(
      divFloor(
        mul(
          evidence("skill-value"),
          evidence("skill-advancement-multiplier"),
          evidence("career-advancement-multiplier-centi"),
          powMilli(constant(1040), evidence("level")),
          constant(2),
        ),
        constant(500000),
      ),
      constant(1),
    ),
  ),
];
