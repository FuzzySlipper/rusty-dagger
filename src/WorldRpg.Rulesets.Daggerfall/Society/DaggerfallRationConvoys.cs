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
/// <paramref name="Origin"/> to <paramref name="Destination"/> on band <paramref name="BandId"/> with its drover and
/// guards, <paramref name="Riders"/>.
/// </summary>
internal sealed record DaggerfallRationConvoySave(
    [property: JsonRequired] string BandId,
    [property: JsonRequired] string Agreement,
    [property: JsonRequired] string Giver,
    [property: JsonRequired] string Receiver,
    [property: JsonRequired] DaggerfallSiteIdSave Origin,
    [property: JsonRequired] DaggerfallSiteIdSave Destination,
    [property: JsonRequired] int Turn,
    [property: JsonRequired] long[] Riders)
{
    internal DaggerfallRationConvoySave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Agreement);
        ArgumentException.ThrowIfNullOrWhiteSpace(Giver);
        ArgumentException.ThrowIfNullOrWhiteSpace(Receiver);
        _ = Origin.Require();
        _ = Destination.Require();
        if (Turn < 0) throw new ArgumentOutOfRangeException(nameof(Turn), Turn, $"Ration convoy '{BandId}' names a negative turn.");
        if (Riders is not { Length: > 0 }) throw new ArgumentException($"Ration convoy '{BandId}' has no one aboard.", nameof(Riders));
        return this;
    }
}

/// <summary>
/// The board's grain on the road (#11180, design <c>wiki/rusty-dagger/ration-convoys</c>). Each Sunday after the board's
/// turn, every grain term of every agreement in force sends that week's ration as a convoy: wagons, a drover and two
/// guards flying the giver's arms, from the chief town of the term's source region to the chief town of the region the
/// ration goes to (<see cref="Trade.RationDestination"/>). The convoy travels as any persistent band does, live near the
/// player and on the road beyond (#11178). What becomes of it is the board's next Sunday's news: an arrival clears the
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

    internal IReadOnlyCollection<DaggerfallRationConvoySave> All => _convoys.Values;

    /// <summary>Binds the board, which hears of each convoy's arrival and loss for its next Sunday.</summary>
    internal void BindBoard(Action<WorldNudge> report) => _report = report;

    internal bool TryGet(string bandId, out DaggerfallRationConvoySave? convoy) => _convoys.TryGetValue(bandId, out convoy);

    /// <summary>
    /// Sends <paramref name="giver"/>'s ration under <paramref name="agreement"/> from <paramref name="origin"/> to
    /// <paramref name="destination"/> on the Sunday of turn <paramref name="turn"/>; null where there is no map to travel or
    /// the two towns are one.
    /// </summary>
    internal DaggerfallRationConvoySave? Dispatch(string agreement, string giver, string giverName, int? giverFaction, string receiver,
        DaggerfallSiteRecord origin, DaggerfallSiteRecord destination, int turn)
    {
        if (overlandParties() is not { } coordinator || origin.Id == destination.Id) return null;
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
        _ = coordinator.DispatchOverlandBand(
            bandId: bandId,
            name: $"{giverName} Grain Convoy",
            kind: DaggerfallBandKind.Convoy,
            memberIds: riders,
            startX: origin.MapPixelX + 0.5,
            startY: origin.MapPixelY + 0.5,
            speedPixelsPerHour: DaggerfallOverlandTraffic.CaravanSpeedPixelsPerHour,
            destination: new DaggerfallExpeditionMark(new(destination.MapPixelX, destination.MapPixelY), destination.Id),
            destinationProfile: DaggerfallWorldProfileIds.Exterior(destination.Id),
            faction: giverName,
            originSite: origin.Id,
            banner: bannerOfSeat(giver));
        DaggerfallRationConvoySave convoy = new DaggerfallRationConvoySave(bandId, agreement, giver, receiver,
            new(origin.Id.Region, origin.Id.Index), new(destination.Id.Region, destination.Id.Index), turn, riders).Validate();
        _convoys[bandId] = convoy;
        return convoy;
    }

    /// <summary>A convoy reached its door: the board hears its ration arrived, and its people go home.</summary>
    internal bool OnBandArrived(string bandId)
    {
        if (!_convoys.Remove(bandId, out DaggerfallRationConvoySave? convoy)) return false;
        DaggerfallRoadEnvoys.Release(actorLedger, bandId, SlotPrefix, convoy.Riders);
        _report?.Invoke(new RationArrived(convoy.Agreement, convoy.Giver));
        return true;
    }

    /// <summary>
    /// A convoy was lost on the road: the board hears its ration is missed, naming the seat whose soldiers took it when the
    /// hazard it fell to was a seat's (<see cref="RaiderOf"/>). Returns the convoy, for the news; null if the band was none.
    /// </summary>
    internal DaggerfallRationConvoySave? OnBandLost(string bandId, DaggerfallWorldConditionToken? raidedUnder)
    {
        if (!_convoys.Remove(bandId, out DaggerfallRationConvoySave? convoy)) return null;
        DaggerfallRoadEnvoys.Release(actorLedger, bandId, SlotPrefix, convoy.Riders);
        _report?.Invoke(new RationLost(convoy.Agreement, convoy.Giver, RaiderOf(raidedUnder, convoy.Giver)));
        return convoy;
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
