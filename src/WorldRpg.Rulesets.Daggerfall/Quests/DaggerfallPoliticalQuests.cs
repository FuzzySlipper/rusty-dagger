using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The political quests (#11121, settled with Patch on the design thread). A seat's own questgivers, its guild halls and
/// its courts, offer one in place of their ordinary work on <see cref="DaggerfallPoliticsTuning.PoliticalOfferPercent"/>
/// of the days, while the live board shows the seat an opportunity where they stand
/// (<see cref="DaggerfallPoliticalBoard.PoliticalPlays"/>). The quest's text names the seat, the region, the rival and
/// the seats that will resent the player for it. Its success moves the board by its play
/// (<see cref="DaggerfallPoliticalBoard.ObservePoliticalQuest"/>) and costs the player standing with those seats in
/// full (<see cref="DaggerfallBoardStandingRipple"/>). The play rides on its quest, so this keeps nothing.
/// </summary>
internal sealed class DaggerfallPoliticalQuests(DaggerfallPoliticalBoard board, DaggerfallBoardStandingRipple ripple,
    DaggerfallOfficeLedger officeLedger, IRandomService random, DaggerfallPoliticsTuning tuning)
{
    /// <summary>The quest each kind of play is offered as, in the <c>daggerfall.quests</c> pack.</summary>
    internal static string SourceFile(DaggerfallPoliticalPlayKind kind) => kind switch
    {
        DaggerfallPoliticalPlayKind.CarryWord => "POLWORD.txt",
        DaggerfallPoliticalPlayKind.TurnOfficial => "POLTURN.txt",
        _ => "POLUNDER.txt",
    };

    /// <summary>
    /// The play a seat's questgiver <paramref name="provider"/> of <paramref name="factionId"/>, standing in catalog region
    /// <paramref name="region"/>, offers on <paramref name="day"/>, or null for their ordinary work. Winning over an
    /// official is offered only while the office has a living holder. The draw is keyed by the day and the giver, so the
    /// same giver answers the same all day.
    /// </summary>
    internal DaggerfallPoliticalPlay? Offer(long provider, int factionId, int region, int day)
    {
        if (board.SeatOfFaction(factionId) is not { } seat) return null;
        DaggerfallPoliticalPlay[] plays = [.. board.PoliticalPlays(seat, region)
            .Where(play => play.Kind != DaggerfallPoliticalPlayKind.TurnOfficial || Official(play) is not null)];
        string key = $"day:{day}:provider:{provider}";
        if (plays.Length == 0 || random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.quest.political-offer", key, 1, 100)).Value > tuning.PoliticalOfferPercent)
            return null;
        return plays[checked((int)random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.quest.political-play", key, 0, plays.Length - 1)).Value)];
    }

    /// <summary>
    /// The local office holder <paramref name="play"/> wins over: its office in the region's chief town, at the town gate
    /// for a watch captain or else in the office's building; null for a play that wins over none, or an office standing
    /// vacant.
    /// </summary>
    internal DaggerfallOfficeHolder? Official(DaggerfallPoliticalPlay play)
    {
        if (play.Office is not { } office || board.Town(play.Region) is not { Exterior: { } exterior } town) return null;
        DaggerfallOfficeKind kind = DaggerfallOfficeAllegiance.Kind(office);
        if (officeLedger.ResolveLocalOffices(town).FirstOrDefault(held => held.Kind == kind) is not
            { IsVacant: false, IsAlive: true, HolderActorId: > 0 } local) return null;
        DaggerfallSiteBuildingSource? building = local.BuildingId is { } id ? exterior.Buildings.Values.FirstOrDefault(held => held.Id == id) : null;
        return new DaggerfallOfficeHolder(local.OfficeFactionId, null, local.HolderActorId, null, local.DisplayName, local.Title,
            null, null, null, null,
            new DaggerfallCastHomeLocation(town, building, building is null ? DaggerfallWorldProfileKind.Exterior : DaggerfallWorldProfileKind.Interior),
            IsAlive: true, FamilyId: local.FamilyId);
    }

    /// <summary>
    /// What a political quest's messages name: <c>_seat_</c> the seat it is for, <c>_region_</c> the region,
    /// <c>_rival_</c> the seat it is played against, <c>_against_</c> the seats that will resent the player for it as
    /// the board stands now, and <c>_office_</c> the office it wins over.
    /// </summary>
    internal IReadOnlyDictionary<string, string> Names(DaggerfallPoliticalPlay play)
    {
        string[] against = [.. ripple.Opposed(play.Seat).Select(seat => board.Board.Seat(seat).Name)];
        Dictionary<string, string> names = new(StringComparer.Ordinal)
        {
            ["seat"] = board.Board.Seat(play.Seat).Name,
            ["region"] = board.Board.Region(play.Region).Name,
            ["rival"] = board.Board.Seat(play.Rival).Name,
            ["against"] = against.Length switch
            {
                0 => "no one, as things stand",
                1 => against[0],
                _ => string.Join(", ", against[..^1]) + " and " + against[^1],
            },
        };
        if (play.Office is { } office)
            names["office"] = Official(play)?.Title ?? DaggerfallOfficeAllegiance.Kind(office) switch
            {
                DaggerfallOfficeKind.WatchCaptain => "watch captain",
                DaggerfallOfficeKind.CourtFactor => "court factor",
                DaggerfallOfficeKind.GuildHallMaster => "guild hall master",
                _ => "temple head",
            };
        return names;
    }

    /// <summary>A political quest succeeded: the board hears its play.</summary>
    internal void Succeeded(DaggerfallPoliticalPlay play) => board.ObservePoliticalQuest(play, tuning);
}
