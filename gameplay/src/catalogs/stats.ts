/**
 * Declared actor vocabulary: classic Daggerfall attributes, skills, and the
 * resource tracks used by current content. Actor definitions may only use
 * ids declared here; adding a new stat/skill/track is a one-line edit in
 * this file (plus whatever derived rules consume it).
 *
 * Donor provenance: Daggerfall Unity `API/DFCareer.cs` (Stats and Skills
 * enums) — adopted as stable classic-named ids.
 */

import { statsSection } from "../authoring/mod.js";

export const ATTRIBUTES = [
  "strength",
  "intelligence",
  "willpower",
  "agility",
  "endurance",
  "personality",
  "speed",
  "luck",
  // Classic player-chosen reflex enum 0=very-high..4=very-low (donor
  // `PlayerReflexes`, EntityEnums.cs:171-178), declared as a stat so
  // expressions can read it; semantically it is not a 0..100 attribute.
  "reflexes",
] as const;

export const SKILLS = [
  "medical",
  "etiquette",
  "streetwise",
  "jumping",
  "orcish",
  "harpy",
  "giantish",
  "dragonish",
  "nymph",
  "daedric",
  "spriggan",
  "centaurian",
  "impish",
  "lockpicking",
  "mercantile",
  "pickpocket",
  "stealth",
  "swimming",
  "climbing",
  "backstabbing",
  "dodging",
  "running",
  "destruction",
  "restoration",
  "illusion",
  "alteration",
  "thaumaturgy",
  "mysticism",
  "short-blade",
  "long-blade",
  "hand-to-hand",
  "axe",
  "blunt-weapon",
  "archery",
  "critical-strike",
] as const;

export const TRACKS = ["health", "stamina", "magicka"] as const;

/**
 * Classic body parts in donor `BodyParts` enum order (EntityStructs.cs) —
 * adopted. Each part becomes an `armor-<part>` stat (classic sbyte range
 * -128..=127; good gear drives armor negative); the struck-body-part roll
 * table indexes this order.
 */
export const ARMOR_PARTS = [
  "head",
  "right-arm",
  "left-arm",
  "chest",
  "hands",
  "legs",
  "feet",
] as const;

/**
 * Progression stats for the kill-XP experiment profile (see derived.ts):
 * wide-range counters (bounds 0..=1_000_000 in the mechanics catalog, not
 * the classic 0..=100 attribute range). The Rust spawn authority attaches
 * them to player-kind actors only (xp 0, level 1); they are never keys in
 * an actor's `stats` map, and live evaluation materializes them from the
 * entity's component, like the `armor-<part>` stats.
 */
export const PROGRESSION = ["xp", "level"] as const;

export const stats = statsSection(ATTRIBUTES, SKILLS, TRACKS, ARMOR_PARTS, PROGRESSION);
