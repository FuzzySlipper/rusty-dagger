using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The board's quarrels follow the player's standing (#11121; Patch: helping a seat at war costs standing with its
/// enemies). When the player's standing with a seat's faction, or a faction under it, rises, the seats the live board
/// sets against that seat lose a share of it (<see cref="DaggerfallPoliticsTuning.OpposedStandingPercent"/>; for a
/// political quest <see cref="DaggerfallPoliticsTuning.PoliticalStandingPercent"/>, by default all of it): those at war
/// with it on the other side, and those hostile to it (relation <see cref="Supply.Hostile"/> or below). The loss
/// spreads through each opposed seat's own faction tree as any change does, and a loss never ripples further. It keeps
/// nothing: the board is read as it stands when the standing rises.
/// </summary>
internal sealed class DaggerfallBoardStandingRipple(
    DaggerfallPoliticalBoard board,
    DaggerfallSocialState social,
    DaggerfallBoardSeatSet seats,
    DaggerfallPoliticsTuning tuning)
{
    /// <summary>
    /// The player's standing with <paramref name="factionId"/> rose by <paramref name="amount"/>
    /// (<see cref="DaggerfallSocialState.StandingGained"/>): the opposed seats take their share of it, the political share
    /// for a political quest.
    /// </summary>
    internal void ObserveGain(int factionId, int amount, DaggerfallStandingGain gain) => Ripple(factionId, amount,
        gain == DaggerfallStandingGain.Political ? tuning.PoliticalStandingPercent : tuning.OpposedStandingPercent);

    /// <summary>The seats the live board sets against <paramref name="seat"/>: at war with it on the other side, or hostile to it.</summary>
    internal IReadOnlyList<string> Opposed(string seat) =>
        [.. board.Board.Seats.Select(other => other.Id).Where(other => other != seat
            && (board.Board.Relation(seat, other) <= Supply.Hostile
                || board.Board.Wars.Any(war => war.Side(seat) is { } mine && war.Side(other) is { } theirs && mine != theirs)))];

    private void Ripple(int factionId, int amount, int percent)
    {
        int loss = amount * percent / 100;
        if (loss <= 0 || board.SeatOfFaction(factionId) is not { } seat) return;
        foreach (string opposed in Opposed(seat))
            if (seats.TryGetBySeatId(opposed, out DaggerfallBoardSeatBinding? binding))
                social.ChangeFactionReputation(binding!.OfficeFactionId, -loss, DaggerfallFactionReputationChange.Propagate);
    }
}
