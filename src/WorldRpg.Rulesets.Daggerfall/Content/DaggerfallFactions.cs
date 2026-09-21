namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How a faction relationship reference is accounted for.</summary>
internal enum DaggerfallFactionLinkDisposition
{
    Resolved,
    Unresolved,
}

/// <summary>How a politic region is accounted for against the faction catalog.</summary>
internal enum DaggerfallRegionFactionDisposition
{
    Claimed,
    Unclaimed,
}

/// <summary>One published faction: its filed identity, relations and bindings.</summary>
/// <param name="Id">The faction's identity.</param>
/// <param name="FiledId">The identity the file states, before duplicate resolution.</param>
/// <param name="Name">The faction's name.</param>
/// <param name="Parent">The parent's identity, or zero when top-level.</param>
/// <param name="ParentDisposition">Whether the parent is a published faction; null when top-level names none.</param>
/// <param name="Children">The identities that name this faction parent, in file order.</param>
/// <param name="Type">The filed faction type.</param>
/// <param name="TypeName">The donor's type name, empty when the type is outside its table.</param>
/// <param name="Region">The converted region: filed minus one, or -1 when filed -1. Values outside the classic range name no region.</param>
/// <param name="Power">The filed power.</param>
/// <param name="Flags">The filed flags.</param>
/// <param name="Ruler">The filed ruler value.</param>
/// <param name="Allies">The filed ally identities, in file order.</param>
/// <param name="AllyDisposition">Whether every ally is a published faction.</param>
/// <param name="Enemies">The filed enemy identities, in file order.</param>
/// <param name="EnemyDisposition">Whether every enemy is a published faction.</param>
/// <param name="Flats">The filed face flats.</param>
/// <param name="Face">The filed face value.</param>
/// <param name="Race">The filed race value.</param>
/// <param name="SocialGroup">The filed social group.</param>
/// <param name="SocialGroupName">The donor's social group name, empty when outside its table.</param>
/// <param name="GuildGroup">The filed guild group.</param>
/// <param name="GuildGroupName">The donor's guild group name, empty when outside its table.</param>
/// <param name="Reputation">The filed base reputation: a source fact, not live standing.</param>
/// <param name="Summon">The filed summon value.</param>
/// <param name="MinimumFame">The filed minimum fame.</param>
/// <param name="MaximumFame">The filed maximum fame.</param>
/// <param name="Vampire">The filed vampire value.</param>
/// <param name="Rank">The filed rank.</param>
internal sealed record DaggerfallFactionDefinition(
    int Id,
    int FiledId,
    string Name,
    int Parent,
    DaggerfallFactionLinkDisposition? ParentDisposition,
    IReadOnlyList<int> Children,
    int Type,
    string TypeName,
    int Region,
    int Power,
    int Flags,
    int Ruler,
    IReadOnlyList<int> Allies,
    DaggerfallFactionLinkDisposition AllyDisposition,
    IReadOnlyList<int> Enemies,
    DaggerfallFactionLinkDisposition EnemyDisposition,
    IReadOnlyList<int> Flats,
    int Face,
    int Race,
    int SocialGroup,
    string SocialGroupName,
    int GuildGroup,
    string GuildGroupName,
    int Reputation,
    int Summon,
    int MinimumFame,
    int MaximumFame,
    int Vampire,
    int Rank);

/// <summary>One politic region with the factions that claim it.</summary>
/// <param name="Region">The zero-based region.</param>
/// <param name="FactionIds">The identities that name the region, in file order.</param>
/// <param name="Disposition">Whether any faction claims the region.</param>
internal sealed record DaggerfallRegionFactionDefinition(int Region, IReadOnlyList<int> FactionIds, DaggerfallRegionFactionDisposition Disposition);

/// <summary>The normalized faction catalog, loaded from the pack alone.</summary>
/// <param name="Factions">The factions by identity.</param>
/// <param name="Regions">The region claims by region.</param>
/// <param name="Names">The faction identities by name: the first identity the file states.</param>
internal sealed record DaggerfallFactionsSet(
    IReadOnlyDictionary<int, DaggerfallFactionDefinition> Factions,
    IReadOnlyDictionary<int, DaggerfallRegionFactionDefinition> Regions,
    IReadOnlyDictionary<string, int> Names)
{
    /// <summary>
    /// Resolves a politic cell's region to the factions that claim it: the records a social
    /// consumer reads, or the explicit unclaimed region when none does.
    /// </summary>
    internal DaggerfallRegionFactionDefinition ResolveRegion(int region) => Regions[region];
}
