using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Stable current-state data for one Daggerfall active effect; compiled policy rebuilds its behavior.</summary>
internal sealed record DaggerfallActiveEffectSave(
    string Instance,
    string EffectKey,
    string Source,
    long? CasterId,
    long TargetId,
    string Settings,
    string? Element,
    ulong? ItemId,
    uint? RemainingRounds,
    ushort Stacks,
    JsonElement State)
{
    public string? BundleId { get; init; }
    public string? BundleName { get; init; }
    public DaggerfallEffectBundleKind BundleKind { get; init; }
    public long BundleSequence { get; init; }
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(EffectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(Settings);
        if (BundleSequence < 0) throw new ArgumentOutOfRangeException(nameof(BundleSequence));
        if (!Enum.IsDefined(BundleKind)) throw new ArgumentException("Saved effect bundle kind is not recognized.");
        if (TargetId <= 0 || CasterId is <= 0 || ItemId == 0 || Stacks == 0)
            throw new ArgumentOutOfRangeException(nameof(TargetId), "Saved effect identities and stacks must be positive.");
        if (State.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Saved effect state must be explicit.", nameof(State));
    }

    internal DaggerfallEffectRequest ToRequest() => new(
        Instance, EffectKey, Source, CasterId, TargetId, Settings, Element, ItemId, Stacks, RemainingRounds, State.Clone()) { BundleId = BundleId, BundleName = BundleName, BundleKind = BundleKind, BundleSequence = BundleSequence };
}
