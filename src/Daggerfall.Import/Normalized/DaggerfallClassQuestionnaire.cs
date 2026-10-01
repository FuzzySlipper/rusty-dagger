using System.Text.RegularExpressions;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

public sealed record DaggerfallClassAnswer(string Text, int Archetype);
public sealed record DaggerfallClassQuestion(int Number, string Text, IReadOnlyList<DaggerfallClassAnswer> Answers);
public sealed record DaggerfallClassRecommendation(int Warrior, int Rogue, int Mage, string CareerId);

/// <summary>Normalized class questions and the original first-match recommendation table.</summary>
public sealed record DaggerfallClassQuestionnaire(
    IReadOnlyList<DaggerfallClassQuestion> Questions,
    IReadOnlyList<DaggerfallClassRecommendation> Recommendations,
    IReadOnlyList<string> Sources)
{
    public void Validate(IReadOnlySet<string> documentedPaths, IReadOnlyList<DaggerfallCareerRecord> careers)
    {
        if (!Questions.Select(question => question.Number).SequenceEqual(Enumerable.Range(1, 40))
            || Questions.Any(question => string.IsNullOrWhiteSpace(question.Text) || question.Answers.Count != 3
                || !question.Answers.Select(answer => answer.Archetype).Order().SequenceEqual(new[] { 0, 1, 2 })
                || question.Answers.Any(answer => string.IsNullOrWhiteSpace(answer.Text))))
            throw new InvalidOperationException("The class questionnaire must carry all forty questions with three classified answers.");
        if (Recommendations.Count != 66 || Recommendations.Select(row => (row.Warrior, row.Rogue, row.Mage)).Distinct().Count() != 66
            || Recommendations.Any(row => row.Warrior < 0 || row.Rogue < 0 || row.Mage < 0
                || row.Warrior + row.Rogue + row.Mage != 10 || !careers.Any(career => career.Id == row.CareerId)))
            throw new InvalidOperationException("The class recommendation table must resolve every ten-answer result to a published career.");
        foreach (string source in Sources) new DaggerfallCatalogSource(source).Validate(documentedPaths);
    }
}

public static partial class DaggerfallClassQuestionnaireBuilder
{
    // CreateCharClassQuestions' answer table: warrior=0, rogue=1, mage=2.
    private static readonly int[] AnswerArchetypes =
    [
        0,2,1, 0,2,1, 0,1,2, 2,0,1, 0,1,2, 1,0,2, 0,1,2, 2,0,1,
        0,2,1, 0,1,2, 0,1,2, 0,1,2, 0,2,1, 0,1,2, 1,0,2, 1,2,0,
        2,0,1, 1,0,2, 0,1,2, 2,0,1, 1,0,2, 0,1,2, 0,2,1, 2,1,0,
        1,0,2, 0,2,1, 2,1,0, 2,0,1, 2,1,0, 2,1,0, 2,0,1, 1,0,2,
        0,2,1, 2,1,0, 1,2,0, 2,0,1, 2,0,1, 1,2,0, 0,1,2, 1,2,0,
    ];

    public static DaggerfallClassQuestionnaire Build(byte[] classes, DaggerfallText text)
    {
        if (classes.Length != 216)
            throw new InvalidOperationException($"CLASSES.DAT must contain 216 bytes; observed {classes.Length}.");
        DaggerfallTextRecord resource = text.Records.Single(record => record.Key == new DaggerfallTextKey(DaggerfallTextKind.Resource, "9000"));
        List<DaggerfallClassQuestion> questions = [];
        List<string> prompt = [], answer = [];
        List<DaggerfallClassAnswer> answers = [];
        int number = 0;
        void FinishAnswer()
        {
            if (answer.Count == 0) return;
            if (number is < 1 or > 40 || answers.Count >= 3)
                throw new InvalidOperationException("TEXT.RSC 9000 has an unexpected class-question answer.");
            answers.Add(new(string.Join(' ', answer), AnswerArchetypes[(number - 1) * 3 + answers.Count]));
            answer.Clear();
        }
        void FinishQuestion()
        {
            if (number == 0) return;
            FinishAnswer();
            questions.Add(new(number, string.Join(' ', prompt), answers.ToArray()));
            prompt.Clear(); answers.Clear();
        }
        foreach (var token in resource.Tokens.Where(token => token.Code == Arena2TextCode.Text))
        {
            string line = token.Text!.Trim();
            var question = QuestionStart().Match(line);
            if (question.Success)
            {
                FinishQuestion();
                number = int.Parse(question.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                prompt.Add(question.Groups[2].Value.Trim());
                continue;
            }
            var choice = AnswerStart().Match(line);
            if (choice.Success)
            {
                string prefix = line[..choice.Index].Trim();
                if (prefix.Length > 0) (answer.Count > 0 ? answer : prompt).Add(prefix);
                FinishAnswer();
                if (choice.Groups[1].Value[0] - 'a' != answers.Count)
                    throw new InvalidOperationException("TEXT.RSC 9000 answers are not ordered a, b, c.");
                answer.Add(choice.Groups[2].Value.Trim());
            }
            else if (line.Length > 0 && line != "{")
            {
                (answer.Count > 0 ? answer : prompt).Add(line);
            }
        }
        FinishQuestion();
        List<DaggerfallClassRecommendation> recommendations = [];
        for (int row = 0; row < 66; row++)
        {
            int header = row / 4;
            int career = header > 3 ? classes[header] & 15 : classes[header];
            int offset = 18 + row * 3;
            recommendations.Add(new(classes[offset], classes[offset + 1], classes[offset + 2], $"class{career:D2}"));
        }
        return new(questions, recommendations, ["arena2/CLASSES.DAT", resource.Source]);
    }

    [GeneratedRegex(@"^\{?(\d+)\.\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex QuestionStart();
    [GeneratedRegex(@"\b([abc])\)\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AnswerStart();
}
