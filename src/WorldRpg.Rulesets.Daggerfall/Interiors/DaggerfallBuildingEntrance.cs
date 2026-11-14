using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>An exterior door's building: its display name, its entry rule now and the texts that rule shows.</summary>
internal sealed record DaggerfallBuildingDoor(string Name, DaggerfallBuildingEntry Entry, string? ClosedNotice, string LockedMessage);

/// <summary>
/// What walking in by a building's door comes to before anything is entered: a refusal, the greeting the player reads
/// first, or the interior to enter, with whether entering is a crime against the building's owner.
/// </summary>
/// <param name="Outcome">The refusal, or what is said while the greeting is read; null when the building is to be entered.</param>
/// <param name="Greeting">The greeting the player reads before entering, holding the entry.</param>
/// <param name="Destination">The interior's profile key.</param>
/// <param name="Inside">The interior's profile to enter.</param>
/// <param name="Crime">Whether entering is a break-in (forced) or a trespass, with the owner's id and the region.</param>
internal sealed record DaggerfallBuildingEntryPlan(
    DaggerfallActivationOutcome? Outcome,
    DaggerfallDungeonTextProjection? Greeting = null,
    DaggerfallWorldProfileKey Destination = default,
    DaggerfallSiteProfile? Inside = null,
    (long Owner, int Region)? Crime = null);

/// <summary>
/// An exterior door's building: its name as the donor names it, its entry rule now (hours, holidays, an owned house or
/// ship, a quest's claim and the guild hall's standing), its discovery, the greeting a shop or hall gives, and what
/// entering it comes to. Entering itself and the crime it may be are the session's <c>EnterExteriorBuilding</c> list.
/// </summary>
/// <param name="greeting">The greeting owner, built once for the session's life.</param>
/// <param name="textContext">What the greeting's text names.</param>
/// <param name="hallStanding">Whether the player belongs to a faction's guild and may enter its hall at any hour, in a region (Society).</param>
/// <param name="ownsHouse">Whether the player owns this house (Economy).</param>
/// <param name="ownsShip">Whether the player owns a ship (Economy).</param>
/// <param name="ownsInterior">Whether the player owns this interior (Economy).</param>
/// <param name="questClaims">Whether an active quest claims this building (Quests).</param>
internal sealed class DaggerfallBuildingEntrance(
    DaggerfallSiteContext site,
    DaggerfallDefinitions definitions,
    DaggerfallWorldTime time,
    DaggerfallBuildingGreeting greeting,
    Func<DaggerfallTextContext> textContext,
    Func<int, int, (bool Member, bool HallAnytime)> hallStanding,
    Func<DaggerfallSiteId, DaggerfallSiteBuildingSource, bool> ownsHouse,
    Func<bool> ownsShip,
    Func<DaggerfallInteriorBuilding, bool> ownsInterior,
    Func<DaggerfallSiteId, DaggerfallSiteBuildingSource, bool> questClaims)
{
    /// <summary>
    /// What entering through an exterior door comes to: null for a door that enters no building; otherwise a refusal,
    /// the greeting first (unless <paramref name="skipGreeting"/>), or the interior with the crime entering it would be.
    /// </summary>
    internal DaggerfallBuildingEntryPlan? Plan(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId door, DaggerfallActivationMode? forceMode,
        bool skipGreeting, DaggerfallWorldProfileKey active, DaggerfallSiteProfiles profiles)
    {
        if (doors.ExteriorBuildingOf(door) is not { } building) return null;
        // The building's interior is the one its id names: an authored override, else assembled from its block.
        DaggerfallWorldProfileKey destination = DaggerfallWorldProfileIds.Interior(active.Site, building);
        if (!profiles.TryGet(destination, out DaggerfallSiteProfile inside))
            return new(new DaggerfallActivationOutcome(false, "This building has no interior to enter."));
        var interior = inside.InteriorBuilding
            ?? throw new InvalidOperationException($"Interior '{destination.LogicalId}' names no placed building.");
        int region = active.Site.Region;
        // The building's door identity is a 64-bit resource hash; a crime names its owner by a positive id, so the
        // hash keeps its low 63 bits (and is never zero).
        long owner = Math.Max(1L, (long)(doors.IdentityOf(door).Value & long.MaxValue));
        bool forced = forceMode is not null;
        bool owned = ownsInterior(interior);
        // Walking in while the building's own entry rule holds it shut is trespass; an entrance the rule
        // opens (its hours, a guild's standing, a quest) is not.
        bool closed = !(Door(doors, door, active.Site)?.Entry.Unlocked
            ?? DaggerfallBuildingEntryPolicy.IsOpenHour(interior.BuildingType, time.Calendar.Hour));
        if (!skipGreeting && Greeting(doors, door, forceMode, closed, owned, active) is { } greeting)
            return new(new DaggerfallActivationOutcome(true, "Read the greeting before entering."), greeting);
        bool trespass = !owned && closed && interior.BuildingType is >= 0 and <= 23;
        return new(null, Destination: destination, Inside: inside, Crime: !owned && (forced || trespass) ? (owner, region) : null);
    }

    /// <summary>The entry a read greeting held, now released to go ahead; null when none was held.</summary>
    internal (DaggerfallRdbDoorId Door, DaggerfallActivationMode? ForceMode)? TakeHeldEntry() => greeting.TakeHeldEntry();

    private DaggerfallDungeonTextProjection? Greeting(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId door,
        DaggerfallActivationMode? forceMode, bool closed, bool owned, DaggerfallWorldProfileKey active) =>
        TryBuilding(doors, door, active.Site, out var building)
            ? greeting.Present(door, forceMode, building, closed, owned, active.LogicalId, textContext)
            : null;

    /// <summary>The placed building an exterior door enters, when the active site's record carries it.</summary>
    private bool TryBuilding(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId door, DaggerfallSiteId active, out DaggerfallSiteBuildingSource building)
    {
        building = null!;
        return doors.ExteriorBuildingOf(door) is { } id
            && site.TryFind(active, out DaggerfallSiteRecord record)
            && record.Exterior is { } exterior
            && exterior.Buildings.TryGetValue(id, out building!);
    }

    /// <summary>Marks an exterior door's building discovered.</summary>
    internal void Discover(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId door, DaggerfallSiteId active)
    {
        if (TryBuilding(doors, door, active, out var building)) site.DiscoverBuilding(active, building.Id);
    }

    /// <summary>The name an exterior door's building is labelled with, or null when it has none.</summary>
    internal string? Name(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId door, DaggerfallSiteId active) =>
        TryBuilding(doors, door, active, out DaggerfallSiteBuildingSource building)
            && DisplayName(active, building) is { Length: > 0 } name ? name : null;

    /// <summary>
    /// The building an exterior door enters, named as the donor names it, and what its entry rule says
    /// now. Null for a door that enters no building the active site's record carries.
    /// </summary>
    internal DaggerfallBuildingDoor? Door(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId door, DaggerfallSiteId active)
    {
        if (!TryBuilding(doors, door, active, out DaggerfallSiteBuildingSource building)) return null;
        int faction = building.Source.FactionId;
        (bool guildMember, bool hallAnytime) = hallStanding(faction, active.Region);
        DaggerfallBuildingEntry entry = DaggerfallBuildingEntryPolicy.Evaluate(new(building.Source.BuildingType, faction, building.Quality,
            time.Calendar.Hour, time.Calendar.GetHolidayId(active.Region),
            OwnedByPlayer: ownsHouse(active, building),
            ActiveQuestBuilding: questClaims(active, building),
            GuildHallAccessAnytime: hallAnytime,
            GuildMember: guildMember,
            OwnsShip: ownsShip()));
        string? closed = entry.Closed is { } notice
            ? InternalText(notice.GuildHall ? "guildClosed" : "storeClosed")
                .Replace("%d1", notice.OpensAtHour.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%d2", notice.ClosesAtHour.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : null;
        return new(DisplayName(active, building), entry, closed, InternalText("lockedExteriorDoor"));
    }

    /// <summary>
    /// The donor's discovered-building name: a residence is only "Residence"; anything else carries its
    /// classic generated name. An unresolvable name is empty, so the door keeps its plain label.
    /// </summary>
    internal string DisplayName(DaggerfallSiteId siteId, DaggerfallSiteBuildingSource building)
    {
        if (site.LearnedBuildingName(siteId, building) is { Length: > 0 } learned) return learned;
        if (DaggerfallBuildingEntryPolicy.IsResidence(building.Source.BuildingType)) return InternalText("residence");
        DaggerfallBuildingNameResult name = site.ResolveBuildingName(siteId, building);
        return name.IsResolved ? name.Name : string.Empty;
    }

    private string InternalText(string key) =>
        string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, key)).TextRuns);
}
