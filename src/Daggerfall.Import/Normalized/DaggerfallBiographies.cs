using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a biography text link is accounted for.</summary>
public enum DaggerfallBiographyLinkDisposition
{
    /// <summary>The linked record is published and the reference resolves.</summary>
    Resolved,
    /// <summary>The linked record is missing, so the reference names nothing.</summary>
    Unresolved,
}

/// <summary>One normalized answer effect: its verbatim line, its kind, and its link state.</summary>
/// <param name="Text">The effect line exactly as the file states it.</param>
/// <param name="Kind">How the donor classifies the line.</param>
/// <param name="First">The first operand the kind carries, or empty.</param>
/// <param name="Second">The second operand the kind carries, or empty.</param>
/// <param name="Third">The third operand the kind carries, or empty.</param>
/// <param name="MacroTarget">The text resource record a text macro names, empty for other kinds.</param>
/// <param name="MacroTargetDisposition">Whether that record exists; other kinds carry no link.</param>
/// <param name="MacroTargetReason">Why the link does not resolve, empty when it does or does not apply.</param>
public sealed record DaggerfallBiographyEffect(
    string Text,
    BiogEffectKind Kind,
    string First,
    string Second,
    string Third,
    string MacroTarget,
    DaggerfallBiographyLinkDisposition? MacroTargetDisposition,
    string MacroTargetReason)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A published biography effect names a kind the contract does not declare.");
        }

        bool macro = Kind == BiogEffectKind.TextMacro;
        if (macro != (MacroTarget.Length != 0))
        {
            throw new InvalidOperationException($"Biography effect '{Text}' carries a macro target exactly when it is a text macro.");
        }

        if (MacroTargetDisposition is not null != macro)
        {
            throw new InvalidOperationException($"Biography effect '{Text}' states a link disposition exactly when it is a text macro.");
        }

        if (MacroTargetReason.Length != 0 && MacroTargetDisposition != DaggerfallBiographyLinkDisposition.Unresolved)
        {
            throw new InvalidOperationException($"Biography effect '{Text}' states a reason without an unresolved link.");
        }
    }
}

/// <summary>One normalized answer: its letter, its text key, and its effects.</summary>
/// <param name="Letter">The answer's letter.</param>
/// <param name="TextKey">The text key the answer's prose resolves through.</param>
/// <param name="Effects">The answer's effects, in file order.</param>
public sealed record DaggerfallBiographyAnswer(string Letter, string TextKey, IReadOnlyList<DaggerfallBiographyEffect> Effects)
{
    public void Validate()
    {
        if (Letter.Length != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Letter), Letter, "A published biography answer carries no single letter.");
        }

        NormalizedImportDocument.RequireLogicalId(TextKey, nameof(TextKey));
        foreach (DaggerfallBiographyEffect effect in Effects)
        {
            effect.Validate();
        }
    }
}

/// <summary>One normalized question: its stated number, its prose keys, and its answers.</summary>
/// <param name="Number">The question number the file states.</param>
/// <param name="TextKeys">The text keys the question's prose lines resolve through, in file order.</param>
/// <param name="Answers">The question's answers, in file order.</param>
public sealed record DaggerfallBiographyQuestion(int Number, IReadOnlyList<string> TextKeys, IReadOnlyList<DaggerfallBiographyAnswer> Answers)
{
    public void Validate()
    {
        if (Number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Number), Number, "A published biography question carries no positive number.");
        }

        if (TextKeys.Count == 0 || TextKeys.Count > BiogQuestionnaireReader.QuestionLines)
        {
            throw new ArgumentOutOfRangeException(nameof(TextKeys), TextKeys.Count, $"Biography question {Number} carries {TextKeys.Count} prose keys.");
        }

        foreach (string key in TextKeys)
        {
            NormalizedImportDocument.RequireLogicalId(key, nameof(TextKeys));
        }

        if (Answers.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Answers), Answers.Count, $"Biography question {Number} carries no answers.");
        }

        NormalizedImportDocument.ValidateUnique(Answers, answer => answer.Letter.ToString(), $"answers of biography question {Number}");
        foreach (DaggerfallBiographyAnswer answer in Answers)
        {
            answer.Validate();
        }
    }
}

/// <summary>The backdrop a questionnaire displays on.</summary>
/// <param name="MediaId">The image identity the donor names.</param>
/// <param name="Source">The logical path of the file that carries it.</param>
/// <param name="Published">Whether any media publication admits the image; none does yet.</param>
/// <param name="Reason">Why the image does not resolve, empty when it does.</param>
public sealed record DaggerfallBiographyImage(string MediaId, string Source, bool Published, string Reason)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        NormalizedImportDocument.RequireLogicalPath(Source, nameof(Source));
        if (Published == !string.IsNullOrWhiteSpace(Reason))
        {
            throw new InvalidOperationException($"Biography image '{MediaId}' states a reason exactly when it does not resolve.");
        }
    }
}

/// <summary>One normalized biography questionnaire: its identity, its questions, and its links.</summary>
/// <param name="ClassIndex">The class index the file name states.</param>
/// <param name="BiographyIndex">The biography index the file name states.</param>
/// <param name="BackstoryId">The backstory text id: explicit or the class default.</param>
/// <param name="BackstoryExplicit">Whether the file states its backstory id.</param>
/// <param name="BackstoryKey">The text key the backstory record resolves through.</param>
/// <param name="BackstoryDisposition">Whether that record exists.</param>
/// <param name="Questions">The twelve questions, in file order.</param>
/// <param name="Image">The backdrop the questionnaire displays on.</param>
/// <param name="Warnings">The reader's recoverable defects, in file order.</param>
public sealed record DaggerfallBiography(
    int ClassIndex,
    int BiographyIndex,
    int BackstoryId,
    bool BackstoryExplicit,
    string BackstoryKey,
    DaggerfallBiographyLinkDisposition BackstoryDisposition,
    IReadOnlyList<DaggerfallBiographyQuestion> Questions,
    DaggerfallBiographyImage Image,
    IReadOnlyList<string> Warnings)
{
    public void Validate()
    {
        if (ClassIndex < 0 || BiographyIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ClassIndex), ClassIndex, "A published biography carries negative indices.");
        }

        NormalizedImportDocument.RequireLogicalId(BackstoryKey, nameof(BackstoryKey));
        if (Questions.Count != BiogQuestionnaireReader.QuestionCount)
        {
            throw new InvalidOperationException($"Biography {ClassIndex}-{BiographyIndex} carries {Questions.Count} questions for a {BiogQuestionnaireReader.QuestionCount}-question questionnaire.");
        }

        foreach (DaggerfallBiographyQuestion question in Questions)
        {
            question.Validate();
        }

        ArgumentNullException.ThrowIfNull(Image);
        Image.Validate();
    }
}

/// <summary>The normalized biographies: every questionnaire the sources state, with text keys per line.</summary>
/// <param name="Sources">The sources the biographies were read from: the default text plus one per questionnaire file.</param>
/// <param name="Biographies">The questionnaires, in class order.</param>
/// <param name="DefaultLines">How many default-biography lines the source states, trailing empties included.</param>
public sealed record DaggerfallBiographies(
    IReadOnlyList<DaggerfallTextSource> Sources,
    IReadOnlyList<DaggerfallBiography> Biographies,
    int DefaultLines)
{
    public void Validate()
    {
        if (Sources.Count == 0)
        {
            throw new InvalidOperationException("Biographies cite no sources.");
        }

        foreach (DaggerfallTextSource source in Sources)
        {
            source.Validate();
            if (source.Kind != DaggerfallTextKind.Biography)
            {
                throw new InvalidOperationException($"Biographies cite source '{source.Path}' of family '{source.Kind}'.");
            }
        }

        NormalizedImportDocument.ValidateUnique(Biographies, biography => $"{biography.ClassIndex}-{biography.BiographyIndex}", "biographies");
        foreach (DaggerfallBiography biography in Biographies)
        {
            biography.Validate();
        }

        if (DefaultLines < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultLines), DefaultLines, "Biographies state a negative default line count.");
        }
    }
}

/// <summary>
/// Builds the normalized biographies from the default-biography file and the questionnaire files.
/// Question and answer prose join the shared text section through the returned records; the
/// biographies keep the questionnaire structure, the backstory and macro links with their
/// dispositions, and the backdrop reference character consumers resolve through.
/// </summary>
public static class DaggerfallBiographiesBuilder
{
    public const string FamilyId = "CNT-014";

    /// <summary>The backdrop every questionnaire displays on, in the donor's naming.</summary>
    public const string BackdropMediaId = "BIOG00I0";

    /// <summary>The logical path of the file that carries the backdrop.</summary>
    public const string BackdropSource = "local/arena2/BIOG00I0.IMG";

    /// <summary>Builds the biographies and the text records their prose resolves through.</summary>
    /// <param name="defaultBytes">The default-biography file's bytes.</param>
    /// <param name="defaultLabel">The default-biography file's documented label.</param>
    /// <param name="questionnaires">One text, label, class and biography index per questionnaire file.</param>
    /// <param name="textRecordIds">The classic text resource record ids, for macro link checks.</param>
    /// <param name="imageBytes">The backdrop file's bytes, or null when it is not supplied.</param>
    /// <param name="inventory">The documented inventory.</param>
    /// <param name="language">The language tag the sources' text is written in.</param>
    public static (DaggerfallBiographies Biographies, IReadOnlyList<DaggerfallTextRecord> Records) Build(
        ReadOnlySpan<byte> defaultBytes,
        string defaultLabel,
        IReadOnlyList<(string Text, string Label, int ClassIndex, int BiographyIndex)> questionnaires,
        IReadOnlySet<string> textRecordIds,
        byte[]? imageBytes,
        IReadOnlyList<SourceInventoryRow> inventory,
        string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLabel);
        ArgumentNullException.ThrowIfNull(questionnaires);
        ArgumentNullException.ThrowIfNull(textRecordIds);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        string defaultRecordId = RequireFile(inventory, defaultLabel);
        BioDatCatalog defaults = BioDatReader.Read(defaultBytes, defaultLabel);

        List<DaggerfallTextSource> sources = [
            new(DaggerfallTextKind.Biography, defaultRecordId, defaultLabel, language, defaultBytes.Length, 0, defaults.Lines.Count),
        ];
        List<DaggerfallTextRecord> records = [];
        for (int index = 0; index < defaults.Lines.Count; index++)
        {
            BioDatLine line = defaults.Lines[index];
            // A line past the final terminator states no bytes at all: it is published as
            // malformed with the reason rather than as readable, because a readable value spans
            // at least its terminator and inventing a byte would be the fabrication. The line
            // itself stays counted, so the split the donor keeps is the split published.
            bool readable = line.Reason.Length == 0 && (line.Text.Length > 0 || HasTerminator(defaultBytes, line));
            records.Add(readable
                ? FamilyTextRecords.Plain(DaggerfallTextKind.Biography, $"default-{index:D2}", defaultLabel, index, line.Offset, line.Text.Length + 1, line.Text)
                : FamilyTextRecords.Malformed(DaggerfallTextKind.Biography, $"default-{index:D2}", defaultLabel, index, line.Offset,
                    line.Reason.Length != 0 ? line.Reason : "the source states no bytes past its final terminator"));
        }

        List<DaggerfallBiography> biographies = [];
        foreach ((string text, string label, int classIndex, int biographyIndex) in questionnaires)
        {
            (DaggerfallBiography biography, IReadOnlyList<DaggerfallTextRecord> prose) = BuildQuestionnaire(
                text, label, classIndex, biographyIndex, textRecordIds, imageBytes, inventory, language);
            sources.Add(new DaggerfallTextSource(
                DaggerfallTextKind.Biography, RequireFile(inventory, label), label, language, text.Length, 0, prose.Count));
            records.AddRange(prose);
            biographies.Add(biography);
        }

        DaggerfallBiographies published = new(sources, biographies.OrderBy(biography => biography.ClassIndex).ThenBy(biography => biography.BiographyIndex).ToList(), defaults.Lines.Count);
        published.Validate();
        foreach (DaggerfallTextRecord record in records)
        {
            record.Validate();
        }

        return (published, records);
    }

    private static (DaggerfallBiography Biography, IReadOnlyList<DaggerfallTextRecord> Records) BuildQuestionnaire(
        string text,
        string label,
        int classIndex,
        int biographyIndex,
        IReadOnlySet<string> textRecordIds,
        byte[]? imageBytes,
        IReadOnlyList<SourceInventoryRow> inventory,
        string language)
    {
        _ = language;
        BiogQuestionnaire questionnaire = BiogQuestionnaireReader.Read(text, classIndex, biographyIndex, label);
        string backstoryKey = new DaggerfallTextKey(DaggerfallTextKind.Resource, questionnaire.BackstoryId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();
        bool backstoryResolved = textRecordIds.Contains(questionnaire.BackstoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        List<DaggerfallBiographyQuestion> questions = [];
        List<DaggerfallTextRecord> records = [];
        int ordinal = 0;
        foreach (BiogQuestion question in questionnaire.Questions)
        {
            List<string> keys = [];
            for (int line = 0; line < question.Text.Count; line++)
            {
                string id = $"{classIndex:D2}-{biographyIndex}-q{question.Number:D2}-l{line}";
                keys.Add(new DaggerfallTextKey(DaggerfallTextKind.Biography, id).ToString());
                records.Add(FamilyTextRecords.Plain(
                    DaggerfallTextKind.Biography, id, label, ordinal,
                    question.TextOffsets[line], question.Text[line].Length, question.Text[line]));
                ordinal++;
            }

            List<DaggerfallBiographyAnswer> answers = [];
            foreach (BiogAnswer answer in question.Answers)
            {
                string answerId = $"{classIndex:D2}-{biographyIndex}-q{question.Number:D2}-{answer.Letter}";
                records.Add(FamilyTextRecords.Plain(
                    DaggerfallTextKind.Biography, answerId, label, ordinal,
                    answer.TextOffset, answer.Text.Length, answer.Text));
                ordinal++;
                List<DaggerfallBiographyEffect> effects = [];
                foreach (BiogEffect effect in answer.Effects)
                {
                    effects.Add(NormalizeEffect(effect, textRecordIds));
                }

                answers.Add(new DaggerfallBiographyAnswer(answer.Letter.ToString(), new DaggerfallTextKey(DaggerfallTextKind.Biography, answerId).ToString(), effects));
            }

            questions.Add(new DaggerfallBiographyQuestion(question.Number, keys, answers));
        }

        DaggerfallBiography biography = new(
            classIndex,
            biographyIndex,
            questionnaire.BackstoryId,
            questionnaire.BackstoryExplicit,
            backstoryKey,
            backstoryResolved ? DaggerfallBiographyLinkDisposition.Resolved : DaggerfallBiographyLinkDisposition.Unresolved,
            questions,
            Backdrop(imageBytes),
            questionnaire.Warnings);
        biography.Validate();
        return (biography, records);
    }

    private static DaggerfallBiographyEffect NormalizeEffect(BiogEffect effect, IReadOnlySet<string> textRecordIds)
    {
        if (effect.Kind != BiogEffectKind.TextMacro)
        {
            return new DaggerfallBiographyEffect(effect.Text, effect.Kind, effect.First, effect.Second, effect.Third, string.Empty, null, string.Empty);
        }

        string target = new DaggerfallTextKey(DaggerfallTextKind.Resource, effect.Second).ToString();
        bool resolved = textRecordIds.Contains(effect.Second);
        return new DaggerfallBiographyEffect(
            effect.Text,
            effect.Kind,
            effect.First,
            effect.Second,
            effect.Third,
            target,
            resolved ? DaggerfallBiographyLinkDisposition.Resolved : DaggerfallBiographyLinkDisposition.Unresolved,
            resolved ? string.Empty : $"The text resource carries no record {effect.Second}.");
    }

    private static DaggerfallBiographyImage Backdrop(byte[]? imageBytes)
    {
        if (imageBytes is null)
        {
            return new DaggerfallBiographyImage(BackdropMediaId, BackdropSource, false, "The backdrop file is not supplied.");
        }

        if (imageBytes.Length != Arena2FormatConstants.HeaderlessUiImgBytes)
        {
            return new DaggerfallBiographyImage(BackdropMediaId, BackdropSource, false, $"The backdrop spans {imageBytes.Length} bytes for a {Arena2FormatConstants.HeaderlessUiImgBytes}-byte canvas.");
        }

        return new DaggerfallBiographyImage(BackdropMediaId, BackdropSource, false, "No media publication admits biography backdrops.");
    }

    private static bool HasTerminator(ReadOnlySpan<byte> bytes, BioDatLine line)
    {
        int end = line.Offset + line.Text.Length;
        return (uint)end < (uint)bytes.Length && bytes[end] == 0;
    }

    private static string RequireFile(IReadOnlyList<SourceInventoryRow> inventory, string label)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.FirstOrDefault(row => row.FamilyId == FamilyId && StringComparer.Ordinal.Equals(row.PathOrPattern, label))?.Id
            ?? throw new InvalidOperationException($"The documented inventory does not carry '{label}', so the biographies cite no provenance.");
    }
}
