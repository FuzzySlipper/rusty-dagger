export type Predicate = Readonly<{
  kind: "evidenceAtLeast";
  evidence: string;
  minimum: number;
}>;

export type Selector = Readonly<{
  kind: "intentTarget";
}>;

export type Operation =
  | Readonly<{ kind: "spendMagicka"; amount: number }>
  | Readonly<{ kind: "damage"; target: Selector; amount: number }>;

export type Program =
  | Readonly<{ kind: "sequence"; steps: readonly Program[] }>
  | Readonly<{
      kind: "when";
      predicate: Predicate;
      thenProgram: Program;
      otherwiseProgram?: Program;
    }>
  | Readonly<{ kind: "operation"; operation: Operation }>;

export type ActionDefinition = Readonly<{
  id: string;
  tags: readonly string[];
  program: Program;
}>;

export type ItemDefinition = Readonly<{
  id: string;
  interceptor: Readonly<{
    kind: "reduceDamage";
    amount: number;
  }>;
}>;

export type RuleDefinition = Readonly<{
  id: string;
  kind: "rejectTagWhileCondition";
  tag: string;
  condition: string;
}>;

export type DaggerGameplayPayload = Readonly<{
  schemaVersion: 1;
  actions: readonly ActionDefinition[];
  items: readonly ItemDefinition[];
  rules: readonly RuleDefinition[];
}>;

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

export const evidenceAtLeast = (
  evidence: string,
  minimum: number,
): Predicate => ({ kind: "evidenceAtLeast", evidence, minimum });

export const action = (
  id: string,
  tags: readonly string[],
  program: Program,
): ActionDefinition => ({ id, tags, program });

export const item = (
  id: string,
  interceptor: ItemDefinition["interceptor"],
): ItemDefinition => ({ id, interceptor });

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

export const packageEnvelope = (payload: DaggerGameplayPayload) => ({
  kind: "rusty.gameplay-rules.package" as const,
  schemaVersion: 1 as const,
  domain: "dagger",
  package: "core",
  version: 1,
  dependencies: [] as const,
  sources: [
    { id: "dagger-core", path: "gameplay/src/privateers-hold.ts" },
  ] as const,
  provenance: [
    { subject: "action.ember-lance", source: "dagger-core", line: 13, column: 3 },
    { subject: "item.ruby-ward", source: "dagger-core", line: 31, column: 3 },
    { subject: "rule.silence", source: "dagger-core", line: 38, column: 3 },
  ] as const,
  payload,
});
