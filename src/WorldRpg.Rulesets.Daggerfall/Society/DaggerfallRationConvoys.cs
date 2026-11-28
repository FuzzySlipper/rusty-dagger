using System.Globalization;
using System.Text.Json.Serialization;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One board ration on the road (#11180): the convoy carrying <paramref name="Giver"/>'s grain under agreement
/// <paramref name="Agreement"/> to <paramref name="Receiver"/>, sent the Sunday of turn <paramref name="Turn"/> from
/// <paramref name="Origin"/> on band <paramref name="BandId"/> with its drover and guards, <paramref name="Riders"/>. It
/// goes by <paramref name="Stops"/>, the towns of the regions the board lets its grain pass through, the last its
/// destination, and is bound for stop <paramref name="Leg"/>.
/// </summary>
internal sealed record DaggerfallRationConvoySave(
    [property: JsonRequired] string BandId,
    [property: JsonRequired] string Agreement,
    [property: JsonRequired] string Giver,
    [property: JsonRequired] string Receiver,
    [property: JsonRequired] DaggerfallSiteIdSave Origin,
    [property: JsonRequired] DaggerfallSiteIdSave[] Stops,
    [property: JsonRequired] int Leg,
    [property: JsonRequired] int Turn,
    [property: JsonRequired] long[] Riders)
{
    /// <summary>The town the convoy's ration is bound for.</summary>
    [JsonIgnore]
    internal DaggerfallSiteIdSave Destination => Stops[^1];

    internal DaggerfallRationConvoySave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Agreement);
        ArgumentException.ThrowIfNullOrWhiteSpace(Giver);
        ArgumentException.ThrowIfNullOrWhiteSpace(Receiver);
        _ = Origin.Require();
        if (Stops is not { Length: > 0 }) throw new ArgumentException($"Ration convoy '{BandId}' is bound nowhere.", nameof(Stops));
        foreach (DaggerfallSiteIdSave stop in Stops) _ = stop.Require();
        if (Leg < 0 || Leg >= Stops.Length) throw new ArgumentOutOfRangeException(nameof(Leg), Leg, $"Ration convoy '{BandId}' is bound for no stop of its own.");
        if (Turn < 0) throw new ArgumentOutOfRangeException(nameof(Turn), Turn, $"Ration convoy '{BandId}' names a negative turn.");
        if (Riders is not { Length: > 0 }) throw new ArgumentException($"Ration convoy '{BandId}' has no one aboard.", nameof(Riders));
        return this;
    }
}

/// <summary>
/// The board's grain on the road (#11180, design <c>wiki/rusty-dagger/ration-convoys</c>). Each Sunday after the board's
/// turn, every grain term of every agreement in force sends that week's ration as a convoy: wagons, a drover and two
/// guards flying the giver's arms, from the chief town of the term's source region to the chief town of the region the
/// ration goes to (<see cref="Trade.RationDestination"/>), by way of the chief towns of the regions the board lets the
/// grain pass through (<see cref="Trade.RationRoute"/>), one leg at a time. The convoy travels as any persistent band
/// does, live near the player and on the road beyond (#11178). What becomes of it is the board's next Sunday's news: an arrival clears the
/// term's losses (<see cref="RationArrived"/>), and a loss is a missed ration (<see cref="RationLost"/>), naming the seat
/// whose soldiers took it when they were a seat's.
/// </summary>
internal sealed class DaggerfallRationConvoys(
    DaggerfallDefinitions definitions,
    DaggerfallActorLedger actorLedger,
    Func<ulong> worldSocialSeed,
    Func<DaggerfallNpcAppearance, int, string>? generateName,
    Func<DaggerfallOverlandPartyCoordinator?> overlandParties,
    Func<string, string?> bannerOfSeat)
{
    internal const string BandPrefix = "convoy:";
    private const string SlotPrefix = "population/convoy/";
    private const int Guards = 2;

    private readonly Dictionary<string, DaggerfallRationConvoySave> _convoys = new(StringComparer.Ordinal);
    private Action<WorldNudge>? _report;
    private Action<DaggerfallRationConvoySave, string?, long>? _lost;

    internal IReadOnlyCollection<DaggerfallRationConvoySave> All => _convoys.Values;

    /// <summary>Binds the board, which hears of each convoy's arrival and loss for its next Sunday.</summary>
    internal void BindBoard(Action<WorldNudge> report) => _report = report;

    /// <summary>Binds what makes a lost convoy news: the convoy, the seat whose soldiers took it if any, and the second it was lost.</summary>
    internal void BindLossNews(Action<DaggerfallRationConvoySave, string?, long> lost) => _lost = lost;

    internal bool TryGet(string bandId, out DaggerfallRationConvoySave? convoy) => _convoys.TryGetValue(bandId, out convoy);

    /// <summary>
    /// Sends <paramref name="giver"/>'s ration under <paramref name="agreement"/> from the first of <paramref name="towns"/>
    /// by way of the rest, the last its destination, on the Sunday of turn <paramref name="turn"/>; null where there is no
    /// map to travel or no town to go to.
    /// </summary>
    internal DaggerfallRationConvoySave? Dispatch(string agreement, string giver, string giverName, int? giverFaction, string receiver,
        IReadOnlyList<DaggerfallSiteRecord> towns, IReadOnlyList<int> avoidRegions, int turn)
    {
        DaggerfallSiteRecord[] route = [.. towns.DistinctBy(town => town.Id)];
        if (overlandParties() is not { } coordinator || route.Length < 2) return null;
        DaggerfallSiteRecord origin = route[0];
        string bandId = string.Create(CultureInfo.InvariantCulture, $"{BandPrefix}{agreement}:{giver}:{turn}");
        if (_convoys.ContainsKey(bandId)) return _convoys[bandId];
        string slot = $"{SlotPrefix}{agreement}:{giver}:{turn}";
        long[] riders =
        [
            DaggerfallRoadEnvoys.Stage(definitions, actorLedger, worldSocialSeed(), generateName, origin, $"{slot}/drover",
                "drover", "drover", "Drover", giverFaction),
            .. Enumerable.Range(1, Guards).Select(guard => DaggerfallRoadEnvoys.Stage(definitions, actorLedger, worldSocialSeed(), generateName,
                origin, string.Create(CultureInfo.InvariantCulture, $"{slot}/guard-{guard}"), "convoy-guard", "convoy guard", "Convoy Guard", giverFaction)),
        ];
        Ride(coordinator, bandId, $"{giverName} Grain Convoy", giverName, bannerOfSeat(giver), riders, origin, route[1], avoidRegions);
        DaggerfallRationConvoySave convoy = new DaggerfallRationConvoySave(bandId, agreement, giver, receiver,
            new(origin.Id.Region, origin.Id.Index), [.. route.Skip(1).Select(town => new DaggerfallSiteIdSave(town.Id.Region, town.Id.Index))],
            0, turn, riders).Validate();
        _convoys[bandId] = convoy;
        return convoy;
    }

    private static void Ride(DaggerfallOverlandPartyCoordinator coordinator, string bandId, string name, string giverName, string? banner,
        long[] riders, DaggerfallSiteRecord from, DaggerfallSiteRecord to, IReadOnlyList<int> avoidRegions) =>
        _ = coordinator.DispatchOverlandBand(
            bandId: bandId,
            name: name,
            kind: DaggerfallBandKind.Convoy,
            memberIds: riders,
            startX: from.MapPixelX + 0.5,
            startY: from.MapPixelY + 0.5,
            speedPixelsPerHour: DaggerfallOverlandTraffic.CaravanSpeedPixelsPerHour,
            destination: new DaggerfallExpeditionMark(new(to.MapPixelX, to.MapPixelY), to.Id),
            destinationProfile: DaggerfallWorldProfileIds.Exterior(to.Id),
            faction: giverName,
            originSite: from.Id,
            banner: banner,
            avoidRegions: avoidRegions);

    /// <summary>
    /// A convoy's band reached the stop it was bound for: short of its destination it sets out at once for the next stop,
    /// the same band with the same people; at its destination the board hears its ration arrived, and its people go home.
    /// </summary>
    internal bool OnBandArrived(DaggerfallPersistentOverlandBand band)
    {
        if (!_convoys.TryGetValue(band.BandId, out DaggerfallRationConvoySave? convoy)) return false;
        if (convoy.Leg + 1 < convoy.Stops.Length && overlandParties() is { } coordinator
            && definitions.Locations.Records.FirstOrDefault(site => site.Id == convoy.Stops[convoy.Leg].Require()) is { } here
            && definitions.Locations.Records.FirstOrDefault(site => site.Id == convoy.Stops[convoy.Leg + 1].Require()) is { } next)
        {
            Ride(coordinator, band.BandId, band.Name, band.Faction ?? convoy.Giver, band.Banner, convoy.Riders, here, next, band.AvoidRegions);
            _convoys[band.BandId] = convoy with { Leg = convoy.Leg + 1 };
            return true;
        }
        return Arrived(band.BandId);
    }

    /// <summary>A convoy whose band is gone from the map without being lost (one the player met) came through.</summary>
    internal bool Arrived(string bandId)
    {
        if (!_convoys.Remove(bandId, out DaggerfallRationConvoySave? convoy)) return false;
        DaggerfallRoadEnvoys.Release(actorLedger, bandId, SlotPrefix, convoy.Riders);
        _report?.Invoke(new RationArrived(convoy.Agreement, convoy.Giver));
        return true;
    }

    /// <summary>
    /// A convoy was lost at absolute second <paramref name="second"/>, on the road or met and left: the board hears its ration
    /// will not arrive, naming the seat whose soldiers took it when the hazard it fell to was a seat's (<see cref="RaiderOf"/>),
    /// and the loss is news. Returns whether the band was a convoy's.
    /// </summary>
    internal bool OnBandLost(string bandId, DaggerfallWorldConditionToken? raidedUnder, long second)
    {
        if (!_convoys.Remove(bandId, out DaggerfallRationConvoySave? convoy)) return false;
        DaggerfallRoadEnvoys.Release(actorLedger, bandId, SlotPrefix, convoy.Riders);
        string? raider = RaiderOf(raidedUnder, convoy.Giver);
        _report?.Invoke(new RationLost(convoy.Agreement, convoy.Giver, raider));
        _lost?.Invoke(convoy, raider, second);
        return true;
    }

    /// <summary>
    /// The seat whose soldiers raided a convoy of <paramref name="giver"/>'s under <paramref name="token"/>: the board's
    /// raiders' own seat, or a war's other side when the giver is one of its sides; none for the road, monsters, a plague,
    /// or a war between others.
    /// </summary>
    internal string? RaiderOf(DaggerfallWorldConditionToken? token, string giver)
    {
        if (token is null || DaggerfallRoadTuning.HazardOf(token) is not { } hazard) return null;
        string? Seat(int? faction) => faction is int id && definitions.BoardSeats.TryGetByFactionId(id, out DaggerfallBoardSeatBinding? seat) ? seat?.SeatId : null;
        string? source = Seat(token.SourceFactionId), target = Seat(token.TargetFactionId);
        return hazard switch
        {
            DaggerfallRoadHazard.Raiders => source != giver ? source : null,
            DaggerfallRoadHazard.War => source == giver ? target : target == giver ? source : null,
            _ => null,
        };
    }

    internal DaggerfallRationConvoySave[] Capture() => [.. _convoys.Values.OrderBy(convoy => convoy.BandId, StringComparer.Ordinal)];

    internal void Restore(IReadOnlyList<DaggerfallRationConvoySave>? saved)
    {
        _convoys.Clear();
        foreach (DaggerfallRationConvoySave convoy in saved ?? [])
            _convoys.Add(convoy.Validate().BandId, convoy);
    }
}
