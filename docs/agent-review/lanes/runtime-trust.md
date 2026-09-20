# Lane: Runtime trust

**Always on (temporary counterbalance).** Run this lane on every task until
the runtime refactors have landed and ordinary trusted-path code is the
established gravity. When that happens, demote this lane to optional or
retire it; do not keep it as a permanent tax.

## One question

Does this change add validation, verification, or defensive machinery to a
trusted first-party runtime path without a concrete failure it prevents?

## Why

The import side of this repository has strong gravity toward checking:
offline bounds/decoding, provenance, and build-time digests are correct
there. That thinking leaks into runtime, where it does not belong. This is a
single-player product built from trusted first-party code; there is no
cheating, MITM, wire-corruption, or future-multiplayer threat model that
justifies policing every mutation.

The recent refactors deliberately removed that ceremony: facts-based
propose/validate/mutate paths, per-action proposal/accept/commit protocols,
revision guards, whole-state snapshots, rollback around terminal callbacks,
and repeated SHA/hash admission of already-admitted bytes. See
`docs/gameplay-design.md` ("Direct mutation, concrete safeguards", "Admit
content once") and rusty-engine Board post 147 ("Default to trust"). A
reviewer that re-asks for the removed machinery undoes the refactor.

## Basis required for an actionable finding

Name all four:

1. the ceremony, quoted, with file and line — e.g. a propose/validate step,
   a hash/reverify on a cache hit or admitted payload, a revision guard or
   snapshot/rollback around ordinary gameplay, a compatibility fingerprint
   or schema-version gate on current development data;
2. why the path is trusted — admitted definitions, attached live components,
   Engine-delivered resources, or current-schema save state the owning
   boundary already established;
3. the concrete failure it claims to prevent, and why that failure has no
   identified caller — or where the owning check already establishes it;
4. the simpler shape: direct mutation, the existing safeguard, deletion, or
   the boundary the check actually belongs at (usually offline import).

A finding that names only "this could be invalid" is not actionable. Name
the caller, the input that reaches the path, and what goes wrong without
the machinery.

## Where checking belongs

- Offline import (`Daggerfall.Import`, source bounds/decoding, provenance,
  build-time artifact digests): validation is expected. Not this lane's
  target.
- Runtime (`WorldRpg.Kit`, `WorldRpg.Host`, Daggerfall session/persistence,
  admitted content, attached state, Engine services): default to trust.
  Keep checks that protect an actual requirement: gameplay eligibility,
  track bounds, valid current content references, coherent save
  relationships, ABI and native lifetime, and understandable failure on
  malformed current data rather than partial success. Optional atomic
  inventory edits for a genuinely all-or-nothing multi-item grant or
  payment/transfer are appropriate; they do not establish a per-action
  transaction protocol.
- Explicitly requested save capture, previews, tooling, or a stated
  multiplayer requirement may justify stronger machinery at its own
  boundary. It never defines the baseline for ordinary gameplay.

## Not a finding

- Import-side bounds, decoding, provenance, or build-time digests.
- A concrete safeguard for an actual requirement listed above, kept local
  to the owner that establishes it.
- An Engine decoder or native-safety error the product surfaces and stops
  on.
- A stronger mechanism the task explicitly requires at a named boundary
  (tooling, explicit save capture, stated multiplayer need).
- A compact structural constant beside the algorithm that owns it. That is
  the ownership lane's call, not this one.
