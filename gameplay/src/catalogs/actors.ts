/**
 * Actor definitions: the player and table-driven classic monsters. Ids join
 * enemies to admitted projects by `mobileId` (arena2 identity).
 *
 * Donor provenance (Utility/EnemyBasics.cs, MobileEnemy table — adapted):
 * - Rat (mobile 0): damage 1-4, health roll 9-16, level 1, armor value 6
 *   (×5 = 30 on the classic to-hit scale), vermin.
 * - Skeletal Warrior (mobile 15): damage 5-15, health roll 17-66, level 9,
 *   armor value 2 (×5 = 10), undead, sees through invisibility.
 *
 * Armor values use the classic convention: higher is EASIER to hit.
 * Behavior speeds/ranges are the current live tuning, carried over unchanged
 * from the starter experiment profile.
 */

import {
  actor,
  add,
  behavior,
  constant,
  dice,
  divFloor,
  mul,
  stat,
  track,
  type ActorDefinition,
} from "../authoring/mod.js";

export const actors: readonly ActorDefinition[] = [
  actor("player", {
    kind: "player",
    stats: {
      strength: 50,
      endurance: 40,
      intelligence: 50,
      willpower: 50,
      agility: 50,
      personality: 50,
      speed: 50,
      luck: 50,
    },
    skills: { "long-blade": 60 },
    armorValue: 0,
    tracks: [
      // baseHealth 25 + endurance * 1.5 (classic HitPointsModifier flavor)
      track(
        "health",
        add(constant(25), divFloor(mul(stat("actor", "endurance"), constant(3)), constant(2))),
      ),
      // (strength + endurance) * 1
      track(
        "stamina",
        mul(add(stat("actor", "strength"), stat("actor", "endurance")), constant(1)),
      ),
      track("magicka", stat("actor", "intelligence")),
    ],
  }),

  actor("rat", {
    kind: "monster",
    mobileId: 0,
    stats: {
      strength: 10,
      endurance: 10,
      intelligence: 0,
      willpower: 0,
      agility: 40,
      personality: 0,
      speed: 50,
      luck: 50,
    },
    skills: { "hand-to-hand": 60 },
    armorValue: 30,
    tracks: [
      track("health", dice("rat.health", 9, 16)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    behavior: behavior("rat-bite", {
      detectionRange: 6.0,
      patrolSpeed: 1.0,
      chaseSpeed: 2.0,
      attackRange: 1.25,
      attackCooldownSeconds: 1.5,
    }),
  }),

  actor("skeletal-warrior", {
    kind: "monster",
    mobileId: 15,
    stats: {
      strength: 35,
      endurance: 30,
      intelligence: 0,
      willpower: 10,
      agility: 40,
      personality: 0,
      speed: 40,
      luck: 50,
    },
    skills: { "long-blade": 65 },
    armorValue: 10,
    tracks: [
      track("health", dice("skeletal-warrior.health", 17, 66)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    behavior: behavior("skeleton-strike", {
      detectionRange: 8.0,
      patrolSpeed: 0.8,
      chaseSpeed: 1.5,
      attackRange: 1.5,
      attackCooldownSeconds: 2.0,
    }),
  }),
];
