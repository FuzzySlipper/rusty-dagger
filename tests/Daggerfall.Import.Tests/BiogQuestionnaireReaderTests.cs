using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>Biography questionnaires: twelve numbered questions, lettered answers, classified effects.</summary>
public sealed class BiogQuestionnaireReaderTests
{
    [Fact]
    public void Reads_all_supplied_questionnaires_end_to_end()
    {
        for (int cls = 0; cls <= 17; cls++)
        {
            string name = $"BIOG{cls:D2}T0.TXT";
            BiogQuestionnaire questionnaire = BiogQuestionnaireReader.Read(
                File.ReadAllText(Corpus(name)), cls, 0, $"local/arena2/{name}");

            Assert.Equal(cls, questionnaire.ClassIndex);
            Assert.Equal(0, questionnaire.BiographyIndex);
            // No supplied file states its own id, so every questionnaire defaults by class.
            Assert.False(questionnaire.BackstoryExplicit);
            Assert.Equal(4116 + cls, questionnaire.BackstoryId);
            Assert.Equal(12, questionnaire.Questions.Count);
            Assert.Equal(Enumerable.Range(1, 12), questionnaire.Questions.Select(question => question.Number));
            Assert.All(questionnaire.Questions, question =>
            {
                Assert.NotEmpty(question.Text);
                Assert.True(question.Text.Count <= 2);
                Assert.NotEmpty(question.Answers);
            });
        }
    }

    [Fact]
    public void Classifies_every_supplied_effect_and_names_the_warnings()
    {
        List<string> warnings = [];
        int invalid = 0;
        int unimplemented = 0;
        int macros = 0;
        for (int cls = 0; cls <= 17; cls++)
        {
            BiogQuestionnaire questionnaire = BiogQuestionnaireReader.Read(
                File.ReadAllText(Corpus($"BIOG{cls:D2}T0.TXT")), cls, 0, "corpus");
            warnings.AddRange(questionnaire.Warnings);
            foreach (BiogEffect effect in questionnaire.Questions.SelectMany(question => question.Answers).SelectMany(answer => answer.Effects))
            {
                if (effect.Kind == BiogEffectKind.Invalid) invalid++;
                if (effect.Kind == BiogEffectKind.Unimplemented) unimplemented++;
                if (effect.Kind == BiogEffectKind.TextMacro) macros++;
                Assert.Equal(effect.Text, effect.Text.Trim());
            }
        }

        // Twelve files each carry one lone `&` line no branch accepts, alongside the
        // unimplemented armor/special commands the donor logs past, and hundreds of
        // backstory text references.
        Assert.Equal(12, invalid);
        Assert.True(unimplemented > 0, "expected unimplemented AE/AF commands");
        Assert.True(macros > 200, $"expected hundreds of text macros, found {macros}");
        Assert.Contains(warnings, warning => warning.StartsWith("invalid command '&'", StringComparison.Ordinal));
        Assert.All(warnings, warning => Assert.True(
            warning.StartsWith("invalid command", StringComparison.Ordinal) || warning.StartsWith("unimplemented command", StringComparison.Ordinal),
            warning));
    }

    [Fact]
    public void Refuses_truncation_and_parses_an_explicit_backstory_id()
    {
        string corpus = File.ReadAllText(Corpus("BIOG00T0.TXT"));
        string cut = string.Join("\n", corpus.Split('\n').Take(10));
        Assert.Throws<Arena2FormatException>(() => BiogQuestionnaireReader.Read(cut, 0, 0, "fixture/BIOG00T0.TXT"));
        Assert.Throws<Arena2FormatException>(() => BiogQuestionnaireReader.Read(string.Empty, 0, 0, "fixture/BIOG00T0.TXT"));

        BiogQuestionnaire explicitId = BiogQuestionnaireReader.Read("#4200\n" + corpus, 0, 0, "fixture/BIOG00T0.TXT");
        Assert.True(explicitId.BackstoryExplicit);
        Assert.Equal(4200, explicitId.BackstoryId);
        Assert.Equal(12, explicitId.Questions.Count);

        BiogQuestionnaire badId = BiogQuestionnaireReader.Read("#oops\n" + corpus, 0, 0, "fixture/BIOG00T0.TXT");
        Assert.False(badId.BackstoryExplicit);
        Assert.Equal(4116, badId.BackstoryId);
        Assert.Contains(badId.Warnings, warning => warning.Contains("invalid backstory id", StringComparison.Ordinal));
    }

    [Fact]
    public void A_two_digit_number_without_a_separator_still_starts_its_question()
    {
        // The donor would consume a bare "10." as an effect because its dot sits at index two;
        // every supplied file separates its questions with blanks, so this follows the evident
        // intent on an input the corpus never presents.
        string text = string.Join("\n", [
            "9.\tNinth?",
            "a.\tYes",
            "10.\tTenth?",
            "a.\tYes",
            "11.\tEleventh?",
            "a.\tYes",
            "12.\tTwelfth?",
            "a.\tYes",
        ]);
        BiogQuestionnaire questionnaire = BiogQuestionnaireReader.Read(
            string.Join("\n", Enumerable.Range(1, 8).Select(i => $"{i}.\tQ{i}?\na.\tYes")) + "\n" + text, 0, 0, "fixture/BIOG00T0.TXT");
        Assert.Equal(12, questionnaire.Questions.Count);
        Assert.Equal(10, questionnaire.Questions[9].Number);
        Assert.Empty(questionnaire.Questions[8].Answers[0].Effects);
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
