# Daggerfall coverage scope and decisions

This is the normative scope contract, behavioral split checklist and decision
register (DEC-01–DEC-11) that coverage tasks cite for exclusions and selected
differences. It complements the [coverage plan](daggerfall-coverage-plan.md).
Den owns tasks, their inventory-ID mapping and their dependencies. The original
preparation narrative, capability ordering and point-in-time source anchors are
archived as `[doc: rusty-dagger/daggerfall-task-preparation-2026-09]`.

## Companion inventories

| Document | Contents |
| --- | --- |
| [Coverage plan](daggerfall-coverage-plan.md) | Twelve-area order, ownership, exclusions and drift-review posture. |
| [Feature ledger](coverage/feature-ledger.md) | Stable F001–F141 IDs with dispositions and named remaining behavior; aliases name their canonical row. |
| [Supplemental behaviors](coverage/supplemental-behaviors.md) | SUP IDs for cross-cutting behavior, including every registered dungeon-action flag. |
| [Magic inventory](coverage/magic-inventory.md) | Named effects, helpers and behavior families behind the broad magic rows. |
| [Quest inventory](coverage/quest-content-inventory.md) | Quest actions and quest-source/catalog scope behind the broad quest rows. |
| [Content scope](coverage/content-scope.md) | Source-family inventory and the authored-content target. |

Read the applicable rows and the current owning source before specifying a task.
Do not turn each file, row or ID into exactly one ticket: a coherent behavior can
involve several IDs, and a broad ID can need several tasks with stable child
identifiers. Every retained leaf must be covered by some task; aliases and
excluded rows must not generate duplicate or pointless work. Inventory counts
include excluded helpers and demos; they are not counts of required tasks,
completed features or usable runtime records.

## Scope contract

Baseline target: the classic game’s playable world, character development,
physical/social/magic systems, main and side quests, and content needed by those
behaviors. Use the local original data and the surveyed DFU implementation as
references. The target is not only the currently published Privateer's Hold pack.

Authored records are part of the backlog: importer support without published and
consumable records does not finish a content family. Conversely, not every raw
unused archive entry needs a new gameplay feature. Every discovered source record
gets an honest imported, required-pending, unused, duplicate, excluded, or unresolved
disposition when that family's importer/publication task enumerates it.

Retain the coverage plan's explicit exclusions: Unity/bootstrap/distribution
topology, runtime Arena2 setup, donor singleton orchestration, Unity serialization
and import bridges, widget/window implementation shapes, and MIDI playback.
Ordinary audio/music remains included, with its long-duration exercise separate.

Do not silently add DFU mods, mod message protocols, editor tools, replacement
graphics packages or optional convenience behavior to the baseline. Keep candidate
DFU-only records in the inventories so they can be assessed instead of disappearing.
Original-language text and adapted UI functionality are the initial baseline;
localization expansion and binary classic/DFU save compatibility are not assumed.

Existing owners are extended unless a task explains a concrete reason to replace
them. A new mechanism must not become a second implementation of an existing
operation; see [gameplay design](gameplay-design.md) for the current entry points.

## Behavioral splits

This is an enumeration checklist, not a prescription for one ticket per bullet.
The feature and specialist inventories provide donor references for these splits.

- **Character:** race/gender/name/face, career/custom class and advantages, reflexes,
  background answers, starting values/items/spells, permanent/live values, skill-use
  attribution, advancement eligibility, level-up choices and gains. Preserve the
  distinction between a formula and every event that invokes it.
- **Combat:** weapon versus unarmed selection, swing/proficiency/race/backstab and
  enemy-type factors, body part, material gates, hit chance, enemy multi-attacks,
  damage and equipment wear, poison/on-hit effects, death/corpse/rewards, ranged
  ammunition/delivery, equipped presentation and attack timing. Effects and items
  share accepted hit results instead of recomputing damage independently.
- **Items:** all selected template categories, material/variant, stackability,
  uniqueness, condition/repair, identification, equip restrictions, weight/money,
  use/consume/drop/transfer, loot generation, stolen/quest/enchantment meaning and
  held/strike/used effects. State follows items across player, corpse, shop and wagon.
- **Movement/world:** normal locomotion modes, fatigue/breath/fall consequences,
  activation modes, locks/doors/bashing, interior/exterior/dungeon transitions,
  terrain/city/dungeon data, water, moving actions/triggers, discovery, time/weather,
  NPC presence and encounter selection. Mechanical support belongs to Engine.
- **Society:** reaction/reputation and faction relations, membership/rank/expulsion,
  service eligibility, theft/witness/guard consequences, arrest/court/prison/fines,
  trading/repair/identification/training, tavern lodging, account/credit/loan/default,
  property/transport purchase, dialogue/directions/rumors and guild quest offers.
- **Magic:** use every leaf of the magic inventory, including duration/chance,
  magnitude, targeting/element, saves/resistance, caster/target lifetime and cleanup;
  recipes and spell/item construction are distinct from casting/using the result.
- **Quests:** use every retained resource/action leaf, including pre-spawn queued
  operations, deadlines during time skips, resource relocation, lifecycle cleanup,
  save/load and main/guild/misc content binding. No unimplemented action is success.
- **UI/audio:** information, choices and consequences belong to the respective
  subsystem task. Name required actions/projections, not DFU window classes. Music
  selection/loop lifetime and the long-duration Engine exercise remain separate.

### Skill-use attribution

A skill use is recorded by the admitted operation that makes it real, with the
typed reason, outcome, amount and cadence its Daggerfall policy declares. Weapon
skill and Critical Strike are tallied on a resolved player hit; Dodging on every
resolved enemy attack against the player, including a miss; Backstabbing on the
facing-away check. Each released spell effect records a use, and a cancelled cast
records nothing. Running records on each admitted running update and Jumping
on an admitted ground-to-jump transition; Climbing on climbing checks and admitted
rappelling updates; Swimming and Stealth once per admitted game-minute interval,
so a repeated minute after a save or repeated update is rejected. Medical records
after accepted recovery rest; Lockpicking after its duplicate-at-skill rejection;
Pickpocket on player attempts; shoplifting attempts and completed Mercantile
trades on their operations. A language records one use on successful pacification
or a failed non-Etiquette/non-Streetwise attempt; Etiquette or Streetwise records
the first tone resolution in an NPC talk session and the selected court response.
Advancement consumes the saved counters and does not itself emit a use. These are
classic one-use events: no DFU three-use pacification boost or one-in-four running
throttle is adopted without an explicit later decision.

### Ordering rules

Quest start/end and common effect admission precede special effects that start
quests and quest actions that cast spells. Do not make “all magic” and “all
quests” depend on one another; the same applies to early NPC identity versus later
civilian wandering, and faction state versus guild quests.

Persistent door/lock state, trigger/action links, quest placement and subsequent
unload/reload must refer to the same identities. Capturing a visible door's state
is not enough if the next load recreates it from unchanged authored data.

## Decision register

A local unresolved detail blocks its affected task specification, not unrelated
work. Record a specific source comparison and decision in the owning task; do not
issue vague research tickets for every formula or silently choose whichever
implementation is easier. If a source comparison exposes a material scope choice
not settled here, isolate and describe that choice while continuing unrelated work.

| ID | Decision / working baseline | Remaining task-local work |
| --- | --- | --- |
| DEC-01 | Classic behavior is the baseline; DFU supplies reverse-engineering evidence and explicitly selected fixes. | For any known difference, identify source behavior and select it explicitly. Unrelated parts can be drafted immediately. |
| DEC-02 | Classic skill-use progression is required; existing experimental kill-XP leveling is not its substitute. | Define skill-use events, advancement and rest/level-up integration in F016 tasks; preserve unrelated experimental tooling unless removal is scoped. |
| DEC-03 | Donor enemy damage selection intentionally differs from classic: `FormulaHelper.CalculateAttackDamage` may prefer stronger unarmed damage for armed enemies. Follow classic equipped-weapon semantics for baseline coverage. | Verify each enemy/career's authored attack data while drafting F011/F020. Do not copy this DFU choice accidentally. |
| DEC-04 | Rusty save semantics are included; reading/writing binary classic/DFU saves is outside the default scope. | Define product schema changes and same-product save behavior; don't implement donor serialization classes. |
| DEC-05 | Base-language text and functional DOM workflows are included. Exact Unity UI layout, widget system, retro postprocess mode, localization expansion and mod protocols are outside baseline. | Preserve gameplay information/actions. Any optional extension receives its own explicit scope, not a hidden prerequisite. |
| DEC-06 | Classic quests are required. DFU-only sources remain individually visible as candidates; inclusion follows behavior/content provenance, not filename availability alone. | Use quest inventory/catalog membership when grouping tasks. Classify helpers needed by retained classic behavior separately from optional added quests. |
| DEC-07 | Source-format quirks and static deterministic generation belong in Import where possible. Runtime randomness uses Engine. | If a dynamic classic sequence is required, specify the exact observable property and verify safe Engine support; no general downstream RNG port. |
| DEC-08 | Existing Engine capabilities are reuse candidates, not hypothetical blockers. | Verify required property in safe packaged API during task drafting; create a narrow upstream task only for a confirmed gap. |
| DEC-09 | Ordinary music looping remains included; MIDI excluded. Local MP3 is an input candidate, not a promised supported runtime format. | Pick a user-provided track, supported admission/conversion path and bounded duration when drafting the audio experiment. Its input is not needed for other tasks. |
| DEC-10 | Full original content coverage is the target; unused/duplicate/malformed records need individual dispositions. | Import/publication tasks enumerate record identities and resolve exceptions; filesystem counts alone do not establish usable content or parity. |
| DEC-11 | When original semantics remain unknown but DFU implements a concrete behavior, use that behavior as an explicitly provisional donor baseline unless it conflicts with a settled decision. | Record the uncertainty and the actual chosen value/rule in the owning task; do not invent a value, claim exact classic fidelity, or require an open-ended reverse-engineering exercise before implementation. Reconcile later if better evidence appears. |
