# Daggerfall ruleset invariants

On top of the root `AGENTS.md`.

## Layout

- Every source file sits in the folder of the domain it serves, judged by what it does, not by its name. The project root holds no source file. The session's partials are all in `Session/`; a new rule is an owner in its domain's folder, never a session partial.
- References point down this order, never up it, within a tier as between tiers:
  1. `Content`
     - Content: reads packs into definitions, and holds the vocabulary and tables later domains give their meaning (doors, dungeon actions, weather, seasons, gates, sky time, stat modifiers, loot tables).
  2. `Facts`, `Scene` and `Text`, shared
     - Facts: the typed facts every domain may raise, with the attack and magic outcome vocabulary they carry.
     - Scene: what the site looks and sounds like (appearance, lighting, held art, sky, music, cinematics), and the small vocabulary drawing and rules share (ground containers, attack ids, racial kind, flight views).
     - Text: text resolution, name banks, the book reader and the notebook.
  3. `Character`, `Condition`, `Time`, `Items`
  4. `Magic`, `Afflictions`, `Combat`, `Actors`
  5. `Exterior`, `Interiors`, `Travel`
     - Exterior: the site as well as the outdoors: the site context and projection with the doors, locks and dungeon motion every site has, and weather, besides the streamed cells, terrain, horizon, ground cover, trees and rivers.
  6. `Society`, `Economy`, `Dialogue`
  7. `Quests`
  8. `UiProjection`, which reads everything before it
  9. `Developer`, then `Session`, which compose everything

  So no domain names the last three and nothing names `Session`. This list is the order's one home (the law reads the numbered lines; the purpose lines under them are for reading): `DomainOrderLawTests` in `tests/WorldRpg.Architecture.Tests` reads it, fails on a type that names a type in a later folder, and holds the few references accepted against it, each with its reason.
- A domain's owner takes what it uses as inputs, never `DaggerfallSession` or `DaggerfallState`, and is tested without the session. Its save and tuning records sit beside it; `DaggerfallSavePayload` and `DaggerfallTuning` compose them. What one rule does across domains is an ordered list in `Session/DaggerfallSession.Update.cs`, each line marked with its domain, like `ResolveExhaustion` (the collapse). An owner that passes time is handed "pass this much time" as an input the session binds to `AdvanceElapsedTime` with that owner's kind of interval, as `DaggerfallRest` is; it never advances `DaggerfallWorldTime` or calls the session for it. What it needs of later domains it may ask through an interface declared beside it that the session implements, like `IDaggerfallRestSurroundings`, or that the later domain's owner implements, like `IDaggerfallReactionSources`. State an owner keeps inside an effect's save entry is serialized by a JSON context declared beside it (`DaggerfallAfflictionJson`), not the session's.
- Tests mirror these folders in `tests/WorldRpg.Rulesets.Daggerfall.Tests`. Shared test support stays in that project's root and `Fakes/`.

## Gameplay

- `CombatResolution` stages keep their order when split: admission, contact, negation, calculation, mitigation, application.
- Read existing stat modifiers; don't apply their bonuses again. Keep calculated damage apart from health actually lost.
- Essential application never depends on a fact subscriber. Facts appended during a reaction wait for the next delivery.
- Change a rule in its owner; effects and items add to it as RuleEvent contributions.
- Saved live items must agree with the durable allocator.
- A new lifecycle feature decides what happens to owned items and native resources.
- A loose pile (dropped or quest-placed, not treasure or furniture) is a body near the player in `DaggerfallLooseItems`, and its pile's position is where the body rests, so the save keeps it there. A pile its owner places anew (a quest moving it) has its body made again there. A site change or origin rebase calls its `LetGo`. Read the bodies with `world.loose-items`.

## Actors

- Keep no second actor dictionary; enumerate the store.
- `EntityStore` holds strictly active-scene and resident-exterior actors; every live actor always carries real `StatsComponent`, `EffectsComponent`, `FactionComponent`, `ActorHarmPolicy`, `ActorInteractionPolicy`, and `ActorRenderComponent` (never nullable `StatsComponent` in live code; sparsity stays at the ledger/save boundary).
- `ActorLedger` (`Origin + SparseDelta`, self-pruning on empty delta) is the single world-scoped actor persistence layer for all actors (people and creatures alike): deterministic `0x4000_0000_0000L`-band IDs for `SeededSlot` / `CastIndividual` and monotonic allocator IDs for `Minted`. Untouched slots write zero save bytes; site admission skips home slots whose ledger `LocationOverride` is non-null (`AtSite` elsewhere, `WithParty`, `OnOverlandBand`, `OffStage`, `Dead`, `Removed`) so actors never duplicate.
- `WorldSocialSeed` lives in `DaggerfallTuning` (default `0`, snapshotted on save) and stays separate from `DaggerfallGameRandom.Seed`; per-slot generation hashes `Hash64(WorldSocialSeed, SiteId, SlotKey)` independently per slot.
- `ActorRenderComponent` sits in `Scene/` (Tier 2); rendering never branches on gameplay `Kind`. Gameplay rules read `ActorHarmPolicy`, `ActorInteractionPolicy`, and `FactionComponent` (regional people + slot guild/service factions for people, including interiors; 22 classic creature-family factions for creatures) rather than `Kind == StaticNpc/Civilian`.
- An actor's look is content: a template's or spec's `look` (`billboard`, `townsperson`, or `mobile` with its `mobileId`; `splat` names the art pack's character by gender), which specs inherit. The factory turns it into the render spec (`DaggerfallActorFactory.NpcVisual`, `DefinitionVisual`); its kind-based rule is only the fallback for content without one. A named character the art lacks draws the sprite, warned once.
- Remove an actor through `DaggerfallActorRoster`, which ends its effects and destroys its items; the directory alone doesn't.
- Place a new actor around the player with `DaggerfallActorPlacement.TrySpawnPose` and find foes with its `QueryEnemies`; don't cast placement rays or perception queries of your own.
- Bring enemies in close to fight with `world.spawn ACTOR COUNT METRES` (for example `world.spawn encounter-warrior 2 5`): each stands on clear floor within METRES, at least 2 m off, in sight of the player and facing them, hostile, through `DaggerfallNearbySpawn`, or the command says why it could not. A start pack's `foes` come the same way; the **Developer: combat start** bundle begins in Privateer's Hold as an armed warrior with two of them.
- Look at an actor with `actor.inspect ID|target|nearest` and follow an enemy's decisions with `actor.trace ID|off` (shown on the `actors` overlay, read back by running it again). Steer enemies for the run with `ai.freeze on|off`, `actor.passive ID on|off`, `actor.force-state ID STATE|off`, `actor.target ID TARGET|off` and `actor.hostile ID on|off`; they live in `DaggerfallEnemyOverrides`, read at `DaggerfallEnemyBehaviorModule`'s own inputs, and are never saved. A policy that decides for an enemy records its gates as `DaggerfallDecisionGate`s (policy, gate, outcome, reason, values) into the list the module opens for the traced actor; untraced actors get none. The trace also records each strike's and cast's target meant beside the one reached, from the delivery facts.
- An enemy's choices are its tactic's, `IDaggerfallEnemyTactic`, the one decision point behind `DaggerfallEnemyBehaviorModule`. The module keeps what is shared: the target (the team rule, `DaggerfallActorPlacement.SelectTarget`, with teams, restraint and infighting as the quests' inputs), perception, pursuit, the observation it assembles, the overrides, and the one action door per Engine update, which admits a chosen spell through casting and a strike through combat and tells the tactic what was admitted. The tactic returns the movement intent and the action, with its gates. A mobile record's `tactic` names it; `legacy` (`DaggerfallLegacyEnemyTactic`: the enhanced EnemyMotor, the classic spell choice, combat's bow-or-melee `EnemyAction`) is the default. Targets are any actor, not only the player. `EnemyTacticEquivalenceTests` holds the legacy tactic to the decisions recorded before the seam; only a change meant to alter them rewrites its baseline (`WORLDRPG_WRITE_TACTIC_BASELINE=1`), so opt in no mobile that fight uses (warrior, barbarian, orc shaman, archer) without meaning to.
- `intelligent` and `beast` are one scoring core, `DaggerfallScoredEnemyTactic`, with two menus. Its options come from what the enemy has: each attack combat lists (`EnemyAttackOptions`, made through the strike's chosen `Attack`), each spell the magic module lists (`SpellOptions`), repositioning and fleeing. Weights and flee numbers are tuning (`enemyBehavior.tactics`); courage and discipline are the mobile record's `tendencies`, refused on a tactic that doesn't weigh them. A traced enemy's every option is a `tactic` gate with its score. Fleeing is an intent: the Kit's `PursuitState.Flee` runs to a navigated destination (`PursuitFlee`), then the perception memory's `DisengagedFrom` leaves the target alone until struck (`Provoke`) or the time passes.
- Harm becomes facts through `DaggerfallDamageFacts`, whatever dealt it; a death is its `ActorDiedFact`.
- What a death does is the list `ReactToDeath` in `Session/DaggerfallSession.Update.cs`, after the quests' foes hear it in `React`; a new consequence is a line there, marked with its domain.
- A townsperson's race is the People of its site's climate cell, nothing else; a site whose climate names none has no townsfolk and is reported. An assembled town's wandering pool is its own region's people faction, never another region's.
- A walking townsperson draws as the gsplat character of its classic townsperson art: its race's `townspeople` archives in the base pack, picked by gender and name seed, and the art pack's `splatPeople` character stating that `archive`. Art without a character draws its sprite and is warned about once. `people.splat off` draws the sprites, `people.splat own` the characters again.
- Look at townsfolk close up with `people.spawn ID COUNT` (walkers in front of you as gsplat character ID), `people.stage COUNT METRES SPREAD`, `people.cross COUNT METRES SPREAD` and `people.unstage`, rather than hunting for them; `people.splat ID` draws every walker as one character, and `people.splat-look GRID COVERAGE own|none POSERATE` and `people.splat-shadow none|ellipsoids|splats` change their look for the run (the tuning's `splatPeople` holds the shipped one); a character with a hybrid body in the art pack (its splats over a skinned base mesh that fills their holes and casts their shadow) draws it in the tuning's `splatPeople.hybrid` look, and `people.splat-hybrid on|off` (`enemies.splat-variant hybrid|gaussians` for enemies) flips between the bodies for the run; `people.splat-pace slow|clip` walks long skirts at their skirt walk's own pace or plays it up to the walkers' pace; `people.splat-blinks on|off` stops or resumes blinks for the run. A character blinks when its GLB marks its eyes (`_GROUP` plus `asset.extras.eyes`, which `splat-people.py` carries into the payload): each walker on its own clock from its name seed, by the tuning's `splatPeople.blinks`, through the Engine's group tint. Each walker walks and stands in the clips its name seed picks from the art pack's `walks` (`skirtWalks` in a long skirt) and `idles`, at a rate the tuning's `gaitJitter` varies, from a seeded start phase.
- A facade property needs its component attached; use Engine `TryGet<T>` for an optional one.
- An effect definition that applies contributions supplies a resume callback, so a load never applies them twice.
- New spell families bind in `DaggerfallEffectCatalog`; an unmapped effect refuses before payment or skill use.
- Read equipment from the live facades, never a held `EquipmentState`. UI item keys are session-local; saves map them through `IdentityOf`.

## Combat

- No to-hit roll (DEC-12): contact decides delivery, mitigation decides what is taken.
- The swing arc is authored in view space, never traced from the drawn weapon. Presentation follows the attack clock.
- Draw at impact with `DaggerfallRandomKey`'s seed and key and `CombatRandomKey`'s scopes and salts; a whiff draws nothing. Every keyed draw takes `DaggerfallRandomKey.Seed` through the session's `DaggerfallGameRandom`, which mixes in the game's own seed (saved), so play differs between games and replays within one; what stays alike in every game is a scoped stream or the LCG, as `DaggerfallRandomKey` says. Spend costs at admission; interruption keeps the charged cooldown.
- Cooldowns count admitted simulation steps. In a catch-up update, apply input on the first step only and impacts once.
- Pending strikes, flights, the guard, reactions and hazards are transient: never saved, never replayed.
- Whether a player's attack can harm civilians is decided once, as it starts (`DaggerfallStrikeAim`).
- Hit feedback never moves the aim or overrides a gameplay rate another owner set.
- Keep `pathGroups` in step with the held definition's `items`.
- See a swing's arc: `world.overlay-on swing`, then `engine.inspect.overlay show set swing` after a first swing.
- A player input that does nothing (a rule refuses it, or it never reaches one) says why through `DaggerfallInputRefusals`; a new refusal or drop reports there too. `hud.readout-on` (the Developer panel's Input readout) shows the latest beside the player's attack, guard, cast and arms state.

## Exterior

- Collider, near and far ground read one height function (`DaggerfallHorizonGround.Ground`). The heightfields don't collide.
- Arrival and load hold play until ground stands under the player and every tracked actor, and the resource batches the Engine was preparing for the scene are drawn (`DrawnScene`); the update that clears the loading screen takes no step.
- The lift's floor is fixed on the map, never measured from the player; its fade is linear.
- Brightness comes from the lights, not exposure; the tone operator stays `Rolloff`.
- Rules read weather from `DaggerfallWeather.Conditions` (the session's `LocalWeather`), not from which sky draws.
- World sprites use `SpriteAlphaMode.Mask`; only glowing and translucent ones blend.
- Tune by eye for one run: `world.horizon-set LIFT FOG`, `world.ground-blend CONTRAST METRES SHARPNESS`, `world.weather KIND`, `world.ground-weather WET SNOW`, `world.hour HOUR`, `world.cloud-edges EDGE EROSION`.

Fuller account: `wiki/rusty-dagger/architecture`, `actors-and-mechanics`, `combat-rules`, `exterior-terrain` and `sky-light-weather`.
