using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the political board's hold on a town's offices does to how its people receive the player (#11127): the
/// reaction a member of the seat that holds an office gains, and the reaction a member of a seat hostile to it loses,
/// with the people of that office (its shopkeepers, its guild hall, its temple).
/// <para>
/// How the board's quarrels follow the player's standing (#11121): the share of a gain in standing with a seat's
/// faction that the seats at war with or hostile to it take away (<see cref="DaggerfallBoardStandingRipple"/>), for
/// ordinary help and for a political quest.
/// </para>
/// <para>
/// And the political quests (#11121, <see cref="DaggerfallPoliticalPlay"/>): the chance that a seat's questgiver offers
/// one while the board shows an opportunity, the influence carrying the seat's word wins, and the influence undermining
/// a rival takes from it and gives the seat.
/// </para>
/// <para>
/// And the board's ration convoys (#11181): the standing a raid on one costs with its giver and its receiver (the seats
/// hostile to them gain their share, <see cref="DaggerfallBoardStandingRipple.ObserveLoss"/>), and the standing a defence
/// of one that came through earns with both.
/// </para>
/// <para>
/// And hiring on as a convoy's guard (#11182): the hours a convoy musters in its town before it sets out, when its
/// giver's factor hires guards; the gold the job pays on arrival and the bonus for an ambush driven off; and the standing
/// a guard who raids the convoy they were hired to guard loses with its giver beyond the raid's own, with the days the
/// giver's court refuses them work.
/// </para>
/// </summary>
internal sealed record DaggerfallPoliticsTuning(int OfficeMemberReaction, int OfficeEnemyReaction, int OpposedStandingPercent,
    int PoliticalStandingPercent, int PoliticalOfferPercent, int CarryWordInfluence, int UndermineInfluence,
    int ConvoyRaidStanding, int ConvoyDefenceStanding, int ConvoyMusterHours, int ConvoyGuardPay, int ConvoyGuardBonus,
    int ConvoyDesertionStanding, int ConvoyDesertionBarDays)
{
    internal static DaggerfallPoliticsTuning Default { get; } = new(10, 10, 25, 100, 25, 2, 1, 10, 10, 12, 150, 75, 15, 30);

    internal DaggerfallPoliticsTuning Validate() =>
        OfficeMemberReaction is >= 0 and <= 50 && OfficeEnemyReaction is >= 0 and <= 50
        && OpposedStandingPercent is >= 0 and <= 100 && PoliticalStandingPercent is >= 0 and <= 100 && PoliticalOfferPercent is >= 0 and <= 100
        && CarryWordInfluence is >= 1 and <= 5 && UndermineInfluence is >= 1 and <= 5
        && ConvoyRaidStanding is >= 0 and <= 50 && ConvoyDefenceStanding is >= 0 and <= 50
        && ConvoyMusterHours is >= 0 and <= 72 && ConvoyGuardPay is >= 0 and <= 10_000 && ConvoyGuardBonus is >= 0 and <= 10_000
        && ConvoyDesertionStanding is >= 0 and <= 50 && ConvoyDesertionBarDays is >= 0 and <= 365
            ? this : throw new ArgumentException(
                "Politics tuning requires office reactions of 0 to 50, standing shares and a political offer chance of 0 to 100 percent, political quest influence of 1 to 5, convoy standing of 0 to 50, a convoy muster of 0 to 72 hours, guard pay and bonus of 0 to 10000 gold, and a desertion bar of 0 to 365 days.");

    internal static DaggerfallPoliticsTuning Read(JsonElement section) => new DaggerfallPoliticsTuning(
        section.GetProperty("officeMemberReaction").GetInt32(),
        section.GetProperty("officeEnemyReaction").GetInt32(),
        section.GetProperty("opposedStandingPercent").GetInt32(),
        section.GetProperty("politicalStandingPercent").GetInt32(),
        section.GetProperty("politicalOfferPercent").GetInt32(),
        section.GetProperty("carryWordInfluence").GetInt32(),
        section.GetProperty("undermineInfluence").GetInt32(),
        section.GetProperty("convoyRaidStanding").GetInt32(),
        section.GetProperty("convoyDefenceStanding").GetInt32(),
        section.GetProperty("convoyMusterHours").GetInt32(),
        section.GetProperty("convoyGuardPay").GetInt32(),
        section.GetProperty("convoyGuardBonus").GetInt32(),
        section.GetProperty("convoyDesertionStanding").GetInt32(),
        section.GetProperty("convoyDesertionBarDays").GetInt32()).Validate();
}
