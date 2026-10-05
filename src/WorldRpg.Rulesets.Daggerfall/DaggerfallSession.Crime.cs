using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>Transient notification beside the newly persisted canonical incident.</summary>
    internal event Action<DaggerfallCrimeIncidentSave>? CrimeReported
    {
        add => State.Crime.IncidentRecorded += value;
        remove => State.Crime.IncidentRecorded -= value;
    }

    private DaggerfallActivationOutcome PickpocketActor(long targetId)
    {
        if (!State.Actors.TryGet(targetId, out var target) || target.IsDefeated
            || State.Actors.Player.IsDefeated)
            return new(false, "That person is not within pickpocket reach.");
        var npc = State.Npcs.All.SingleOrDefault(value => value.DurableId == targetId);
        if (!IsPickpocketTarget(targetId)) return new(false, "That actor cannot be pickpocketed.");
        int? region = _site.Region ?? npc?.Site.Region;
        if (region is null) return new(false, "The current region is unavailable.");
        // Classic civilians allow one attempt; enemy mobiles allow repeated attempts. The
        // canonical saved attempt supplies the civilian disposition, without another component.
        string operation = npc is not null ? $"pickpocket:{targetId}"
            : $"pickpocket:{targetId}:{State.Crime.Attempts.Count + 1}";
        if (npc is not null && State.Crime.Attempts.Any(attempt => attempt.OperationId == operation))
            return new(false, "You already tried to pickpocket this person.");
        int? level = npc is not null ? null : _roster.Definitions[targetId].Level ?? 1;
        int skill = State.Actors.Player.Stats.GetStat(StatId.Parse("pickpocket")).ValueInt;
        int chance = DaggerfallCrimePolicy.CalculatePickpocketingChance(skill, State.Progression.Level, level);
        _ = State.SkillUses.Record(new("pickpocket", DaggerfallSkillUseReason.PickpocketAttempt, DaggerfallSkillUseOutcome.Attempted));
        bool success = CrimeRoll(operation, "success", 1, 100) <= chance;
        ulong gold = success && CrimeRoll(operation, "valuable", 1, 100) > 33 ? checked((ulong)CrimeRoll(operation, "gold", 1, 6)) : 0;
        string? transferRefusal = null;
        if (gold > 0)
        {
            var definition = _definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            InventoryStackId stack = InventoryStackId.Parse($"daggerfall.pickpocket.{targetId}.{State.Crime.Attempts.Count}");
            // Classic pickpocket creates its 1..6 gold result; it does not transfer an enemy loot
            // purse. A distinct canonical stack preserves stolen provenance beside clean money.
            try
            {
                State.Currency.TrySpendGold(0, [new InventoryAtomicGrant(new InventoryItemId(definition.Id.Value), gold, Stack: stack)]);
                State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack,
                    DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with { Stolen = true });
            }
            catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
            {
                gold = 0;
                transferRefusal = "Your pack cannot hold the stolen gold.";
            }
        }
        var witnesses = !success && npc is not null ? QueryCrimeWitnesses() : DaggerfallCrimeWitnessEvidence.NotQueried;
        var outcome = !success ? DaggerfallCrimeAttemptOutcome.Failed : gold > 0
            ? DaggerfallCrimeAttemptOutcome.PropertyTransferred : DaggerfallCrimeAttemptOutcome.SucceededWithoutTakingProperty;
        long minute = MinuteIndex(_time.Calendar);
        State.Crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Pickpocket, DaggerfallActorIdentity.PlayerEntityId,
            targetId, region.Value, minute, outcome, witnesses));
        if (gold > 0) State.Crime.RecordGuildRequirementProgress(operation, DaggerfallCrimeGuildCredit.Thieving, minute);
        if (!success && npc is not null)
            ReportCrime(new(operation, DaggerfallCrimeKind.Pickpocketing, DaggerfallCrimeStage.Attempted,
                DaggerfallActorIdentity.PlayerEntityId, targetId, region.Value, minute, DaggerfallCrimeTargetKind.Civilian, witnesses, DaggerfallCrimeGuildCredit.None, Reported: true));
        else if (!success)
        {
            if (_enemyBehavior.IsPacified(targetId)) _enemyBehavior.MakeActiveEnemiesHostile();
            _enemyBehavior.MakeHostile(targetId);
        }
        return new(true, transferRefusal ?? (!success ? "Your pickpocket attempt failed." : gold > 0 ? $"You pinched {gold} gold pieces." : "You found nothing valuable."));
    }

    private bool OwnsInteriorBuilding(DaggerfallInteriorBuilding building) =>
        building.BuildingType == 24 && State.Property.OwnsShip
        || State.Property.OwnedHouses.Any(house => house.Site == _activeProfileKey.Site
            && house.Building == building.Building && house.BlockX == building.BlockX && house.BlockY == building.BlockY);

    private bool IsPickpocketTarget(long actorId)
    {
        var npc = State.Npcs.All.SingleOrDefault(value => value.DurableId == actorId);
        if (npc is not null) return npc.Kind == DaggerfallNpcKind.Civilian
            && State.Npcs.IsGameplayActive(npc.DurableId)
            && npc.Site.Region == _site.Region && npc.Site.Location == _site.ActiveSite?.Name;
        return _roster.Definitions.TryGetValue(actorId, out var definition)
            && definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass;
    }

    private int CrimeRoll(string operation, string purpose, int minimum, int maximum) =>
        checked((int)_random.DrawKeyed(new(0, "daggerfall.crime.v1", $"{operation}:{purpose}", minimum, maximum)).Value);

    private DaggerfallCrimeWitnessEvidence QueryCrimeWitnesses() => QueryCrimeWitnesses(null);

    private DaggerfallCrimeWitnessEvidence QueryCrimeWitnesses(Func<ActorState, bool>? eligibleObserver, double? maximumDistance = null, double? minimumFacingCosine = null)
    {
        if (State.PlayerControl.Position is not WorldPoint player) return DaggerfallCrimeWitnessEvidence.NotQueried;
        var observers = State.Actors.All.Where(actor => !actor.IsDefeated && actor.DurableId != DaggerfallActorIdentity.PlayerEntityId && eligibleObserver?.Invoke(actor) != false &&
            (State.Npcs.All.Any(npc => npc.DurableId == actor.DurableId && State.Npcs.IsGameplayActive(npc.DurableId)
                && npc.Site.Region == _site.Region && npc.Site.Location == _site.ActiveSite?.Name)
             || _roster.Definitions.GetValueOrDefault(actor.DurableId)?.MobileId == 146))
            .Select(actor => new PerceptionObserver(checked((ulong)actor.DurableId), actor.Position.ToVector(),
                new Vector3(MathF.Sin(actor.HeadingYawRadians), 0, -MathF.Cos(actor.HeadingYawRadians)),
                maximumDistance ?? DaggerfallPerceptionQueryDefaults.SightRadius, minimumFacingCosine ?? DaggerfallPerceptionQueryDefaults.MinimumFacingCosine, 1d)).ToArray();
        if (observers.Length == 0) return DaggerfallCrimeWitnessEvidence.NotQueried;
        PerceptionQueryRequest query = new(_spatial.Session, observers,
            new[] { new PerceptionTarget(DaggerfallActorIdentity.PlayerEntityId, player.ToVector()) },
            ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 0, 64);
        List<long> witnesses = [];
        PerceptionReadoutResult receipt;
        do
        {
            receipt = _engine.Perception.QueryVisibility(query);
            witnesses.AddRange(receipt.Pairs.ToArray().Where(pair => pair.Target == DaggerfallActorIdentity.PlayerEntityId
                && pair.Kind == PerceptionPairKind.Visible).Select(pair => checked((long)pair.Observer)));
            if (receipt.HasNextPairCursor) query = query with { PairCursor = receipt.NextPairCursor, ExpectedProjectionIdentity = receipt.ProjectionIdentity };
        } while (receipt.HasNextPairCursor);
        long[] ids = witnesses.Distinct().Order().ToArray();
        return new(ids.Length == 0 ? DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses : DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, ids);
    }

    private void ReportCrime(DaggerfallCrimeIncidentSave incident)
    {
        State.Crime.RecordIncident(incident);
    }

    private void ObserveCrimeHit(AttackHitFact damage)
    {
        // SetPlayerCrime supplies a charge, not a fabricated assault or witness query. A real
        // damaging watch hit admits it to the existing legal-response owner, as in EnemyAttack.
        if (damage.TargetId == DaggerfallActorIdentity.PlayerEntityId && damage.ActualHealthLost > 0
            && _roster.Definitions.GetValueOrDefault(damage.AttackerId)?.MobileId == 146
            && State.Crime.ScriptedCrime is { } crime && _site.Region is int region
            && State.RacialOverrides.Current?.SuppressCrime != true)
        {
            ReportCrime(new($"quest-charge:{damage.OriginatingGeneration}:{damage.OriginatingSimulationStep}:{damage.AttackerId}", crime,
                DaggerfallCrimeStage.Completed, DaggerfallActorIdentity.PlayerEntityId, null, region, MinuteIndex(_time.Calendar),
                DaggerfallCrimeTargetKind.Unknown, DaggerfallCrimeWitnessEvidence.NotQueried, DaggerfallCrimeGuildCredit.None, Reported: true));
            State.Crime.SetScriptedCrime(null);
        }
        ObserveLegalAttack(damage.AttackerId, damage.TargetId,
            $"damage:{damage.OriginatingGeneration}:{damage.OriginatingSimulationStep}:{damage.AttackerId}:{damage.TargetId}");
    }

    private void ObserveLegalAttack(long attacker, long target, string operation)
    {
        if (attacker != DaggerfallActorIdentity.PlayerEntityId) return;
        var npc = State.Npcs.All.SingleOrDefault(value => value.DurableId == target);
        bool cityWatch = _roster.Definitions.GetValueOrDefault(target)?.MobileId == 146;
        bool mobileGuard = npc?.Kind == DaggerfallNpcKind.Civilian && npc.Role == "guard";
        bool guard = cityWatch || mobileGuard;
        bool civilian = npc?.Kind == DaggerfallNpcKind.Civilian;
        if (!civilian && !guard) return;
        bool dead = State.Actors.Get(target).IsDefeated;
        int? region = _site.Region ?? npc?.Site.Region;
        if (region is null) return;
        if (State.Crime.HasAttempt(operation)) return;
        if (dead && State.RacialOverrides.Current is not null) State.RacialOverrides.Satiate(MinuteIndex(_time.Calendar));
        var witnesses = QueryCrimeWitnesses();
        var crime = dead && !mobileGuard ? DaggerfallCrimeKind.Murder : DaggerfallCrimeKind.Assault;
        var kind = guard ? DaggerfallCrimeTargetKind.Guard : DaggerfallCrimeTargetKind.Civilian;
        var credit = !dead || mobileGuard ? DaggerfallCrimeGuildCredit.None : guard ? DaggerfallCrimeGuildCredit.GuardMurder : DaggerfallCrimeGuildCredit.CivilianMurder;
        long minute = MinuteIndex(_time.Calendar);
        State.Crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Assault, attacker,
            target, region.Value, minute, DaggerfallCrimeAttemptOutcome.DamageAccepted, witnesses));
        ReportCrime(new(operation, crime, DaggerfallCrimeStage.Completed, attacker, target,
            region.Value, minute, kind, witnesses, credit));
    }

    private sealed class DaggerfallCrimeActivationOwner(DaggerfallDialogueService dialogue, ActorsState actors,
        Func<long, bool> eligible, Func<long, DaggerfallActivationOutcome> pickpocket,
        Func<long, bool> lawGuard, Func<long, DaggerfallActivationOutcome> surrender, Func<long, bool> questActor, Func<long, bool> questClick) : IDaggerfallNpcActivationOwner
    {
        public IEnumerable<DaggerfallActivationTarget> NpcTargets()
        {
            return dialogue.NpcTargets();
        }
        public IEnumerable<DaggerfallActivationTarget> NpcTargets(DaggerfallActivationMode mode)
        {
            HashSet<long> included = [];
            if (mode != DaggerfallActivationMode.Steal)
                foreach (var person in dialogue.NpcTargets()) { included.Add(checked((long)person.Identity.Value)); yield return person; }
            if (mode is DaggerfallActivationMode.Grab or DaggerfallActivationMode.Talk or DaggerfallActivationMode.Steal)
            {
                foreach (var person in dialogue.QuestNpcTargets(questActor))
                    if (included.Add(checked((long)person.Identity.Value))) yield return person;
                foreach (var actor in actors.All.Where(actor => !actor.IsDefeated && questActor(actor.DurableId) && included.Add(actor.DurableId)))
                    yield return Target(actor);
            }
            foreach (var actor in actors.All.Where(actor => !actor.IsDefeated && actor.DurableId != DaggerfallActorIdentity.PlayerEntityId
                && (mode == DaggerfallActivationMode.Steal ? eligible(actor.DurableId) : mode == DaggerfallActivationMode.Talk && lawGuard(actor.DurableId))
                && included.Add(actor.DurableId)))
                yield return Target(actor);
        }
        private static DaggerfallActivationTarget Target(ActorState actor) =>
            new(DaggerfallActivationTargetKind.Npc, ActorsState.Identity(actor.DurableId), actor.Actor.Entity,
                checked((ulong)actor.DurableId), actor.Position, 1, ReachDistance: 3.2);
        public DaggerfallActivationOutcome ActivateNpc(DaggerfallActivationSelection selection)
        {
            long id = checked((long)selection.Target.Identity.Value);
            if (!(actors.TryGet(id, out var actor) && !actor.IsDefeated && actor.Actor.Entity == selection.Target.Entity)
                && !dialogue.IsCurrentNpcSelection(selection))
                return new(false, "That person is no longer available here.");
            if (selection.Mode is DaggerfallActivationMode.Grab or DaggerfallActivationMode.Talk or DaggerfallActivationMode.Steal && questClick(id))
                return new(true, "You address them.");
            if (selection.Mode == DaggerfallActivationMode.Steal) return pickpocket(id);
            return lawGuard(id) ? surrender(id) : dialogue.ActivateNpc(selection);
        }
    }
}
