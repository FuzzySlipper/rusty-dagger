# Lane: Engine reuse

**Always on.** Run this lane on every task.

## One question

Does this change recreate a mechanism that is already safely available upstream
in `Rusty.Engine`?

## Why

A downstream agent that cannot find a capability will build one. That produces a
second implementation of something the Engine already guarantees, which then
drifts, and it hides the real signal: either the Engine API should be used, or it
is genuinely missing and deserves one narrow upstream request. Product policy
belongs downstream; guarantees do not.

## Basis required for an actionable finding

Name all four:

1. the current safe Engine API that already covers the behavior;
2. the local duplicate the change introduces, at file and line;
3. the concrete adoption or replacement path;
4. whether the difference is an Engine guarantee or product policy.

A finding that names only "this looks like something the Engine might do" is not
actionable. Verify the contract in the generated safe C# surface before claiming
it exists, and prefer `obj/`-generated bindings plus the packaged SDK over
recollection.

## Not a finding

- Engine machinery that is not exposed safely. That is an upstream gap, not a
  duplicate — report it as a gap and say so.
- Product policy implemented downstream on top of an Engine mechanism. That is
  the intended split.
- A local helper that is genuinely narrower than the Engine API and does not
  duplicate its guarantees.
