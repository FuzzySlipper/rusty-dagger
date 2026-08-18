/**
 * Classic loot tables: all 22 `LootChanceMatrix` entries of the donor's
 * `DefaultLootTables` (Daggerfall Unity
 * `Assets/Scripts/Game/Items/LootTables.cs:77-110` — adopted verbatim).
 * Where the donor header notes FALL.EXE diverging from the Daggerfall
 * Chronicles print tables, the FALL.EXE values below win.
 *
 * Category names map to the donor fields (see `LOOT_CATEGORY_NAMES`):
 * plant1/plant2 = P1/P2, creature1..3 = C1..C3, misc1/misc2 = M1/M2 (the
 * seven ingredient groups), armor = AM, weapons = WP, magic = MI,
 * clothing = CL, books = BK, religious = RL. All values are integer
 * percentage chances; omitted categories are 0.
 *
 * Generation semantics are owned by the Rust evaluator
 * (`dagger-rpg::resolution::loot`), not here: gold rolls uniform
 * [min, max] x player level; per category, successes repeat at
 * geometrically halved chance; the first four ingredient categories
 * (C1, C2, P1, P2) roll chance x player level while everything else rolls
 * the raw table value (donor quirk verified against classic).
 */

import { lootTable, type LootTableDefinition } from "../authoring/mod.js";

export const lootTables: readonly LootTableDefinition[] = [
  // Default table: no gold, no categories.
  lootTable("-", { gold: { min: 0, max: 0 }, categories: {} }),
  lootTable("A", {
    gold: { min: 1, max: 10 },
    categories: { misc2: 2, armor: 5, weapons: 5, magic: 2, clothing: 4 },
  }),
  // The donor notes the Chronicles prints B with 10 for Warm Plant and Misc.
  // Monster, but FALL.EXE has Temperate Plant (P1) and Warm Plant (P2).
  lootTable("B", {
    gold: { min: 0, max: 0 },
    categories: { plant1: 10, plant2: 10 },
  }),
  lootTable("C", {
    gold: { min: 2, max: 20 },
    categories: {
      plant1: 10,
      plant2: 10,
      creature1: 5,
      creature2: 5,
      creature3: 5,
      misc1: 5,
      misc2: 2,
      armor: 5,
      weapons: 25,
      magic: 3,
      books: 2,
      religious: 2,
    },
  }),
  lootTable("D", {
    gold: { min: 1, max: 4 },
    categories: {
      plant1: 6,
      plant2: 6,
      creature1: 6,
      creature2: 6,
      creature3: 6,
      misc1: 6,
      religious: 4,
    },
  }),
  lootTable("E", {
    gold: { min: 20, max: 80 },
    categories: { misc2: 1, armor: 10, weapons: 10, magic: 3, clothing: 4, books: 2, religious: 15 },
  }),
  lootTable("F", {
    gold: { min: 4, max: 30 },
    categories: {
      plant1: 2,
      plant2: 2,
      creature1: 5,
      creature2: 5,
      creature3: 5,
      misc1: 2,
      misc2: 3,
      armor: 50,
      weapons: 50,
      magic: 1,
    },
  }),
  lootTable("G", {
    gold: { min: 3, max: 15 },
    categories: { misc2: 3, armor: 50, weapons: 50, magic: 1, clothing: 5 },
  }),
  lootTable("H", {
    gold: { min: 2, max: 10 },
    categories: { weapons: 100, magic: 1, clothing: 2 },
  }),
  // The donor notes the Chronicles is missing "I" (its data is printed under
  // "J"); from here the print tables are off by one against FALL.EXE.
  lootTable("I", {
    gold: { min: 0, max: 0 },
    categories: { magic: 2, religious: 5 },
  }),
  lootTable("J", {
    gold: { min: 50, max: 150 },
    categories: { armor: 5, weapons: 5, magic: 3 },
  }),
  lootTable("K", {
    gold: { min: 1, max: 10 },
    categories: {
      plant1: 3,
      plant2: 3,
      creature1: 3,
      creature2: 3,
      creature3: 3,
      misc1: 3,
      misc2: 2,
      armor: 5,
      weapons: 5,
      magic: 3,
      books: 5,
      religious: 100,
    },
  }),
  lootTable("L", {
    gold: { min: 1, max: 20 },
    categories: {
      creature1: 3,
      creature2: 3,
      creature3: 3,
      misc1: 3,
      misc2: 5,
      armor: 50,
      weapons: 50,
      magic: 1,
      clothing: 75,
      religious: 3,
    },
  }),
  lootTable("M", {
    gold: { min: 1, max: 15 },
    categories: {
      plant1: 1,
      plant2: 1,
      creature1: 1,
      creature2: 1,
      creature3: 1,
      misc1: 2,
      misc2: 3,
      armor: 10,
      weapons: 10,
      magic: 1,
      clothing: 15,
      books: 2,
      religious: 1,
    },
  }),
  lootTable("N", {
    gold: { min: 1, max: 80 },
    categories: {
      plant1: 5,
      plant2: 5,
      creature1: 5,
      creature2: 5,
      creature3: 5,
      misc1: 5,
      misc2: 2,
      armor: 5,
      weapons: 5,
      magic: 1,
      clothing: 20,
      books: 5,
      religious: 5,
    },
  }),
  lootTable("O", {
    gold: { min: 5, max: 20 },
    categories: {
      plant1: 1,
      plant2: 1,
      creature1: 1,
      creature2: 1,
      creature3: 1,
      misc1: 1,
      armor: 10,
      weapons: 15,
      magic: 2,
    },
  }),
  lootTable("P", {
    gold: { min: 5, max: 20 },
    categories: {
      plant1: 5,
      plant2: 5,
      creature1: 5,
      creature2: 5,
      creature3: 5,
      misc1: 5,
      misc2: 5,
      armor: 5,
      weapons: 10,
      magic: 2,
      books: 10,
    },
  }),
  lootTable("Q", {
    gold: { min: 20, max: 80 },
    categories: {
      plant1: 2,
      plant2: 2,
      creature1: 8,
      creature2: 8,
      creature3: 8,
      misc1: 2,
      misc2: 3,
      armor: 10,
      weapons: 25,
      magic: 3,
      clothing: 35,
      books: 5,
    },
  }),
  lootTable("R", {
    gold: { min: 5, max: 20 },
    categories: { creature1: 3, creature2: 3, creature3: 3, misc1: 5, armor: 5, weapons: 15, magic: 2 },
  }),
  lootTable("S", {
    gold: { min: 50, max: 125 },
    categories: {
      plant1: 5,
      plant2: 5,
      creature1: 5,
      creature2: 5,
      creature3: 5,
      misc1: 15,
      misc2: 5,
      armor: 10,
      weapons: 10,
      magic: 3,
      books: 5,
    },
  }),
  lootTable("T", {
    gold: { min: 20, max: 80 },
    categories: { armor: 100, weapons: 100, magic: 1 },
  }),
  lootTable("U", {
    gold: { min: 7, max: 30 },
    categories: {
      plant1: 5,
      plant2: 5,
      creature1: 5,
      creature2: 5,
      creature3: 5,
      misc1: 10,
      misc2: 2,
      armor: 10,
      weapons: 10,
      magic: 2,
      books: 2,
      religious: 10,
    },
  }),
];
