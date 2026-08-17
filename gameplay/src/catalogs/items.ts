/**
 * Item definitions. Weapons carry their classic damage range, material, and
 * mapped skill; `weaponDice` rolls inside action programs read these ranges.
 *
 * Donor provenance: FormulaHelper CalculateWeaponMin/MaxDamage — Longsword
 * 2-16 (adapted; iron material is the classic base tier with no to-hit
 * modifier).
 */

import { item, weapon, type ItemDefinition } from "../authoring/mod.js";

export const items: readonly ItemDefinition[] = [
  item("iron-longsword", {
    weapon: weapon(2, 16, "iron", "long-blade"),
  }),
];
