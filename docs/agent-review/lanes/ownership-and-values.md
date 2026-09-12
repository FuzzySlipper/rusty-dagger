# Lane: Ownership and values

**Optional.** Use when the change crosses an ownership seam, adds tuning, or
touches Daggerfall vocabulary.

## One question

Does this change leak Daggerfall policy into Kit or Host, source-format quirks
into runtime, or authored and tunable values into incidental code?

## Basis required for an actionable finding

Name all three:

1. the actual assumption or value, quoted, with file and line;
2. its current owner and its correct owner, in `AGENTS.md`'s terms;
3. the affected uses — what breaks or becomes wrong when that value changes.

## Not a finding

- A demand for a universal abstraction, a new interface, or a generic factory.
- A constant for every literal. Compact structural constants stay beside the
  algorithm that owns them; only genuinely adjustable or authored values are
  promoted.
- Kit code that is merely world-RPG shaped. Kit is allowed to be opinionated
  about world-RPG mechanisms; it is not allowed to mention Daggerfall, Arena2,
  Privateer's Hold, or DFUnity vocabulary.
- A rename that moves Daggerfall vocabulary somewhere without changing who owns
  the decision.
