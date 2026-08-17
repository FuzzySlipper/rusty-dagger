/**
 * Item definitions. The full classic weapon table at base (iron) material:
 * damage ranges and skill mappings from `FormulaHelper`
 * (CalculateWeaponMinDamage / CalculateWeaponMaxDamage — adopted).
 * Material tiers above iron modify to-hit by tier x 10 and gate damage
 * against `minMetalToHit` on monsters (errata note; tier effects are not
 * yet modeled). `weaponDice` rolls inside action programs read these ranges.
 */

import { item, weapon, type ItemDefinition } from "../authoring/mod.js";

export const items: readonly ItemDefinition[] = [
  item("iron-dagger", { weapon: weapon(1, 6, "iron", "short-blade") }),
  item("iron-tanto", { weapon: weapon(1, 8, "iron", "short-blade") }),
  item("iron-wakazashi", { weapon: weapon(1, 10, "iron", "short-blade") }),
  item("iron-shortsword", { weapon: weapon(1, 8, "iron", "long-blade") }),
  item("iron-broadsword", { weapon: weapon(1, 12, "iron", "long-blade") }),
  item("iron-saber", { weapon: weapon(3, 12, "iron", "long-blade") }),
  item("iron-katana", { weapon: weapon(3, 16, "iron", "long-blade") }),
  item("iron-dai-katana", { weapon: weapon(3, 21, "iron", "long-blade") }),
  item("iron-longsword", { weapon: weapon(2, 16, "iron", "long-blade") }),
  item("iron-claymore", { weapon: weapon(2, 18, "iron", "long-blade") }),
  item("iron-staff", { weapon: weapon(1, 8, "iron", "blunt-weapon") }),
  item("iron-mace", { weapon: weapon(1, 12, "iron", "blunt-weapon") }),
  item("iron-flail", { weapon: weapon(2, 14, "iron", "blunt-weapon") }),
  item("iron-warhammer", { weapon: weapon(3, 18, "iron", "blunt-weapon") }),
  item("iron-battle-axe", { weapon: weapon(2, 12, "iron", "axe") }),
  item("iron-war-axe", { weapon: weapon(2, 16, "iron", "axe") }),
  item("iron-short-bow", { weapon: weapon(4, 16, "iron", "archery") }),
  item("iron-long-bow", { weapon: weapon(4, 18, "iron", "archery") }),
];
