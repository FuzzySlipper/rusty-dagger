/**
 * Action programs: sequencing, conditional execution, and operations over the
 * resolution kernel's structural `Program` grammar. Rust compiles these trees
 * into the Engine's program grammar and is the only executor.
 */

import { embedDaggerExpr, type EmbeddedExpr, type Expr } from "./expressions.js";

export type CmpOp = "lt" | "lte" | "eq" | "gte" | "gt";

/** Predicates compare two derived-value expressions. */
export type Predicate = Readonly<{
  kind: "cmp";
  op: CmpOp;
  left: EmbeddedExpr;
  right: EmbeddedExpr;
}>;

export type Selector = Readonly<{
  kind: "intentTarget";
}>;

export type Operation =
  | Readonly<{ kind: "spendTrack"; track: string; amount: EmbeddedExpr }>
  | Readonly<{ kind: "damage"; target: Selector; amount: EmbeddedExpr }>;

export type Program =
  | Readonly<{ kind: "sequence"; steps: readonly Program[] }>
  | Readonly<{
      kind: "when";
      predicate: Predicate;
      thenProgram: Program;
      otherwiseProgram?: Program;
    }>
  | Readonly<{ kind: "operation"; operation: Operation }>;

export const cmp = (op: CmpOp, left: Expr, right: Expr): Predicate => ({
  kind: "cmp",
  op,
  left: embedDaggerExpr(left),
  right: embedDaggerExpr(right),
});

export const intentTarget = (): Selector => ({ kind: "intentTarget" });

export const spendTrack = (track: string, amount: Expr): Operation => ({
  kind: "spendTrack",
  track,
  amount: embedDaggerExpr(amount),
});

export const damage = (amount: Expr, target: Selector = intentTarget()): Operation => ({
  kind: "damage",
  target,
  amount: embedDaggerExpr(amount),
});

export const sequence = (...steps: readonly Program[]): Program => ({
  kind: "sequence",
  steps,
});

export const when = (
  predicate: Predicate,
  thenProgram: Program,
  otherwiseProgram?: Program,
): Program => ({
  kind: "when",
  predicate,
  thenProgram,
  ...(otherwiseProgram === undefined ? {} : { otherwiseProgram }),
});

export const operation = (value: Operation): Program => ({
  kind: "operation",
  operation: value,
});
