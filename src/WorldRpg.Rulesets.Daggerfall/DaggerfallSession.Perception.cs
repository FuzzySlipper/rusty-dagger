using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Live Daggerfall inputs for the EnemySenses policy.  The session supplies
/// current state here; the policy still owns the donor formulas and retained
/// per-enemy memory.
/// </summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// Reads one enemy's perception context from the current session state.
    /// This method is used as the behavior module's admitted context provider,
    /// so it is evaluated inside the same Engine update as the perception query.
    /// </summary>
    private DaggerfallEnemyPerceptionContext BuildEnemyPerceptionContext(long actorId)
    {
        if (!_definitionsByActor.TryGetValue(actorId, out DaggerfallActorDefinition? definition))
            throw new InvalidOperationException($"Enemy perception actor '{actorId}' has no admitted Daggerfall definition.");

        StatsComponent playerStats = State.Actors.Player.Stats;
        int stealth = playerStats.GetStat(StatId.Parse(DaggerfallSkills.Stealth)).ValueInt;
        int personality = playerStats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).ValueInt;
        string? language = DaggerfallFormulaPolicy.LanguageSkillFor(definition);
        int languageValue = language is null ? 0 : playerStats.GetStat(StatId.Parse(language)).ValueInt;

        Vector3 velocity = State.PlayerControl.Motion.ControlledVelocity
            + State.PlayerControl.Motion.ExternalVelocity;
        double horizontalSpeed = Math.Sqrt((velocity.X * velocity.X) + (velocity.Z * velocity.Z));
        float walkSpeed = _locomotion.WalkSpeed(playerStats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Speed.Value)).ValueInt);
        bool movingLessThanHalfSpeed = horizontalSpeed < walkSpeed * .5d;
        bool authoredHostile = definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass;
        bool targetPacified = _enemyBehavior.IsPacified(actorId);
        DaggerfallPerceptionEffectState perceptionEffects = State.Effects.PerceptionFor(DaggerfallActorIdentity.PlayerEntityId);
        DaggerfallMobileDefinition? mobile = definition.MobileId is int mobileId
            && _definitions.Mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? byMobileId)
            ? byMobileId
            : _definitions.Mobiles.ForActor(definition.Id.Value);

        long minute = MinuteIndex(_time.Calendar);
        int drawIndex = 0;
        Func<int, int> rollPercent = exclusiveMaximum =>
        {
            if (exclusiveMaximum <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            int ordinal = drawIndex++;
            return checked((int)_random.DrawKeyed(new KeyedRngRequest(
                CombatRandomKey.Seed,
                "daggerfall.enemy-perception.v1",
                $"actor:{actorId}:minute:{minute}:roll:{ordinal}",
                0,
                exclusiveMaximum - 1)).Value);
        };

        return new DaggerfallEnemyPerceptionContext(
            GameMinute: minute,
            StealthSkill: stealth,
            Personality: personality,
            LanguageSkill: language,
            LanguageSkillValue: languageValue,
            TargetMovingLessThanHalfSpeed: movingLessThanHalfSpeed,
            TargetInsideDungeonCastle: _activeProfileKey.Kind is DaggerfallWorldProfileKind.Interior or DaggerfallWorldProfileKind.Dungeon,
            TargetInvisible: perceptionEffects.Invisible,
            TargetBlending: perceptionEffects.Blending,
            TargetShade: perceptionEffects.Shade,
            EnemySeesThroughInvisibility: mobile?.SeesThroughInvisibility == true,
            TargetPacified: targetPacified,
            EnemyHostile: authoredHostile && !targetPacified,
            TargetWeaponSheathed: !_appearance.IsWeaponDrawn,
            ComprehendLanguagesBonus: perceptionEffects.ComprehendLanguagesBonus,
            Noise: horizontalSpeed,
            RollPercent: rollPercent,
            IsWithinClassicSpawnRange: true).Validate();
    }
}
