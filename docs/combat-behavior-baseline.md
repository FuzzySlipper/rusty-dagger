# Combat timing baseline

This records the current, source-backed combat timing, randomness, cooldown and
save behavior. It describes existing behavior, not a new design requirement.
Later work should preserve it or explicitly record a gameplay decision.
[combat-contact-rules.md](combat-contact-rules.md) records how a blow connects
and what the target does to it.

## Policy and owners

Dagger sessions route attacks through `DaggerfallState.Kit`. Kit owns the attack
lifecycle (`AttackExecution<TFact>`: readiness, cooldown, pending impact and
interruption, over the attached `AttackState`), its input/AI entry points
(`AttackCapabilities<TFact>`) and target selection (`TargetingService`).
`DaggerCombatRules` supplies Daggerfall admission, formulas, application and
effects through Kit `CombatResolution`; when harm is decided and whom it reaches
is the physical delivery seam's owner (`DaggerfallContactDelivery`), and
what the target does to it is the physical mitigation seam's owner (`DaggerfallContactMitigation`). Kit Ai and Loot mechanisms coordinate
behavior and corpse handling around those rules. `DaggerSessionPersistence`
saves a charged cooldown, but held input, AI/perception work, native
continuation, presentation, and an in-flight attack are transient or rebuilt
after restore. Save slots are managed by the Host's `WorldRpgSaveSlots` from the
game menu.

Paths below are relative to `src/WorldRpg.Rulesets.Daggerfall/` unless stated;
Kit attack types are under `src/WorldRpg.Kit/Combat/`.

| Concern | Current behavior | Source owners |
| --- | --- | --- |
| Player timing | The rules' attack clock (`DaggerfallPlayerAttackClock`) owns the player's attack, stepped by admitted steps; presentation only follows it. A press released before the power threshold is a fast attack, and one held past it charges a power attack (`powerIn`, then `powerHold` until release, then `powerOut`). A press during a recovery chains the next fast attack (`fastA` to `fastD`, then round again), one made earlier in a swing waits for the recovery, and a swing that runs out resets the chain. The kick has its own binding; a bow plays `shot`. Each motion has a windup, a commit and a recovery, tuned per weapon class in the `attacks` tuning group. Admission spends stamina (a power release costs more) and publishes `PlayerAttackStartedFact`; nothing about the blow is decided there. The blow resolves by contact as the windup ends. | `Modules/Combat/DaggerfallPlayerAttackClock.cs`, `DaggerfallAttackTuning.cs`; `DaggerfallSession.PlayerAttack.cs`; Kit `AttackCapabilities.TryPlayerMelee`, `AttackExecution.Start`; `DaggerCombatRules` (`TryAdmitPlayerAttack`); `Presentation/DaggerfallSiteAppearance.cs` (`PlayAttackMotion`) |
| Enemy timing | Eligible visible enemies within their engage distance start an attack and publish its wind-up (`EnemyAttackTelegraphFact`). The blow resolves by contact at the first authored damage marker of the attack animation, no sooner than the wind-up ends. An animation without a damage marker resolves its impact immediately. Multiple markers produce one impact. | `Modules/Behavior/DaggerfallEnemyBehaviorModule.cs`; Kit `AttackCapabilities.TryBeginEnemyAttack`; `DaggerfallSiteAppearance` enemy attack playback |
| RNG | Seed zero, separate player/enemy scopes (`dagger.combat.v1`, `dagger.combat.ai.v1`), keyed by generation, simulation step, attacker, target and salt. A blow that connects draws its body part, deflection, damage and critical; a whiff draws nothing. All draws happen at impact, not at admission. | `Modules/Combat/CombatDefinitions.cs` (`CombatRandomKey`); `DaggerfallContactDelivery`, `DaggerfallContactMitigation` |
| Empty swings | An admitted swing that meets no one spends stamina, starts the swing, charges cooldown, and reports `AttackWhiffedFact` as its windup ends. Unknown/defeated participant, pending-attack and cooldown rejections precede admission; policy rejections follow in admission. Low fatigue never refuses a swing: admission charges the authored 11-unit cost clamped at zero, and an emptied pool is the session's exhaustion collapse. | Kit `AttackExecution.Start`; `DaggerCombatRules.TryAdmitPlayerAttack`, `Apply`; `DaggerfallSession.Exhaustion.cs` |
| Harm pipeline | Physical attacks are one configuration of the Kit harm pipeline, on the channel of the weapon's skill. Delivery is physical contact, with no admission roll; a monster's natural attack lands the slots that clear the player's reflexes. Mitigation is the weapon-material requirement (including a racial silver override) as an immune negation decided before the damage roll, then deflection and absorption ([combat-contact-rules.md](combat-contact-rules.md)). Spell effects negate on their element channel in the donor order (immunity, spell absorption, reflection as a deflection back at the caster, active resistance) and take the FORM-06 saving throw as resisted mitigation; disease and poison exposures and the Molag Bal and Mehrunes Razor saves negate the same way, with `DaggerfallMagicAdmissionPolicy` the one home of the tolerance and saving-throw formulas. A swing resolves delivery and mitigation at its hit frame. A released shot flies segment by segment, and contact, damage and mitigation resolve where it meets an actor; the world stops it. | Kit `CombatResolution.Resolve`, `Contact`, `Mitigate`; `Modules/Combat/DaggerfallContactDelivery.cs` (`Apply`, `Connect`, `MonsterSlots`, `AdvanceFlight`); `Modules/Combat/DaggerfallContactMitigation.cs`; `DaggerfallCasting` (`DeliverTo`, `SavingThrow`); `DaggerfallDiseasePolicy.InflictDisease`; `DaggerfallPoisonPolicy.InflictPoison`; `Modules/Combat/CombatDefinitions.cs` (`DaggerfallHarmChannels`) |
| Cooldowns | Keyed by generation and attacker; duration is `max(1, ceil(cooldownSeconds / fixedDeltaSeconds))` admitted steps. | Kit `AttackExecution.Start`, `IsReady` |
| Interruption and duplicates | One pending swing per generation/attacker. Leaving attack state cancels delivery without removing the charged cooldown. Expired or stale-generation impacts are consumed without damage; missing or defeated participants drop the impact. An impact notice must match the pending attack's generation, step and target, and presentation event identity and first-marker tracking suppress repeated starts/impacts. | Kit `AttackExecution.Interrupt`, `ApplyImpacts`; Kit `PursuitCoordinator` (leaving attack state); appearance playback |
| Catch-up | The session processes each admitted fixed step, applies semantic input only on the first, and advances presentation and applies attack impacts once for the outer update. Cooldowns use simulation-step indices. | `DaggerfallSession.cs` (`Update`, `SimulateStep`, `ApplyAttackImpacts`) |
| Pause | A paused `WorldRpgProduct` returns before forwarding the update. The ordinary menu releases controls but does not pause the world. Whether Engine simulation indices advance across a product-only pause is not established; cooldown aging across that pause needs an explicit check when changing the lifecycle. | `src/WorldRpg.Host/WorldRpgProduct.cs` (`Update`); `DaggerfallSession` |
| Save/restore | Save captures remaining cooldown steps relative to the current timeline; restore anchors them to the first resumed step. Pending strikes and presentation playback are transient: a mid-swing save retains the charged cooldown but does not replay the pending hit. | `DaggerSessionPersistence.cs`; Kit `AttackExecution.CaptureCooldowns`, `RestoreCooldowns`, `ObserveTimeline` |

## Existing evidence

The session suites in `tests/WorldRpg.Rulesets.Daggerfall.Tests/` (`PlayerAttackSessionTests`,
`PlayerViewmodelTests`, `EnemyCombatSessionTests`, `ControlsInputSessionTests` and
`SessionPersistenceTests`) cover:

- Empty-space swing: eleven fatigue units spent and a six-step cooldown with a
  0.125-second fixed step (`Player_swing_admission_starts_once_for_empty_space_and_explicit_material_rejection`).
- Player strikes delivered on the classic hit frame, and expiry of a swing that
  ends before it (`A_targeted_swing_plays_at_its_published_tick_and_delivers_on_the_classic_hit_frame`,
  `A_swing_that_ends_before_its_hit_frame_expires_its_impact_instead_of_landing_late`).
- Duplicate marker crossings, loss of reach, death during a swing, expiration and
  multiple or missing authored damage frames.
- Input batching without held or catch-up replay
  (`Mapped_attack_press_reaches_combat_once_without_held_or_catchup_replay`).
- Relative cooldown restore and save during an enemy swing
  (`Current_save_keeps_relative_cooldown_but_starts_a_fresh_spatial_continuation`,
  `A_save_taken_mid_swing_keeps_the_charge_and_never_replays_the_strike`).

Preserve the distinction between calculated damage and actual health lost when
changing Kit combat rules. This baseline does not require historical-save
compatibility or replay machinery.
