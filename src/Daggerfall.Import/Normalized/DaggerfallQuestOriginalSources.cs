using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

/// <summary>Which offline source, if any, supplies the normalized quest text for one classic stem.</summary>
public enum DaggerfallQuestOriginalSourceSelection
{
    /// <summary>A parsed, compiled rewritten text source is available for offline normalization.</summary>
    RewrittenText,

    /// <summary>A rewritten source exists but it is diagnosed and remains unavailable to execution.</summary>
    DiagnosedRewrittenText,

    /// <summary>No rewritten text source exists; the binary and resource data are recorded but not enabled.</summary>
    NoRewrittenText,
}

/// <summary>One physical classic QRC record after only its record delimiters are decoded.</summary>
public sealed record DaggerfallQuestOriginalMessage(int Id, int Occurrence, int Offset, string Text, string Digest);

/// <summary>The comparison of one original QRC message identity with rewritten text.</summary>
public sealed record DaggerfallQuestMessageComparison(
    int Id,
    int OriginalRecordCount,
    bool PresentInRewrittenText,
    bool TextMatches,
    string? RewrittenSourceFile,
    string Difference);

/// <summary>
/// The source selection and differential evidence for one original classic quest stem.
/// These records are offline provenance only. They never grant a binary or a diagnosed
/// text source permission to execute.
/// </summary>
public sealed record DaggerfallQuestOriginalSource(
    string Stem,
    string? BinaryPath,
    string? ResourcePath,
    string? RewrittenSourceFile,
    DaggerfallQuestOriginalSourceSelection Selection,
    string SourceDisposition,
    string Availability,
    QuestBinaryHeader? BinaryHeader,
    IReadOnlyList<QuestBinaryTextReference> BinaryResourceReferences,
    IReadOnlyList<QuestBinaryOpcodeReference> BinaryOpcodeReferences,
    IReadOnlyList<DaggerfallQuestOriginalMessage> OriginalMessages,
    IReadOnlyList<int> RewrittenMessageIds,
    IReadOnlyList<string> RewrittenResourceMessageReferences,
    IReadOnlyList<int> RewrittenResourceMessageIds,
    IReadOnlyList<DaggerfallQuestMessageComparison> MessageComparisons,
    IReadOnlyList<int> BinaryMessageIdsMissingFromOriginalResources,
    IReadOnlyList<int> BinaryMessageIdsMissingFromRewrittenText,
    IReadOnlyList<int> BinaryResourceIdsMissingFromRewrittenResources,
    IReadOnlyList<string> Differences);

/// <summary>All original classic stems and their explicit normalized-source selections.</summary>
public sealed record DaggerfallQuestOriginalSourceSet(IReadOnlyList<DaggerfallQuestOriginalSource> Quests)
{
    public const int ExpectedStemCount = 307;
    public void Validate()
    {
        if (Quests.Count != ExpectedStemCount)
        {
            throw new InvalidOperationException($"The original quest source set has {Quests.Count} stems where the supplied corpus has {ExpectedStemCount}.");
        }

        NormalizedImportDocument.ValidateUnique(Quests, quest => quest.Stem, "original quest stems");
        foreach (DaggerfallQuestOriginalSource quest in Quests)
        {
            NormalizedImportDocument.RequireLogicalId(quest.Stem, nameof(quest.Stem));
            if (quest.BinaryPath is null && quest.ResourcePath is null)
            {
                throw new InvalidOperationException($"Original quest '{quest.Stem}' names no supplied classic file.");
            }

            if (quest.Selection == DaggerfallQuestOriginalSourceSelection.RewrittenText
                && !string.Equals(quest.SourceDisposition, "compiled", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Original quest '{quest.Stem}' selects rewritten text without a compiled source.");
            }

            if (quest.Selection != DaggerfallQuestOriginalSourceSelection.RewrittenText
                && !quest.Availability.Contains("not enabled", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unavailable original quest '{quest.Stem}' does not explain that it is not enabled.");
            }
        }
    }
}

/// <summary>
/// Compares the supplied Arena2 QBN/QRC records with the separately rewritten quest text.
/// The builder decodes record delimiters and fixed QBN identity fields only; it does not
/// infer binary behavior, make a text equivalence claim, or enable an unavailable source.
/// </summary>
public static class DaggerfallQuestOriginalSourceBuilder
{
    /// <summary>Builds one disposition for every supplied QBN/QRC stem.</summary>
    public static DaggerfallQuestOriginalSourceSet Build(string arena2, QuestSourceInventory inventory, DaggerfallQuestPack rewritten)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arena2);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(rewritten);
        Dictionary<string, DaggerfallQuestRecord> textByStem = rewritten.Quests
            .GroupBy(quest => Path.GetFileNameWithoutExtension(quest.SourceFile), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        List<DaggerfallQuestOriginalSource> quests = [];
        foreach (IGrouping<string, QuestSourceFile> group in inventory.Files.GroupBy(file => file.Stem, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            QuestSourceFile? binary = group.SingleOrDefault(file => file.Family == QuestSourceFamily.QuestBinary);
            QuestSourceFile? resource = group.SingleOrDefault(file => file.Family == QuestSourceFamily.QuestResources);
            DaggerfallQuestRecord? text = textByStem.GetValueOrDefault(group.Key);
            byte[]? binaryBytes = binary is null ? null : File.ReadAllBytes(Path.Combine(arena2, binary.Path));
            if (binary is not null)
            {
                QuestBinaryEnvelope envelope = QuestBinaryEnvelope.Decode(binaryBytes!, binary.Path);
                if (envelope.Disposition != QuestBinaryEnvelopeDisposition.WellFormed)
                {
                    throw new Arena2FormatException(binary.Path, 0, $"QBN envelope is {envelope.Disposition}: {envelope.Note}");
                }
            }

            QuestBinaryRecords? qbn = binary is null ? null : QuestBinaryRecords.Decode(binaryBytes!, binary.Path);
            if (qbn is not null && qbn.Disposition != QuestBinaryRecordDisposition.Decoded)
            {
                throw new Arena2FormatException(binary!.Path, 0, $"QBN identity decode is {qbn.Disposition}: {qbn.Note}");
            }

            IReadOnlyList<DaggerfallQuestOriginalMessage> originalMessages = resource is null
                ? []
                : ReadOriginalMessages(arena2, resource);
            DaggerfallQuestOriginalSourceSelection selection = text?.Disposition switch
            {
                DaggerfallQuestDisposition.Compiled => DaggerfallQuestOriginalSourceSelection.RewrittenText,
                DaggerfallQuestDisposition.Diagnosed => DaggerfallQuestOriginalSourceSelection.DiagnosedRewrittenText,
                _ => DaggerfallQuestOriginalSourceSelection.NoRewrittenText,
            };
            int[] rewrittenMessages = text is null ? [] : [.. text.Messages.Select(message => message.Id).Distinct().Order()];
            string[] rewrittenResourceReferences = text is null ? [] : RewrittenResourceReferences(rewritten, text);
            int[] rewrittenResources = [.. rewrittenResourceReferences
                .Select(reference => reference[(reference.IndexOf(':') + 1)..])
                .Where(value => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                .Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order()];
            int[] qrcIds = [.. originalMessages.Select(message => message.Id).Distinct().Order()];
            int[] binaryIds = qbn is null ? [] : BinaryMessageIds(qbn);
            int[] binaryResourceIds = qbn is null ? [] : BinaryResourceIds(qbn);
            List<DaggerfallQuestMessageComparison> comparisons = CompareMessages(originalMessages, text);
            List<string> differences = [.. comparisons.Where(value => value.Difference.Length != 0).Select(value => value.Difference)];
            int[] binaryMissingQrc = [.. binaryIds.Where(id => !qrcIds.Contains(id)).Order()];
            int[] binaryMissingText = [.. binaryIds.Where(id => !rewrittenMessages.Contains(id)).Order()];
            int[] binaryResourceMissingText = [.. binaryResourceIds.Where(id => !rewrittenResources.Contains(id)).Order()];
            if (binaryMissingQrc.Length != 0) differences.Add($"QBN text-record values absent from its supplied QRC: {string.Join(", ", binaryMissingQrc)}.");
            if (binaryMissingText.Length != 0) differences.Add($"QBN text-record values absent from rewritten QRC text: {string.Join(", ", binaryMissingText)}.");
            if (binaryResourceMissingText.Length != 0) differences.Add($"QBN resource-record text values absent from rewritten resource declarations: {string.Join(", ", binaryResourceMissingText)}.");
            string availability = SelectionAvailability(selection, binary, resource, text);
            if (selection != DaggerfallQuestOriginalSourceSelection.RewrittenText) differences.Add(availability);
            quests.Add(new DaggerfallQuestOriginalSource(
                group.Key,
                binary?.Path,
                resource?.Path,
                text?.SourceFile,
                selection,
                text?.Disposition.ToString().ToLowerInvariant() ?? "missing",
                availability,
                qbn?.Header,
                qbn?.ResourceReferences ?? [],
                qbn?.OpcodeReferences ?? [],
                originalMessages,
                rewrittenMessages,
                rewrittenResourceReferences,
                rewrittenResources,
                comparisons,
                binaryMissingQrc,
                binaryMissingText,
                binaryResourceMissingText,
                differences));
        }

        DaggerfallQuestOriginalSourceSet result = new([.. quests]);
        result.Validate();
        return result;
    }

    private static IReadOnlyList<DaggerfallQuestOriginalMessage> ReadOriginalMessages(string arena2, QuestSourceFile resource)
    {
        QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(File.ReadAllBytes(Path.Combine(arena2, resource.Path)), resource.Path);
        if (envelope.Disposition != QuestResourceEnvelopeDisposition.Decoded)
        {
            throw new Arena2FormatException(resource.Path, 0, $"QRC record decode is {envelope.Disposition}: {envelope.Note}");
        }

        Dictionary<int, int> occurrence = [];
        List<DaggerfallQuestOriginalMessage> result = [];
        foreach (QuestResourceRecord record in envelope.Records)
        {
            int id = record.Id;
            int number = occurrence.GetValueOrDefault(id);
            occurrence[id] = number + 1;
            result.Add(new DaggerfallQuestOriginalMessage(id, number, record.Offset, DecodeRecordText(record.Payload.Span), Digest(record.Payload.Span)));
        }

        return result;
    }

    private static string DecodeRecordText(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0 || payload[^1] != 0xfe)
        {
            throw new InvalidOperationException("A decoded QRC record does not end in the corpus record terminator.");
        }

        StringBuilder text = new(payload.Length);
        foreach (byte value in payload[..^1])
        {
            text.Append(value switch
            {
                0xfc => '\n', // Letter line terminator.
                0xfd => ' ', // Text line terminator.
                0xff => '\u001f', // Subrecord boundary retained explicitly below.
                _ => (char)value,
            });
        }

        return text.ToString().Replace("\u001f", "\n<--->\n", StringComparison.Ordinal).Trim();
    }

    private static List<DaggerfallQuestMessageComparison> CompareMessages(IReadOnlyList<DaggerfallQuestOriginalMessage> original, DaggerfallQuestRecord? text)
    {
        Dictionary<int, DaggerfallQuestMessage> rewritten = text?.Messages.ToDictionary(message => message.Id) ?? [];
        List<DaggerfallQuestMessageComparison> result = [];
        foreach (IGrouping<int, DaggerfallQuestOriginalMessage> group in original.GroupBy(message => message.Id).OrderBy(group => group.Key))
        {
            if (!rewritten.TryGetValue(group.Key, out DaggerfallQuestMessage? message))
            {
                result.Add(new(group.Key, group.Count(), false, false, text?.SourceFile,
                    $"Original QRC id {group.Key} has {group.Count()} physical record(s) and no rewritten message."));
                continue;
            }

            string originalText = string.Join("\n<record>\n", group.Select(value => value.Text));
            string rewrittenText = string.Join("\n", message.Lines).Trim();
            bool match = string.Equals(NormalizeForComparison(originalText), NormalizeForComparison(rewrittenText), StringComparison.Ordinal);
            result.Add(new(group.Key, group.Count(), true, match, text!.SourceFile,
                match ? string.Empty : $"Original QRC id {group.Key} differs from rewritten message {group.Key}; record delimiters are decoded but layout and macro text are otherwise compared literally."));
        }

        foreach (DaggerfallQuestMessage message in rewritten.Values.Where(message => !original.Any(value => value.Id == message.Id)).OrderBy(message => message.Id))
        {
            result.Add(new(message.Id, 0, true, false, text!.SourceFile,
                $"Rewritten message id {message.Id} has no physical record in the supplied original QRC."));
        }

        return result;
    }

    private static string NormalizeForComparison(string value) => Regex.Replace(value.Replace("\r", string.Empty, StringComparison.Ordinal), @"[ \t\n]+", " ").Trim();

    private static string[] RewrittenResourceReferences(DaggerfallQuestPack pack, DaggerfallQuestRecord text)
    {
        DaggerfallQuestResources resources = pack.Resources
            ?? throw new InvalidOperationException("The rewritten quest pack has no normalized resource declarations.");
        List<string> references = [];
        foreach (DaggerfallQuestResourceDeclaration declaration in resources.Declarations.Where(declaration => string.Equals(declaration.SourceFile, text.SourceFile, StringComparison.Ordinal)))
        {
            // The typed item fields are the canonical resource contract where they apply.
            if (declaration.Item?.UsedMessage is string used) references.Add($"used:{used}");
            if (declaration.Item?.AnyInfoMessage is string anyInfo) references.Add($"anyInfo:{anyInfo}");
            // Parameters preserve every remaining source field, including person and place
            // values for which the broader resource model has no narrower typed property.
            for (int index = 0; index + 1 < declaration.Parameters.Count; index++)
            {
                string key = declaration.Parameters[index];
                if (key.Equals("used", StringComparison.OrdinalIgnoreCase) || key.Equals("anyInfo", StringComparison.OrdinalIgnoreCase))
                {
                    references.Add($"{key.ToLowerInvariant()}:{declaration.Parameters[index + 1]}");
                }
            }
        }

        return [.. references.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static int[] BinaryMessageIds(QuestBinaryRecords qbn) =>
        [.. qbn.ResourceReferences.SelectMany(reference => new[] { (int)reference.FirstMessageId, reference.SecondMessageId })
            .Concat(qbn.OpcodeReferences.Select(reference => (int)reference.MessageId))
            .Distinct().Order()];

    private static int[] BinaryResourceIds(QuestBinaryRecords qbn) =>
        [.. qbn.ResourceReferences.SelectMany(reference => new[] { (int)reference.FirstMessageId, reference.SecondMessageId })
            .Distinct().Order()];

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string SelectionAvailability(DaggerfallQuestOriginalSourceSelection selection, QuestSourceFile? binary, QuestSourceFile? resource, DaggerfallQuestRecord? text) => selection switch
    {
        DaggerfallQuestOriginalSourceSelection.RewrittenText => "Compiled rewritten text is selected for offline normalization; this provenance record does not enable binary execution.",
        DaggerfallQuestOriginalSourceSelection.DiagnosedRewrittenText => $"Rewritten source '{text!.SourceFile}' is diagnosed and not enabled; classic source presence is {ClassicPresence(binary, resource)}.",
        _ => $"No rewritten text source is supplied, so classic source presence is {ClassicPresence(binary, resource)} and the stem is not enabled.",
    };

    private static string ClassicPresence(QuestSourceFile? binary, QuestSourceFile? resource) =>
        $"QBN {(binary is null ? "absent" : "recorded only")}; QRC {(resource is null ? "absent" : "recorded only")}";
}
