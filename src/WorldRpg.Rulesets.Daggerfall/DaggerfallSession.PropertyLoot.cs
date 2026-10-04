using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private void ReconcilePropertyContainers()
    {
        var inputs = _sites.Projection.Inputs;
        foreach (var placement in inputs.PropertyContainers)
            _groundContainers.EnsureProperty(placement, -1, true, new(_definitions, _random), _uniqueItems,
                (key, min, max) => CrimeRoll(key, "property-stock", min, max), State.Progression.Level, State.Character.Identity.RaceId,
                State.Character.Identity.Gender.ToString().ToLowerInvariant());
    }

    private WorldRpg.Kit.Controls.WorldPoint PropertyInteractionPoint(DaggerfallGroundContainer container)
    {
        if (container.PropertyPlacement is null || State.PlayerControl.Position is not { } origin) return container.Position;
        var placement = _sites.Projection.Inputs.PropertyContainers.Single(value => value.Id == container.PropertyPlacement);
        return placement.InteractionPoints.MinBy(point => System.Numerics.Vector3.DistanceSquared(origin.ToVector(), point.ToVector()));
    }

    private DaggerfallActivationOutcome OpenPropertyLoot(long id)
    {
        if (!_groundContainers.TryGet(id, out var container)) return new(false, "That container is no longer available.");
        bool privateProperty = false;
        if (container.PropertyPlacement is { } source)
        {
            var placement = _sites.Projection.Inputs.PropertyContainers.Single(value => value.Id == source);
            bool owned = CurrentInteriorBuilding() is { } building && OwnsInteriorBuilding(building);
            privateProperty = !owned;
            _groundContainers.EnsureProperty(placement, _time.Calendar.DayNumber, owned, new(_definitions, _random), _uniqueItems,
                (key, min, max) => CrimeRoll(key, "property-stock", min, max), State.Progression.Level,
                State.Character.Identity.RaceId, State.Character.Identity.Gender.ToString().ToLowerInvariant());
        }
        return _lootUi.OpenGround(id, privateProperty) ? new(true, _lootUi.Message) : new(false, _lootUi.Message);
    }

    private void ObservePropertyLoot(PendingGroundLoot take, DaggerfallGroundContainer source,
        InventoryContainerTransferReceipt transfer)
    {
        if (source.PropertyPlacement is null || CurrentInteriorBuilding() is not { } building || OwnsInteriorBuilding(building)) return;
        foreach (var stack in transfer.Stacks)
            State.ItemInstances.ReplaceStack(DaggerfallItemOwner.Player, stack.DestinationStack,
                State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, stack.DestinationStack) with { Stolen = true });
        foreach (var item in transfer.UniqueItems)
        {
            ulong id = State.Actors.Entities.IdentityOf(new(item.EntityId)).Value;
            State.ItemInstances.ReplaceUnique(id, State.ItemInstances.RequireUnique(id) with { Stolen = true });
        }
        var definition = _definitions.RequireItem(new(take.Definition));
        int quality = _site.RequireBuildingSource(_activeProfileKey.Site, new(building.BlockX, building.BlockY, building.Building.Index)).Quality;
        int skill = State.Actors.Player.Stats.GetStat(StatId.Parse("pickpocket")).ValueInt;
        int chance = DaggerfallCrimePolicy.CalculateShopliftingChance(skill, quality,
            DaggerfallCrimePolicy.TheftWeight(definition) * take.Quantity, 1);
        string operation = $"property:{State.Crime.OperationCount + 1}";
        bool caught = CrimeRoll(operation, "noticed", 0, 99) < chance;
        var witnesses = QueryCrimeWitnesses();
        long minute = MinuteIndex(_time.Calendar);
        State.Crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Theft, DaggerfallActorIdentity.PlayerEntityId,
            source.Id, _activeProfileKey.Site.Region, minute, DaggerfallCrimeAttemptOutcome.PropertyTransferred, witnesses));
        ReportCrime(new(operation, DaggerfallCrimeKind.Theft, DaggerfallCrimeStage.Completed,
            DaggerfallActorIdentity.PlayerEntityId, source.Id, _activeProfileKey.Site.Region, minute,
            DaggerfallCrimeTargetKind.Other, witnesses, DaggerfallCrimeGuildCredit.Thieving, Reported: caught));
        if (!caught) State.SkillUses.Record(new("pickpocket", DaggerfallSkillUseReason.ShopliftingAttempt, DaggerfallSkillUseOutcome.Attempted));
    }
}
