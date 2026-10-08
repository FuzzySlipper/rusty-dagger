# Combat timing baseline

This records the current, source-backed combat timing, randomness, cooldown and
save behavior. It describes existing behavior, not a new design requirement.
Later work should preserve it or explicitly record a gameplay decision.

## Policy and owners

Dagger sessions route attacks through `DaggerfallState.Kit`. Kit owns the attack
lifecycle (`AttackExecution<TFact>`: readiness, cooldown, pending impact and
interruption, over the attached `AttackState`), its input/AI entry points
(`AttackCapabilities<TFact>`) and target selection (`TargetingService`).
`DaggerCombatRules` supplies Daggerfall admission, formulas, application and
effects through Kit `CombatResolution`. Kit Ai and Loot mechanisms coordinate
behavior and corpse handling around those rules. `DaggerSessionPersistence`
saves a charged cooldown, but held input, AI/perception work, native
continuation, presentation, and an in-flight attack are transient or rebuilt
after restore. Save slots are managed by the Host's `WorldRpgSaveSlots` from the
game menu.

Paths below are relative to `src/WorldRpg.Rulesets.Daggerfall/` unless stated;
Kit attack types are under `src/WorldRpg.Kit/Combat/`.

| Concern | Current behavior | Source owners |
| --- | --- | --- |
| Player timing | A swing needs a drawn weapon with no strike playing, and a ready cooldown. Admission spends stamina and publishes `PlayerAttackStartedFact`; the hit, body and damage rolls are made at admission. A swing with a target in reach delivers its prepared outcome when the viewmodel reaches the classic hit frame; a swing with no target resolves inside the same update. If the viewmodel cannot play the strike, the impact is delivered in that update; if the swing ends before its hit frame, the impact expires. | `DaggerfallSession.cs` (`Attack` input); Kit `AttackCapabilities.TryPlayerMelee`, `AttackExecution.Start`; `Modules/Combat/DaggerCombatRules.cs` (`TryPrepare`, `TryAdmitPlayerAttack`, `Apply`); `Presentation/DaggerfallSiteAppearance.cs` (`CanStartPlayerAttack`, weapon strike playback) |
| Enemy timing | Eligible visible enemies in reach roll hit/body/damage at attack start and retain the outcome until the first authored damage marker of the attack animation. An animation without a damage marker resolves its impact immediately. Multiple markers produce one impact. | `Modules/Behavior/DaggerfallEnemyBehaviorModule.cs`; Kit `AttackCapabilities.TryBeginEnemyAttack`; `DaggerfallSiteAppearance` enemy attack playback |
| RNG | Seed zero, separate player/enemy scopes (`dagger.combat.v1`, `dagger.combat.ai.v1`), keyed by generation, simulation step, attacker, target and salt. A targeted attack draws its body part and hit roll; damage is drawn only on a hit. All draws happen at admission, not at impact. | `Modules/Combat/CombatDefinitions.cs` (`CombatRandomKey`); `DaggerCombatRules.TryPrepare` |
| Empty swings and misses | An admitted empty-space attack spends stamina, starts the swing, charges cooldown, then reports `NoTargetInReach`. A miss also spends stamina and charges cooldown. Unknown/defeated participant, pending-attack and cooldown rejections precede admission; policy rejections follow in admission. Low fatigue never refuses a swing: admission charges the authored 11-unit cost clamped at zero, and an emptied pool is the session's exhaustion collapse. | Kit `AttackExecution.Start`; `DaggerCombatRules.TryAdmitPlayerAttack`, `Apply`; `DaggerfallSession.Exhaustion.cs` |
| Cooldowns | Keyed by generation and attacker; duration is `max(1, ceil(cooldownSeconds / fixedDeltaSeconds))` admitted steps. | Kit `AttackExecution.Start`, `IsReady` |
| Interruption and duplicates | One pending swing per generation/attacker. Leaving attack state cancels delivery without removing the charged cooldown. Expired or stale-generation impacts are consumed without damage; missing or defeated participants drop the impact. An impact notice must match the pending attack's generation, step and target, and presentation event identity and first-marker tracking suppress repeated starts/impacts. | Kit `AttackExecution.Interrupt`, `ApplyImpacts`; Kit `PursuitCoordinator` (leaving attack state); appearance playback |
| Catch-up | The session processes each admitted fixed step, applies semantic input only on the first, and advances presentation and applies animation impacts once for the outer update. Cooldowns use simulation-step indices. | `DaggerfallSession.cs` (`Update`, `SimulateStep`, `ApplyAttackImpacts`) |
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
