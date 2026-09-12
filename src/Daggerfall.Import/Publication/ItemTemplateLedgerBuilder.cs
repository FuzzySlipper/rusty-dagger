using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Publication;

/// <summary>
/// Builds the item-template ledger the base pack carries: the classic 288 target indices,
/// what the donor's group enumerations say about each, and the provenance and disposition
/// every target must have before any catalog publication claims a native template fact.
/// </summary>
/// <remarks>
/// The ledger exists because the native template records live in FALL.EXE, which is not
/// supplied. Every target therefore carries <see cref="UnresolvedDisposition"/> and a
/// provenance naming the donor baseline it rests on, and the section records the absent
/// source beside the published item count so a reader cannot mistake a migrated value for
/// a decoded one.
/// </remarks>
public static class ItemTemplateLedgerBuilder
{
    /// <summary>The pack section this builder owns.</summary>
    public const string SectionName = "itemTemplateLedger";

    /// <summary>The disposition every target carries while the native source is absent.</summary>
    public const string UnresolvedDisposition = "unresolved";

    /// <summary>The provenance of a target's group attribution.</summary>
    public const string DonorBaselineProvenance = "donor-group-enumeration";

    /// <summary>The provenance of a target no donor group names.</summary>
    public const string NoDonorGroupProvenance = "no-donor-group";

    /// <summary>
    /// Maps a documented inventory disposition to the native source's status, and refuses
    /// any disposition that does not establish one.
    /// </summary>
    /// <remarks>
    /// The two dispositions that mean the bytes are not supplied map to absent. Everything
    /// else is refused rather than read as supply: the documented next state after a source
    /// gap is still a state without bytes, and a ledger that claimed supply from it would
    /// let a resolved target pass the reader's gate with no source behind it.
    /// </remarks>
    public static string StatusFor(string documentedDisposition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentedDisposition);
        return documentedDisposition switch
        {
            "source-gap" or "pending-import" => AbsentStatus,
            _ => throw new InvalidOperationException($"The documented disposition '{documentedDisposition}' does not establish whether the native item template bytes are supplied, so this ledger will not claim either state from it."),
        };
    }

    /// <summary>The status a target has while the native bytes are not supplied.</summary>
    public const string AbsentStatus = "absent";

    /// <summary>Builds the ledger section from a donor baseline.</summary>
    /// <param name="baseline">The donor baseline read from the donor's own sources.</param>
    /// <param name="targetRecordId">The documented inventory record for the native source.</param>
    /// <param name="targetPath">The documented path of the native source.</param>
    /// <param name="targetStatus">The native source's status.</param>
    /// <param name="publishedItemCount">How many item definitions the pack publishes.</param>
    public static JsonObject Build(
        ItemTemplateBaseline baseline,
        string targetRecordId,
        string targetPath,
        string targetStatus,
        int publishedItemCount)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRecordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetStatus);
        if (baseline.OutOfRangeIndices.Count != 0)
        {
            throw new InvalidOperationException($"The donor's group enumerations name template indices outside the classic space: [{string.Join(", ", baseline.OutOfRangeIndices)}].");
        }

        JsonArray rules = [];
        foreach (ItemTemplateRule rule in baseline.Rules)
        {
            rules.Add(new JsonObject
            {
                ["id"] = rule.Id,
                ["rule"] = rule.Rule,
                ["evidence"] = rule.Evidence,
            });
        }

        JsonArray targets = [];
        foreach (ItemTemplateTarget target in baseline.Targets)
        {
            JsonArray groups = [];
            foreach (string group in target.DonorGroups)
            {
                groups.Add(group);
            }

            JsonArray referenceGroups = [];
            foreach (string group in target.DonorReferenceGroups)
            {
                referenceGroups.Add(group);
            }

            targets.Add(new JsonObject
            {
                ["index"] = target.Index,
                ["donorGroups"] = groups,
                ["donorReferenceGroups"] = referenceGroups,
                ["provenance"] = target.IsReferenced ? DonorBaselineProvenance : NoDonorGroupProvenance,
                ["disposition"] = UnresolvedDisposition,
            });
        }

        return new JsonObject
        {
            ["target"] = new JsonObject
            {
                ["recordId"] = targetRecordId,
                ["path"] = targetPath,
                ["status"] = targetStatus,
                ["reason"] = "The native item template records live in this file, which the supplied source does not carry, so no native template field is decoded and no target is resolved.",
            },
            ["baseline"] = new JsonObject
            {
                ["rule"] = "DEC-11",
                ["citation"] = "donor-code",
                ["donorSource"] = "donor:Assets/Scripts/Game/Items/ItemEnums.cs + ItemHelper.GetEnumArray",
                ["attribution"] = "Group attribution is the donor's own mapping from its item groups to its enumerations; it establishes which indices a group names, not what any template contains. This baseline cites donor code rather than a supplied source, which is why it carries no inventory record: the inventory documents the native file the target names, not the donor code standing in for it.",
                ["rules"] = rules,
            },
            ["substitute"] = new JsonObject
            {
                ["status"] = "available",
                ["path"] = "donor:Assets/Resources/ItemTemplates.txt (and MagicItemTemplates.txt beside it)",
                ["provenance"] = "The donor loads this table at runtime and states it was exported from FALL.EXE. It is a substitute for the byte source, not the byte source.",
                ["recordId"] = string.Empty,
                ["caveat"] = "No byte fidelity against FALL.EXE can be checked here: the executable is in neither checkout. Decoding from the substitute is the receiving task's work and is not claimed by this ledger.",
            },
            ["publishedItems"] = new JsonObject
            {
                ["count"] = publishedItemCount,
                ["valueProvenance"] = "catalog-migration",
                ["restsOn"] = new JsonArray
                {
                    "donor:Assets/Resources/ItemTemplates.txt (basePrice, weight and handedness agree with it)",
                    "donor:FormulaHelper.CalculateWeaponMin/MaxDamage (weapon damage ranges agree with it)",
                },
                ["nativeDecoding"] = false,
                ["note"] = "Published item values were carried over by the catalog migration and agree with the donor's exported table and its weapon damage rules. This task decoded nothing: no native index is claimed for them and the agreement is a provenance record, not a re-derivation.",
            },
            ["summary"] = new JsonObject
            {
                ["targets"] = baseline.Targets.Count,
                ["referencedByDonorGroups"] = baseline.Targets.Count(target => target.IsReferenced),
                ["unreferencedByAnyGroup"] = baseline.Unreferenced.Count(),
                ["nativeTemplatesDecoded"] = 0,
            },
            ["targets"] = targets,
        };
    }
}
