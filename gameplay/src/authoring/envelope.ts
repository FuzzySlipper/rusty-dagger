/**
 * Package envelope composition through the Engine's canonical binary64
 * authoring API (schema 2). Provenance is computed here from the section →
 * source-file map the package entry supplies, so no catalog file ever
 * hand-writes line/column pairs that rot on the next edit.
 *
 * The artifact's canonicalJson is the exact byte string the Engine
 * fingerprints; materialization writes it verbatim (plus a trailing
 * newline), keeping TypeScript output and Rust admission byte-identical.
 */

import { authorBinary64RulePackage } from "@rusty-engine/gameplay-rules-authoring";

import type {
  ActionDefinition,
  ActorDefinition,
  DerivedRule,
  EncounterDefinition,
  EquipmentSection,
  ItemDefinition,
  LootTableDefinition,
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
  derived: readonly DerivedRule[];
  /** Equipment slots and capacity metrics items bind against (additive). */
  equipment?: EquipmentSection;
  /** Classic loot tables (additive); actors reference them by `lootTableKey`. */
  lootTables?: readonly LootTableDefinition[];
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
  record(
    "derived",
    payload.derived.map((entry) => `derived.${entry.id}`),
  );
  if (payload.equipment !== undefined) {
    record("equipment", [
      ...payload.equipment.capacityMetrics.map((id) => `equipment.capacity-metric.${id}`),
      ...payload.equipment.slots.map((entry) => `equipment.slot.${entry.id}`),
    ]);
  }
  if (payload.lootTables !== undefined) {
    record(
      "lootTables",
      payload.lootTables.map((entry) => `lootTable.${entry.key}`),
    );
  }
  return authorBinary64RulePackage({
    domain: "dagger",
    package: input.packageId,
    version: input.version,
    dependencies: [],
    sources,
    provenance,
    payload,
  });
};
