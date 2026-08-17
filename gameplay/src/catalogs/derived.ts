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
 * - Rapid-healing career flags and the NoRegenSpellPoints career flag gate
 *   recovery in classic; those flags are not yet modeled (recorded as a
 *   grammar gap).
 */

import {
  add,
  constant,
  derivedRule,
  divFloor,
  evidence,
  maxOf,
  mul,
  skill,
  stat,
  statModifier,
  sub,
  type DerivedRule,
} from "../authoring/mod.js";

const enduranceModifier = () => statModifier("actor", "endurance", 10);

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
  // FormulaHelper.SpellPoints at the common 1.5x class multiplier
  derivedRule(
    "spell-points",
    divFloor(mul(stat("actor", "intelligence"), constant(3)), constant(2)),
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
  // endurance modifier + (medical + 60) * maxHealth / 1000, minimum 1;
  // rapid-healing flags raise the +60 to +100 and are a recorded gap)
  derivedRule(
    "health-recovery-rate",
    maxOf(
      constant(1),
      add(
        enduranceModifier(),
        divFloor(
          mul(
            add(skill("actor", "medical"), constant(60)),
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
  // FormulaHelper.CalculateSpellPointRecoveryRate (per hour of rest)
  derivedRule(
    "spell-point-recovery-rate",
    maxOf(constant(1), divFloor(evidence("max-magicka"), constant(8))),
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
  // FormulaHelper.CalculateSkillUsesForAdvancement: uses required to raise a
  // skill — floor(skillValue * skillMult * careerMult * 2 / 5 + 1), where
  // the multipliers are career-owned evidence. GRAMMAR GAP: classic also
  // scales by 1.04^level; the expression grammar has no power node, so the
  // level factor is not yet expressible.
  derivedRule(
    "skill-uses-for-advancement",
    add(
      divFloor(
        mul(
          evidence("skill-value"),
          evidence("skill-advancement-multiplier"),
          evidence("career-advancement-multiplier"),
          constant(2),
        ),
        constant(5),
      ),
      constant(1),
    ),
  ),
];
