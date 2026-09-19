# Combat behavior before the Kit refactor

Source-backed baseline for campaign #8327, recorded during #8328. This describes
existing behavior, not a new design requirement. Later tasks should preserve it
or explicitly record a gameplay decision. The relevant seam tests ran as part
of pair-adoption verification; this is not a browser playthrough report.

Paths below are relative to `src/WorldRpg.Rulesets.Daggerfall/` unless stated.

| Concern | Current behavior | Source owners |
| --- | --- | --- |
| Player timing | After movement and current-look resolution, combat spends stamina, rolls and applies damage synchronously in that admitted simulation step. The `PlayerAttackStartedFact` starts presentation separately. Presentation readiness and combat cooldown both gate a new attack. | `DaggerfallSession.cs` (per-step `Update` overload); `Modules/Combat/CombatModule.cs` (`TryPlayerMelee`, `ResolveExplicit`); `Presentation/PrivateersHoldAppearance.cs` (`CanStartPlayerAttack`) |
| Enemy timing | Eligible visible enemies in reach roll hit/body/damage at attack start and retain the outcome until the first authored animation damage marker. An animation without a damage marker resolves its impact immediately. Multiple markers produce one impact. | `Modules/Behavior/DaggerfallEnemyBehaviorModule.cs`; `CombatModule.TryBeginEnemyAttack`; `PrivateersHoldAppearance` enemy attack playback |
| RNG | Seed zero, separate player/enemy scopes (`dagger.combat.v1`, `dagger.combat.ai.v1`), keyed by generation, simulation step, attacker, target and salt. Hit rolls occur on resolved attacks; body/damage draws only on a hit. Enemy draws occur at start, not impact. | `Modules/Combat/CombatDefinitions.cs`; `CombatModule.ResolveExplicit`, `TryBeginEnemyAttack` |
| Empty swings and misses | An admitted empty-space attack spends stamina, starts the swing, charges cooldown, then reports `NoTargetInReach`. A miss also spends stamina and charges cooldown. Cooldown, defeated/unknown-target and insufficient-stamina rejections precede successful admission. Only `PlayerAttackStartedFact` resets the stamina recovery delay. | `CombatModule.TryPlayerMelee`, `TryAdmitPlayerAttack`, `ResolveExplicit`; `Modules/Combat/DaggerfallStaminaRecoveryModule.cs` |
| Cooldowns | Keyed by generation and attacker; duration is `max(1, ceil(cooldownSeconds / fixedDeltaSeconds))` admitted steps. | `CombatModule` cooldown admission/latching |
| Interruption and duplicates | One pending enemy swing per generation/attacker. Leaving attack state cancels delivery without removing the charged cooldown. Expired/stale-generation impacts are consumed without damage; missing or defeated participants drop the impact. Event identity and first-marker tracking suppress repeated starts/impacts. | `CombatModule.InterruptPendingAttack`, `ResolveEnemyImpacts`; enemy behavior and appearance playback |
| Catch-up | The session processes each admitted fixed step, applies semantic input only on the first, and advances presentation once for the outer update. Cooldowns use simulation-step indices. | `DaggerfallSession.Update` and simulation-step processing |
| Pause | A paused `WorldRpgProduct` returns before forwarding the update. The ordinary menu releases controls but does not pause the world. Whether Engine simulation indices advance across a product-only pause was not established by this survey; cooldown aging across that pause needs an explicit check when changing the lifecycle. | `src/WorldRpg.Host/WorldRpgProduct.cs` (`Update`); `DaggerfallSession` |
| Save/restore | Save captures remaining cooldown steps relative to the current timeline; restore anchors them to the first resumed step. Pending enemy strikes and presentation playback are transient: a mid-swing save retains the charged cooldown but does not replay the pending hit. | `DaggerfallSession.CaptureSave`, restore/timeline initialization; `CombatModule.CaptureCooldowns`, `RestoreCooldowns` |

## Existing evidence to carry forward

`tests/WorldRpg.Rulesets.Daggerfall.Tests/NormalizedRuntimeSeamTests.cs` covers:

- Empty-space swing: five stamina spent, six-step cooldown with a 0.125-second
  fixed step. Miss expenditure follows source ordering, rather than a dedicated
  assertion in that test.
- Duplicate impact delivery, loss of reach, death during a swing, expiration and
  multiple authored damage markers.
- Three-step input batching and an enemy swing that does not damage during the
  catch-up update which decided it.
- Relative cooldown restore and save during an enemy swing.

Preserve the distinction between calculated damage and actual health lost when
moving application into Kit rules. Gameplay refactoring belongs to #8334–#8336;
current-state save reconstruction belongs to #8339. This baseline does not
require historical-save compatibility or replay machinery.
