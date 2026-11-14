using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Crime;

/// <summary>
/// The player's crimes as they happen: pickpocketing, who witnessed an act, and what a hit or an attack on a civilian or
/// a guard charges. Each incident goes to the canonical <see cref="DaggerfallCrimeState"/>, whose notification the law answers.
/// </summary>
/// <param name="actorDefinitions">The roster's definition of each live actor, which says who is the city watch.</param>
/// <param name="grantGold">Grants stolen gold to the player's purse; a full pack refuses it with <see cref="MechanicsRefusal.Capacity"/>.</param>
/// <param name="provoke">What a failed theft from an enemy does to it (and, when it was pacified, to every active enemy).</param>
internal sealed class DaggerfallCrimes(
    DaggerfallCrimeState crime,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    PlayerControlState player,
    DaggerfallSiteContext site,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> actorDefinitions,
    DaggerfallDefinitions definitions,
    DaggerfallItemInstances itemInstances,
    DaggerfallSkillUseReactions skillUses,
    DaggerfallRacialOverrides racialOverrides,
    IRandomService random,
    IPerceptionService perception,
    Func<SpatialSession> spatial,
    Func<DaggerfallCalendar> calendar,
    Action<InventoryAtomicGrant> grantGold,
    Action<long> provoke)
{
    /// <summary>Whether this actor is the city watch.</summary>
    internal bool IsCityWatch(long actor) => actorDefinitions.GetValueOrDefault(actor)?.MobileId == 146;

    /// <summary>One keyed draw of a crime's operation, between the bounds inclusive.</summary>
    internal int Roll(string operation, string purpose, int minimum, int maximum) =>
        checked((int)random.DrawKeyed(new(0, "daggerfall.crime.v1", $"{operation}:{purpose}", minimum, maximum)).Value);

    /// <summary>Records an incident with the canonical crime owner, which tells the law once.</summary>
    internal void Report(DaggerfallCrimeIncidentSave incident) => crime.RecordIncident(incident);

    internal DaggerfallActivationOutcome Pickpocket(long targetId)
    {
        if (!actors.TryGet(targetId, out var target) || target.IsDefeated
            || actors.Player.IsDefeated)
            return new(false, "That person is not within pickpocket reach.");
        var npc = npcs.All.SingleOrDefault(value => value.DurableId == targetId);
        if (!IsPickpocketTarget(targetId)) return new(false, "That actor cannot be pickpocketed.");
        int? region = site.Region ?? npc?.Site.Region;
        if (region is null) return new(false, "The current region is unavailable.");
        // Classic civilians allow one attempt; enemy mobiles allow repeated attempts. The
        // canonical saved attempt supplies the civilian disposition, without another component.
        string operation = npc is not null ? $"pickpocket:{targetId}"
            : $"pickpocket:{targetId}:{crime.Attempts.Count + 1}";
        if (npc is not null && crime.Attempts.Any(attempt => attempt.OperationId == operation))
            return new(false, "You already tried to pickpocket this person.");
        int? level = npc is not null ? null : actorDefinitions[targetId].Level ?? 1;
        int skill = actors.Player.Stats.GetStat(StatId.Parse("pickpocket")).ValueInt;
        int chance = DaggerfallCrimePolicy.CalculatePickpocketingChance(skill, actors.Player.Progression.Level, level);
        _ = skillUses.Record(new("pickpocket", DaggerfallSkillUseReason.PickpocketAttempt, DaggerfallSkillUseOutcome.Attempted));
        bool success = Roll(operation, "success", 1, 100) <= chance;
        ulong gold = success && Roll(operation, "valuable", 1, 100) > 33 ? checked((ulong)Roll(operation, "gold", 1, 6)) : 0;
        string? transferRefusal = null;
        if (gold > 0)
        {
            var definition = definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            InventoryStackId stack = InventoryStackId.Parse($"daggerfall.pickpocket.{targetId}.{crime.Attempts.Count}");
            // Classic pickpocket creates its 1..6 gold result; it does not transfer an enemy loot
            // purse. A distinct canonical stack preserves stolen provenance beside clean money.
            try
            {
                grantGold(new InventoryAtomicGrant(new InventoryItemId(definition.Id.Value), gold, Stack: stack));
                itemInstances.RegisterStack(DaggerfallItemOwner.Player, stack,
                    DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with { Stolen = true });
            }
            catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
            {
                gold = 0;
                transferRefusal = "Your pack cannot hold the stolen gold.";
            }
        }
        var witnesses = !success && npc is not null ? QueryWitnesses() : DaggerfallCrimeWitnessEvidence.NotQueried;
        var outcome = !success ? DaggerfallCrimeAttemptOutcome.Failed : gold > 0
            ? DaggerfallCrimeAttemptOutcome.PropertyTransferred : DaggerfallCrimeAttemptOutcome.SucceededWithoutTakingProperty;
        long minute = calendar().MinuteNumber;
        crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Pickpocket, DaggerfallActorIdentity.PlayerEntityId,
            targetId, region.Value, minute, outcome, witnesses));
        if (gold > 0) crime.RecordGuildRequirementProgress(operation, DaggerfallCrimeGuildCredit.Thieving, minute);
        if (!success && npc is not null)
            Report(new(operation, DaggerfallCrimeKind.Pickpocketing, DaggerfallCrimeStage.Attempted,
                DaggerfallActorIdentity.PlayerEntityId, targetId, region.Value, minute, DaggerfallCrimeTargetKind.Civilian, witnesses, DaggerfallCrimeGuildCredit.None, Reported: true));
        else if (!success) provoke(targetId);
        return new(true, transferRefusal ?? (!success ? "Your pickpocket attempt failed." : gold > 0 ? $"You pinched {gold} gold pieces." : "You found nothing valuable."));
    }

    /// <summary>A civilian of the active site who is in play, or a monster or class enemy.</summary>
    internal bool IsPickpocketTarget(long actorId)
    {
        var npc = npcs.All.SingleOrDefault(value => value.DurableId == actorId);
        if (npc is not null) return npc.Kind == DaggerfallNpcKind.Civilian
            && npcs.IsGameplayActive(npc.DurableId)
            && npc.Site.Region == site.Region && npc.Site.Location == site.ActiveSite?.Name;
        return actorDefinitions.TryGetValue(actorId, out var definition)
            && definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass;
    }

    /// <summary>
    /// Which of the active site's people in play, and the city watch, can see the player now; optionally only the
    /// observers <paramref name="eligibleObserver"/> admits, within a narrower sight than the perception defaults.
    /// </summary>
    internal DaggerfallCrimeWitnessEvidence QueryWitnesses(Func<ActorState, bool>? eligibleObserver = null, double? maximumDistance = null, double? minimumFacingCosine = null)
    {
        if (player.Position is not WorldPoint position) return DaggerfallCrimeWitnessEvidence.NotQueried;
        var observers = actors.All.Where(actor => !actor.IsDefeated && actor.DurableId != DaggerfallActorIdentity.PlayerEntityId && eligibleObserver?.Invoke(actor) != false &&
            (npcs.All.Any(npc => npc.DurableId == actor.DurableId && npcs.IsGameplayActive(npc.DurableId)
                && npc.Site.Region == site.Region && npc.Site.Location == site.ActiveSite?.Name)
             || IsCityWatch(actor.DurableId)))
            .Select(actor => new PerceptionObserver(checked((ulong)actor.DurableId), actor.Position.ToVector(),
                ActorHeading.Forward(actor.HeadingYawRadians),
                maximumDistance ?? DaggerfallPerceptionQueryDefaults.SightRadius, minimumFacingCosine ?? DaggerfallPerceptionQueryDefaults.MinimumFacingCosine, 1d)).ToArray();
        if (observers.Length == 0) return DaggerfallCrimeWitnessEvidence.NotQueried;
        PerceptionQueryRequest query = new(spatial(), observers,
            new[] { new PerceptionTarget(DaggerfallActorIdentity.PlayerEntityId, position.ToVector()) },
            ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 64);
        List<long> witnesses = [];
        PerceptionReadoutResult receipt;
        do
        {
            receipt = perception.QueryVisibility(query);
            witnesses.AddRange(receipt.Pairs.ToArray().Where(pair => pair.Target == DaggerfallActorIdentity.PlayerEntityId
                && pair.Kind == PerceptionPairKind.Visible).Select(pair => checked((long)pair.Observer)));
            if (receipt.HasNextPairCursor) query = query with { PairCursor = receipt.NextPairCursor };
        } while (receipt.HasNextPairCursor);
        long[] ids = witnesses.Distinct().Order().ToArray();
        return new(ids.Length == 0 ? DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses : DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, ids);
    }

    /// <summary>What a landed hit charges: a vampire's feeding, a quest's scripted charge on a watch hit, and the attack itself.</summary>
    internal void ObserveHit(AttackHitFact damage)
    {
        if (damage.AttackerId == DaggerfallActorIdentity.PlayerEntityId && racialOverrides.Current?.IsVampire == true)
            racialOverrides.Feed(calendar().MinuteNumber);
        // SetPlayerCrime supplies a charge, not a fabricated assault or witness query. A real
        // damaging watch hit admits it to the existing legal-response owner, as in EnemyAttack.
        if (damage.TargetId == DaggerfallActorIdentity.PlayerEntityId && damage.ActualHealthLost > 0
            && IsCityWatch(damage.AttackerId)
            && crime.ScriptedCrime is { } scripted && site.Region is int region
            && racialOverrides.Current?.SuppressCrime != true)
        {
            Report(new($"quest-charge:{damage.OriginatingGeneration}:{damage.OriginatingSimulationStep}:{damage.AttackerId}", scripted,
                DaggerfallCrimeStage.Completed, DaggerfallActorIdentity.PlayerEntityId, null, region, calendar().MinuteNumber,
                DaggerfallCrimeTargetKind.Unknown, DaggerfallCrimeWitnessEvidence.NotQueried, DaggerfallCrimeGuildCredit.None, Reported: true));
            crime.SetScriptedCrime(null);
        }
        ObserveAttack(damage.AttackerId, damage.TargetId,
            $"damage:{damage.OriginatingGeneration}:{damage.OriginatingSimulationStep}:{damage.AttackerId}:{damage.TargetId}");
    }

    /// <summary>The player's attack on a civilian or a guard, once per operation: an assault, or a murder when it died.</summary>
    internal void ObserveAttack(long attacker, long target, string operation)
    {
        if (attacker != DaggerfallActorIdentity.PlayerEntityId) return;
        var npc = npcs.All.SingleOrDefault(value => value.DurableId == target);
        bool cityWatch = IsCityWatch(target);
        bool mobileGuard = npc?.Kind == DaggerfallNpcKind.Civilian && npc.Role == "guard";
        bool guard = cityWatch || mobileGuard;
        bool civilian = npc?.Kind == DaggerfallNpcKind.Civilian;
        if (!civilian && !guard) return;
        bool dead = actors.Get(target).IsDefeated;
        int? region = site.Region ?? npc?.Site.Region;
        if (region is null) return;
        if (crime.HasAttempt(operation)) return;
        if (dead && racialOverrides.Current is { IsVampire: false }) racialOverrides.Satiate(calendar().MinuteNumber);
        var witnesses = QueryWitnesses();
        var kind = dead && !mobileGuard ? DaggerfallCrimeKind.Murder : DaggerfallCrimeKind.Assault;
        var targetKind = guard ? DaggerfallCrimeTargetKind.Guard : DaggerfallCrimeTargetKind.Civilian;
        var credit = !dead || mobileGuard ? DaggerfallCrimeGuildCredit.None : guard ? DaggerfallCrimeGuildCredit.GuardMurder : DaggerfallCrimeGuildCredit.CivilianMurder;
        long minute = calendar().MinuteNumber;
        crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Assault, attacker,
            target, region.Value, minute, DaggerfallCrimeAttemptOutcome.DamageAccepted, witnesses));
        Report(new(operation, kind, DaggerfallCrimeStage.Completed, attacker, target,
            region.Value, minute, targetKind, witnesses, credit));
    }
}
