/**
 * Definition shapes for the Dagger gameplay catalogs. These are data tables
 * with light builders; the meaning of every field is owned by the Rust
 * compiler in `crates/dagger-rpg`.
 */

import type { Expr } from "./expressions.js";
import type { Program } from "./programs.js";

/** Declared vocabulary: actor stat, skill, track, armor-part, and progression ids in one place. */
export type StatsSection = Readonly<{
  attributes: readonly string[];
  skills: readonly string[];
  tracks: readonly string[];
  /**
   * Classic body parts (head, right-arm, left-arm, chest, hands, legs,
   * feet); each becomes an `armor-<part>` stat in the Rust compiler.
   */
  armorParts: readonly string[];
  /**
   * Progression stats (xp, level): wide-range counters the Rust spawn
   * authority attaches to player-kind actors only. Not actor stat-map keys.
   */
  progression: readonly string[];
}>;

/** A classic melee damage range (one sub-attack's inclusive bounds). */
export type DamageRange = Readonly<{ min: number; max: number }>;

/** One actor resource track; `max` is a derived rule evaluated in Rust. */
export type TrackDefinition = Readonly<{
  id: string;
  max: Expr;
}>;

/**
 * Encounter behavior tuning consumed by the Rust patrol/AI owner. Speeds and
 * ranges are ordinary world-unit numbers (schema-2 binary64 on the wire);
 * `action` references an authored action id the enemy attempts when in range.
 */
export type BehaviorDefinition = Readonly<{
  detectionRange: number;
  patrolSpeed: number;
  chaseSpeed: number;
  attackRange: number;
  attackCooldownSeconds: number;
  action: string;
}>;

export type ActorKind = "player" | "monster" | "enemy-class";

/** One inventory entry in an actor's spawn loadout. */
export type LoadoutEntry = Readonly<{
  /** Item definition id from the items catalog. */
  item: string;
  /** Stack size for fungible items; unique items are always one entity. */
  quantity?: number;
  /** Equipment slot id (from the equipment section) to equip into at spawn. */
  equipSlot?: string;
}>;

export type ActorDefinition = Readonly<{
  id: string;
  kind: ActorKind;
  /** Classic mobile identity owned by `arena2`; enemies join to projects by it. */
  mobileId?: number;
  stats: Readonly<Record<string, number>>;
  skills: Readonly<Record<string, number>>;
  /** Classic convention: higher armor value is EASIER to hit (signed). */
  armorValue: number;
  tracks: readonly TrackDefinition[];
  /** Player movement speed in world units/second. */
  moveSpeed?: number;
  behavior?: BehaviorDefinition;
  /** Classic errata: mobile level (drives enemy skill levels and class health). */
  level?: number;
  /** Classic errata: weight, used for knockback resistance. */
  weight?: number;
  /** Classic errata: minimum weapon material required to damage this actor. */
  minMetalToHit?: string;
  /** Classic errata: mobile team/faction id. */
  team?: string;
  /** Classic errata: loot table key used when generating drops. */
  lootTableKey?: string;
  /**
   * Experiment kill-XP profile: xp the player earns for killing this actor
   * (monsters and class enemies). Classic has no kill XP — see derived.ts.
   */
  xpReward?: number;
  /**
   * Career-owned hit-points-per-level bound (player): the level-up roll is
   * [hitPointsPerLevel/2, hitPointsPerLevel] plus the endurance modifier
   * (donor FormulaHelper.CalculateHitPointsPerLevelUp).
   */
  hitPointsPerLevel?: number;
  /**
   * Classic melee attack damage ranges (1-3 sub-attacks per swing in the
   * donor). Structured data for future multi-attack execution; today the
   * authored attack actions carry their own dice.
   */
  attacks?: readonly DamageRange[];
  /** Spawn loadout bound into upstream inventory/equipment components. */
  inventory?: readonly LoadoutEntry[];
}>;

/** A named classic formula over one subject's stats, evaluated on demand. */
export type DerivedRule = Readonly<{
  id: string;
  expr: Expr;
}>;

export type ActionDefinition = Readonly<{
  id: string;
  tags: readonly string[];
  program: Program;
  /** Melee reach in world units, when the action has one. */
  reach?: number;
  /** Cooldown between attempts in seconds. */
  cooldownSeconds?: number;
}>;

/**
 * Weapon handedness (donor `ItemEquipTable.GetItemHands` — adopted):
 * `either` hand, `both` hands (two-handed), or `leftOnly`.
 */
export type WeaponHands = "either" | "both" | "leftOnly";

export type WeaponDefinition = Readonly<{
  damage: Readonly<{ min: number; max: number }>;
  material: string;
  /** Skill id the weapon maps to for hit checks. */
  skill: string;
  hands: WeaponHands;
}>;

/** Body slot an armor piece occupies; drives its `armor-<piece>` classification. */
export type ArmorPiece =
  | "head"
  | "chest"
  | "right-arm"
  | "left-arm"
  | "legs"
  | "hands"
  | "feet";

/**
 * Armor is valued per MATERIAL, not per piece (donor
 * `DaggerfallUnityItem.GetMaterialArmorValue` — adopted): the Rust compiler
 * derives the piece's armor value from this material via the classic table.
 */
export type ArmorDefinition = Readonly<{
  material: string;
  piece: ArmorPiece;
}>;

/** Shields carry their own per-type armor value (donor `GetShieldArmorValue`). */
export type ShieldDefinition = Readonly<{ value: number }>;

/**
 * One item definition. Items with a weapon/armor/shield block are unique
 * equippable entities; items without one (gold, arrows) are fungible stacks.
 */
export type ItemDefinition = Readonly<{
  id: string;
  weapon?: WeaponDefinition;
  armor?: ArmorDefinition;
  shield?: ShieldDefinition;
  /** Weight in the classic quarter-kg unit (integer; the name carries the unit). */
  weightUnits: number;
  /** Value in gold pieces. */
  value: number;
}>;

/** One classic equipment slot; empty classifications mean unrestricted. */
export type EquipmentSlotDefinition = Readonly<{
  id: string;
  allowedClassifications: readonly string[];
}>;

/** Capacity metrics and equipment slots the package's items bind against. */
export type EquipmentSection = Readonly<{
  capacityMetrics: readonly string[];
  slots: readonly EquipmentSlotDefinition[];
}>;

export type RuleDefinition = Readonly<{
  id: string;
  kind: "rejectTagWhileCondition";
  tag: string;
  condition: string;
}>;

/** Gold roll bounds of one classic loot table (donor MinGold/MaxGold). */
export type LootGoldRange = Readonly<{ min: number; max: number }>;

/**
 * The classic loot category names in donor generation order. The names map
 * to the donor `LootChanceMatrix` fields: plant1/plant2 = P1/P2,
 * creature1..3 = C1..C3, misc1/misc2 = M1/M2 (the seven ingredient groups),
 * armor = AM, weapons = WP, magic = MI, clothing = CL, books = BK,
 * religious = RL.
 */
export const LOOT_CATEGORY_NAMES = [
  "plant1",
  "plant2",
  "creature1",
  "creature2",
  "creature3",
  "misc1",
  "misc2",
  "armor",
  "weapons",
  "magic",
  "clothing",
  "books",
  "religious",
] as const;

export type LootCategoryName = (typeof LOOT_CATEGORY_NAMES)[number];

/** Per-category integer percentage chances (0..100); omitted means 0. */
export type LootTableCategories = Readonly<Record<LootCategoryName, number>>;

/**
 * One classic loot table (donor `LootChanceMatrix` — adopted): gold bounds
 * and category chances. The Rust evaluator owns the generation semantics
 * (gold x player level; C1/C2/P1/P2 chances x level; repeated rolls at
 * halved chance).
 */
export type LootTableDefinition = Readonly<{
  /** Classic table key: `-` (default) or a single uppercase letter. */
  key: string;
  gold: LootGoldRange;
  categories: LootTableCategories;
}>;

/** Named encounter grouping; activation/routing stays with the Rust scheduler. */
export type EncounterDefinition = Readonly<{
  id: string;
  name: string;
  objective: string;
  routeCode: string;
  memberEntityIds: readonly number[];
}>;

export const statsSection = (
  attributes: readonly string[],
  skills: readonly string[],
  tracks: readonly string[],
  armorParts: readonly string[],
  progression: readonly string[],
): StatsSection => ({ attributes, skills, tracks, armorParts, progression });

export const track = (id: string, max: Expr): TrackDefinition => ({ id, max });

export const behavior = (
  action: string,
  values: Readonly<{
    detectionRange: number;
    patrolSpeed: number;
    chaseSpeed: number;
    attackRange: number;
    attackCooldownSeconds: number;
  }>,
): BehaviorDefinition => {
  for (const [field, value] of Object.entries(values)) {
    if (!Number.isFinite(value) || value < 0) {
      throw new Error(`behavior ${field} must be a finite non-negative number, got ${value}`);
    }
  }
  return { action, ...values };
};

export const actor = (
  id: string,
  definition: Readonly<{
    kind: ActorKind;
    mobileId?: number;
    stats: Readonly<Record<string, number>>;
    skills: Readonly<Record<string, number>>;
    armorValue: number;
    tracks: readonly TrackDefinition[];
    moveSpeed?: number;
    behavior?: BehaviorDefinition;
    level?: number;
    weight?: number;
    minMetalToHit?: string;
    team?: string;
    lootTableKey?: string;
    xpReward?: number;
    hitPointsPerLevel?: number;
    attacks?: readonly DamageRange[];
    inventory?: readonly LoadoutEntry[];
  }>,
): ActorDefinition => {
  for (const [field, value] of [
    ["xpReward", definition.xpReward],
    ["hitPointsPerLevel", definition.hitPointsPerLevel],
  ] as const) {
    if (value !== undefined && (!Number.isInteger(value) || value < 0)) {
      throw new Error(`actor ${id} ${field} must be a non-negative integer, got ${value}`);
    }
  }
  return { id, ...definition };
};

export const derivedRule = (id: string, expr: Expr): DerivedRule => ({ id, expr });

export const action = (
  id: string,
  tags: readonly string[],
  program: Program,
  options?: Readonly<{ reach?: number; cooldownSeconds?: number }>,
): ActionDefinition => ({
  id,
  tags,
  program,
  ...(options?.reach === undefined ? {} : { reach: options.reach }),
  ...(options?.cooldownSeconds === undefined
    ? {}
    : { cooldownSeconds: options.cooldownSeconds }),
});

export const weapon = (
  min: number,
  max: number,
  material: string,
  skill: string,
  hands: WeaponHands,
): WeaponDefinition => ({ damage: { min, max }, material, skill, hands });

export const armorPiece = (material: string, piece: ArmorPiece): ArmorDefinition => ({
  material,
  piece,
});

export const shield = (value: number): ShieldDefinition => ({ value });

export const item = (
  id: string,
  definition: Readonly<{
    weapon?: WeaponDefinition;
    armor?: ArmorDefinition;
    shield?: ShieldDefinition;
    weightUnits: number;
    value: number;
  }>,
): ItemDefinition => ({ id, ...definition });

export const equipmentSlot = (
  id: string,
  allowedClassifications: readonly string[],
): EquipmentSlotDefinition => ({ id, allowedClassifications });

export const equipmentSection = (
  capacityMetrics: readonly string[],
  slots: readonly EquipmentSlotDefinition[],
): EquipmentSection => ({ capacityMetrics, slots });

export const rule = (
  id: string,
  tag: string,
  condition: string,
): RuleDefinition => ({
  id,
  kind: "rejectTagWhileCondition",
  tag,
  condition,
});

export const lootTable = (
  key: string,
  definition: Readonly<{
    gold: LootGoldRange;
    categories: Readonly<Partial<Record<LootCategoryName, number>>>;
  }>,
): LootTableDefinition => {
  if (!/^(-|[A-Z])$/.test(key)) {
    throw new Error(`loot table key must be "-" or a single uppercase letter, got "${key}"`);
  }
  const { min, max } = definition.gold;
  if (!Number.isInteger(min) || !Number.isInteger(max) || min < 0 || min > max) {
    throw new Error(`loot table ${key} gold must satisfy 0 <= min <= max, got ${min}..${max}`);
  }
  const categories: Record<LootCategoryName, number> = {} as Record<LootCategoryName, number>;
  for (const name of LOOT_CATEGORY_NAMES) {
    const value = definition.categories[name] ?? 0;
    if (!Number.isInteger(value) || value < 0 || value > 100) {
      throw new Error(
        `loot table ${key} category ${name} must be an integer percentage 0..100, got ${value}`,
      );
    }
    categories[name] = value;
  }
  return { key, gold: { min, max }, categories };
};
export const encounter = (
  id: string,
  values: Readonly<{
    name: string;
    objective: string;
    routeCode: string;
    memberEntityIds: readonly number[];
  }>,
): EncounterDefinition => ({ id, ...values });
