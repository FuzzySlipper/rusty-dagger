using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// One native item template target: its index in the classic 288-entry space and the
/// donor groups whose enumerations name that index. An index no group names is retained
/// with an empty group list rather than dropped, because the target exists whether or not
/// the donor has a group for it.
/// </summary>
public sealed record ItemTemplateTarget(
    int Index,
    IReadOnlyList<string> DonorGroups,
    IReadOnlyList<string> DonorReferenceGroups)
{
    /// <summary>Whether any donor group names this index as a template index.</summary>
    public bool IsReferenced => DonorGroups.Count != 0;
}

/// <summary>
/// One rule the baseline was built under, with the donor evidence it rests on. The rules
/// are published with the ledger so a reader can reconstruct the reading rather than
/// inferring it from code comments.
/// </summary>
public sealed record ItemTemplateRule(string Id, string Rule, string Evidence);

/// <summary>
/// One item group as the donor maps it to an enumeration: its <c>ItemGroups</c> value, its name, whether its members are
/// template indices, and its members' values in the exact GetEnumArray order, aliases and implicit members kept.
/// </summary>
public sealed record ItemGroupEnumeration(int Id, string Name, bool TemplateIndices, IReadOnlyList<int> Values);

/// <summary>
/// Our item-group table, taken once from the donor's <c>ItemEnums.cs</c> (the enumerations), <c>ItemHelper.cs</c>
/// (the group-to-enumeration mapping, in its order) and <c>ItemsFile.cs</c> (the native template count).
/// </summary>
public sealed record ItemGroupTable(TakenFrom ItemEnums, TakenFrom ItemHelper, TakenFrom ItemsFile, int TemplateCount, IReadOnlyList<ItemGroupEnumeration> Groups);

/// <summary>
/// The donor's item-template baseline: which of the classic 288 template indices the
/// donor's group enumerations reference, built from our item-group table, which was taken
/// from the donor's own sources rather than transcribed by hand.
/// </summary>
/// <remarks>
/// This is a baseline about *indices*, not about item data. The native template records
/// live in FALL.EXE, which is not supplied, so nothing here carries a template's name,
/// weight, price or damage. The donor establishes two things and only two: the index
/// space is 0..287, and its group enumerations assign some of those indices to groups.
/// Both are facts about donor code, which is why the ledger built from this marks them as
/// a provisional baseline under DEC-11 rather than as native decoding.
/// </remarks>
public sealed class ItemTemplateBaseline
{
    /// <summary>The classic native template count.</summary>
    public const int TargetCount = 288;

    private ItemTemplateBaseline(
        string source,
        IReadOnlyList<ItemTemplateTarget> targets,
        IReadOnlyList<int> outOfRangeIndices,
        IReadOnlyList<ItemTemplateRule> rules)
    {
        Source = source;
        Targets = targets;
        OutOfRangeIndices = outOfRangeIndices;
        Rules = rules;
    }

    /// <summary>Logical source identity supplied to <see cref="FromGroups"/>.</summary>
    public string Source { get; }

    /// <summary>Every target index in ascending order.</summary>
    public IReadOnlyList<ItemTemplateTarget> Targets { get; }

    /// <summary>
    /// Indices the donor's enumerations name beyond the classic template space. Retained
    /// rather than refused: an index past 287 means donor drift, which the ledger reports.
    /// A negative member is the donor's own "none" sentinel rather than an index and is
    /// not reported here.
    /// </summary>
    public IReadOnlyList<int> OutOfRangeIndices { get; }

    /// <summary>The rules the baseline was read under, each with its donor evidence.</summary>
    public IReadOnlyList<ItemTemplateRule> Rules { get; }

    /// <summary>The targets no donor group names as a template index.</summary>
    public IEnumerable<ItemTemplateTarget> Unreferenced => Targets.Where(target => !target.IsReferenced);

    /// <summary>
    /// The baseline over our item-group table: the index space it declares, and the indices each group names, in
    /// the order the donor maps groups to enumerations.
    /// </summary>
    public static ItemTemplateBaseline FromGroups(ItemGroupTable table, string source)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (table.Groups.Count == 0)
        {
            throw new Arena2FormatException(source, 0, "the item-group table names no group, so no template index can be attributed");
        }

        // The native template count is the donor's own constant; the baseline refuses to
        // build a space of a different size than the source it cites declares.
        int declaredCount = table.TemplateCount;
        if (declaredCount != TargetCount)
        {
            throw new Arena2FormatException(source, 0, $"the donor declares {declaredCount} native item templates where this baseline covers {TargetCount}");
        }

        Dictionary<int, List<string>> groupsByIndex = [];
        Dictionary<int, List<string>> referenceGroupsByIndex = [];
        List<ItemTemplateRule> rules =
        [
            new("index-space", $"The target space is the {TargetCount} native template indices 0..{TargetCount - 1}.", $"daggerfall-unity/Assets/Scripts/API/ItemsFile.cs declares totalItems = {declaredCount} and daggerfall-unity/Assets/Scripts/Game/Items/ItemHelper.cs declares LastDFTemplate = {TargetCount - 1}."),
            new("declaration-comment", "A mapped enumeration whose declaration says its values are not template indices contributes reference ids, not template indices.", "daggerfall-unity/Assets/Scripts/Game/Items/ItemEnums.cs marks MagicItemSubTypes 'Not mapped to a specific item template index' and ArtifactsSubTypes 'Mapped to artifact definitions in MAGIC.DEF'."),
            new("implicit-values", "A member without an explicit value takes the previous member's value plus one, as the donor's runtime enumerations do.", "daggerfall-unity/Assets/Scripts/Game/Items/ItemEnums.cs leaves Deeds, the magic sub-type and MiscItems.Unused unvalued, and the donor's GetEnumArray returns the runtime array."),
            new("sentinel-exclusion", "A negative member names no template and takes no place in the index space.", "daggerfall-unity/Assets/Scripts/Game/Items/ItemEnums.cs declares None = -1 in ItemGroups and ArtifactsSubTypes."),
            new("alias-once", "A group naming one index through several members counts once.", "daggerfall-unity/Assets/Scripts/Game/Items/ItemEnums.cs gives the four Books members the value 277."),
            new("substitute-source", "The native file is absent, and the donor's exported table is the substitute the decode must rest on until an authorized byte source appears.", "daggerfall-unity/Assets/Scripts/Game/Items/ItemHelper.cs loads Assets/Resources/ItemTemplates.txt, which it states was exported from FALL.EXE, and MagicItemTemplates.txt beside it."),
        ];

        foreach (ItemGroupEnumeration enumeration in table.Groups)
        {
            string group = enumeration.Name;
            bool templateIndices = enumeration.TemplateIndices;
            foreach (int value in enumeration.Values)
            {
                // A negative member is the enumeration's "none" sentinel: it names no
                // template, so it takes no place in the index space.
                if (value < 0)
                {
                    continue;
                }

                Dictionary<int, List<string>> byIndex = templateIndices ? groupsByIndex : referenceGroupsByIndex;
                if (!byIndex.TryGetValue(value, out List<string>? groups))
                {
                    byIndex[value] = groups = [];
                }

                // A group may name one index through several members: the donor's four book
                // variants all name index 277. The group is recorded once, since the aliasing
                // is a fact about the donor's naming rather than about the index space.
                if (!groups.Contains(group, StringComparer.Ordinal))
                {
                    groups.Add(group);
                }
            }
        }

        List<ItemTemplateTarget> targets = new(TargetCount);
        for (int index = 0; index < TargetCount; index++)
        {
            targets.Add(new ItemTemplateTarget(
                index,
                groupsByIndex.TryGetValue(index, out List<string>? groups) ? groups : [],
                referenceGroupsByIndex.TryGetValue(index, out List<string>? references) ? references : []));
        }

        int[] outOfRange = [.. groupsByIndex.Keys.Concat(referenceGroupsByIndex.Keys).Where(index => index >= TargetCount).Order()];
        return new ItemTemplateBaseline(source, targets, outOfRange, rules);
    }
}
