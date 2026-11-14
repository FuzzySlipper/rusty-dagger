using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The goods in an interior's property containers (a shop's shelves, a house's chests): stocking them, where the
/// player reaches one from, restocking one as it opens, and the theft of what the player takes from a building they do
/// not own. The containers are the ground containers'; a theft is the crime owner's to witness and report.
/// </summary>
/// <param name="placements">The active site's property container placements.</param>
/// <param name="building">The admitted interior building the player stands in.</param>
/// <param name="owned">Whether the player owns this interior (Economy's holdings).</param>
internal sealed class DaggerfallPropertyLoot(
    DaggerfallGroundContainers containers,
    DaggerfallDefinitions definitions,
    IRandomService random,
    DaggerfallUniqueItemAllocator uniqueItems,
    DaggerfallCrimes crimes,
    DaggerfallCrimeState crime,
    DaggerfallItemInstances instances,
    ActorsState actors,
    ProgressionState progression,
    DaggerfallCharacterState character,
    DaggerfallSkillUseReactions skillUses,
    PlayerControlState player,
    DaggerfallSiteContext site,
    Func<DaggerfallCalendar> calendar,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<IReadOnlyList<DaggerfallPropertyContainerPlacement>> placements,
    Func<DaggerfallInteriorBuilding?> building,
    Func<DaggerfallInteriorBuilding, bool> owned)
{
    /// <summary>Places every property container of the active site, unstocked and as the player's own until one opens.</summary>
    internal void Stock()
    {
        foreach (var placement in placements())
            Ensure(placement, -1, true);
    }

    /// <summary>The point of a property container the player reaches it from: the nearest of its interaction points.</summary>
    internal WorldPoint InteractionPoint(DaggerfallGroundContainer container)
    {
        if (container.PropertyPlacement is null || player.Position is not { } origin) return container.Position;
        var placement = placements().Single(value => value.Id == container.PropertyPlacement);
        return placement.InteractionPoints.MinBy(point => System.Numerics.Vector3.DistanceSquared(origin.ToVector(), point.ToVector()));
    }

    /// <summary>
    /// Restocks a property container as it opens for today. Returns whether its goods are private (the player does not
    /// own the building), false for any other container, or none when the container is gone.
    /// </summary>
    internal bool? Open(long id)
    {
        if (!containers.TryGet(id, out var container)) return null;
        bool privateProperty = false;
        if (container.PropertyPlacement is { } source)
        {
            var placement = placements().Single(value => value.Id == source);
            bool playerOwns = building() is { } interior && owned(interior);
            privateProperty = !playerOwns;
            Ensure(placement, calendar().DayNumber, playerOwns);
        }
        return privateProperty;
    }

    /// <summary>
    /// What taking <paramref name="quantity"/> of <paramref name="definition"/> from a property container does: in a
    /// building the player does not own the goods are stolen, the theft is rolled against the shop's quality and the
    /// player's pickpocketing, recorded with its witnesses and reported, and an unnoticed theft practises pickpocketing.
    /// </summary>
    internal void ObserveTake(DaggerfallGroundContainer source, InventoryContainerTransferReceipt transfer, string definition, ulong quantity)
    {
        if (source.PropertyPlacement is null || building() is not { } interior || owned(interior)) return;
        foreach (var stack in transfer.Stacks)
            instances.ReplaceStack(DaggerfallItemOwner.Player, stack.DestinationStack,
                instances.RequireStack(DaggerfallItemOwner.Player, stack.DestinationStack) with { Stolen = true });
        foreach (var item in transfer.UniqueItems)
        {
            ulong id = actors.Entities.IdentityOf(new(item.EntityId)).Value;
            instances.ReplaceUnique(id, instances.RequireUnique(id) with { Stolen = true });
        }
        var taken = definitions.RequireItem(new(definition));
        DaggerfallWorldProfileKey profile = activeProfile();
        int quality = site.RequireBuildingSource(profile.Site, new(interior.BlockX, interior.BlockY, interior.Building.Index)).Quality;
        int skill = actors.Player.Stats.GetStat(StatId.Parse("pickpocket")).ValueInt;
        int chance = DaggerfallCrimePolicy.CalculateShopliftingChance(skill, quality,
            DaggerfallCrimePolicy.TheftWeight(taken) * quantity, 1);
        string operation = $"property:{crime.OperationCount + 1}";
        bool caught = crimes.Roll(operation, "noticed", 0, 99) < chance;
        var witnesses = crimes.QueryWitnesses();
        long minute = calendar().MinuteNumber;
        crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Theft, DaggerfallActorIdentity.PlayerEntityId,
            source.Id, profile.Site.Region, minute, DaggerfallCrimeAttemptOutcome.PropertyTransferred, witnesses));
        crimes.Report(new(operation, DaggerfallCrimeKind.Theft, DaggerfallCrimeStage.Completed,
            DaggerfallActorIdentity.PlayerEntityId, source.Id, profile.Site.Region, minute,
            DaggerfallCrimeTargetKind.Other, witnesses, DaggerfallCrimeGuildCredit.Thieving, Reported: caught));
        if (!caught) skillUses.Record(new("pickpocket", DaggerfallSkillUseReason.ShopliftingAttempt, DaggerfallSkillUseOutcome.Attempted));
    }

    private void Ensure(DaggerfallPropertyContainerPlacement placement, long day, bool playerOwned) =>
        containers.EnsureProperty(placement, day, playerOwned, new(definitions, random), uniqueItems,
            (key, min, max) => crimes.Roll(key, "property-stock", min, max), progression.Level, character.Identity.RaceId,
            character.Identity.Gender.ToString().ToLowerInvariant());
}
