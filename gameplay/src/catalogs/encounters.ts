/**
 * Named encounter groupings. Activation, routing, and outcome scheduling stay
 * with the Rust runtime; this catalog owns the content (objective, members,
 * route code) so encounters are authored next to the actors that fight in
 * them.
 */

import { encounter, type EncounterDefinition } from "../authoring/mod.js";

export const encounters: readonly EncounterDefinition[] = [
  encounter("rat-introduction", {
    name: "Rat Cellar",
    objective: "Defeat the rat and inspect its classic corpse marker.",
    routeCode: "Digit1",
    memberEntityIds: [2007],
  }),
  encounter("skeletal-guardroom", {
    name: "Skeletal Guardroom",
    objective:
      "Defeat the tougher Skeletal Warrior and survive its slower, heavier attacks.",
    routeCode: "Digit2",
    memberEntityIds: [2000],
  }),
];
