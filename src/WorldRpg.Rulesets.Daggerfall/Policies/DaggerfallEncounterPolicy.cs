using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>Compiled interpretation of the classic random-encounter tables.</summary>
/// <remarks>
/// The tables themselves remain normalized content. This policy owns the donor's context mapping,
/// spawn chance and level-band choice; callers own when a selected result becomes a live actor.
/// </remarks>
internal static class DaggerfallEncounterPolicy
{
    internal const int LocationNightDenominator = 24;
    internal const int WildernessDayDenominator = 36;
    internal const int WildernessNightDenominator = 24;
    internal const int DungeonDenominator = 36;

    internal static DaggerfallEncounterChoice Choose(
        DaggerfallEncounterSet encounters,
        DaggerfallEncounterRequest request,
        Func<string, int, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(draw);
        request.Validate();

        if (request.Context == DaggerfallEncounterContext.Dungeon && !request.EnemyAlert)
            return DaggerfallEncounterChoice.None(request.Context, "dungeon-not-alert");

        int denominator = DenominatorFor(request.Context);
        int chance = Draw(draw, "chance", 0, denominator - 1);
        if (chance != 0)
            return DaggerfallEncounterChoice.None(request.Context, "source-chance-missed", chance, denominator);

        int table = TableFor(request);
        int percentile = Draw(draw, "level-band", 1, 100);
        (int minimum, int maximum) = LevelBand(percentile, request.PlayerLevel);
        int index = Draw(draw, "table-entry", minimum, maximum);
        int mobile = encounters.Tables[table][index];
        return new DaggerfallEncounterChoice(request.Context, table, index, mobile, percentile, chance, denominator, null);
    }

    internal static int TableFor(DaggerfallEncounterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return request.Context switch
        {
            DaggerfallEncounterContext.Dungeon => request.DungeonType!.Value,
            DaggerfallEncounterContext.LocationNight => ClimateTable(request.Climate!.Value, 20),
            DaggerfallEncounterContext.WildernessDay => ClimateTable(request.Climate!.Value, 21),
            DaggerfallEncounterContext.WildernessNight => ClimateTable(request.Climate!.Value, 22),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    internal static (int Minimum, int Maximum) LevelBand(int percentile, int playerLevel)
    {
        if (percentile is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(percentile));
        if (playerLevel < 1) throw new ArgumentOutOfRangeException(nameof(playerLevel));
        int minimum;
        int maximum;
        if (percentile > 80)
        {
            if (percentile > 95)
            {
                minimum = 0;
                maximum = playerLevel <= 5 ? playerLevel + 2 : 19;
            }
            else
            {
                minimum = 0;
                maximum = playerLevel + 1;
            }
        }
        else
        {
            minimum = playerLevel - 3;
            maximum = playerLevel + 3;
        }

        if (minimum < 0) (minimum, maximum) = (0, 5);
        if (maximum > 19) (minimum, maximum) = (14, 19);
        return (minimum, maximum);
    }

    private static int ClimateTable(int climate, int offset) => climate switch
    {
        224 or 225 => offset,
        226 => offset + 3,
        227 => offset + 6,
        229 => offset + 9,
        228 or 230 or 231 => offset + 12,
        232 => offset + 15,
        _ => throw new ArgumentException($"Climate value {climate} has no classic random encounter table.", nameof(climate)),
    };

    internal static int DenominatorFor(DaggerfallEncounterContext context) => context switch
    {
        DaggerfallEncounterContext.LocationNight => LocationNightDenominator,
        DaggerfallEncounterContext.WildernessDay => WildernessDayDenominator,
        DaggerfallEncounterContext.WildernessNight => WildernessNightDenominator,
        DaggerfallEncounterContext.Dungeon => DungeonDenominator,
        _ => throw new ArgumentOutOfRangeException(nameof(context)),
    };

    private static int Draw(Func<string, int, int, int> draw, string id, int minimum, int maximum)
    {
        int value = draw(id, minimum, maximum);
        if (value < minimum || value > maximum)
            throw new InvalidOperationException($"Encounter random roll '{id}' returned {value}, outside [{minimum}, {maximum}].");
        return value;
    }
}

public enum DaggerfallEncounterContext { LocationNight, WildernessDay, WildernessNight, Dungeon }

/// <summary>One explicit consequence request from rest, travel, or elapsed world time.</summary>
internal sealed record DaggerfallEncounterRequest(
    DaggerfallEncounterContext Context,
    int PlayerLevel,
    int? Climate = null,
    int? DungeonType = null,
    bool EnemyAlert = false)
{
    internal void Validate()
    {
        if (PlayerLevel < 1) throw new ArgumentOutOfRangeException(nameof(PlayerLevel));
        if (!Enum.IsDefined(Context)) throw new ArgumentOutOfRangeException(nameof(Context));
        if (Context == DaggerfallEncounterContext.Dungeon)
        {
            if (DungeonType is null or < 0 or > 18) throw new ArgumentOutOfRangeException(nameof(DungeonType));
            if (Climate is not null) throw new ArgumentException("A dungeon encounter does not carry climate.");
        }
        else
        {
            if (Climate is null || DungeonType is not null)
                throw new ArgumentException("An exterior encounter carries climate and no dungeon type.");
            if (Climate.Value is not (224 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 232))
                throw new ArgumentException($"Climate value {Climate.Value} has no classic random encounter table.", nameof(Climate));
        }
    }
}

/// <summary>A complete selected result. A null mobile is a genuine source-chance or context miss.</summary>
internal sealed record DaggerfallEncounterChoice(
    DaggerfallEncounterContext Context,
    int? Table,
    int? Entry,
    int? MobileId,
    int? LevelPercentile,
    int? ChanceRoll,
    int? ChanceDenominator,
    string? Reason)
{
    internal static DaggerfallEncounterChoice None(DaggerfallEncounterContext context, string reason, int? roll = null, int? denominator = null) =>
        new(context, null, null, null, null, roll, denominator, reason);
}
