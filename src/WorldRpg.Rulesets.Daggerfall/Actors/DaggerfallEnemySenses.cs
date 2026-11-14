using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What an enemy senses of the player and its target now: whether it is hostile, the live inputs EnemySenses and
/// EnemyMotor read (the policies own the donor formulas and each enemy's retained memory), and whether a hostile
/// actor near the player bars rest and travel.
/// </summary>
/// <param name="definitions">Every live actor's definition by durable identity (the roster's).</param>
/// <param name="foeTeam">The team an actor fights for now, quest overrides included.</param>
/// <param name="isPacified">Whether an enemy is pacified.</param>
/// <param name="lastPerception">Each enemy's most recent perception decision.</param>
/// <param name="weaponDrawn">Whether the player holds a weapon drawn.</param>
/// <param name="playerHoldsBow">Whether the player's drawn weapon is a bow.</param>
/// <param name="minute">The current game minute.</param>
/// <param name="activeProfile">The site profile the player is in.</param>
internal sealed class DaggerfallEnemySenses(ActorsState actors, PlayerControlState player, DaggerfallDefinitions content,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions, Func<long, string?> foeTeam, Func<long, bool> isPacified,
    Func<IReadOnlyDictionary<long, DaggerfallEnemyPerceptionDecision>> lastPerception, DaggerfallEffectLifecycle effects,
    DaggerfallLocomotionPolicy locomotion, Func<bool> weaponDrawn, Func<bool> playerHoldsBow, Func<long> minute,
    IRandomService random, Func<DaggerfallWorldProfileKey> activeProfile, DaggerfallEncounterRuntime encounters)
{
    /// <summary>Whether a monster or enemy class fights the player now: not an ally of theirs and not pacified.</summary>
    internal bool IsHostile(long actorId, DaggerfallActorDefinition definition) =>
        definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass
        && foeTeam(actorId) != "player-ally" && !isPacified(actorId);

    /// <summary>What EnemyMotor reads of an actor and its target: their levels, a paralysed target, a turned back, an open guard.</summary>
    internal DaggerfallEnemyMotorContext MotorContext(long actorId, long targetId)
    {
        var actor = actors.Get(actorId);
        var target = actors.Get(targetId);
        var targetPosition = targetId == DaggerfallActorIdentity.PlayerEntityId
            ? player.Position!.Value : target.Position;
        Vector3 towardActor = actor.Position.ToVector() - targetPosition.ToVector();
        float yaw = targetId == DaggerfallActorIdentity.PlayerEntityId ? player.YawRadians : target.HeadingYawRadians;
        bool backTurned = Vector3.Dot(ActorHeading.Forward(yaw), towardActor) < 0;
        int Level(long id) => id == DaggerfallActorIdentity.PlayerEntityId ? actors.Player.Progression.Level : definitions[id].Level ?? 1;
        bool paralyzed = effects.Active.Any(effect => effect.Definition.Key == "paralyze" && effect.Context.Target.Value == checked((ulong)targetId));
        return new(Level(actorId), Level(targetId), paralyzed, backTurned,
            targetId == DaggerfallActorIdentity.PlayerEntityId && (!weaponDrawn() || playerHoldsBow()));
    }

    /// <summary>
    /// One enemy's perception context from the current state. The behaviour module calls it as its admitted context
    /// provider, so it is read inside the same Engine update as the perception query.
    /// </summary>
    internal DaggerfallEnemyPerceptionContext PerceptionContext(long actorId)
    {
        if (!definitions.TryGetValue(actorId, out DaggerfallActorDefinition? definition))
            throw new InvalidOperationException($"Enemy perception actor '{actorId}' has no admitted Daggerfall definition.");

        StatsComponent playerStats = actors.Player.Stats;
        int stealth = playerStats.GetStat(StatId.Parse(DaggerfallSkills.Stealth)).ValueInt;
        int personality = playerStats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).ValueInt;
        string? language = DaggerfallFormulaPolicy.LanguageSkillFor(definition);
        int languageValue = language is null ? 0 : playerStats.GetStat(StatId.Parse(language)).ValueInt;

        Vector3 velocity = player.Motion.ControlledVelocity + player.Motion.ExternalVelocity;
        double horizontalSpeed = Math.Sqrt((velocity.X * velocity.X) + (velocity.Z * velocity.Z));
        float walkSpeed = locomotion.WalkSpeed(playerStats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Speed.Value)).ValueInt);
        bool movingLessThanHalfSpeed = horizontalSpeed < walkSpeed * .5d;

        bool targetPacified = isPacified(actorId);
        DaggerfallPerceptionEffectState perceptionEffects = effects.PerceptionFor(DaggerfallActorIdentity.PlayerEntityId);
        DaggerfallMobileDefinition? mobile = definition.MobileId is int mobileId
            && content.Mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? byMobileId)
            ? byMobileId
            : content.Mobiles.ForActor(definition.Id.Value);

        long now = minute();
        int drawIndex = 0;
        Func<int, int> rollPercent = exclusiveMaximum =>
        {
            if (exclusiveMaximum <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            int ordinal = drawIndex++;
            return checked((int)random.DrawKeyed(new KeyedRngRequest(
                DaggerfallRandomKey.Seed,
                "daggerfall.enemy-perception.v1",
                $"actor:{actorId}:minute:{now}:roll:{ordinal}",
                0,
                exclusiveMaximum - 1)).Value);
        };

        DaggerfallWorldProfileKind kind = activeProfile().Kind;
        return new DaggerfallEnemyPerceptionContext(
            GameMinute: now,
            StealthSkill: stealth,
            Personality: personality,
            LanguageSkill: language,
            LanguageSkillValue: languageValue,
            TargetMovingLessThanHalfSpeed: movingLessThanHalfSpeed,
            TargetInsideDungeonCastle: kind is DaggerfallWorldProfileKind.Interior or DaggerfallWorldProfileKind.Dungeon,
            TargetInvisible: perceptionEffects.Invisible,
            TargetBlending: perceptionEffects.Blending,
            TargetShade: perceptionEffects.Shade,
            EnemySeesThroughInvisibility: mobile?.SeesThroughInvisibility == true,
            TargetPacified: targetPacified,
            EnemyHostile: IsHostile(actorId, definition),
            TargetWeaponSheathed: !weaponDrawn(),
            ComprehendLanguagesBonus: perceptionEffects.ComprehendLanguagesBonus,
            Noise: horizontalSpeed,
            RollPercent: rollPercent,
            IsWithinClassicSpawnRange: player.Position is { } playerPosition && DaggerfallClassicSpawnRange.Update(
                actors.Get(actorId).Actor.Get<DaggerfallEnemyPerceptionMemory>(),
                actors.Get(actorId).Position, playerPosition,
                kind is not DaggerfallWorldProfileKind.Exterior)).Validate();
    }

    /// <summary>
    /// Whether a hostile actor is in sight or within twelve metres of the player, which bars rest and travel and makes
    /// a collapse fatal.
    /// </summary>
    internal bool EnemyNearby()
    {
        if (player.Position is not { } standing) return false;
        // As the donor's pending foe spawner sits on the player, a selected encounter still waiting for its
        // placement is already an enemy nearby.
        if (encounters.HasPending(activeProfile().LogicalId)) return true;
        foreach (var actor in actors.All)
        {
            if (actor.IsDefeated || isPacified(actor.DurableId)
                || !definitions.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition)
                || definition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
                || foeTeam(actor.DurableId) == "player-ally") continue;
            bool inSight = lastPerception().TryGetValue(actor.DurableId, out var perception)
                && perception.InSight;
            if (inSight || Vector3.DistanceSquared(actor.Position.ToVector(), standing.ToVector()) <= 12f * 12f)
                return true;
        }
        return false;
    }
}
