# Daggerfall ruleset invariants

On top of the root `AGENTS.md`.

## Layout

- Every source file sits in the folder of the domain it serves, judged by what it does, not by its name. The project root holds no source file. A `DaggerfallSession.X.cs` partial goes in its domain's folder.
- References point down this order, never up it:
  1. `Content`
  2. `Scene` and `Text`, shared services
  3. `Character`, `Items`, `Time`, `Condition`
  4. `Actors`, `Magic`, `Afflictions`, `Combat`
  5. `Interiors`, `Exterior`, `Travel`
  6. `Economy`, `Society`, `Dialogue`
  7. `Quests`

  `Session` and `Developer` compose everything and `UiProjection` reads everything; nothing references those three. Existing code still has references against the order. Don't add new ones.
- A domain's owner takes what it uses as inputs, never `DaggerfallSession` or `DaggerfallState`, and is tested without the session. What one rule does across domains is an ordered list in `Session/DaggerfallSession.Update.cs`, each line marked with its domain, like `ResolveExhaustion` (the collapse).
- Tests mirror these folders in `tests/WorldRpg.Rulesets.Daggerfall.Tests`. Shared test support stays in that project's root and `Fakes/`.

## Gameplay

- `CombatResolution` stages keep their order when split: admission, contact, negation, calculation, mitigation, application.
- Read existing stat modifiers; don't apply their bonuses again. Keep calculated damage apart from health actually lost.
- Essential application never depends on a fact subscriber. Facts appended during a reaction wait for the next delivery.
- Change a rule in its owner; effects and items add to it as RuleEvent contributions.
- Saved live items must agree with the durable allocator.
- A new lifecycle feature decides what happens to owned items and native resources.

## Actors

- Keep no second actor dictionary; enumerate the store.
- Remove an actor through `DaggerfallActorRoster`, which ends its effects and destroys its items; the directory alone doesn't.
- A facade property needs its component attached; use Engine `TryGet<T>` for an optional one.
- An effect definition that applies contributions supplies a resume callback, so a load never applies them twice.
- New spell families bind in `DaggerfallEffectCatalog`; an unmapped effect refuses before payment or skill use.
- Read equipment from the live facades, never a held `EquipmentState`. UI item keys are session-local; saves map them through `IdentityOf`.

## Combat

- No to-hit roll (DEC-12): contact decides delivery, mitigation decides what is taken.
- The swing arc is authored in view space, never traced from the drawn weapon. Presentation follows the attack clock.
- Draw at impact with `CombatRandomKey`; a whiff draws nothing. Spend costs at admission; interruption keeps the charged cooldown.
- Cooldowns count admitted simulation steps. In a catch-up update, apply input on the first step only and impacts once.
- Pending strikes, flights, the guard, reactions and hazards are transient: never saved, never replayed.
- Whether a player's attack can harm civilians is decided once, as it starts.
- Hit feedback never moves the aim or overrides a gameplay rate another owner set.
- Keep `pathGroups` in step with the held definition's `items`.
- See a swing's arc: `world.overlay-on swing`, then `engine.inspect.overlay show set swing` after a first swing.

## Exterior

- Collider, near and far ground read one height function (`DaggerfallHorizonGround.Ground`). The heightfields don't collide.
- Arrival and load hold play until ground stands under the player and every tracked actor.
- The lift's floor is fixed on the map, never measured from the player; its fade is linear.
- Brightness comes from the lights, not exposure; the tone operator stays `Rolloff`.
- Rules read weather from `DaggerfallSession.CurrentConditions`, not from which sky draws.
- World sprites use `SpriteAlphaMode.Mask`; only glowing and translucent ones blend.
- Tune by eye for one run: `world.horizon-set LIFT FOG`, `world.ground-blend CONTRAST METRES SHARPNESS`, `world.weather KIND`, `world.ground-weather WET SNOW`, `world.hour HOUR`.

Fuller account: `wiki/rusty-dagger/architecture`, `actors-and-mechanics`, `combat-rules`, `exterior-terrain` and `sky-light-weather`.
