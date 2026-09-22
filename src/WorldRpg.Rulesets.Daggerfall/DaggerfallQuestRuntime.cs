using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Meaningful current state of donor quest training; the calendar remains the time authority.</summary>
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

/// <summary>Named session adapter from compiled quest operations to the current player, progression, and calendar owners.</summary>
internal sealed class DaggerfallQuestRuntime(
    ProgressionState progression,
    StatsComponent stats,
    DaggerfallDefinitions definitions,
    DaggerfallQuestTrainingState training,
    DaggerfallLocomotionTuning locomotion,
    IRandomService random,
    Func<DaggerfallCalendar> calendar,
    Action<long> advanceElapsed)
{
    private const int TrainingSeconds = 3 * DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour;
    private readonly ProgressionState _progression = progression ?? throw new ArgumentNullException(nameof(progression));
    private readonly StatsComponent _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly DaggerfallQuestTrainingState _training = training ?? throw new ArgumentNullException(nameof(training));
    private readonly DaggerfallLocomotionTuning _locomotion = (locomotion ?? throw new ArgumentNullException(nameof(locomotion))).Validate();
    private readonly IRandomService _random = random ?? throw new ArgumentNullException(nameof(random));
    private readonly Func<DaggerfallCalendar> _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    private readonly Action<long> _advanceElapsed = advanceElapsed ?? throw new ArgumentNullException(nameof(advanceElapsed));

    internal bool IsLevelCompleted(int minimum) => _progression.Level >= minimum;
    internal bool IsAttributeAtLeast(string attribute, int minimum) => Stat(attribute, _definitions.Vocabulary.Attributes).ValueInt >= minimum;
    internal bool IsSkillAtLeast(string skill, int minimum) => Stat(skill, _definitions.Vocabulary.Skills).ValueInt >= minimum;

    internal void Train(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        ArgumentNullException.ThrowIfNull(instance);
        string skill = Resolve(operation.Targets.Single(), _definitions.Vocabulary.Skills);
        int multiplier = DaggerfallFormulaPolicy.SkillAdvancementMultiplier(skill);
        int roll = checked((int)_random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.quest.train-pc", $"{instance.InstanceId}:{operation.SourceLine}:{skill}", 10, 20)).Value);
        instance.Succeeded = true;
        _training.Record(_calendar().ToAbsoluteSeconds());
        _advanceElapsed(TrainingSeconds);
        Track fatigue = _stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        _ = fatigue.Spend(Math.Min(fatigue.Current, checked(_locomotion.IdleFatiguePerGameMinute * 180)));
        _progression.TallySkillUse(skill, checked(roll * multiplier), DaggerfallSkillUseReactions.MaximumSkillUses);
    }

    private Stat Stat(string requested, IReadOnlyList<DaggerfallStatId> candidates) =>
        _stats.GetStat(StatId.Parse(Resolve(requested, candidates)));

    private static string Resolve(string requested, IReadOnlyList<DaggerfallStatId> candidates)
    {
        string normalized = Normalize(requested);
        DaggerfallStatId match = candidates.SingleOrDefault(candidate => Normalize(candidate.Value) == normalized);
        return string.IsNullOrEmpty(match.Value)
            ? throw new ArgumentException($"Quest runtime names unknown player stat '{requested}'.")
            : match.Value;
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
