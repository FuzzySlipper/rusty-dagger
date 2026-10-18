# Contact + mitigation variant

`daggerfall.contact` is a compiled rules variant of the Daggerfall ruleset: a
deliberate deviation from classic combat that a game bundle selects. The
`daggerfall.contact` bundle selects it over the same content packs as
`daggerfall.classic`; its own tuning profile (`daggerfall.contact`, payload
`content/worldrpg/variant-tuning-payloads/daggerfall.contact.json`) holds every
value below. Classic combat, recorded in
[combat-behavior-baseline.md](combat-behavior-baseline.md), is unchanged: the
variant replaces whole seam owners and no classic owner branches on it.

Code lives in `src/WorldRpg.Rulesets.Daggerfall.Variants/`; the seams and their
classic owners live under `src/WorldRpg.Rulesets.Daggerfall/Variation/` and
`Modules/Combat/`. A save records the variant set it was made under, and the
Host refuses to load it under another set.

## What changes

Classic decides a blow with a d100 to-hit roll at admission. Under the variant
there is no to-hit roll: a blow hits whatever its body physically meets, and the
target's mitigation decides what it takes.

| Concern | Under the variant | Owner |
| --- | --- | --- |
| Melee delivery | A swing, targeted or not, waits for its hit frame and reaches every actor body its swing volume (a tuned box ahead of the attacker's eye, as deep as the attack's reach) touches there, through `ActorContact.Volume` with world occlusion. It may reach an actor it was not aimed at, several, or none, and it still resolves if its aimed target fell first. | `DaggerfallContactDelivery` (physical delivery seam) |
| Ranged delivery | An arrow flies segment by segment each admitted step from the shooter's eye and harms the first actor body it meets; the world stops it. | `DaggerfallContactDelivery` |
| Mitigation | The weapon-material requirement still makes a target immune. Deflection is the target's chance (base, Dodging, Agility above average): on a channel tuned full it negates the blow before damage is rolled, on a partial channel it turns aside a share. Absorption is the armour or shield over the struck body part, a monster's natural armour, and the player's worn armour enchantments, scaled per channel. | `DaggerfallContactMitigation` (physical mitigation seam) |
| Skills | Dodging is deflection and records a use on every enemy blow that connects with the player. Critical Strike (with a held enchantment's attack-chance shift) is a chance of bonus damage. A weapon skill records a use on every connect, deflected or not. Backstab keeps its chance of tripled damage from behind; a monster's natural attack lands the slots that clear the player's reflexes; a whiff runs no Strikes payload. | `DaggerfallContactDelivery`, `DaggerfallContactMitigation` |
| Block | Holding the product's `block` intent raises the player's guard while a weapon is drawn and the player is free to act. It costs stamina each second, adds a deflection bonus (more behind a shield), stays down for a swing's whole strike, and keeps action-driven time running while it rises, holds or settles. The guard is transient and not saved. Enemies do not block. | `DaggerfallContactGuard` (player guard seam) |
| Spells | Missiles fly on contact as in classic. A touch spell reaches every actor its touch volume contacts at the cast frame; a touch that meets no one is still released and paid for, and misses. Spell channels are elemental and keep classic resistance. | `DaggerfallContactTouch` (touch delivery seam) |
| Presentation | A whiff (`AttackWhiffedFact`) sounds the swing alone; a deflection (`AttackNegatedFact`) sounds the donor parry family; a blow the armour took whole is an unpitched hit with no hurt state; a landed blow reads as classic, with what was deflected and absorbed (`AttackHitFact.Mitigated`) appended to its line. | `DaggerfallSiteAppearance`, `DaggerfallOutcomePresentation` |
| Enemy AI | A melee enemy closes to a tuned fraction of its reach before it swings and spaces its maneuvers around that distance. Each enemy swing publishes its wind-up (`EnemyAttackTelegraphFact`) and its damage lands no sooner, however early the strike's frame comes. An archer leads a moving target by the arrow's flight time and holds a shot another body or the world would stop, before spending an arrow. | `DaggerfallContactEngagement` (enemy engagement seam), `DaggerfallContactDelivery` |

## Tuning

The variant's payload groups its values by concern: `melee` (swing volume and
default reach), `projectile` (arrow speed and default range), `critical`,
`deflection` (chances and the full or partial mode per channel), `absorption`
(per armour, shield and armour-value point, monster baseline, per-channel
multipliers), `guard`, `touch` (touch volume) and `engagement` (melee engage
fraction, enemy wind-up, archer lead).
