/**
 * Derived-value expressions: the closed grammar for every computed number in
 * Dagger gameplay (hit chances, damage, track maximums, costs).
 *
 * TypeScript only describes these trees. Rust admits, validates, and is the
 * only evaluator. There is no arithmetic in this file beyond literal
 * construction.
 *
 * Rolls: `dice` and `weaponDice` are bounded named rolls. The evaluator never
 * generates randomness; the caller (runtime, diagnostic) supplies roll
 * evidence and admission declares the bounds. `dice("rat-bite.damage", 1, 4)`
 * reads evidence id "rat-bite.damage" and rejects values outside [1, 4].
 * `weaponDice("iron-longsword")` reads evidence id
 * `weapon-damage.iron-longsword` bounded by the item's declared damage range.
 * Deterministic replay is therefore just "supply the same evidence".
 */

export type Subject = "actor" | "target";

export type Expr =
  | Readonly<{ kind: "const"; value: number }>
  | Readonly<{ kind: "stat"; subject: Subject; id: string }>
  | Readonly<{ kind: "skill"; subject: Subject; id: string }>
  | Readonly<{ kind: "armor"; subject: Subject }>
  | Readonly<{ kind: "evidence"; id: string }>
  | Readonly<{ kind: "dice"; id: string; min: number; max: number }>
  | Readonly<{ kind: "weaponDice"; item: string }>
  | Readonly<{ kind: "track"; subject: Subject; id: string }>
  | Readonly<{ kind: "trackMax"; subject: Subject; id: string }>
  | Readonly<{ kind: "powMilli"; base: Expr; exponent: Expr }>
  | Readonly<{ kind: "add"; terms: readonly Expr[] }>
  | Readonly<{ kind: "sub"; left: Expr; right: Expr }>
  | Readonly<{ kind: "mul"; terms: readonly Expr[] }>
  | Readonly<{ kind: "divFloor"; left: Expr; right: Expr }>
  | Readonly<{ kind: "min"; terms: readonly Expr[] }>
  | Readonly<{ kind: "max"; terms: readonly Expr[] }>;

export const constant = (value: number): Expr => ({ kind: "const", value });

export const stat = (subject: Subject, id: string): Expr => ({
  kind: "stat",
  subject,
  id,
});

export const skill = (subject: Subject, id: string): Expr => ({
  kind: "skill",
  subject,
  id,
});

/** Classic armor convention: higher armor value is EASIER to hit. */
export const armor = (subject: Subject): Expr => ({ kind: "armor", subject });

/** Read one caller-supplied evidence value by exact id. */
export const evidence = (id: string): Expr => ({ kind: "evidence", id });

/** Bounded named roll; the caller supplies the value as evidence. */
export const dice = (id: string, min: number, max: number): Expr => ({
  kind: "dice",
  id,
  min,
  max,
});

/** Bounded roll over a weapon item's declared damage range. */
export const weaponDice = (item: string): Expr => ({ kind: "weaponDice", item });

/** Current value of one of the subject's resource tracks. */
export const trackCurrent = (subject: Subject, id: string): Expr => ({
  kind: "track",
  subject,
  id,
});

/** Spawn-derived maximum of one of the subject's tracks (its `{id}-max` stat). */
export const trackMax = (subject: Subject, id: string): Expr => ({
  kind: "trackMax",
  subject,
  id,
});

/**
 * Fixed-point power: `base^exponent` scaled by 1000 (milli), computed
 * iteratively with floor division at each step — the deterministic integer
 * approximation of the donor's f64 pow (e.g. 1.04^level is
 * `powMilli(constant(1040), evidence("level"))`).
 */
export const powMilli = (base: Expr, exponent: Expr): Expr => ({
  kind: "powMilli",
  base,
  exponent,
});

export const add = (...terms: readonly Expr[]): Expr => ({
  kind: "add",
  terms,
});

export const sub = (left: Expr, right: Expr): Expr => ({
  kind: "sub",
  left,
  right,
});

export const mul = (...terms: readonly Expr[]): Expr => ({
  kind: "mul",
  terms,
});

export const divFloor = (left: Expr, right: Expr): Expr => ({
  kind: "divFloor",
  left,
  right,
});

export const minOf = (...terms: readonly Expr[]): Expr => ({
  kind: "min",
  terms,
});

export const maxOf = (...terms: readonly Expr[]): Expr => ({
  kind: "max",
  terms,
});

/** Classic chance-to-hit shape: modifiers summed, clamped to 3..97. */
export const clampedChance = (...modifiers: readonly Expr[]): Expr =>
  minOf(constant(97), maxOf(constant(3), add(...modifiers)));

/** Classic stat modifier shape: floor((stat - 50) / divisor). */
export const statModifier = (
  subject: Subject,
  id: string,
  divisor: number,
): Expr => divFloor(sub(stat(subject, id), constant(50)), constant(divisor));
