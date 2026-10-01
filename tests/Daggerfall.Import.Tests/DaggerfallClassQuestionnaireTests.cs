using System.Text.RegularExpressions;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallClassQuestionnaireTests
{
    [CorpusAndDonorFact(["CLASSES.DAT", "TEXT.RSC"], ["Assets/Scripts/Game/UserInterfaceWindows/CreateCharClassQuestions.cs"])]
    public void Published_questions_and_every_recommendation_match_the_classic_source_and_donor()
    {
        byte[] classes = File.ReadAllBytes(TestData.Corpus("CLASSES.DAT"));
        var text = DaggerfallTextBuilder.Build(File.ReadAllBytes(TestData.Corpus("TEXT.RSC")), "arena2/TEXT.RSC", SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "data/content-source-manifest.csv"))), "en");
        var questions = DaggerfallClassQuestionnaireBuilder.Build(classes, text);
        string donor = File.ReadAllText(TestData.Donor("Assets/Scripts/Game/UserInterfaceWindows/CreateCharClassQuestions.cs"));
        string table = Regex.Match(donor, @"answerTable\s*=\s*\{([^}]+)\}", RegexOptions.Singleline).Groups[1].Value;
        int[] expected = Regex.Matches(table, @"\b\d+\b").Select(match => int.Parse(match.Value)).ToArray();
        Assert.Equal(120, expected.Length);
        Assert.Equal(expected, questions.Questions.SelectMany(question => question.Answers).Select(answer => answer.Archetype));
        Assert.Equal(Enumerable.Range(1, 40), questions.Questions.Select(question => question.Number));
        Assert.Equal(66, questions.Recommendations.Select(row => (row.Warrior, row.Rogue, row.Mage)).Distinct().Count());
        for (int index = 0; index < 66; index++)
        {
            var row = questions.Recommendations[index];
            int header = index / 4;
            int career = header <= 3 ? classes[header] : classes[header] & 0x0f;
            Assert.Equal((classes[18 + index * 3], classes[19 + index * 3], classes[20 + index * 3], $"class{career:D2}"),
                ((byte)row.Warrior, (byte)row.Rogue, (byte)row.Mage, row.CareerId));
        }
        // These source lines embed an answer marker at the end of a prompt or previous answer.
        Assert.EndsWith("Are you most inclined to:", questions.Questions[21].Text, StringComparison.Ordinal);
        Assert.StartsWith("Ask your friend", questions.Questions[21].Answers[0].Text, StringComparison.Ordinal);
        Assert.EndsWith("brother's death. The village lord should let him go free.", questions.Questions[25].Answers[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Even as you sympathize", questions.Questions[25].Answers[1].Text, StringComparison.Ordinal);
        Assert.Equal(new[] { "arena2/CLASSES.DAT", "arena2/TEXT.RSC" }, questions.Sources);
        Assert.Throws<InvalidOperationException>(() => DaggerfallClassQuestionnaireBuilder.Build(classes[..^1], text));
    }
}
