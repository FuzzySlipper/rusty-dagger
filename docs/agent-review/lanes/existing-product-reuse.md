# Lane: Existing product reuse

**Always on.** Run this lane on every task.

## One question

Does this change create a competing mechanism instead of extending
rusty-dagger's existing owner for that behavior?

## Why

The same failure as upstream reinvention, one level down. An agent working from a
task description will add a new service, catalog, or state holder without
noticing that this repository already owns that concept. The result is two owners
for one concept, which is worse than an absent feature because both look correct
in isolation.

## Basis required for an actionable finding

Name all four:

1. the existing owner in this repository, by file and type or member;
2. the new duplicate, by file and line;
3. the overlapping state, behavior, or authority the two share;
4. the consequence — which callers now disagree, or which invariant can no longer
   hold — and the relevant callers by name.

Search before concluding: the existing owner may live in `src/WorldRpg.Kit/`,
`src/WorldRpg.Rulesets.Daggerfall/`, `src/WorldRpg.Host/`, or `Daggerfall.Import`
depending on what the concept is. Read `AGENTS.md` for the current ownership
table and `docs/gameplay-design.md` for the discovery table rather than
assuming from a directory name.

## Placement and mechanism shape

When the change adds gameplay behavior rather than duplicating it, check two
further things as part of this same lane:

1. **Kit vs ruleset home.** Reusable execution and extension points belong in
   Kit; formulas, eligibility, capacities, timing policy, content meaning,
   and special rules belong in Daggerfall. A reusable mechanism landed in
   Dagger modules, or Dagger policy landed in Kit, is a finding with the
   same four-part basis: the correct owner, the landed location, the shared
   concept, and which future callers now look in the wrong place.
2. **RuleEvent path and modularity.** When independently authored rules can
   contribute to an interaction, the change should use the existing typed
   resolution (e.g. `CombatResolution` with `TryHitEvent` / `DamageEvent` /
   `ApplyHitEvent`) and explicit composition (`DaggerfallState.Kit`
   `GameplayServices`, named services, typed notifications) instead of a new
   ad-hoc resolution, generic bus, ambient `Resolve<T>()`, reflection scan,
   or gameplay DSL. A parallel resolution path is a finding with the same
   four-part basis: the existing resolution owner, the new parallel path,
   the overlapping participants, and which callers now resolve through
   different machinery.

## Not a finding

- A new file, or a similar name. Overlap must be shown in state or behavior.
- Extension of an existing owner that happens to add a type, member, or file.
  That is the desired outcome of this lane.
- A deliberate second implementation that the task explicitly calls for, for
  example a canary that must not share the production path.
- Simple reads/actions done through direct methods rather than the RuleEvent
  path. Typed resolution is for interactions with real participant
  contributions; demanding it for simple work is ceremony, and belongs to
  the runtime-trust lane.
