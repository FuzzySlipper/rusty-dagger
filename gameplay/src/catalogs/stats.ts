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

export const stats = statsSection(ATTRIBUTES, SKILLS, TRACKS);
