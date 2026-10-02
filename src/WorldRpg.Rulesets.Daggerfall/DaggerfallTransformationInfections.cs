using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallInfectionExposure(string Instance, string Source, long? CasterId,
    long TargetId, DaggerfallInfectionKind Kind, int InfectionRegion);
internal enum DaggerfallInfectionAdmission { Started, Incumbent, RacialOverride, TargetUnavailable }
internal enum DaggerfallInfectionCleanup { Cured, Cancelled, Consumed }
internal sealed record DaggerfallInfectionTerminal(string Instance, DaggerfallInfectionKind Kind,
    DaggerfallInfectionCleanup Outcome, long Day);
internal sealed record DaggerfallInfectionsSave(DaggerfallInfectionTerminal[] LastOutcomes)
{
    internal static DaggerfallInfectionsSave Empty { get; } = new([]);
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(LastOutcomes);
        if (LastOutcomes.Any(value => value is null || string.IsNullOrWhiteSpace(value.Instance)
            || !Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.Outcome) || value.Day < 0)
            || LastOutcomes.Select(value => value.Kind).Distinct().Count() != LastOutcomes.Length)
            throw new ArgumentException("Infection cleanup state is malformed.");
    }
}
internal sealed record DaggerfallInfectionConsumption(bool Applied, string? Unavailable)
{
    internal void Validate()
    {
        if (Applied ? Unavailable is not null : string.IsNullOrWhiteSpace(Unavailable))
            throw new ArgumentException("A permanent infection consumer must report applied or an explicit unavailable reason.");
    }
}
/// <summary>
/// The compiled racial owner admits all permanent consequences before returning Applied. An unavailable
/// admission leaves the infection intact and retryable. No permanent work runs inside an effect round.
/// </summary>
internal interface IDaggerfallTransformationConsumer
{
    bool HasRacialOverride { get; }
    DaggerfallInfectionConsumption Consume(DaggerfallInfectionTransition transition);
}

/// <summary>Staged Daggerfall policy over the canonical effect, calendar and cinematic owners.</summary>
internal sealed class DaggerfallTransformationInfections : IDisposable
{
    private readonly DaggerfallEffectLifecycle _effects;
    private readonly Func<long> _day;
    private readonly DaggerfallCinematicPresentation? _presentation;
    private readonly bool _videosEnabled;
    private readonly Action<string> _report;
    private readonly Dictionary<DaggerfallInfectionKind, DaggerfallInfectionTerminal> _last = [];
    private string? _playingInstance;
    private string? _consuming;
    private IDaggerfallTransformationConsumer? _consumer;

    internal DaggerfallTransformationInfections(DaggerfallEffectLifecycle effects, Func<long> day,
        DaggerfallCinematicPresentation? presentation, bool videosEnabled, Action<string> report,
        DaggerfallInfectionsSave? saved = null)
    {
        _effects = effects; _day = day; _presentation = presentation; _videosEnabled = videosEnabled; _report = report;
        (saved ?? DaggerfallInfectionsSave.Empty).Validate();
        foreach (var value in (saved ?? DaggerfallInfectionsSave.Empty).LastOutcomes)
        {
            if (value.Day > day()) throw new ArgumentException("Infection cleanup is later than the current calendar.");
            _last.Add(value.Kind, value);
        }
        effects.Completed += OnCompleted;
    }

    internal void BindConsumer(IDaggerfallTransformationConsumer consumer) =>
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));

    internal DaggerfallInfectionAdmission Inflict(DaggerfallInfectionExposure exposure)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        var state = new DaggerfallInfectionState(exposure.Kind, _day(), exposure.InfectionRegion, DaggerfallInfectionStage.Incubating, OriginActorId: exposure.CasterId).Validate();
        if (exposure.TargetId != DaggerfallActorIdentity.PlayerEntityId) return DaggerfallInfectionAdmission.TargetUnavailable;
        if (_consumer?.HasRacialOverride == true) return DaggerfallInfectionAdmission.RacialOverride;
        var result = _effects.Start(new(exposure.Instance, DaggerfallTransformationInfectionPolicy.Key(exposure.Kind),
            exposure.Source, null, exposure.TargetId, "infection", "Disease", null, 1, null,
            JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallInfectionState)));
        return result == DaggerfallEffectAdmissionOutcome.Rejected ? DaggerfallInfectionAdmission.Incumbent : DaggerfallInfectionAdmission.Started;
    }

    internal DaggerfallInfectionState Advance(DaggerfallActiveEffect effect)
    {
        var before = DaggerfallTransformationInfectionPolicy.Read(effect);
        var state = DaggerfallTransformationInfectionPolicy.Advance(before, _day());
        if (state != before) DaggerfallTransformationInfectionPolicy.Write(effect, state);
        return state;
    }

    internal IReadOnlyList<DaggerfallInfectionTransition> Ready => _effects.Active
        .Where(value => DaggerfallTransformationInfectionPolicy.IsInfection(value.Definition.Key))
        .Select(value => (Effect: value, State: DaggerfallTransformationInfectionPolicy.Read(value)))
        .Where(value => value.State.Stage == DaggerfallInfectionStage.ReadyForTransformation)
        .Select(value => Transition(value.Effect, value.State)).ToArray();

    /// <summary>Called inside the existing admitted update, after the one cinematic owner has polled.</summary>
    internal void Poll(bool openingActive)
    {
        if (openingActive) return;
        if (_playingInstance is { } playing)
        {
            var effect = _effects.Active.FirstOrDefault(value => value.Context.Instance.Value == playing);
            if (effect is null) { StopOwnedMedia(); return; }
            var result = _presentation?.TakeResult();
            if (result is null) return;
            Set(effect, DaggerfallTransformationInfectionPolicy.Advance(DaggerfallTransformationInfectionPolicy.Read(effect), _day(), result));
            _playingInstance = null;
            return;
        }
        foreach (var effect in _effects.Active.Where(value => DaggerfallTransformationInfectionPolicy.IsInfection(value.Definition.Key)).ToArray())
        {
            var state = Advance(effect);
            if (state.Stage == DaggerfallInfectionStage.ReadyForTransformation)
            {
                if (_consumer is null) { Set(effect, state with { Unavailable = $"Permanent {state.Kind} transformation consumer is unavailable." }); continue; }
                var outcome = _consumer.Consume(Transition(effect, state));
                outcome.Validate();
                if (!outcome.Applied) { Set(effect, state with { Unavailable = outcome.Unavailable }); continue; }
                _consuming = effect.Context.Instance.Value;
                try { _effects.Cancel(effect.Context.Instance); }
                finally { _consuming = null; }
                // The permanent racial owner now excludes every other infection.
                foreach (var other in _effects.Active.Where(value => DaggerfallTransformationInfectionPolicy.IsInfection(value.Definition.Key)).ToArray())
                    _effects.Cancel(other.Context.Instance);
                return;
            }
            string? media = DaggerfallTransformationInfectionPolicy.PendingMedia(state);
            if (media is null || state.Unavailable is not null) continue;
            if (!_videosEnabled)
            {
                Set(effect, DaggerfallTransformationInfectionPolicy.Advance(state, _day(), new(media, VideoRealizationFactKind.Skipped, null)));
                return;
            }
            if (_presentation?.ActiveSource is not null) return;
            try
            {
                if (_presentation is null) throw new InvalidOperationException("Admitted cinematic content is unavailable.");
                _presentation.Play(media);
                _playingInstance = effect.Context.Instance.Value;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException)
            { Set(effect, state with { Unavailable = error.Message }); }
            return;
        }
    }

    internal bool Retry(string instance)
    {
        var effect = _effects.Active.FirstOrDefault(value => value.Context.Instance.Value == instance
            && DaggerfallTransformationInfectionPolicy.IsInfection(value.Definition.Key));
        if (effect is null) return false;
        var state = DaggerfallTransformationInfectionPolicy.Read(effect);
        if (state.Unavailable is null) return false;
        Set(effect, state with { Unavailable = null });
        return true;
    }
    internal DaggerfallInfectionsSave Capture() => new([.. _last.Values.OrderBy(value => value.Kind)]);
    private static DaggerfallInfectionTransition Transition(DaggerfallActiveEffect effect, DaggerfallInfectionState state) =>
        new(effect.Context.Instance.Value, state.Kind, state.StartingDay, state.InfectionRegion);
    private void Set(DaggerfallActiveEffect effect, DaggerfallInfectionState state)
    {
        var before = DaggerfallTransformationInfectionPolicy.Read(effect);
        if (before == state) return;
        DaggerfallTransformationInfectionPolicy.Write(effect, state);
        if (state.Unavailable is not null) _report(state.Unavailable);
    }
    private void OnCompleted(DaggerfallEffectOutcome outcome)
    {
        if (!DaggerfallTransformationInfectionPolicy.IsInfection(outcome.EffectKey)
            || outcome.Kind is not (DaggerfallEffectOutcomeKind.Cured or DaggerfallEffectOutcomeKind.Cancelled)) return;
        var kind = Enum.GetValues<DaggerfallInfectionKind>().Single(value => DaggerfallTransformationInfectionPolicy.Key(value) == outcome.EffectKey);
        _last[kind] = new(outcome.Instance, kind, _consuming == outcome.Instance ? DaggerfallInfectionCleanup.Consumed
            : outcome.Kind == DaggerfallEffectOutcomeKind.Cured ? DaggerfallInfectionCleanup.Cured : DaggerfallInfectionCleanup.Cancelled, _day());
        if (_playingInstance == outcome.Instance) StopOwnedMedia();
    }
    private void StopOwnedMedia()
    {
        if (_playingInstance is null) return;
        _presentation?.Stop();
        _ = _presentation?.TakeResult();
        _playingInstance = null;
    }
    public void Dispose() { StopOwnedMedia(); _effects.Completed -= OnCompleted; }
}
