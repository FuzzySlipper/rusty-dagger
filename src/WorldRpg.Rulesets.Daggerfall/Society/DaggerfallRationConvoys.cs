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
    [property: JsonRequired] long[] Riders,
    DaggerfallConvoySide? Side = null,
    long[]? Raiders = null,
    bool StruckConvoy = false,
    bool StruckRaiders = false,
    bool Raided = false)
{
    /// <summary>The convoy's guards, who fight; the first aboard is its drover, who does not.</summary>
    [JsonIgnore]
    internal IEnumerable<long> Guards => Riders.Skip(1);

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
/// The side the player took with a convoy they came upon (#11181): approached it in peace, defended it from its raiders,
/// joined its raiders, or raided it.
/// </summary>
internal enum DaggerfallConvoySide { Approach, Defend, JoinRaiders, Raid }

/// <summary>
/// What a convoy fight asks of the world (#11181): to give a convoy's guard a guard's arms and craft, to make an actor
/// fight beside the player or against them, and to bring the crime and standing of a raid, or the standing of a defence
/// that saw the convoy through.
/// </summary>
internal interface IDaggerfallConvoyFight
{
    void Arm(long guardId);
    void Ally(long actorId);
    void Foe(long actorId);
    void Raided(DaggerfallRationConvoySave convoy);
    void Defended(DaggerfallRationConvoySave convoy);
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
    private IDaggerfallConvoyFight? _fight;

    internal IReadOnlyCollection<DaggerfallRationConvoySave> All => _convoys.Values;

    /// <summary>Binds the board, which hears of each convoy's arrival and loss for its next Sunday.</summary>
    internal void BindBoard(Action<WorldNudge> report) => _report = report;

    /// <summary>Binds what makes a lost convoy news: the convoy, the seat whose soldiers took it if any, and the second it was lost.</summary>
    internal void BindLossNews(Action<DaggerfallRationConvoySave, string?, long> lost) => _lost = lost;

    internal bool TryGet(string bandId, out DaggerfallRationConvoySave? convoy) => _convoys.TryGetValue(bandId, out convoy);

    /// <summary>Binds what a convoy fight asks of the world (#11181).</summary>
    internal void BindFight(IDaggerfallConvoyFight fight) => _fight = fight;

    /// <summary>
    /// The sides the player may take with the convoy on band <paramref name="bandId"/>, come upon on the map, beyond
    /// approaching it and passing it by (#11181): under attack, defend it or join its raiders; quiet, raid it. None for a
    /// band that is no convoy.
    /// </summary>
    internal IReadOnlyList<DaggerfallConvoySide> Sides(string bandId, bool underAttack) =>
        !_convoys.ContainsKey(bandId) ? []
        : underAttack ? [DaggerfallConvoySide.Defend, DaggerfallConvoySide.JoinRaiders]
        : [DaggerfallConvoySide.Raid];

    /// <summary>
    /// The player takes <paramref name="side"/> with the convoy on band <paramref name="bandId"/> as they meet it. Joining
    /// its raiders or raiding it is a raid at once: its crime and standing fall on the player.
    /// </summary>
    internal bool Choose(string bandId, DaggerfallConvoySide side)
    {
        if (!_convoys.TryGetValue(bandId, out DaggerfallRationConvoySave? convoy)) return false;
        _convoys[bandId] = convoy = convoy with { Side = side };
        if (side is DaggerfallConvoySide.JoinRaiders or DaggerfallConvoySide.Raid) Raid(convoy);
        return true;
    }

    /// <summary>
    /// The scene staged the convoy on band <paramref name="bandId"/> met on the map, its people among
    /// <paramref name="primary"/> and its raiders <paramref name="rivals"/>, once as it is entered and again as late
    /// raiders find room: each takes the side the player stands on with them, which is the side chosen unless the player
    /// has since struck it. Its guards fight beside a defender who has not struck the convoy and against anyone else who
    /// raids it; its raiders fight beside one who joined them and has not struck them.
    /// </summary>
    internal void Staged(string bandId, IReadOnlyList<long> primary, IReadOnlyList<long> rivals)
    {
        if (!_convoys.TryGetValue(bandId, out DaggerfallRationConvoySave? convoy) || _fight is null) return;
        DaggerfallConvoySide side = convoy.Side ?? DaggerfallConvoySide.Approach;
        _convoys[bandId] = convoy = convoy with { Side = side, Raiders = [.. (convoy.Raiders ?? []).Union(rivals)] };
        long[] guards = [.. convoy.Guards.Where(primary.Contains)];
        foreach (long guard in guards) _fight.Arm(guard);
        bool guardsHostile = side is DaggerfallConvoySide.JoinRaiders or DaggerfallConvoySide.Raid || convoy.StruckConvoy;
        if (guardsHostile)
            foreach (long guard in guards) _fight.Foe(guard);
        else if (side == DaggerfallConvoySide.Defend)
            foreach (long guard in guards) _fight.Ally(guard);
        if (side == DaggerfallConvoySide.JoinRaiders)
            foreach (long raider in rivals)
                if (convoy.StruckRaiders) _fight.Foe(raider);
                else _fight.Ally(raider);
    }

    /// <summary>
    /// The player struck <paramref name="actorId"/> (#11181). Who they hurt overrides the side they chose: striking a
    /// convoy's people raids it, turning its guards on them if they came as its friend; striking raiders they joined turns
    /// those raiders on them. Each strike is remembered for the credit a defence needs.
    /// </summary>
    internal void Struck(long actorId)
    {
        foreach (DaggerfallRationConvoySave convoy in _convoys.Values.Where(convoy => convoy.Side is not null).ToArray())
        {
            if (convoy.Riders.Contains(actorId))
            {
                DaggerfallRationConvoySave struck = _convoys[convoy.BandId] = convoy with { StruckConvoy = true };
                if (!struck.Raided) Raid(struck);
                if (struck.Side is DaggerfallConvoySide.Defend or DaggerfallConvoySide.Approach)
                    foreach (long guard in struck.Guards) _fight?.Foe(guard);
            }
            else if (convoy.Raiders?.Contains(actorId) == true)
            {
                _convoys[convoy.BandId] = convoy with { StruckRaiders = true };
                if (convoy.Side == DaggerfallConvoySide.JoinRaiders && !convoy.StruckRaiders)
                    foreach (long raider in convoy.Raiders) _fight?.Foe(raider);
            }
        }
    }

    private void Raid(DaggerfallRationConvoySave convoy)
    {
        DaggerfallRationConvoySave raided = _convoys[convoy.BandId] = _convoys[convoy.BandId] with { Raided = true };
        _fight?.Raided(raided);
    }

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
        // A ration goes only if every leg has a way that keeps out of the regions it may not pass; otherwise it stays home
        // this week, its ration never arriving, rather than take a forbidden road.
        if (route.Zip(route.Skip(1)).Any(leg => coordinator.Policy.OverlandRoute(new(leg.First.MapPixelX, leg.First.MapPixelY),
                new(leg.Second.MapPixelX, leg.Second.MapPixelY), avoidRegions) is null))
            return null;
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

    /// <summary>
    /// A convoy whose band is gone from the map without being lost (one the player met) came through. A defender who hurt
    /// its raiders, and never it, earns the standing of seeing it through (#11181); watching earns nothing.
    /// </summary>
    internal bool Arrived(string bandId)
    {
        if (!_convoys.Remove(bandId, out DaggerfallRationConvoySave? convoy)) return false;
        if (convoy is { Side: DaggerfallConvoySide.Defend or DaggerfallConvoySide.Approach, StruckRaiders: true, Raided: false })
            _fight?.Defended(convoy);
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
