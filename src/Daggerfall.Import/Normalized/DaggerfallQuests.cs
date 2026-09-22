using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a quest source is accounted for.</summary>
public enum DaggerfallQuestDisposition
{
    /// <summary>Every section and top-level QBN block matches the source compiler; action bodies remain unexecuted source records.</summary>
    Compiled,
    /// <summary>Some line matches nothing; the quest is diagnosed and must not run.</summary>
    Diagnosed,
}

/// <summary>One normalized quest source: its identity, spans and blocks.</summary>
/// <param name="Name">The quest name, unique in the pack.</param>
/// <param name="DisplayName">The display name, empty when the source states none.</param>
/// <param name="SourceFile">The source file name.</param>
/// <param name="Disposition">Whether the quest may run.</param>
/// <param name="Messages">The QRC message blocks.</param>
/// <param name="Blocks">The QBN blocks.</param>
/// <param name="Diagnostics">The unknown-signature diagnostics with source lines.</param>
public sealed record DaggerfallQuestRecord(
    string Name,
    string DisplayName,
    string SourceFile,
    DaggerfallQuestDisposition Disposition,
    IReadOnlyList<DaggerfallQuestMessage> Messages,
    IReadOnlyList<DaggerfallQuestBlock> Blocks,
    IReadOnlyList<DaggerfallQuestDiagnostic> Diagnostics)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Name, nameof(Name));
        NormalizedImportDocument.RequireLogicalPath(SourceFile, nameof(SourceFile));
        if (!Enum.IsDefined(Disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(Disposition), Disposition, "A quest names a disposition the contract does not declare.");
        }

        NormalizedImportDocument.ValidateUnique(Messages, message => message.Id.ToString(), $"quest {Name} messages");
        foreach (DaggerfallQuestBlock block in Blocks)
        {
            block.Validate();
        }

        foreach (DaggerfallQuestDiagnostic diagnostic in Diagnostics)
        {
            diagnostic.Validate();
        }

        if (Disposition == DaggerfallQuestDisposition.Compiled && Diagnostics.Count != 0)
        {
            throw new InvalidOperationException($"Quest '{Name}' is compiled with diagnostics attached.");
        }

        if (Disposition == DaggerfallQuestDisposition.Diagnosed && Diagnostics.Count == 0)
        {
            throw new InvalidOperationException($"Quest '{Name}' is diagnosed with no diagnostic attached.");
        }
    }
}

/// <summary>One normalized message block: its id, first line and lines.</summary>
/// <param name="Id">The message id.</param>
/// <param name="FirstLine">The 1-based first line.</param>
/// <param name="Lines">The message lines.</param>
public sealed record DaggerfallQuestMessage(int Id, int FirstLine, IReadOnlyList<string> Lines);

/// <summary>One normalized QBN block: its top-level kind, ordered source lines and global link.</summary>
/// <param name="Kind">The block kind.</param>
/// <param name="FirstLine">The 1-based first line.</param>
/// <param name="Lines">The ordered source lines, retained for later action compilation rather than treated as executable here.</param>
/// <param name="Global">The linked global key, if any.</param>
public sealed record DaggerfallQuestBlock(QuestBlockKind Kind, int FirstLine, IReadOnlyList<string> Lines, int? Global)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A quest block names a kind the contract does not declare.");
        }

        if (FirstLine <= 0 || Lines.Count == 0)
        {
            throw new ArgumentException("A quest block states no lines.", nameof(Lines));
        }
    }
}

/// <summary>One quest diagnostic: what the source states that no signature claims.</summary>
/// <param name="Line">The 1-based source line.</param>
/// <param name="Text">The offending line.</param>
/// <param name="Reason">Why no signature claims it.</param>
public sealed record DaggerfallQuestDiagnostic(int Line, string Text, string Reason)
{
    public void Validate()
    {
        if (Line <= 0 || string.IsNullOrWhiteSpace(Text) || string.IsNullOrWhiteSpace(Reason))
        {
            throw new ArgumentException("A quest diagnostic states no line, text or reason.", nameof(Reason));
        }
    }
}

/// <summary>The normalized quest source pack.</summary>
/// <param name="Source">The provenance both tables were read from.</param>
/// <param name="Quests">The quests in source order.</param>
public sealed record DaggerfallQuestPack(
    DaggerfallTextSource Source,
    IReadOnlyList<DaggerfallQuestRecord> Quests,
    DaggerfallQuestResources? Resources = null)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        NormalizedImportDocument.ValidateUnique(Quests, quest => quest.Name + "\0" + quest.SourceFile, "quests");
        foreach (DaggerfallQuestRecord quest in Quests)
        {
            quest.Validate();
        }

        Resources?.Validate();
    }
}

/// <summary>
/// Builds the normalized quest source pack from parsed quest text. Duplicate quest names are
/// refused: a semantic id must resolve to exactly one source. A quest whose every line matches
/// a known signature is compiled; any refusal the reader raises becomes a diagnosed quest that
/// keeps its source lines rather than a silent drop.
/// </summary>
public static class DaggerfallQuestPackBuilder
{
    // The decompiled text carries both sections; it is admitted under the QBN quest-source
    // family because that row documents quest behavior sources rather than message binaries.
    /// <summary>The inventory family quest sources are documented under.</summary>
    public const string QuestsFamily = "CNT-017-QBN";

    /// <summary>Builds the quest source pack.</summary>
    public static DaggerfallQuestPack Build(
        IReadOnlyList<QuestSourceDocument> documents,
        IReadOnlyList<(string FileName, string QuestName, int Line, string Reason)> failures,
        string label,
        byte[] bytes,
        IReadOnlyList<SourceInventoryRow> inventory)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(inventory);
        SourceInventoryRow family = SourceInventoryRow.RequireFamily(inventory, QuestsFamily);

        List<DaggerfallQuestRecord> quests = [];
        foreach (QuestSourceDocument document in documents)
        {
            quests.Add(new DaggerfallQuestRecord(
                document.QuestName,
                document.DisplayName,
                document.FileName,
                DaggerfallQuestDisposition.Compiled,
                [.. document.Messages.Select(message => new DaggerfallQuestMessage(message.Id, message.FirstLine, message.Lines))],
                [.. document.Blocks.Select(block => new DaggerfallQuestBlock(block.Kind, block.FirstLine, block.Lines, block.Global))],
                []));
        }

        foreach ((string fileName, string questName, int line, string reason) in failures)
        {
            quests.Add(new DaggerfallQuestRecord(
                questName,
                string.Empty,
                fileName,
                DaggerfallQuestDisposition.Diagnosed,
                [],
                [],
                [new DaggerfallQuestDiagnostic(line, fileName, reason)]));
        }

        // A duplicate semantic id resolves to neither source: both records are diagnosed and
        // neither may run, because a quest name must name exactly one source.
        Dictionary<string, int> names = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestRecord quest in quests)
        {
            names[quest.Name] = names.GetValueOrDefault(quest.Name) + 1;
        }

        Dictionary<string, int> nameLines = [];
        foreach (QuestSourceDocument document in documents)
        {
            nameLines[document.FileName] = document.QuestNameLine;
        }

        List<DaggerfallQuestRecord> resolved = [];
        foreach (DaggerfallQuestRecord quest in quests)
        {
            if (names[quest.Name] > 1)
            {
                resolved.Add(quest with
                {
                    Disposition = DaggerfallQuestDisposition.Diagnosed,
                    Diagnostics = [.. quest.Diagnostics, new DaggerfallQuestDiagnostic(nameLines.GetValueOrDefault(quest.SourceFile), quest.SourceFile, $"Quest name '{quest.Name}' resolves to {names[quest.Name]} sources.")],
                });
            }
            else
            {
                resolved.Add(quest);
            }
        }

        DaggerfallQuestPack pack = new(
            new DaggerfallTextSource(DaggerfallTextKind.Resource, family.Id, label, "en", bytes.LongLength, 0, resolved.Count),
            [.. resolved.OrderBy(quest => quest.SourceFile, StringComparer.Ordinal)]);
        pack = pack with { Resources = DaggerfallQuestResourceBuilder.Build(pack) };
        pack.Validate();
        return pack;
    }
}
