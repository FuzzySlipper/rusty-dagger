/**
 * Package envelope composition. Provenance is computed here from the section
 * → source-file map the package entry supplies, so no catalog file ever
 * hand-writes line/column pairs that rot on the next edit.
 */

import type {
  ActionDefinition,
  ActorDefinition,
  EncounterDefinition,
  ItemDefinition,
  RuleDefinition,
  StatsSection,
} from "./definitions.js";

export type DaggerGameplayPayload = Readonly<{
  schemaVersion: 1;
  stats: StatsSection;
  actors: readonly ActorDefinition[];
  actions: readonly ActionDefinition[];
  items: readonly ItemDefinition[];
  rules: readonly RuleDefinition[];
  encounters: readonly EncounterDefinition[];
}>;

export type PackageInput = Readonly<{
  /** Package id inside the `dagger` domain, e.g. "core". */
  packageId: string;
  version: number;
  /** Section name → source path relative to the repository root. */
  sources: Readonly<Record<string, string>>;
  payload: DaggerGameplayPayload;
}>;

export const composePackage = (input: PackageInput) => {
  const { payload } = input;
  const sources = Object.entries(input.sources).map(([id, path]) => ({
    id,
    path,
  }));
  const provenance: { subject: string; source: string }[] = [];
  const record = (section: string, subjects: readonly string[]): void => {
    for (const subject of subjects) {
      provenance.push({ subject, source: section });
    }
  };
  record("stats", ["stats"]);
  record(
    "actors",
    payload.actors.map((entry) => `actor.${entry.id}`),
  );
  record(
    "actions",
    payload.actions.map((entry) => `action.${entry.id}`),
  );
  record(
    "items",
    payload.items.map((entry) => `item.${entry.id}`),
  );
  record(
    "rules",
    payload.rules.map((entry) => `rule.${entry.id}`),
  );
  record(
    "encounters",
    payload.encounters.map((entry) => `encounter.${entry.id}`),
  );
  return {
    kind: "rusty.gameplay-rules.package" as const,
    schemaVersion: 1 as const,
    domain: "dagger",
    package: input.packageId,
    version: input.version,
    dependencies: [] as const,
    sources,
    provenance,
    payload,
  };
};
