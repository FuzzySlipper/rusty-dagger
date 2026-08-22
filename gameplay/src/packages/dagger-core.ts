/**
 * The core Dagger gameplay package: classic stat vocabulary, actors, actions,
 * items, rules, and encounters composed into the deterministic envelope Rust
 * admits. Materialization walks `packages/`; one entry per package.
 */

import { composePackage } from "../authoring/mod.js";
import { actions } from "../catalogs/actions.js";
import { actors } from "../catalogs/actors.js";
import { derived } from "../catalogs/derived.js";
import { encounters } from "../catalogs/encounters.js";
import { equipment } from "../catalogs/equipment.js";
import { items } from "../catalogs/items.js";
import { lootTables } from "../catalogs/loot.js";
import { monsters } from "../catalogs/monsters.js";
import { rules } from "../catalogs/rules.js";
import { stats } from "../catalogs/stats.js";

export const gameplayPackage = composePackage({
  packageId: "core",
  version: 1,
  sources: {
    stats: "gameplay/src/catalogs/stats.ts",
    actors: "gameplay/src/catalogs/actors.ts",
    monsters: "gameplay/src/catalogs/monsters.ts",
    actions: "gameplay/src/catalogs/actions.ts",
    items: "gameplay/src/catalogs/items.ts",
    rules: "gameplay/src/catalogs/rules.ts",
    encounters: "gameplay/src/catalogs/encounters.ts",
    derived: "gameplay/src/catalogs/derived.ts",
    equipment: "gameplay/src/catalogs/equipment.ts",
    lootTables: "gameplay/src/catalogs/loot.ts",
    dagger: "gameplay/src/authoring/expressions.ts",
  },
  payload: {
    schemaVersion: 1,
    stats,
    actors: [...actors, ...monsters],
    actions,
    items,
    rules,
    encounters,
    derived,
    equipment,
    lootTables,
  },
});
