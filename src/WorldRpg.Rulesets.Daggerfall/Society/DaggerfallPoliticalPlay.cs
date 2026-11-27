using WorldRpg.Politics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The three political quests a seat's questgivers offer (#11121, settled with Patch on the design thread).</summary>
internal enum DaggerfallPoliticalPlayKind
{
    /// <summary>Carry the seat's sealed word to a notable of the region: the seat gains influence there.</summary>
    CarryWord = 1,

    /// <summary>Win over a local office holder of the region's chief town: the seat takes the office, its incumbent turned.</summary>
    TurnOfficial = 2,

    /// <summary>Steal a rival's papers in the region: the rival loses influence there and the seat gains it.</summary>
    UndermineRival = 3,
}

/// <summary>
/// A political play offered as a quest (#11121): its kind, the seat that sponsors it, the board region it is played in
/// (the questgiver's), the rival the board sets it against there, and for <see cref="DaggerfallPoliticalPlayKind.TurnOfficial"/>
/// the office. It rides on the quest it was offered as, and its success lands on the board at the next Sunday
/// (<see cref="DaggerfallPoliticalBoard.ObservePoliticalQuest"/>).
/// </summary>
internal sealed record DaggerfallPoliticalPlay(DaggerfallPoliticalPlayKind Kind, string Seat, string Region, string Rival, Office? Office)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Kind) || string.IsNullOrWhiteSpace(Seat) || string.IsNullOrWhiteSpace(Region) || string.IsNullOrWhiteSpace(Rival)
            || Rival == Seat || (Kind == DaggerfallPoliticalPlayKind.TurnOfficial) != Office is not null
            || Office is { } office && !Enum.IsDefined(office))
            throw new ArgumentException($"Political play {Kind} for '{Seat}' in '{Region}' is malformed.");
    }
}
