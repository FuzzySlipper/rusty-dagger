using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the political board's hold on a town's offices does to how its people receive the player (#11127): the
/// reaction a member of the seat that holds an office gains, and the reaction a member of a seat hostile to it loses,
/// with the people of that office (its shopkeepers, its guild hall, its temple).
/// <para>
/// And how the board's quarrels follow the player's standing (#11121): the share of a gain in standing with a seat's
/// faction that the seats at war with or hostile to it take away (<see cref="DaggerfallBoardStandingRipple"/>).
/// </para>
/// </summary>
internal sealed record DaggerfallPoliticsTuning(int OfficeMemberReaction, int OfficeEnemyReaction, int OpposedStandingPercent)
{
    internal static DaggerfallPoliticsTuning Default { get; } = new(10, 10, 25);

    internal DaggerfallPoliticsTuning Validate() =>
        OfficeMemberReaction is >= 0 and <= 50 && OfficeEnemyReaction is >= 0 and <= 50 && OpposedStandingPercent is >= 0 and <= 100
            ? this : throw new ArgumentException("Politics tuning requires office reactions of 0 to 50 and an opposed standing share of 0 to 100 percent.");

    internal static DaggerfallPoliticsTuning Read(JsonElement section) => new DaggerfallPoliticsTuning(
        section.GetProperty("officeMemberReaction").GetInt32(),
        section.GetProperty("officeEnemyReaction").GetInt32(),
        section.GetProperty("opposedStandingPercent").GetInt32()).Validate();
}
