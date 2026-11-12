using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

public sealed record DaggerfallRaceFlags(int ResistanceFlags, int ImmunityFlags, int LowToleranceFlags, int CriticalWeaknessFlags);

/// <summary>
/// Our race table, taken once from the donor's default <c>RaceTemplate</c> constructors: each race's exposure flags by
/// the donor's class name.
/// </summary>
public sealed record DaggerfallRaceTable(TakenFrom Source, IReadOnlyDictionary<string, DaggerfallRaceFlags> Races);
