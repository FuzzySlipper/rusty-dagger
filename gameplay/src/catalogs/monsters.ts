/**
 * The full classic table-driven monster table (donor: `Utility/EnemyBasics.cs`
 * MobileEnemy entries — adopted; attributes and career skill lists from the
 * classic MONSTER.BSA career records in `local/arena2`, parsed per DFU's
 * ClassFile layout). Enemy skills are level-derived in classic
 * (min(100, level * 5 + 30)); table values carry that derivation at the
 * mobile's fixed level. Armor values use the classic convention (higher is
 * EASIER to hit; table armor x 5). Monster damage ranges live on their
 * attack actions, not here — Privateer's Hold attackers are authored in
 * `actions.ts`; the remaining attack actions are future errata.
 */

import {
  actor,
  behavior,
  constant,
  dice,
  track,
  type ActorDefinition,
} from "../authoring/mod.js";

export const monsters: readonly ActorDefinition[] = [
  // Rat (mobile 0) — table: hp 9-16, dmg 1-4, armor 6, level 1.
  actor("rat", {
    kind: "monster",
    mobileId: 0,
    stats: { strength: 40, intelligence: 10, willpower: 70, agility: 80, endurance: 55, personality: 50, speed: 45, luck: 50 },
    skills: { "climbing": 35, "backstabbing": 35, "stealth": 35, "axe": 35, "nymph": 35, "streetwise": 35, "centaurian": 35, "lockpicking": 35, "hand-to-hand": 35, "critical-strike": 35, "mysticism": 35, "giantish": 35 },
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
    level: 1, weight: 2, team: "vermin"
  }),
  // Imp (mobile 1) — table: hp 11-18, dmg 2-15, armor 3, level 2.
  actor("imp", {
    kind: "monster",
    mobileId: 1,
    stats: { strength: 40, intelligence: 65, willpower: 70, agility: 80, endurance: 55, personality: 50, speed: 70, luck: 50 },
    skills: { "stealth": 40, "daedric": 40, "archery": 40, "climbing": 40, "nymph": 40, "lockpicking": 40, "blunt-weapon": 40, "pickpocket": 40, "etiquette": 40, "streetwise": 40, "running": 40, "centaurian": 40 },
    armorValue: 15,
    tracks: [
      track("health", dice("imp.health", 11, 18)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 2, weight: 40, minMetalToHit: "steel", team: "magic", lootTableKey: "D"
  }),
  // Spriggan (mobile 2) — table: hp 12-26, dmg 1-8, armor -4, level 3.
  actor("spriggan", {
    kind: "monster",
    mobileId: 2,
    stats: { strength: 80, intelligence: 50, willpower: 30, agility: 50, endurance: 75, personality: 50, speed: 40, luck: 50 },
    skills: { "dodging": 45, "climbing": 45, "stealth": 45, "backstabbing": 45, "etiquette": 45, "nymph": 45, "impish": 45, "daedric": 45, "centaurian": 45, "lockpicking": 45, "spriggan": 45, "orcish": 45 },
    armorValue: -20,
    tracks: [
      track("health", dice("spriggan.health", 12, 26)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 3, weight: 240, team: "spriggans", lootTableKey: "B"
  }),
  // Giant Bat (mobile 3) — table: hp 12-26, dmg 2-12, armor 6, level 3.
  actor("giant-bat", {
    kind: "monster",
    mobileId: 3,
    stats: { strength: 50, intelligence: 40, willpower: 60, agility: 70, endurance: 55, personality: 40, speed: 70, luck: 50 },
    skills: { "archery": 45, "axe": 45, "climbing": 45, "streetwise": 45, "giantish": 45, "critical-strike": 45, "nymph": 45, "daedric": 45, "lockpicking": 45, "etiquette": 45, "running": 45, "centaurian": 45 },
    armorValue: 30,
    tracks: [
      track("health", dice("giant-bat.health", 12, 26)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 3, weight: 80, team: "vermin"
  }),
  // Grizzly Bear (mobile 4) — table: hp 13-34, dmg 1-8, armor 6, level 4.
  actor("grizzly-bear", {
    kind: "monster",
    mobileId: 4,
    stats: { strength: 90, intelligence: 10, willpower: 30, agility: 70, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "archery": 50, "axe": 50, "critical-strike": 50, "climbing": 50, "stealth": 50, "streetwise": 50, "nymph": 50, "etiquette": 50, "blunt-weapon": 50, "daedric": 50, "impish": 50, "spriggan": 50 },
    armorValue: 30,
    tracks: [
      track("health", dice("grizzly-bear.health", 13, 34)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 4, weight: 1000, team: "bears"
  }),
  // Sabertooth Tiger (mobile 5) — table: hp 13-34, dmg 1-10, armor 6, level 4.
  actor("sabertooth-tiger", {
    kind: "monster",
    mobileId: 5,
    stats: { strength: 90, intelligence: 60, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "backstabbing": 50, "long-blade": 50, "climbing": 50, "axe": 50, "hand-to-hand": 50, "nymph": 50, "thaumaturgy": 50, "etiquette": 50, "lockpicking": 50, "daedric": 50, "centaurian": 50 },
    armorValue: 30,
    tracks: [
      track("health", dice("sabertooth-tiger.health", 13, 34)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 4, weight: 1000, team: "tigers"
  }),
  // Spider (mobile 6) — table: hp 13-34, dmg 5-15, armor 5, level 4.
  actor("spider", {
    kind: "monster",
    mobileId: 6,
    stats: { strength: 50, intelligence: 10, willpower: 30, agility: 80, endurance: 75, personality: 50, speed: 70, luck: 50 },
    skills: { "blunt-weapon": 50, "long-blade": 50, "hand-to-hand": 50, "dodging": 50, "harpy": 50, "axe": 50, "swimming": 50, "climbing": 50, "stealth": 50, "nymph": 50, "etiquette": 50, "daedric": 50 },
    armorValue: 25,
    tracks: [
      track("health", dice("spider.health", 13, 34)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 4, weight: 400, team: "spiders"
  }),
  // Orc (mobile 7) — table: hp 13-34, dmg 1-6, armor 7, level 5.
  actor("orc", {
    kind: "monster",
    mobileId: 7,
    stats: { strength: 90, intelligence: 40, willpower: 80, agility: 50, endurance: 75, personality: 50, speed: 50, luck: 50 },
    skills: { "long-blade": 55, "climbing": 55, "backstabbing": 55, "hand-to-hand": 55, "blunt-weapon": 55, "archery": 55, "axe": 55, "thaumaturgy": 55, "jumping": 55, "giantish": 55, "dodging": 55, "critical-strike": 55 },
    armorValue: 35,
    tracks: [
      track("health", dice("orc.health", 13, 34)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 5, weight: 600, team: "orcs", lootTableKey: "A"
  }),
  // Centaur (mobile 8) — table: hp 14-46, dmg 5-15, armor 6, level 5.
  actor("centaur", {
    kind: "monster",
    mobileId: 8,
    stats: { strength: 80, intelligence: 80, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "climbing": 55, "centaurian": 55, "backstabbing": 55, "hand-to-hand": 55, "archery": 55, "medical": 55, "blunt-weapon": 55, "alteration": 55, "swimming": 55, "axe": 55, "harpy": 55, "giantish": 55 },
    armorValue: 30,
    tracks: [
      track("health", dice("centaur.health", 14, 46)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 5, weight: 1200, team: "centaurs", lootTableKey: "C"
  }),
  // Werewolf (mobile 9) — table: hp 17-66, dmg 1-10, armor 5, level 6.
  actor("werewolf", {
    kind: "monster",
    mobileId: 9,
    stats: { strength: 100, intelligence: 20, willpower: 40, agility: 80, endurance: 65, personality: 30, speed: 85, luck: 50 },
    skills: { "critical-strike": 60, "backstabbing": 60, "long-blade": 60, "axe": 60, "hand-to-hand": 60, "jumping": 60, "stealth": 60, "blunt-weapon": 60, "etiquette": 60, "lockpicking": 60, "giantish": 60, "orcish": 60 },
    armorValue: 25,
    tracks: [
      track("health", dice("werewolf.health", 17, 66)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 6, weight: 480, minMetalToHit: "silver", team: "werecreatures"
  }),
  // Nymph (mobile 10) — table: hp 15-50, dmg 1-5, armor 0, level 6.
  actor("nymph", {
    kind: "monster",
    mobileId: 10,
    stats: { strength: 40, intelligence: 70, willpower: 60, agility: 80, endurance: 55, personality: 60, speed: 70, luck: 50 },
    skills: { "blunt-weapon": 60, "backstabbing": 60, "axe": 60, "mercantile": 60, "long-blade": 60, "climbing": 60, "hand-to-hand": 60, "spriggan": 60, "pickpocket": 60, "harpy": 60, "orcish": 60, "daedric": 60 },
    armorValue: 0,
    tracks: [
      track("health", dice("nymph.health", 15, 50)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 6, weight: 200, minMetalToHit: "silver", team: "nymphs", lootTableKey: "C"
  }),
  // Slaughterfish (mobile 11) — table: hp 15-50, dmg 2-12, armor 6, level 7.
  actor("slaughterfish", {
    kind: "monster",
    mobileId: 11,
    stats: { strength: 70, intelligence: 50, willpower: 80, agility: 80, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "mysticism": 65, "backstabbing": 65, "long-blade": 65, "archery": 65, "hand-to-hand": 65, "harpy": 65, "streetwise": 65, "nymph": 65, "daedric": 65, "lockpicking": 65, "etiquette": 65, "centaurian": 65 },
    armorValue: 30,
    tracks: [
      track("health", dice("slaughterfish.health", 15, 50)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 7, weight: 400, team: "aquatic"
  }),
  // Orc Sergeant (mobile 12) — table: hp 15-50, dmg 5-15, armor 5, level 7.
  actor("orc-sergeant", {
    kind: "monster",
    mobileId: 12,
    stats: { strength: 90, intelligence: 50, willpower: 80, agility: 50, endurance: 75, personality: 50, speed: 50, luck: 50 },
    skills: { "axe": 65, "blunt-weapon": 65, "climbing": 65, "critical-strike": 65, "long-blade": 65, "dodging": 65, "hand-to-hand": 65, "harpy": 65, "orcish": 65, "etiquette": 65, "running": 65, "thaumaturgy": 65 },
    armorValue: 25,
    tracks: [
      track("health", dice("orc-sergeant.health", 15, 50)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 7, weight: 600, team: "orcs", lootTableKey: "A"
  }),
  // Harpy (mobile 13) — table: hp 16-85, dmg 5-15, armor 2, level 8.
  actor("harpy", {
    kind: "monster",
    mobileId: 13,
    stats: { strength: 70, intelligence: 50, willpower: 30, agility: 80, endurance: 55, personality: 50, speed: 70, luck: 50 },
    skills: { "axe": 70, "long-blade": 70, "archery": 70, "spriggan": 70, "pickpocket": 70, "hand-to-hand": 70, "etiquette": 70, "running": 70, "centaurian": 70, "orcish": 70, "harpy": 70, "giantish": 70 },
    armorValue: 10,
    tracks: [
      track("health", dice("harpy.health", 16, 85)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 8, weight: 200, minMetalToHit: "dwarven", team: "harpies", lootTableKey: "D"
  }),
  // Wereboar (mobile 14) — table: hp 17-66, dmg 2-12, armor 3, level 8.
  actor("wereboar", {
    kind: "monster",
    mobileId: 14,
    stats: { strength: 80, intelligence: 10, willpower: 40, agility: 70, endurance: 75, personality: 40, speed: 70, luck: 50 },
    skills: { "long-blade": 70, "archery": 70, "dodging": 70, "daedric": 70, "hand-to-hand": 70, "medical": 70, "dragonish": 70, "harpy": 70, "climbing": 70, "critical-strike": 70, "blunt-weapon": 70, "streetwise": 70 },
    armorValue: 15,
    tracks: [
      track("health", dice("wereboar.health", 17, 66)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 8, weight: 560, minMetalToHit: "silver", team: "werecreatures"
  }),
  // Skeletal Warrior (mobile 15) — table: hp 17-66, dmg 5-15, armor 2, level 9.
  actor("skeletal-warrior", {
    kind: "monster",
    mobileId: 15,
    stats: { strength: 50, intelligence: 65, willpower: 40, agility: 80, endurance: 55, personality: 50, speed: 70, luck: 50 },
    skills: { "archery": 75, "axe": 75, "long-blade": 75, "hand-to-hand": 75, "daedric": 75, "dodging": 75, "giantish": 75, "etiquette": 75, "dragonish": 75, "climbing": 75, "blunt-weapon": 75, "stealth": 75 },
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
    level: 9, weight: 80, team: "undead", lootTableKey: "H"
  }),
  // Giant (mobile 16) — table: hp 18-74, dmg 10-30, armor 3, level 10.
  actor("giant", {
    kind: "monster",
    mobileId: 16,
    stats: { strength: 110, intelligence: 40, willpower: 50, agility: 70, endurance: 75, personality: 40, speed: 60, luck: 50 },
    skills: { "dodging": 80, "etiquette": 80, "hand-to-hand": 80, "impish": 80, "giantish": 80, "harpy": 80, "dragonish": 80, "long-blade": 80, "climbing": 80, "blunt-weapon": 80, "nymph": 80, "daedric": 80 },
    armorValue: 15,
    tracks: [
      track("health", dice("giant.health", 18, 74)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 10, weight: 3000, team: "giants", lootTableKey: "F"
  }),
  // Zombie (mobile 17) — table: hp 52-66, dmg 15-50, armor 0, level 10.
  actor("zombie", {
    kind: "monster",
    mobileId: 17,
    stats: { strength: 150, intelligence: 40, willpower: 90, agility: 80, endurance: 100, personality: 50, speed: 70, luck: 50 },
    skills: { "axe": 80, "critical-strike": 80, "long-blade": 80, "archery": 80, "etiquette": 80, "dragonish": 80, "mysticism": 80, "climbing": 80, "stealth": 80, "streetwise": 80, "blunt-weapon": 80, "hand-to-hand": 80 },
    armorValue: 0,
    tracks: [
      track("health", dice("zombie.health", 52, 66)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 10, weight: 4000, team: "undead", lootTableKey: "G"
  }),
  // Ghost (mobile 18) — table: hp 17-66, dmg 10-35, armor 0, level 11.
  actor("ghost", {
    kind: "monster",
    mobileId: 18,
    stats: { strength: 100, intelligence: 65, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "blunt-weapon": 85, "daedric": 85, "dodging": 85, "hand-to-hand": 85, "thaumaturgy": 85, "axe": 85, "etiquette": 85, "long-blade": 85, "harpy": 85, "critical-strike": 85, "climbing": 85, "stealth": 85 },
    armorValue: 0,
    tracks: [
      track("health", dice("ghost.health", 17, 66)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 11, weight: 0, minMetalToHit: "silver", team: "undead", lootTableKey: "I"
  }),
  // Mummy (mobile 19) — table: hp 17-66, dmg 5-15, armor 2, level 11.
  actor("mummy", {
    kind: "monster",
    mobileId: 19,
    stats: { strength: 100, intelligence: 40, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "long-blade": 85, "thaumaturgy": 85, "daedric": 85, "critical-strike": 85, "dragonish": 85, "axe": 85, "climbing": 85, "stealth": 85, "blunt-weapon": 85, "hand-to-hand": 85, "streetwise": 85, "etiquette": 85 },
    armorValue: 10,
    tracks: [
      track("health", dice("mummy.health", 17, 66)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 11, weight: 300, minMetalToHit: "silver", team: "undead", lootTableKey: "E"
  }),
  // Giant Scorpion (mobile 20) — table: hp 18-74, dmg 15-25, armor 0, level 12.
  actor("giant-scorpion", {
    kind: "monster",
    mobileId: 20,
    stats: { strength: 120, intelligence: 10, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "long-blade": 90, "dodging": 90, "alteration": 90, "axe": 90, "dragonish": 90, "giantish": 90, "climbing": 90, "short-blade": 90, "stealth": 90, "blunt-weapon": 90, "streetwise": 90, "hand-to-hand": 90 },
    armorValue: 0,
    tracks: [
      track("health", dice("giant-scorpion.health", 18, 74)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 12, weight: 600, team: "scorpions"
  }),
  // Orc Shaman (mobile 21) — table: hp 18-74, dmg 2-20, armor 7, level 13.
  actor("orc-shaman", {
    kind: "monster",
    mobileId: 21,
    stats: { strength: 90, intelligence: 70, willpower: 80, agility: 50, endurance: 75, personality: 50, speed: 50, luck: 50 },
    skills: { "axe": 95, "backstabbing": 95, "long-blade": 95, "climbing": 95, "dodging": 95, "blunt-weapon": 95, "critical-strike": 95, "etiquette": 95, "archery": 95, "hand-to-hand": 95, "giantish": 95, "harpy": 95 },
    armorValue: 35,
    tracks: [
      track("health", dice("orc-shaman.health", 18, 74)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 13, weight: 400, team: "orcs", lootTableKey: "U"
  }),
  // Gargoyle (mobile 22) — table: hp 19-82, dmg 10-15, armor 0, level 14.
  actor("gargoyle", {
    kind: "monster",
    mobileId: 22,
    stats: { strength: 90, intelligence: 10, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 70, luck: 50 },
    skills: { "long-blade": 100, "archery": 100, "axe": 100, "hand-to-hand": 100, "orcish": 100, "thaumaturgy": 100, "dodging": 100, "daedric": 100, "etiquette": 100, "illusion": 100, "climbing": 100, "streetwise": 100 },
    armorValue: 0,
    tracks: [
      track("health", dice("gargoyle.health", 19, 82)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 14, weight: 300, minMetalToHit: "mithril", team: "magic"
  }),
  // Wraith (mobile 23) — table: hp 30-90, dmg 20-45, armor 0, level 15.
  actor("wraith", {
    kind: "monster",
    mobileId: 23,
    stats: { strength: 100, intelligence: 10, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 85, luck: 50 },
    skills: { "blunt-weapon": 100, "axe": 100, "dodging": 100, "climbing": 100, "centaurian": 100, "long-blade": 100, "hand-to-hand": 100, "thaumaturgy": 100, "giantish": 100, "archery": 100, "impish": 100, "etiquette": 100 },
    armorValue: 0,
    tracks: [
      track("health", dice("wraith.health", 30, 90)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 15, weight: 0, minMetalToHit: "silver", team: "undead", lootTableKey: "I"
  }),
  // Orc Warlord (mobile 24) — table: hp 20-90, dmg 5-50, armor 0, level 16.
  actor("orc-warlord", {
    kind: "monster",
    mobileId: 24,
    stats: { strength: 100, intelligence: 70, willpower: 80, agility: 50, endurance: 75, personality: 50, speed: 50, luck: 50 },
    skills: { "backstabbing": 100, "long-blade": 100, "hand-to-hand": 100, "thaumaturgy": 100, "blunt-weapon": 100, "dodging": 100, "dragonish": 100, "etiquette": 100, "alteration": 100, "giantish": 100, "critical-strike": 100, "nymph": 100 },
    armorValue: 0,
    tracks: [
      track("health", dice("orc-warlord.health", 20, 90)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 700, team: "orcs", lootTableKey: "T"
  }),
  // Frost Daedra (mobile 25) — table: hp 25-130, dmg 50-100, armor -5, level 17.
  actor("frost-daedra", {
    kind: "monster",
    mobileId: 25,
    stats: { strength: 120, intelligence: 80, willpower: 30, agility: 80, endurance: 95, personality: 50, speed: 95, luck: 50 },
    skills: { "blunt-weapon": 100, "archery": 100, "centaurian": 100, "daedric": 100, "dodging": 100, "stealth": 100, "long-blade": 100, "hand-to-hand": 100, "axe": 100, "harpy": 100, "giantish": 100, "orcish": 100 },
    armorValue: -25,
    tracks: [
      track("health", dice("frost-daedra.health", 25, 130)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 17, weight: 800, minMetalToHit: "mithril", team: "daedra", lootTableKey: "J"
  }),
  // Fire Daedra (mobile 26) — table: hp 26-138, dmg 15-50, armor 1, level 17.
  actor("fire-daedra", {
    kind: "monster",
    mobileId: 26,
    stats: { strength: 150, intelligence: 110, willpower: 70, agility: 100, endurance: 95, personality: 90, speed: 95, luck: 50 },
    skills: { "blunt-weapon": 100, "centaurian": 100, "daedric": 100, "climbing": 100, "stealth": 100, "dragonish": 100, "impish": 100, "critical-strike": 100, "long-blade": 100, "harpy": 100, "giantish": 100, "axe": 100 },
    armorValue: 5,
    tracks: [
      track("health", dice("fire-daedra.health", 26, 138)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 17, weight: 800, minMetalToHit: "mithril", team: "daedra", lootTableKey: "J"
  }),
  // Daedroth (mobile 27) — table: hp 27-146, dmg 15-50, armor 1, level 18.
  actor("daedroth", {
    kind: "monster",
    mobileId: 27,
    stats: { strength: 150, intelligence: 110, willpower: 70, agility: 100, endurance: 95, personality: 90, speed: 100, luck: 50 },
    skills: { "blunt-weapon": 100, "archery": 100, "climbing": 100, "critical-strike": 100, "stealth": 100, "dodging": 100, "long-blade": 100, "hand-to-hand": 100, "dragonish": 100, "thaumaturgy": 100, "axe": 100, "etiquette": 100 },
    armorValue: 5,
    tracks: [
      track("health", dice("daedroth.health", 27, 146)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 18, weight: 400, minMetalToHit: "mithril", team: "daedra", lootTableKey: "E"
  }),
  // Vampire (mobile 28) — table: hp 28-154, dmg 20-50, armor -2, level 19.
  actor("vampire", {
    kind: "monster",
    mobileId: 28,
    stats: { strength: 100, intelligence: 90, willpower: 80, agility: 100, endurance: 95, personality: 100, speed: 100, luck: 50 },
    skills: { "backstabbing": 100, "blunt-weapon": 100, "daedric": 100, "long-blade": 100, "hand-to-hand": 100, "axe": 100, "critical-strike": 100, "giantish": 100, "etiquette": 100, "orcish": 100, "harpy": 100, "thaumaturgy": 100 },
    armorValue: -10,
    tracks: [
      track("health", dice("vampire.health", 28, 154)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 19, weight: 400, minMetalToHit: "silver", team: "undead", lootTableKey: "Q"
  }),
  // Daedra Seducer (mobile 29) — table: hp 27-146, dmg 15-50, armor 1, level 19.
  actor("daedra-seducer", {
    kind: "monster",
    mobileId: 29,
    stats: { strength: 150, intelligence: 60, willpower: 70, agility: 100, endurance: 95, personality: 120, speed: 70, luck: 50 },
    skills: { "blunt-weapon": 100, "dragonish": 100, "archery": 100, "daedric": 100, "stealth": 100, "long-blade": 100, "etiquette": 100, "critical-strike": 100, "harpy": 100, "axe": 100, "hand-to-hand": 100, "impish": 100 },
    armorValue: 5,
    tracks: [
      track("health", dice("daedra-seducer.health", 27, 146)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 19, weight: 200, minMetalToHit: "mithril", team: "daedra", lootTableKey: "Q"
  }),
  // Vampire Ancient (mobile 30) — table: hp 30-170, dmg 20-60, armor -5, level 20.
  actor("vampire-ancient", {
    kind: "monster",
    mobileId: 30,
    stats: { strength: 120, intelligence: 90, willpower: 80, agility: 90, endurance: 95, personality: 100, speed: 120, luck: 50 },
    skills: { "long-blade": 100, "blunt-weapon": 100, "daedric": 100, "archery": 100, "stealth": 100, "backstabbing": 100, "giantish": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: -25,
    tracks: [
      track("health", dice("vampire-ancient.health", 30, 170)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 20, weight: 400, minMetalToHit: "mithril", team: "undead", lootTableKey: "Q"
  }),
  // Daedra Lord (mobile 31) — table: hp 35-210, dmg 15-50, armor -10, level 20.
  actor("daedra-lord", {
    kind: "monster",
    mobileId: 31,
    stats: { strength: 150, intelligence: 110, willpower: 70, agility: 100, endurance: 95, personality: 90, speed: 120, luck: 50 },
    skills: { "axe": 100, "blunt-weapon": 100, "etiquette": 100, "dragonish": 100, "critical-strike": 100, "stealth": 100, "archery": 100, "long-blade": 100, "thaumaturgy": 100, "dodging": 100, "hand-to-hand": 100, "daedric": 100 },
    armorValue: -50,
    tracks: [
      track("health", dice("daedra-lord.health", 35, 210)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 20, weight: 1000, minMetalToHit: "mithril", team: "daedra", lootTableKey: "S"
  }),
  // Lich (mobile 32) — table: hp 30-170, dmg 70-100, armor -10, level 20.
  actor("lich", {
    kind: "monster",
    mobileId: 32,
    stats: { strength: 80, intelligence: 120, willpower: 95, agility: 90, endurance: 95, personality: 50, speed: 80, luck: 50 },
    skills: { "backstabbing": 100, "blunt-weapon": 100, "centaurian": 100, "archery": 100, "critical-strike": 100, "dragonish": 100, "giantish": 100, "orcish": 100, "harpy": 100, "thaumaturgy": 100, "long-blade": 100, "hand-to-hand": 100 },
    armorValue: -50,
    tracks: [
      track("health", dice("lich.health", 30, 170)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 20, weight: 300, minMetalToHit: "mithril", team: "undead", lootTableKey: "S"
  }),
  // Ancient Lich (mobile 33) — table: hp 30-170, dmg 70-100, armor -12, level 21.
  actor("ancient-lich", {
    kind: "monster",
    mobileId: 33,
    stats: { strength: 110, intelligence: 200, willpower: 95, agility: 90, endurance: 95, personality: 50, speed: 100, luck: 50 },
    skills: { "climbing": 100, "blunt-weapon": 100, "etiquette": 100, "dodging": 100, "critical-strike": 100, "thaumaturgy": 100, "long-blade": 100, "archery": 100, "axe": 100, "harpy": 100, "giantish": 100, "orcish": 100 },
    armorValue: -60,
    tracks: [
      track("health", dice("ancient-lich.health", 30, 170)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 21, weight: 300, minMetalToHit: "mithril", team: "undead", lootTableKey: "S"
  }),
  // Dragonling (mobile 34) — table: hp 14-42, dmg 5-15, armor 6, level 16.
  actor("dragonling", {
    kind: "monster",
    mobileId: 34,
    stats: { strength: 250, intelligence: 30, willpower: 70, agility: 100, endurance: 95, personality: 50, speed: 100, luck: 50 },
    skills: { "archery": 100, "axe": 100, "centaurian": 100, "critical-strike": 100, "climbing": 100, "stealth": 100, "blunt-weapon": 100, "streetwise": 100, "nymph": 100, "etiquette": 100, "lockpicking": 100, "daedric": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("dragonling.health", 14, 42)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 10000, team: "dragonlings"
  }),
  // Fire Atronach (mobile 35) — table: hp 25-130, dmg 5-15, armor 6, level 16.
  actor("fire-atronach", {
    kind: "monster",
    mobileId: 35,
    stats: { strength: 130, intelligence: 78, willpower: 80, agility: 90, endurance: 95, personality: 100, speed: 60, luck: 50 },
    skills: { "long-blade": 100, "blunt-weapon": 100, "archery": 100, "running": 100, "stealth": 100, "etiquette": 100, "illusion": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("fire-atronach.health", 25, 130)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 1000, team: "magic"
  }),
  // Iron Atronach (mobile 36) — table: hp 25-130, dmg 5-15, armor 6, level 16.
  actor("iron-atronach", {
    kind: "monster",
    mobileId: 36,
    stats: { strength: 140, intelligence: 78, willpower: 80, agility: 90, endurance: 95, personality: 100, speed: 55, luck: 50 },
    skills: { "long-blade": 100, "blunt-weapon": 100, "archery": 100, "running": 100, "stealth": 100, "daedric": 100, "illusion": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("iron-atronach.health", 25, 130)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 1000, team: "magic"
  }),
  // Flesh Atronach (mobile 37) — table: hp 25-130, dmg 5-15, armor 6, level 16.
  actor("flesh-atronach", {
    kind: "monster",
    mobileId: 37,
    stats: { strength: 125, intelligence: 78, willpower: 80, agility: 90, endurance: 95, personality: 69, speed: 61, luck: 55 },
    skills: { "long-blade": 100, "blunt-weapon": 100, "archery": 100, "running": 100, "stealth": 100, "dragonish": 100, "illusion": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("flesh-atronach.health", 25, 130)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 1000, team: "magic"
  }),
  // Ice Atronach (mobile 38) — table: hp 25-130, dmg 5-15, armor 6, level 16.
  actor("ice-atronach", {
    kind: "monster",
    mobileId: 38,
    stats: { strength: 150, intelligence: 78, willpower: 80, agility: 90, endurance: 95, personality: 69, speed: 65, luck: 60 },
    skills: { "long-blade": 100, "blunt-weapon": 100, "centaurian": 100, "running": 100, "stealth": 100, "climbing": 100, "illusion": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("ice-atronach.health", 25, 130)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 1000, team: "magic"
  }),
  // NOTE: mobile 39 (Horse (unused, but can appear in merchant-sold soul traps)) has no table stats in the donor; skipped.
  // Dragonling (mobile 40) — table: hp 14-42, dmg 5-15, armor 6, level 16.
  actor("dragonling-40", {
    kind: "monster",
    mobileId: 40,
    stats: { strength: 61, intelligence: 93, willpower: 80, agility: 80, endurance: 95, personality: 62, speed: 70, luck: 60 },
    skills: { "long-blade": 100, "blunt-weapon": 100, "archery": 100, "running": 100, "stealth": 100, "dragonish": 100, "illusion": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("dragonling-40.health", 14, 42)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 10000, team: "dragonlings"
  }),
  // Dreugh (mobile 41) — table: hp 13-34, dmg 5-15, armor 6, level 16.
  actor("dreugh", {
    kind: "monster",
    mobileId: 41,
    stats: { strength: 71, intelligence: 84, willpower: 80, agility: 80, endurance: 95, personality: 62, speed: 70, luck: 60 },
    skills: { "backstabbing": 100, "blunt-weapon": 100, "daedric": 100, "running": 100, "stealth": 100, "etiquette": 100, "illusion": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("dreugh.health", 13, 34)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 600, team: "aquatic", lootTableKey: "R"
  }),
  // Lamia (mobile 42) — table: hp 16-58, dmg 5-15, armor 6, level 16.
  actor("lamia", {
    kind: "monster",
    mobileId: 42,
    stats: { strength: 75, intelligence: 84, willpower: 80, agility: 85, endurance: 95, personality: 62, speed: 65, luck: 70 },
    skills: { "backstabbing": 100, "blunt-weapon": 100, "climbing": 100, "running": 100, "stealth": 100, "dragonish": 100, "illusion": 100, "critical-strike": 100, "axe": 100, "hand-to-hand": 100, "thaumaturgy": 100, "orcish": 100 },
    armorValue: 30,
    tracks: [
      track("health", dice("lamia.health", 16, 58)),
      track("stamina", constant(0)),
      track("magicka", constant(0)),
    ],
    level: 16, weight: 200, team: "aquatic", lootTableKey: "R"
  }),
];
