using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a faction relationship reference is accounted for.</summary>
public enum DaggerfallFactionLinkDisposition
{
    /// <summary>The referenced identity is a published faction.</summary>
    Resolved,
    /// <summary>The referenced identity is no published faction.</summary>
    Unresolved,
}

/// <summary>How a politic region is accounted for against the faction catalog.</summary>
public enum DaggerfallRegionFactionDisposition
{
    /// <summary>At least one faction names the region.</summary>
    Claimed,
    /// <summary>No faction names the region.</summary>
    Unclaimed,
}

/// <summary>The source a normalized faction catalog was read from.</summary>
/// <param name="RecordId">The documented inventory record the catalog is read under.</param>
/// <param name="Path">The logical source path.</param>
/// <param name="ByteLength">The source file's byte length.</param>
/// <param name="Records">How many factions the catalog publishes.</param>
public sealed record DaggerfallFactionSource(string RecordId, string Path, long ByteLength, int Records)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(RecordId, nameof(RecordId));
        NormalizedImportDocument.RequireLogicalPath(Path, nameof(Path));
        if (ByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, $"Faction source '{Path}' states no bytes.");
        }

        if (Records <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Records), Records, $"Faction source '{Path}' publishes no factions.");
        }
    }
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
/// <param name="AllyDisposition">Whether every ally is a published faction; vacuously resolved when empty.</param>
/// <param name="Enemies">The filed enemy identities, in file order.</param>
/// <param name="EnemyDisposition">Whether every enemy is a published faction; vacuously resolved when empty.</param>
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
public sealed record DaggerfallFaction(
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
    int Rank)
{
    public void Validate()
    {
        if (Id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Id), Id, "A published faction identity is not negative.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException($"Faction {Id} states no name.", nameof(Name));
        }

        if ((Parent == 0) != (ParentDisposition is null))
        {
            throw new InvalidOperationException($"Faction {Id} names parent {Parent} with no matching parent disposition.");
        }

        if (ParentDisposition is not null && !Enum.IsDefined(ParentDisposition.Value)
            || !Enum.IsDefined(AllyDisposition) || !Enum.IsDefined(EnemyDisposition))
        {
            throw new ArgumentOutOfRangeException(nameof(ParentDisposition), ParentDisposition, "A published faction names a link disposition the contract does not declare.");
        }
    }
}

/// <summary>One politic region with the factions that claim it.</summary>
/// <param name="Region">The zero-based region.</param>
/// <param name="FactionIds">The identities that name the region, in file order.</param>
/// <param name="Disposition">Whether any faction claims the region.</param>
public sealed record DaggerfallRegionFactions(int Region, IReadOnlyList<int> FactionIds, DaggerfallRegionFactionDisposition Disposition)
{
    public void Validate()
    {
        if (Region is < 0 or > 61)
        {
            throw new ArgumentOutOfRangeException(nameof(Region), Region, "A published region faction names a region outside the 62 classic regions.");
        }

        if (!Enum.IsDefined(Disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(Disposition), Disposition, "A published region faction names a disposition the contract does not declare.");
        }

        if ((Disposition == DaggerfallRegionFactionDisposition.Claimed) != (FactionIds.Count > 0))
        {
            throw new InvalidOperationException($"Region {Region} claims {FactionIds.Count} factions as {Disposition}.");
        }
    }
}

/// <summary>The normalized faction catalog: every faction with its relations and region claims.</summary>
/// <param name="Source">The documented inventory family the factions are read under.</param>/// <param name="Factions">The factions in file order.</param>
/// <param name="Regions">The region claims in region order.</param>
/// <param name="DuplicateNames">The names the file states more than once, each with the identities that share it in file order.</param>
public sealed record DaggerfallFactions(
    DaggerfallFactionSource Source,
    IReadOnlyList<DaggerfallFaction> Factions,
    IReadOnlyList<DaggerfallRegionFactions> Regions,
    IReadOnlyList<DaggerfallFactionNameAlias> DuplicateNames)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Factions.Count == 0)
        {
            throw new InvalidOperationException("The faction catalog carries no factions.");
        }

        NormalizedImportDocument.ValidateUnique(Factions, faction => faction.Id.ToString(), "factions");
        foreach (DaggerfallFaction faction in Factions)
        {
            faction.Validate();
        }

        if (Regions.Count != DaggerfallWorldGridsBuilder.RegionCount)
        {
            throw new InvalidOperationException($"The faction catalog claims {Regions.Count} regions for {DaggerfallWorldGridsBuilder.RegionCount} classic regions.");
        }

        foreach (DaggerfallRegionFactions region in Regions)
        {
            region.Validate();
        }

        foreach (DaggerfallFactionNameAlias alias in DuplicateNames)
        {
            alias.Validate();
        }
    }
}

/// <summary>One name the file states for more than one identity: the donor keeps the first.</summary>
/// <param name="Name">The shared name.</param>
/// <param name="Ids">The identities that state it, in file order.</param>
/// <param name="ResolvedId">The identity the donor's name lookup answers: the first.</param>
public sealed record DaggerfallFactionNameAlias(string Name, IReadOnlyList<int> Ids, int ResolvedId)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A faction name alias states no name.", nameof(Name));
        }

        if (Ids.Count < 2 || Ids[0] != ResolvedId)
        {
            throw new InvalidOperationException($"Faction name '{Name}' aliases {Ids.Count} identities outside the first.");
        }
    }
}

/// <summary>
/// Builds the normalized faction catalog from the classic faction file. Relations keep their filed
/// identities with the disposition each resolves to; regions keep the factions that claim them with
/// the unclaimed regions explicit, so a political cell resolves to faction records or to nothing
/// rather than to a guess. Reputation, rank and service behavior stay out: the catalog publishes
/// filed values, and policy belongs to the social consumers that read them.
/// </summary>
public static class DaggerfallFactionsBuilder
{
    /// <summary>The inventory family the faction file is documented under.</summary>
    public const string FactionsFamily = "CNT-013";

    /// <summary>The donor's faction type names by filed value.</summary>
    public static readonly IReadOnlyDictionary<int, string> TypeNames = new Dictionary<int, string>
    {
        [-1] = "None",
        [0] = "Daedra",
        [1] = "God",
        [2] = "Group",
        [3] = "Subgroup",
        [4] = "Individual",
        [5] = "Official",
        [6] = "VampireClan",
        [7] = "Province",
        [8] = "WitchesCoven",
        [9] = "Temple",
        [10] = "KnightlyGuard",
        [11] = "MagicUser",
        [12] = "Generic",
        [13] = "Thieves",
        [14] = "Courts",
        [15] = "People",
    };

    /// <summary>The donor's social group names by filed value.</summary>
    public static readonly IReadOnlyDictionary<int, string> SocialGroupNames = new Dictionary<int, string>
    {
        [-1] = "None",
        [0] = "Commoners",
        [1] = "Merchants",
        [2] = "Scholars",
        [3] = "Nobility",
        [4] = "Underworld",
        [6] = "SupernaturalBeings",
        [7] = "GuildMembers",
    };

    /// <summary>The donor's guild group names by filed value.</summary>
    public static readonly IReadOnlyDictionary<int, string> GuildGroupNames = new Dictionary<int, string>
    {
        [-1] = "None",
        [2] = "Oblivion",
        [3] = "DarkBrotherHood",
        [4] = "GeneralPopulace",
        [5] = "Bards",
        [6] = "TheFey",
        [7] = "Prostitutes",
        [9] = "KnightlyOrder",
        [10] = "MagesGuild",
        [11] = "FightersGuild",
        [14] = "Necromancers",
        [15] = "Region",
        [17] = "HolyOrder",
        [22] = "Witches",
        [23] = "Vampires",
        [24] = "Orsinium",
    };

    /// <summary>Builds the faction catalog and its region claims.</summary>
    public static DaggerfallFactions Build(string text, string label, byte[] bytes, IReadOnlyList<SourceInventoryRow> inventory)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(inventory);
        SourceInventoryRow family = SourceInventoryRow.RequireFamily(inventory, FactionsFamily);

        IReadOnlyList<ClassicFaction> parsed = FactionReader.Read(text, label);
        HashSet<int> ids = [.. parsed.Select(faction => faction.Id)];
        List<DaggerfallFaction> factions = [];
        foreach (ClassicFaction faction in parsed)
        {
            factions.Add(new DaggerfallFaction(
                faction.Id,
                faction.FiledId,
                faction.Name,
                faction.Parent,
                faction.Parent == 0 ? null : ids.Contains(faction.Parent) ? DaggerfallFactionLinkDisposition.Resolved : DaggerfallFactionLinkDisposition.Unresolved,
                faction.Children,
                faction.Type,
                TypeNames.GetValueOrDefault(faction.Type, string.Empty),
                faction.Region,
                faction.Power,
                faction.Flags,
                faction.Ruler,
                faction.Allies,
                faction.Allies.All(ids.Contains) ? DaggerfallFactionLinkDisposition.Resolved : DaggerfallFactionLinkDisposition.Unresolved,
                faction.Enemies,
                faction.Enemies.All(ids.Contains) ? DaggerfallFactionLinkDisposition.Resolved : DaggerfallFactionLinkDisposition.Unresolved,
                faction.Flats,
                faction.Face,
                faction.Race,
                faction.SocialGroup,
                SocialGroupNames.GetValueOrDefault(faction.SocialGroup, string.Empty),
                faction.GuildGroup,
                GuildGroupNames.GetValueOrDefault(faction.GuildGroup, string.Empty),
                faction.Reputation,
                faction.Summon,
                faction.MinimumFame,
                faction.MaximumFame,
                faction.Vampire,
                faction.Rank));
        }

        List<DaggerfallRegionFactions> regions = [];
        for (int region = 0; region < DaggerfallWorldGridsBuilder.RegionCount; region++)
        {
            int claimed = region;
            IReadOnlyList<int> claimants = factions.Where(faction => faction.Region == claimed).Select(faction => faction.Id).ToArray();
            regions.Add(new DaggerfallRegionFactions(
                region,
                claimants,
                claimants.Count > 0 ? DaggerfallRegionFactionDisposition.Claimed : DaggerfallRegionFactionDisposition.Unclaimed));
        }

        Dictionary<string, List<int>> names = [];
        foreach (DaggerfallFaction faction in factions)
        {
            if (!names.TryGetValue(faction.Name, out List<int>? shared))
            {
                names[faction.Name] = shared = [];
            }

            shared.Add(faction.Id);
        }

        List<DaggerfallFactionNameAlias> aliases = [];
        foreach ((string name, List<int> shared) in names)
        {
            if (shared.Count > 1)
            {
                aliases.Add(new DaggerfallFactionNameAlias(name, shared, shared[0]));
            }
        }

        DaggerfallFactions catalog = new(
            new DaggerfallFactionSource(family.Id, family.PathOrPattern, bytes.LongLength, factions.Count),
            factions,
            regions,
            [.. aliases.OrderBy(alias => alias.Name, StringComparer.Ordinal)]);
        catalog.Validate();
        return catalog;
    }
}
