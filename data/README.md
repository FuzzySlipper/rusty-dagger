# data/ — committed authored data

This directory holds committed data files: encounter routes, content overlays
like `sprite-names.json` (display nicknames that `dagger-import` consults when
naming exported billboard files), and the generated gameplay packages under
`gameplay/`. It is not hand-edited at runtime. `content/` is different: its
files are committed generated output (regenerated with
`scripts/regenerate.sh`); hand edits are legitimate there but are overwritten
by a full regeneration — see the `content/` posture in AGENTS.md.

## Gameplay packages

`gameplay/dagger-core.package.json` is generated output of the TypeScript
authoring workspace in `gameplay/src` (`pnpm gameplay:build`) and is
drift-checked by `pnpm gameplay:check`. It is the only source of gameplay
truth: `dagger-rpg` admits it, and the runtime, diagnostics, and Dagger Lab
(read-only explorer) all consume the admitted form. See
`docs/gameplay-resolution.md` and `gameplay/README.md`.

## Encounters

`encounters/privateers-hold.json` and `encounters/encounter-gallery.json`
author the two compact named combat routes used by the committed product and
its focused test room. They contain names, objectives, physical route keys,
and admitted entity membership only. Rust owns activation, victory/defeat,
reset, AI, and combat semantics. Encounter content is migrating into the
gameplay catalogs (`gameplay/src/catalogs/encounters.ts`); these files remain
the runtime's installed route source until that join is complete.

- JSON is appropriate for plain values and tables. TypeScript authoring modules
  own anything with semantics: formulas, actions, actors, rules.
- Validation should produce useful author errors for unknown fields,
  unsupported schema values, non-finite values, unusable ranges, and invalid
  derived results. Focused examples
  and regressions are sufficient; exact-fidelity matrices, replay proofs,
  fingerprints, per-value provenance, and revision governance are out of scope.
- Sibling games are references, not dependencies. See
  `docs/companion-reuse.md` for the current reuse boundary.
