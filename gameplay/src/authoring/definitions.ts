/**
 * Definition shapes for the Dagger gameplay catalogs. These are data tables
 * with light builders; the meaning of every field is owned by the Rust
 * compiler in `crates/dagger-rpg`.
 */

import type { Expr } from "./expressions.js";
import type { Program } from "./programs.js";

/** Declared vocabulary: actor stat, skill, and track ids in one place. */
export type StatsSection = Readonly<{
  attributes: readonly string[];
  skills: readonly string[];
  tracks: readonly string[];
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
): StatsSection => ({ attributes, skills, tracks });

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
    attacks?: readonly DamageRange[];
    inventory?: readonly LoadoutEntry[];
  }>,
): ActorDefinition => ({ id, ...definition });

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

export const encounter = (
  id: string,
  values: Readonly<{
    name: string;
    objective: string;
    routeCode: string;
    memberEntityIds: readonly number[];
  }>,
): EncounterDefinition => ({ id, ...values });
