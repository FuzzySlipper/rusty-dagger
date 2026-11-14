namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The last skill training, which guild and quest training share as one cooldown; the calendar remains the time authority.</summary>
internal sealed record DaggerfallQuestTrainingSave(long? LastSkillTrainingSecond)
{
    internal DaggerfallQuestTrainingSave Validate()
    {
        if (LastSkillTrainingSecond is < 0) throw new ArgumentOutOfRangeException(nameof(LastSkillTrainingSecond));
        return this;
    }
}

internal sealed class DaggerfallQuestTrainingState(DaggerfallQuestTrainingSave? saved = null)
{
    internal long? LastSkillTrainingSecond { get; private set; } = saved?.Validate().LastSkillTrainingSecond;
    internal void Record(long second)
    {
        if (second < 0) throw new ArgumentOutOfRangeException(nameof(second));
        LastSkillTrainingSecond = second;
    }
    internal DaggerfallQuestTrainingSave Capture() => new(LastSkillTrainingSecond);
}
