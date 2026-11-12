using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallCharacterState
{
    private int[]? _classQuestions;
    private readonly int[] _classWeights = new int[3];
    private int _classAnswers;
    private int _classQuestionSequence;

    internal string? CreationMode => Pending is null ? null : _classQuestions is null ? "character-pick" : "character-generation";

    internal DaggerfallClassQuizPresentation? ReadClassQuiz()
    {
        if (_classQuestions is null) return null;
        var questionnaire = _definitions.Catalogs.ClassQuestionnaire!;
        return new(_classAnswers, _classQuestions.Length, questionnaire.Questions[_classQuestions[_classAnswers]]);
    }

    internal void BeginClassQuestions(IRandomService random, DaggerfallCharacterCreationChoices? choices = null)
    {
        if (Pending is null) throw new ArgumentException("Open character choices before answering class questions.");
        if (_background is not null) throw new ArgumentException("Character creation has already been committed.");
        if (_classQuestions is not null) throw new ArgumentException("Finish or leave the current class questionnaire before starting another.");
        var questionnaire = _definitions.Catalogs.ClassQuestionnaire
            ?? throw new ArgumentException("This content pack supplies no class questionnaire.");
        var draft = choices ?? Pending;
        Validate(draft.ToIdentity());
        if (choices is not null)
        {
            // Capture edits from the existing pick form when advancing. The old background can be
            // retained only while its race and career inputs still describe this draft.
            var background = draft.RaceId == Pending.RaceId && draft.CareerId == Pending.CareerId && draft.CustomCareer == Pending.CustomCareer
                ? Pending.Background : null;
            draft = draft with { Background = background ?? DaggerfallCharacterBackgroundPolicy.Roll(_definitions,
                CurrentCareer(draft), draft.ToIdentity(), random, NextBackgroundRollSequence()) };
        }
        int sequence = checked(++_classQuestionSequence);
        int[] selected = new int[10];
        for (int question = 0; question < selected.Length; question++)
        {
            int index = checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed,
                "daggerfall.class-questions.v1", $"{sequence}.{question}", 0, questionnaire.Questions.Count - 1)).Value);
            while (selected.Take(question).Contains(index)) index = (index + 1) % questionnaire.Questions.Count;
            selected[question] = index;
        }
        _classQuestions = selected;
        Pending = draft;
        _classAnswers = 0;
        System.Array.Clear(_classWeights);
    }

    internal void AnswerClassQuestion(int number, int answer, IRandomService random)
    {
        var question = ReadClassQuiz()?.Question ?? throw new ArgumentException("There is no class question awaiting an answer.");
        if (question.Number != number || answer is < 0 or > 2)
            throw new ArgumentException("The answer does not name the current class question and one of its three choices.");
        int archetype = question.Answers[answer].Archetype;
        if (_classAnswers + 1 < _classQuestions!.Length)
        {
            _classWeights[archetype]++;
            _classAnswers++;
            return;
        }
        // Resolve the final recommendation and its background before consuming the answer. A
        // rejected content value leaves the current question available for retry or cancellation.
        int[] weights = (int[])_classWeights.Clone();
        weights[archetype]++;
        var recommendation = _definitions.Catalogs.ClassQuestionnaire!.Recommendations.First(row =>
            row.Warrior == weights[0] && row.Rogue == weights[1] && row.Mage == weights[2]);
        var draft = Pending! with { CareerId = recommendation.CareerId, CustomCareer = null, Background = null };
        Pending = draft with { Background = DaggerfallCharacterBackgroundPolicy.Roll(_definitions,
            _definitions.Catalogs.RequireCareer(recommendation.CareerId), draft.ToIdentity(), random, NextBackgroundRollSequence()) };
        // The existing pick/commit step confirms the recommendation; no actor stats change here.
        _classQuestions = null;
    }

    internal void BackToClassPick()
    {
        if (_classQuestions is null) throw new ArgumentException("There is no class questionnaire to leave.");
        _classQuestions = null;
    }
}

internal sealed record DaggerfallClassQuizPresentation(int Answered, int Total, DaggerfallClassQuestion Question);
