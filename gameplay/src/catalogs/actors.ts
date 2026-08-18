/**
 * Non-table actor definitions: the player and class-career enemies.
 * Table-driven monsters (Rat, Skeletal Warrior, and the full classic table)
 * live in `monsters.ts`.
 *
 * Class-career enemies (donor: `Entities/EnemyEntity.cs SetEnemyCareer` +
 * `Entities/DaggerfallEntity.cs GetClassCareerTemplate` — adapted) draw
 * stats and career skill lists from the classic CLASS*.CFG career records
 * in `local/arena2` (parsed per DFU's ClassFile layout). Enemy skills are
 * level-derived (min(100, level * 5 + 30)); class enemies level to the
 * player in classic — table values carry that derivation at level 1.
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
      // Classic default reflexes (Average); player-owned in classic, so
      // monsters do not declare it.
      reflexes: 2,
    },
    skills: { "long-blade": 60, "hand-to-hand": 40, medical: 30, backstabbing: 30 },
    armorValue: 0,
    moveSpeed: 3.5,
    tracks: [
      // baseHealth 25 + endurance * 1.5 (classic HitPointsModifier flavor)
      track(
        "health",
        add(constant(25), divFloor(mul(stat("actor", "endurance"), constant(3)), constant(2))),
      ),
      // (strength + endurance) * 1 — simplified stamina profile, not the
      // classic (strength + endurance) * 64 fatigue units (see derived.ts)
      track(
        "stamina",
        mul(add(stat("actor", "strength"), stat("actor", "endurance")), constant(1)),
      ),
      track("magicka", stat("actor", "intelligence")),
    ],
    // Spawn loadout bound into the upstream inventory/equipment components:
    // a longsword in the right hand plus a fungible gold stack.
    inventory: [
      { item: "iron-longsword", equipSlot: "right-hand" },
      { item: "gold-piece", quantity: 25 },
    ],
  }),

  // Thief (mobile 138, class career CLASS10.CFG): attributes and career
  // skills from the classic record; health 10 + level rolls of 1-10
  // (RollEnemyClassMaxHealth at level 1).
  actor("thief", {
    kind: "enemy-class",
    mobileId: 138,
    stats: {
      strength: 45,
      intelligence: 47,
      willpower: 45,
      agility: 58,
      endurance: 50,
      personality: 47,
      speed: 58,
      luck: 50,
    },
    skills: {
      pickpocket: 35,
      stealth: 35,
      "short-blade": 35,
      backstabbing: 35,
      climbing: 35,
      lockpicking: 35,
      "critical-strike": 35,
      jumping: 35,
      running: 35,
      dodging: 35,
      streetwise: 35,
      mercantile: 35,
    },
    // No armor: class enemies armor from equipment, not yet modeled.
    armorValue: 0,
    tracks: [
      track("health", dice("thief.health", 11, 20)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    behavior: behavior("thief-strike", {
      detectionRange: 8.0,
      patrolSpeed: 1.0,
      chaseSpeed: 2.5,
      attackRange: 1.5,
      attackCooldownSeconds: 1.2,
    }),
    team: "criminals",
    lootTableKey: "T",
  }),
];
