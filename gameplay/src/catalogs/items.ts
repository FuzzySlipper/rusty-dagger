/**
 * Item definitions. The full classic weapon table at base (iron) material:
 * damage ranges, handedness, and skill mappings from the donor
 * (`FormulaHelper.CalculateWeaponMinDamage/MaxDamage`,
 * `DaggerfallUnityItem.GetWeaponSkillUsed`, `ItemEquipTable.GetItemHands` —
 * adopted). Weights and prices are the exact FALL.EXE template values the
 * donor commits as `Assets/Resources/ItemTemplates.txt` (quarter-kg weight
 * units, gold pieces).
 *
 * Material tiers above iron modify to-hit by tier x 10 and gate damage
 * against `minMetalToHit` on monsters (errata note; tier to-hit effects are
 * not yet modeled — the material gate is Rust policy, see
 * `docs/gameplay-resolution.md`). `equippedWeaponDice` rolls inside action
 * programs read these ranges through the acting subject's live equipment.
 * This catalog stays at the iron tier plus gold/arrows; other materials are
 * loot-campaign content.
 */

import {
  armorPiece,
  item,
  shield,
  weapon,
  type ItemDefinition,
} from "../authoring/mod.js";

/**
 * Classic per-material armor values (donor
 * `DaggerfallUnityItem.GetMaterialArmorValue` — adopted). Armor value is a
 * property of the material, not the piece. Rust owns the same table at
 * compile time; this record is authoring-side reference data.
 */
export const ARMOR_VALUES_BY_MATERIAL: Readonly<Record<string, number>> = {
  leather: 3,
  chain: 6,
  iron: 7,
  steel: 9,
  silver: 9,
  elven: 11,
  dwarven: 13,
  mithril: 15,
  adamantium: 15,
  ebony: 17,
  orcish: 19,
  daedric: 21,
};

export const items: readonly ItemDefinition[] = [
  // One-handed blades and blunts ("either" hand in the donor).
  item("iron-dagger", {
    weapon: weapon(1, 6, "iron", "short-blade", "either"),
    weightUnits: 2, // 0.5 kg
    value: 1,
  }),
  item("iron-tanto", {
    weapon: weapon(1, 8, "iron", "short-blade", "either"),
    weightUnits: 3, // 0.75 kg
    value: 3,
  }),
  // Donor template 117 spells it "Wakizashi"; the classic/DFU weapon enum
  // name Wakazashi stays as our id.
  item("iron-wakazashi", {
    weapon: weapon(1, 10, "iron", "short-blade", "either"),
    weightUnits: 8, // 2.0 kg
    value: 8,
  }),
  item("iron-shortsword", {
    weapon: weapon(1, 8, "iron", "short-blade", "either"),
    weightUnits: 10, // 2.5 kg
    value: 5,
  }),
  item("iron-broadsword", {
    weapon: weapon(1, 12, "iron", "long-blade", "either"),
    weightUnits: 20, // 5.0 kg
    value: 10,
  }),
  item("iron-saber", {
    weapon: weapon(3, 12, "iron", "long-blade", "either"),
    weightUnits: 15, // 3.75 kg
    value: 12,
  }),
  item("iron-katana", {
    weapon: weapon(3, 16, "iron", "long-blade", "either"),
    weightUnits: 10, // 2.5 kg
    value: 25,
  }),
  item("iron-longsword", {
    weapon: weapon(2, 16, "iron", "long-blade", "either"),
    weightUnits: 18, // 4.5 kg
    value: 15,
  }),
  item("iron-mace", {
    weapon: weapon(1, 12, "iron", "blunt-weapon", "either"),
    weightUnits: 18, // 4.5 kg
    value: 10,
  }),
  item("iron-battle-axe", {
    weapon: weapon(2, 12, "iron", "axe", "either"),
    weightUnits: 24, // 6.0 kg
    value: 20,
  }),

  // Two-handed weapons.
  item("iron-claymore", {
    weapon: weapon(2, 18, "iron", "long-blade", "both"),
    weightUnits: 30, // 7.5 kg
    value: 30,
  }),
  item("iron-dai-katana", {
    weapon: weapon(3, 21, "iron", "long-blade", "both"),
    weightUnits: 14, // 3.5 kg
    value: 50,
  }),
  item("iron-staff", {
    weapon: weapon(1, 8, "iron", "blunt-weapon", "both"),
    weightUnits: 8, // 2.0 kg
    value: 5,
  }),
  item("iron-flail", {
    weapon: weapon(2, 14, "iron", "blunt-weapon", "both"),
    weightUnits: 28, // 7.0 kg
    value: 15,
  }),
  item("iron-warhammer", {
    weapon: weapon(3, 18, "iron", "blunt-weapon", "both"),
    weightUnits: 28, // 7.0 kg
    value: 20,
  }),
  item("iron-war-axe", {
    weapon: weapon(2, 16, "iron", "axe", "both"),
    weightUnits: 30, // 7.5 kg
    value: 20,
  }),
  item("iron-short-bow", {
    weapon: weapon(4, 16, "iron", "archery", "both"),
    weightUnits: 4, // 1.0 kg
    value: 10,
  }),
  item("iron-long-bow", {
    weapon: weapon(4, 18, "iron", "archery", "both"),
    weightUnits: 6, // 1.5 kg
    value: 20,
  }),

  // Iron armor set. Armor value is per-material (iron 7 via
  // ARMOR_VALUES_BY_MATERIAL), derived by the Rust compiler.
  item("iron-helm", { armor: armorPiece("iron", "head"), weightUnits: 10, value: 80 }),
  item("iron-cuirass", { armor: armorPiece("iron", "chest"), weightUnits: 50, value: 100 }),
  item("iron-right-pauldron", {
    armor: armorPiece("iron", "right-arm"),
    weightUnits: 8,
    value: 60,
  }),
  item("iron-left-pauldron", {
    armor: armorPiece("iron", "left-arm"),
    weightUnits: 8,
    value: 60,
  }),
  item("iron-gauntlets", { armor: armorPiece("iron", "hands"), weightUnits: 5, value: 50 }),
  item("iron-greaves", { armor: armorPiece("iron", "legs"), weightUnits: 10, value: 80 }),
  item("iron-boots", { armor: armorPiece("iron", "feet"), weightUnits: 8, value: 50 }),

  // Shields: left-hand only in classic, per-type armor value (donor
  // GetShieldArmorValue).
  item("buckler", { shield: shield(1), weightUnits: 8, value: 10 }),
  item("round-shield", { shield: shield(2), weightUnits: 18, value: 20 }),
  item("kite-shield", { shield: shield(3), weightUnits: 30, value: 30 }),
  item("tower-shield", { shield: shield(4), weightUnits: 50, value: 50 }),

  // Fungible stacks. A gold piece weighs 1/400 kg — below the quarter-kg
  // unit resolution, so its weight is authored as 0 units.
  item("gold-piece", { weightUnits: 0, value: 1 }),
  // Donor: arrows are stackable and not equippable; damage comes from the bow.
  item("arrow", { weightUnits: 1, value: 2 }),
];
