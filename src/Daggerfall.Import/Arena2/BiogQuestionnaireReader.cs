namespace Daggerfall.Import.Arena2;

/// <summary>How a questionnaire answer effect is classified, following the donor's application order.</summary>
public enum BiogEffectKind
{
    /// <summary>A skill adjustment: a skill identity plus a bonus.</summary>
    Skill,
    /// <summary>A gold adjustment: a signed amount.</summary>
    Gold,
    /// <summary>An item grant: a group, an index and a material.</summary>
    Item,
    /// <summary>A faction reputation change: a faction identity plus an amount.</summary>
    FactionReputation,
    /// <summary>A social-group reputation change: a group identity plus an amount.</summary>
    SocialReputation,
    /// <summary>A biography modifier: poison, fatigue, reaction, disease, magic or to-hit plus an amount.</summary>
    BiographyModifier,
    /// <summary>A backstory text reference into the classic text resource.</summary>
    TextMacro,
    /// <summary>A command the donor names but does not implement.</summary>
    Unimplemented,
    /// <summary>A line no donor branch accepts.</summary>
    Invalid,
}

/// <summary>One parsed answer effect: its verbatim line, its kind, and the operands that kind carries.</summary>
/// <param name="Text">The effect line exactly as the file states it.</param>
/// <param name="Kind">How the donor classifies the line.</param>
/// <param name="First">The first operand: skill, sign-aware gold, group, faction or group identity, modifier name, or macro prefix.</param>
/// <param name="Second">The second operand: bonus, group index, amount, macro target, or empty.</param>
/// <param name="Third">The third operand: item material, or empty.</param>
public sealed record BiogEffect(string Text, BiogEffectKind Kind, string First, string Second, string Third);

/// <summary>One questionnaire answer: its letter, its prose, and its parsed effects.</summary>
/// <param name="Letter">The answer's letter, which is how the file addresses it.</param>
/// <param name="Text">The answer's prose.</param>
/// <param name="TextOffset">The byte the answer's line starts at.</param>
/// <param name="Effects">The answer's effect lines, in file order.</param>
public sealed record BiogAnswer(char Letter, string Text, int TextOffset, IReadOnlyList<BiogEffect> Effects);

/// <summary>One questionnaire question: its stated number, its prose lines, and its answers.</summary>
/// <param name="Number">The question number the file states.</param>
/// <param name="Text">The question's prose lines, one or two, in file order.</param>
/// <param name="TextOffsets">The byte each prose line starts at, in the same order.</param>
/// <param name="Answers">The question's answers, in file order.</param>
public sealed record BiogQuestion(int Number, IReadOnlyList<string> Text, IReadOnlyList<int> TextOffsets, IReadOnlyList<BiogAnswer> Answers);

/// <summary>One parsed biography questionnaire.</summary>
/// <param name="ClassIndex">The class index the file name states.</param>
/// <param name="BiographyIndex">The biography index the file name states.</param>
/// <param name="BackstoryId">The backstory text id: explicit when the file states one, otherwise the donor's class default.</param>
/// <param name="BackstoryExplicit">Whether the file states its backstory id or the default applies.</param>
/// <param name="Questions">The twelve questions, in file order.</param>
/// <param name="Warnings">Recoverable defects the donor logs past: a bad backstory id, an unimplemented command, or a line no branch accepts.</param>
public sealed record BiogQuestionnaire(
    int ClassIndex,
    int BiographyIndex,
    int BackstoryId,
    bool BackstoryExplicit,
    IReadOnlyList<BiogQuestion> Questions,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a classic biography questionnaire exactly as the donor does: an optional backstory id,
/// twelve numbered questions of one or two prose lines, lettered answers, and effect lines the
/// donor classifies in application order. Blank lines are insignificant the way the donor's own
/// skips make them, so they are set aside up front; truncation is a malformed file because the
/// donor cannot read past it either, while a line no branch accepts is a recorded warning because
/// the donor logs past it and keeps the questionnaire.
/// </summary>
public static class BiogQuestionnaireReader
{
    /// <summary>How many questions a questionnaire carries.</summary>
    public const int QuestionCount = 12;

    /// <summary>How many prose lines one question carries at most.</summary>
    public const int QuestionLines = 2;

    /// <summary>The backstory text id a questionnaire defaults to, plus its class index.</summary>
    public const int DefaultBackstoriesStart = 4116;

    private static readonly string[] BiographyModifiers = ["RP", "FT", "RR", "RD", "MR", "TH"];

    /// <summary>Reads one questionnaire, or reports the first line that is not one.</summary>
    public static BiogQuestionnaire Read(string text, int classIndex, int biographyIndex, string label)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (classIndex < 0 || biographyIndex < 0)
        {
            throw new Arena2FormatException(label, 0, $"biography indices must be non-negative, not class {classIndex} biography {biographyIndex}");
        }

        List<string> lines = SplitLines(text, out List<int> offsets);
        int position = 0;
        List<string> warnings = [];
        int backstoryId = DefaultBackstoriesStart + classIndex;
        bool backstoryExplicit = false;
        if (position < lines.Count && lines[position].Length > 0 && lines[position][0] == '#')
        {
            if (int.TryParse(lines[position].Substring(1), out int stated))
            {
                backstoryId = stated;
                backstoryExplicit = true;
            }
            else
            {
                warnings.Add($"invalid backstory id '{lines[position]}'; the class default {backstoryId} applies");
            }

            position++;
        }

        List<BiogQuestion> questions = [];
        for (int expected = 1; expected <= QuestionCount; expected++)
        {
            questions.Add(ReadQuestion(lines, offsets, ref position, label, warnings));
        }

        return new BiogQuestionnaire(classIndex, biographyIndex, backstoryId, backstoryExplicit, questions, warnings);
    }

    private static BiogQuestion ReadQuestion(List<string> lines, List<int> offsets, ref int position, string label, List<string> warnings)
    {
        string number = At(lines, ref position, label, "a question number");
        int numberOffset = offsets[position - 1];
        int dot = number.IndexOf('.');
        if (dot < 0 || !int.TryParse(number[..dot], out int stated))
        {
            throw new Arena2FormatException(label, position, $"question line '{number}' carries no question number");
        }

        List<string> prose = [number[(dot + 1)..].Trim()];
        List<int> proseOffsets = [numberOffset];
        for (int line = 1; line < QuestionLines && position < lines.Count; line++)
        {
            // A following line that starts an answer or the next question ends the prose, exactly
            // as the donor's dot-at-one-or-two check does.
            int followingDot = lines[position].IndexOf('.');
            if (followingDot is 1 or 2)
            {
                break;
            }

            prose.Add(lines[position].Trim());
            proseOffsets.Add(offsets[position]);
            position++;
        }

        List<BiogAnswer> answers = [];
        while (position < lines.Count
            && lines[position].Length >= 2
            && lines[position].IndexOf('.') == 1
            && char.IsLetter(lines[position][0]))
        {
            answers.Add(ReadAnswer(lines, offsets, ref position, warnings));
        }

        if (answers.Count == 0)
        {
            throw new Arena2FormatException(label, position, $"question {stated} carries no answers");
        }

        return new BiogQuestion(stated, prose, proseOffsets, answers);
    }

    private static BiogAnswer ReadAnswer(List<string> lines, List<int> offsets, ref int position, List<string> warnings)
    {
        string line = lines[position];
        int lineOffset = offsets[position];
        position++;
        List<BiogEffect> effects = [];
        // A following question number ends the effects too: the donor stops at single-digit numbers
        // because their dot sits at index one, but would consume a two-digit number as an effect.
        // Every supplied file separates its questions with blank lines, so no published record
        // differs; without the separator this reader follows the evident intent instead.
        while (position < lines.Count && lines[position].IndexOf('.') != 1 && !IsQuestionNumber(lines[position]))
        {
            // Effect lines are indented; the donor trims them on the way in, so classification
            // sees the trimmed line and the published text keeps no indentation.
            effects.Add(Classify(lines[position].Trim(), warnings));
            position++;
        }

        return new BiogAnswer(line[0], line.Split('.')[1].Trim(), lineOffset, effects);
    }

    private static bool IsQuestionNumber(string line)
    {
        int index = 0;
        while (index < line.Length && char.IsAsciiDigit(line[index]))
        {
            index++;
        }

        return index > 0 && index < line.Length && line[index] == '.';
    }

    private static string At(List<string> lines, ref int position, string label, string what)
    {
        if (position >= lines.Count)
        {
            throw new Arena2FormatException(label, position, $"biography ends before {what}");
        }

        return lines[position++];
    }

    /// <summary>Classifies one effect line in the donor's application order.</summary>
    internal static BiogEffect Classify(string line, List<string> warnings)
    {
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2 && int.TryParse(tokens[0], out _) && short.TryParse(tokens[1], out _))
        {
            return new BiogEffect(line, BiogEffectKind.Skill, tokens[0], tokens[1], string.Empty);
        }

        if (line.StartsWith("GP", StringComparison.Ordinal))
        {
            string amount = tokens.Length > 2 ? tokens[1] + tokens[2] : tokens.Length > 1 ? tokens[1] : string.Empty;
            if (int.TryParse(amount, out _))
            {
                return new BiogEffect(line, BiogEffectKind.Gold, amount, string.Empty, string.Empty);
            }

            return Invalid(line, warnings);
        }

        if (line.StartsWith("IT", StringComparison.Ordinal))
        {
            if (tokens.Length >= 4 && int.TryParse(tokens[1], out _) && int.TryParse(tokens[2], out _) && int.TryParse(tokens[3], out _))
            {
                return new BiogEffect(line, BiogEffectKind.Item, tokens[1], tokens[2], tokens[3]);
            }

            return Invalid(line, warnings);
        }

        if (line.StartsWith("r", StringComparison.Ordinal) && line.Length > 1)
        {
            if (line[1] == 'f')
            {
                if (tokens.Length >= 2 && tokens[0].Length > 1 && int.TryParse(tokens[0].Split('f')[1], out _) && int.TryParse(tokens[1], out _))
                {
                    return new BiogEffect(line, BiogEffectKind.FactionReputation, tokens[0].Split('f')[1], tokens[1], string.Empty);
                }
            }
            else if (tokens.Length >= 2 && int.TryParse(tokens[0].Split('r')[1], out _) && int.TryParse(tokens[1], out _))
            {
                return new BiogEffect(line, BiogEffectKind.SocialReputation, tokens[0].Split('r')[1], tokens[1], string.Empty);
            }

            return Invalid(line, warnings);
        }

        foreach (string mod in BiographyModifiers)
        {
            if (line.StartsWith(mod, StringComparison.Ordinal))
            {
                if (tokens.Length >= 2 && int.TryParse(tokens[1], out _))
                {
                    return new BiogEffect(line, BiogEffectKind.BiographyModifier, mod, tokens[1], string.Empty);
                }

                return Invalid(line, warnings);
            }
        }

        if (line.Length > 0 && line[0] is '#' or '!' or '?')
        {
            if (int.TryParse(line.Substring(1).Split(' ')[0], out _))
            {
                return new BiogEffect(line, BiogEffectKind.TextMacro, line[0].ToString(), line.Substring(1).Split(' ')[0], string.Empty);
            }

            return Invalid(line, warnings);
        }

        if (line.StartsWith("AE", StringComparison.Ordinal) || line.StartsWith("AF", StringComparison.Ordinal) || line.StartsWith("AO", StringComparison.Ordinal))
        {
            warnings.Add($"unimplemented command '{line}'");
            return new BiogEffect(line, BiogEffectKind.Unimplemented, string.Empty, string.Empty, string.Empty);
        }

        return Invalid(line, warnings);
    }

    private static BiogEffect Invalid(string line, List<string> warnings)
    {
        warnings.Add($"invalid command '{line}'");
        return new BiogEffect(line, BiogEffectKind.Invalid, string.Empty, string.Empty, string.Empty);
    }

    private static List<string> SplitLines(string text, out List<int> offsets)
    {
        // The donor's line reader splits carriage returns, line feeds and their pairs; blank
        // lines are insignificant because every donor read skips lines of length one or less.
        // Offsets track the bytes so text values cite where their lines begin.
        List<string> lines = [];
        offsets = [];
        int start = 0;
        int index = 0;
        while (index <= text.Length)
        {
            bool end = index == text.Length;
            bool feed = !end && text[index] == '\n';
            bool ret = !end && text[index] == '\r';
            if (end || feed || ret)
            {
                string line = text[start..index];
                int next = index + 1;
                if (ret && next < text.Length && text[next] == '\n')
                {
                    next++;
                }

                if (line.Length > 1)
                {
                    lines.Add(line);
                    offsets.Add(start);
                }

                start = next;
                index = next;
                continue;
            }

            index++;
        }

        return lines;
    }
}
