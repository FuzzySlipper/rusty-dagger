using System.Text.Json;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallInfectionKind { Vampire, Werewolf, Wereboar }
internal enum DaggerfallInfectionStage { Incubating, WarningPending, Warned, DeathPending, ReadyForTransformation }

/// <summary>Meaningful infection state; the common effect owner retains its lifetime and provenance.</summary>
internal sealed record DaggerfallInfectionState(DaggerfallInfectionKind Kind, long StartingDay,
    int InfectionRegion, DaggerfallInfectionStage Stage, string? Unavailable = null, long? OriginActorId = null)
{
    internal DaggerfallInfectionState Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Stage) || StartingDay < 0 || InfectionRegion is < 0 or > 61
            || Kind != DaggerfallInfectionKind.Vampire && Stage == DaggerfallInfectionStage.DeathPending
            || OriginActorId is <= 0
            || Unavailable is not null && string.IsNullOrWhiteSpace(Unavailable))
            throw new ArgumentException("Transformation infection state is malformed.");
        return this;
    }
}

/// <summary>
/// Typed stage output consumed by the permanent curse owner. Readiness does not install a racial
/// override, grant spells, relocate the player or report a completed permanent transformation.
/// </summary>
internal sealed record DaggerfallInfectionTransition(string Instance, DaggerfallInfectionKind Kind,
    long StartingDay, int InfectionRegion);

internal static class DaggerfallTransformationInfectionPolicy
{
    internal static string Key(DaggerfallInfectionKind kind) => kind switch
    {
        DaggerfallInfectionKind.Vampire => "infection-vampire",
        DaggerfallInfectionKind.Werewolf => "infection-werewolf",
        DaggerfallInfectionKind.Wereboar => "infection-wereboar",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static bool IsInfection(string key) => Enum.GetValues<DaggerfallInfectionKind>().Any(kind => Key(kind) == key);

    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Action<DaggerfallActiveEffect> progress)
    {
        foreach (DaggerfallInfectionKind kind in Enum.GetValues<DaggerfallInfectionKind>())
        {
            DaggerfallInfectionKind selected = kind;
            yield return new(Key(kind), kind == DaggerfallInfectionKind.Vampire ? "infection-vampire" : "infection-lycanthropy",
                DaggerfallEffectStacking.Reject, 1, 1,
                Apply: effect => Validate(effect, selected), Resume: effect => Validate(effect, selected),
                MagicRound: progress, ShowSpellIcon: false);
        }
    }

    internal static DaggerfallInfectionState Read(DaggerfallActiveEffect effect) =>
        (effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallInfectionState)
            ?? throw new ArgumentException("Transformation infection has no state.")).Validate();

    internal static void Write(DaggerfallActiveEffect effect, DaggerfallInfectionState state) =>
        effect.State = JsonSerializer.SerializeToElement(state.Validate(), DaggerfallSaveJsonContext.Default.DaggerfallInfectionState);

    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, DaggerfallInfectionKind kind)
    {
        DaggerfallInfectionState state = Read(effect);
        if (state.Kind != kind || effect.Context.Target.Value != (ulong)DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentException("Transformation infection must match its player-only disease definition.");
        return [];
    }

    internal static void ValidateSaved(DaggerfallActiveEffectSave effect, long day)
    {
        if (!IsInfection(effect.EffectKey)) return;
        var state = (effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallInfectionState)
            ?? throw new ArgumentException("Infection state is missing.")).Validate();
        long elapsed = day - state.StartingDay;
        if (effect.EffectKey != Key(state.Kind) || effect.TargetId != DaggerfallActorIdentity.PlayerEntityId
            || effect.RemainingRounds is not null || elapsed < 0
            || state.Stage != DaggerfallInfectionStage.Incubating && elapsed <= 0
            || state.Stage is DaggerfallInfectionStage.DeathPending or DaggerfallInfectionStage.ReadyForTransformation && elapsed <= 3)
            throw new ArgumentException("Saved infection kind, target, lifetime or elapsed milestone is malformed.");
    }

    internal static string? PendingMedia(DaggerfallInfectionState state) => state.Stage switch
    {
        DaggerfallInfectionStage.WarningPending => state.Kind == DaggerfallInfectionKind.Vampire ? "ANIM0004.VID" : "ANIM0002.VID",
        DaggerfallInfectionStage.DeathPending => "ANIM0012.VID",
        _ => null,
    };

    /// <summary>The donor's strict day boundaries; a long elapsed jump still observes warning completion first.</summary>
    internal static DaggerfallInfectionState Advance(DaggerfallInfectionState state, long day,
        DaggerfallCinematicResult? result = null)
    {
        state.Validate();
        if (day < state.StartingDay) throw new ArgumentException("Infection start is later than the current calendar.");
        if (state.Unavailable is not null) return state;
        if (result is not null)
        {
            if (PendingMedia(state) != result.Source)
                throw new ArgumentException("Cinematic completion does not match the pending infection stage.");
            if (result.Kind == VideoRealizationFactKind.Failed)
                return state with { Unavailable = result.Failure ?? "Infection cinematic failed." };
            if (result.Kind is not (VideoRealizationFactKind.Completed or VideoRealizationFactKind.Skipped))
                throw new ArgumentException("Infection cinematic result is not terminal.");
            return state with { Stage = state.Stage == DaggerfallInfectionStage.WarningPending
                ? DaggerfallInfectionStage.Warned : DaggerfallInfectionStage.ReadyForTransformation };
        }
        long elapsed = day - state.StartingDay;
        return state.Stage switch
        {
            DaggerfallInfectionStage.Incubating when elapsed > 0 => state with { Stage = DaggerfallInfectionStage.WarningPending },
            DaggerfallInfectionStage.Warned when elapsed > 3 => state with { Stage = state.Kind == DaggerfallInfectionKind.Vampire
                ? DaggerfallInfectionStage.DeathPending : DaggerfallInfectionStage.ReadyForTransformation },
            _ => state,
        };
    }
}
