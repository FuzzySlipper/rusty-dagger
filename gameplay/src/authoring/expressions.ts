/**
 * Derived-value expressions: the closed grammar for every computed number in
 * Dagger gameplay (hit chances, damage, track maximums, costs).
 *
 * TypeScript only describes these trees. Rust admits, validates, and is the
 * only evaluator. There is no arithmetic in this file beyond literal
 * construction.
 *
 * Rolls: Engine `boundedRoll`, `equippedWeaponDice`, and `struckArmor` are bounded named
 * rolls. The evaluator never generates randomness; the caller (runtime,
 * diagnostic) supplies roll evidence and admission declares the bounds.
 * `boundedRoll("rat-bite.damage", 1, 4)` reads evidence id "rat-bite.damage" and
 * rejects values outside [1, 4]. `equippedWeaponDice("actor",
 * "melee-attack.equipped-weapon-damage")` reads that explicit evidence id,
 * bounded at evaluation by the subject's CURRENTLY equipped weapon's damage
 * range (unarmed: the derived hand-to-hand range). `struckArmor("target",
 * "melee-attack.struck-body-part")` reads a 0..19 struck-body-part roll and
 * maps it through the classic table to the target's `armor-<part>` stat.
 * Focused evaluation is repeatable when the caller supplies the same evidence.
 */

import {
  composeEmbeddedComposedExactDefinition,
  declareComposedExactProductCodec,
} from "@rusty-engine/gameplay-standard-authoring";
import {
  assertComposedExactPayload,
  decodeComposedExactPayload,
  type ComposedExactTree,
  type ComposedExactDefinitionPayload,
  type JsonValue,
} from "@rusty-engine/gameplay-standard-contracts";

export type Subject = "actor" | "target";

/**
 * Dagger owns only these typed product leaves.  All literal, input, and
 * arithmetic nodes are the Engine-generated composedExact grammar.
 */
export type DaggerExprLeaf =
  | Readonly<{ kind: "equipped-weapon-skill"; payload: Readonly<{ subject: Subject }> }>
  | Readonly<{ kind: "equipped-weapon-dice"; payload: Readonly<{ subject: Subject; id: string }> }>
  | Readonly<{ kind: "struck-armor"; payload: Readonly<{ subject: Subject; id: string }> }>;

/**
 * The strict codec receives only a product payload, not its enclosing tree
 * kind. Carry the product kind inside the payload as a checked discriminator
 * so TS can independently validate the exact leaf shape Rust will admit.
 */
export type DaggerExprPayload =
  | Readonly<{ kind: "equipped-weapon-skill"; value: Readonly<{ subject: Subject }> }>
  | Readonly<{ kind: "equipped-weapon-dice"; value: Readonly<{ subject: Subject; id: string }> }>
  | Readonly<{ kind: "struck-armor"; value: Readonly<{ subject: Subject; id: string }> }>;

export type Expr = ComposedExactTree<DaggerExprPayload>;
/** A generated composedExact definition embedded in Dagger's schema-2 aggregate. */
export type EmbeddedExpr = ComposedExactDefinitionPayload<DaggerExprPayload>;

const role = (subject: Subject): Subject => subject;
const record = (value: unknown): Record<string, unknown> => {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error("Dagger product expression payload must be an object");
  }
  return value as Record<string, unknown>;
};

const exactFields = (value: Record<string, unknown>, fields: readonly string[]): void => {
  const keys = Object.keys(value).sort();
  if (keys.length !== fields.length || keys.some((key, index) => key !== fields[index])) {
    throw new Error("Dagger product expression payload has unknown or missing fields");
  }
};

const subject = (value: unknown): Subject => {
  if (value !== "actor" && value !== "target") {
    throw new Error("Dagger product expression payload subject must be actor or target");
  }
  return value;
};

const id = (value: unknown): string => {
  if (
    typeof value !== "string"
    || new TextEncoder().encode(value).byteLength > 96
    || !/^[a-z][a-z0-9._-]*$/.test(value)
  ) {
    throw new Error("Dagger product expression payload id must use the Dagger lowercase id grammar");
  }
  return value;
};

const decodeNestedExpr = (value: unknown): Expr => {
  const tree = decodeComposedExactPayload(
    {
      family: "composedExact",
      extension: daggerComposedExactCodec.schema,
      roles: [
        { role: "actor", capabilities: [] },
        { role: "target", capabilities: [] },
      ],
      semanticsVersion: 1,
      source: "dagger",
      subject: "dagger",
      tree: value,
    },
    daggerComposedExactCodec,
  ).tree;
  assertDaggerProductKinds(tree);
  return tree;
};

const decodeDaggerExprPayload = (payload: unknown): DaggerExprPayload => {
  const wrapper = record(payload);
  exactFields(wrapper, ["kind", "value"]);
  const kind = wrapper.kind;
  const value = record(wrapper.value);
  switch (kind) {
    case "equipped-weapon-skill":
      exactFields(value, ["subject"]);
      return { kind, value: { subject: subject(value.subject) } };
    case "equipped-weapon-dice":
    case "struck-armor":
      exactFields(value, ["id", "subject"]);
      return { kind, value: { subject: subject(value.subject), id: id(value.id) } };
    default:
      throw new Error("Dagger product expression payload kind is unsupported");
  }
};

const daggerComposedExactCodec = declareComposedExactProductCodec<DaggerExprPayload>({
  schema: { namespace: "dagger.exact", schemaVersion: 1 },
  decode: decodeDaggerExprPayload,
  encode: (payload: DaggerExprPayload): JsonValue => payload as JsonValue,
});

/** The upstream leaf codec cannot see its enclosing product kind. */
function assertDaggerProductKinds(tree: Expr): void {
  switch (tree.op) {
    case "product":
      if (tree.subject !== "dagger" || tree.source !== "dagger") {
        throw new Error("Dagger product leaves require the documented dagger subject and source");
      }
      if (tree.kind !== tree.payload.kind) {
        throw new Error("Dagger product payload kind must match the enclosing product kind");
      }
      return;
    case "add":
    case "subtract":
    case "multiply":
    case "floorDivide":
    case "truncatingDivide":
      assertDaggerProductKinds(tree.left);
      assertDaggerProductKinds(tree.right);
      return;
    case "fixedPower":
      assertDaggerProductKinds(tree.base);
      assertDaggerProductKinds(tree.exponent);
      return;
    case "min":
    case "max":
      tree.values.forEach(assertDaggerProductKinds);
      return;
    case "literal":
    case "input":
      return;
  }
}

/** Strictly decodes one Dagger/Engine composedExact tree at the TS boundary. */
export const decodeDaggerExpr = (value: unknown): Expr => decodeNestedExpr(value);

/** Validates each authored generic tree through the Engine-generated grammar. */
const checked = (tree: Expr): Expr => {
  assertComposedExactPayload(
    {
      family: "composedExact",
      extension: daggerComposedExactCodec.schema,
      roles: [
        { role: "actor", capabilities: [] },
        { role: "target", capabilities: [] },
      ],
      semanticsVersion: 1,
      source: "dagger",
      subject: "dagger",
      tree,
    },
    daggerComposedExactCodec,
  );
  assertDaggerProductKinds(tree);
  return tree;
};
const product = <T extends DaggerExprLeaf>(leaf: T): Expr => checked({
  op: "product",
  kind: leaf.kind,
  payload: { kind: leaf.kind, value: leaf.payload } as DaggerExprPayload,
  subject: "dagger",
  source: "dagger",
});

/**
 * Embeds one generated tree in Dagger's already-admitted schema-2 aggregate.
 * The Engine helper is the only generic grammar validation/composition path.
 */
export const embedDaggerExpr = (tree: Expr): EmbeddedExpr =>
  composeEmbeddedComposedExactDefinition({
    codec: daggerComposedExactCodec,
    definition: {
      family: "composedExact",
      semanticsVersion: 1,
      subject: "dagger",
      source: "dagger",
      extension: daggerComposedExactCodec.schema,
      roles: [
        { role: "actor", capabilities: [] },
        { role: "target", capabilities: [] },
      ],
      tree,
    },
    parentSchemaVersion: 2,
    provenance: [{ subject: "dagger", source: "dagger" }],
  });

export const constant = (value: number): Expr => checked({ op: "literal", value });

export const stat = (subject: Subject, id: string): Expr => checked({
  op: "input", input: { kind: "standardStat", role: role(subject), stat: id },
});

export const skill = (subject: Subject, id: string): Expr => checked({
  op: "input", input: { kind: "standardStat", role: role(subject), stat: id },
});

/**
 * The subject's skill for the equipped weapon: right-hand weapon first, then
 * left-hand (donor: the right hand is primary); unarmed reads hand-to-hand.
 */
export const equippedWeaponSkill = (subject: Subject): Expr =>
  product({ kind: "equipped-weapon-skill", payload: { subject } });

/** Read one caller-supplied evidence value by exact id. */
export const evidence = (id: string): Expr => checked({
  op: "input", input: { kind: "roll", role: "actor", id },
});

/** Engine-owned bounded named input; the caller supplies the value as evidence. */
export const boundedRoll = (id: string, minimum: number, maximum: number): Expr =>
  checked({ op: "input", input: { kind: "boundedRoll", role: "actor", id, minimum, maximum } });

/**
 * Bounded named roll over the subject's equipped weapon's damage range. The
 * evidence id is explicit; the bounds are the live equipment's declared
 * range (unarmed: the derived hand-to-hand range), checked at evaluation.
 */
export const equippedWeaponDice = (subject: Subject, id: string): Expr =>
  product({ kind: "equipped-weapon-dice", payload: { subject, id } });

/**
 * Struck-body-part armor: reads a caller-supplied 0..19 roll, maps it
 * through the classic table (donor `FormulaHelper.CalculateStruckBodyPart`
 * — adopted as an evaluator constant), then reads the subject's
 * `armor-<part>` stat. Classic armor convention: higher is EASIER to hit.
 */
export const struckArmor = (subject: Subject, id: string): Expr =>
  product({ kind: "struck-armor", payload: { subject, id } });

/** Current value of one of the subject's resource tracks. */
export const trackCurrent = (subject: Subject, id: string): Expr => checked({
  op: "input", input: { kind: "standardTrackCurrent", role: role(subject), track: id },
});

/** Spawn-derived maximum of one of the subject's tracks (its `{id}-max` stat). */
export const trackMax = (subject: Subject, id: string): Expr => checked({
  op: "input", input: { kind: "standardTrackMaximum", role: role(subject), track: id },
});

/**
 * Engine-owned fixed-point power. Dagger authors the formula and its bounded
 * exponent input; the shared exact evaluator owns arithmetic and validation.
 */
export const fixedPower = (base: Expr, exponent: Expr, scale: number): Expr =>
  checked({ op: "fixedPower", base, exponent, scale });

export const add = (...terms: readonly Expr[]): Expr =>
  terms.reduce<Expr>((left, right) => checked({ op: "add", left, right }), constant(0));

export const sub = (left: Expr, right: Expr): Expr => checked({ op: "subtract", left, right });

export const mul = (...terms: readonly Expr[]): Expr =>
  terms.reduce<Expr>((left, right) => checked({ op: "multiply", left, right }), constant(1));

export const divFloor = (left: Expr, right: Expr): Expr => checked({ op: "floorDivide", left, right });

/**
 * Truncating division (toward zero): the donor's C# integer division
 * semantics. Use for signed differentials where floor division would
 * over-penalize negative nonmultiples ((attacker − target) / 10).
 */
export const divTrunc = (left: Expr, right: Expr): Expr => checked({ op: "truncatingDivide", left, right });

export const minOf = (...terms: readonly Expr[]): Expr => checked({ op: "min", values: terms });

export const maxOf = (...terms: readonly Expr[]): Expr => checked({ op: "max", values: terms });

/** Classic chance-to-hit shape: modifiers summed, clamped to 3..97. */
export const clampedChance = (...modifiers: readonly Expr[]): Expr =>
  minOf(constant(97), maxOf(constant(3), add(...modifiers)));

/** Classic stat modifier shape: floor((stat - 50) / divisor). */
export const statModifier = (
  subject: Subject,
  id: string,
  divisor: number,
): Expr => divFloor(sub(stat(subject, id), constant(50)), constant(divisor));
