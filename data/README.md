# data/ — hand-authored experiment defaults

This directory may hold committed JSON defaults for the Dagger gameplay-lab
experiment document. It is not generated. `content/` is different: its files
are committed generated output and must be regenerated with
`scripts/regenerate.sh`, never hand-edited.

## Current convention (program 6682, tasks 6683 through 6685)

`experiments/privateers-hold-starter.json` is the active default consumed by
`dagger-runtime` and displayed by Dagger Lab.

- JSON is appropriate for plain values and tables. TypeScript authoring modules
  may produce the same immutable experiment document when typed builders make
  formulas or composition materially clearer.
- Do not require one file per future domain, a public schema-version strategy,
  or generated contracts before a real experiment needs them. The TS/Angular
  and Rust sides evolve as one internal lockstep contract for now.
- The current document authors named inputs, not expression syntax. `dagger-rpg`
  owns and explains fixed health, stamina, magicka, melee hit, and melee damage
  formulas for the player and the Rat gameplay definition; TS/Angular never evaluates gameplay
  semantics. Enemy gameplay keys to the Arena2-owned mobile ID and does not
  duplicate classic identity/name/sprite data. A closed
  expression vocabulary should be added only when a playable experiment needs
  more than named formula shapes.
- `dagger-runtime` installs a complete admitted experiment and owns live state.
  Angular edits use the same document and an explicit apply/reset loop. The
  runtime supplies each d100 roll, checks live range and collision line of
  sight, and owns Rat health mutation/death and semantic combat history.
- Validation should produce useful author errors for unknown fields,
  unsupported schema values, non-finite values, unusable ranges, and invalid
  derived results. Focused examples
  and regressions are sufficient; exact-fidelity matrices, replay proofs,
  fingerprints, per-value provenance, and revision governance are out of scope.
- Sibling games are references, not dependencies. See
  `docs/companion-reuse.md` for the current reuse boundary.
