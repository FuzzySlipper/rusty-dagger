using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the political board's hold on a town's offices does to how its people receive the player (#11127): the
/// reaction a member of the seat that holds an office gains, and the reaction a member of a seat hostile to it loses,
/// with the people of that office (its shopkeepers, its guild hall, its temple).
/// </summary>
internal sealed record DaggerfallPoliticsTuning(int OfficeMemberReaction, int OfficeEnemyReaction)
{
    internal static DaggerfallPoliticsTuning Default { get; } = new(10, 10);

    internal DaggerfallPoliticsTuning Validate() =>
        OfficeMemberReaction is >= 0 and <= 50 && OfficeEnemyReaction is >= 0 and <= 50
            ? this : throw new ArgumentException("Politics tuning requires office reactions of 0 to 50.");

    internal static DaggerfallPoliticsTuning Read(JsonElement section) => new DaggerfallPoliticsTuning(
        section.GetProperty("officeMemberReaction").GetInt32(),
        section.GetProperty("officeEnemyReaction").GetInt32()).Validate();
}
