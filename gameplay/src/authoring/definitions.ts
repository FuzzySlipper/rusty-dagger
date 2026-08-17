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

/** One actor resource track; `max` is a derived rule evaluated in Rust. */
export type TrackDefinition = Readonly<{
  id: string;
  max: Expr;
}>;

/**
 * Encounter behavior tuning consumed by the Rust patrol/AI owner. Speeds and
 * ranges are authored as world-unit floats and serialized as milli-unit
 * integers (the rules-package envelope is canonical integer-only JSON).
 * `action` references an authored action id the enemy attempts when in range.
 */
export type BehaviorDefinition = Readonly<{
  detectionRangeMilli: number;
  patrolSpeedMilli: number;
  chaseSpeedMilli: number;
  attackRangeMilli: number;
  attackCooldownMillis: number;
  action: string;
}>;

export type ActorKind = "player" | "monster";

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
  behavior?: BehaviorDefinition;
}>;

export type ActionDefinition = Readonly<{
  id: string;
  tags: readonly string[];
  program: Program;
}>;

export type WeaponDefinition = Readonly<{
  damage: Readonly<{ min: number; max: number }>;
  material: string;
  /** Skill id the weapon maps to for hit checks. */
  skill: string;
}>;

export type Interceptor = Readonly<{
  kind: "reduceDamage";
  amount: number;
}>;

export type ItemDefinition = Readonly<{
  id: string;
  weapon?: WeaponDefinition;
  interceptor?: Interceptor;
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

const toMilli = (value: number, field: string): number => {
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`behavior ${field} must be a finite non-negative number, got ${value}`);
  }
  return Math.round(value * 1000);
};

export const behavior = (
  action: string,
  values: Readonly<{
    detectionRange: number;
    patrolSpeed: number;
    chaseSpeed: number;
    attackRange: number;
    attackCooldownSeconds: number;
  }>,
): BehaviorDefinition => ({
  action,
  detectionRangeMilli: toMilli(values.detectionRange, "detectionRange"),
  patrolSpeedMilli: toMilli(values.patrolSpeed, "patrolSpeed"),
  chaseSpeedMilli: toMilli(values.chaseSpeed, "chaseSpeed"),
  attackRangeMilli: toMilli(values.attackRange, "attackRange"),
  attackCooldownMillis: toMilli(values.attackCooldownSeconds, "attackCooldownSeconds"),
});

export const actor = (
  id: string,
  definition: Readonly<{
    kind: ActorKind;
    mobileId?: number;
    stats: Readonly<Record<string, number>>;
    skills: Readonly<Record<string, number>>;
    armorValue: number;
    tracks: readonly TrackDefinition[];
    behavior?: BehaviorDefinition;
  }>,
): ActorDefinition => ({ id, ...definition });

export const action = (
  id: string,
  tags: readonly string[],
  program: Program,
): ActionDefinition => ({ id, tags, program });

export const weapon = (
  min: number,
  max: number,
  material: string,
  skill: string,
): WeaponDefinition => ({ damage: { min, max }, material, skill });

export const item = (
  id: string,
  definition: Readonly<{ weapon?: WeaponDefinition; interceptor?: Interceptor }>,
): ItemDefinition => ({ id, ...definition });

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
