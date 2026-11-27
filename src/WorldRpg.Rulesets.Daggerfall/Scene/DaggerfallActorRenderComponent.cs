namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How an actor is drawn in the world, decoupled from gameplay kind.</summary>
internal enum ActorVisualKind
{
    Billboard,
    DirectionalMobile,
    SplatCharacter,
    PartsRig,
    PortraitOnly,
    None,
}

/// <summary>Decoupled visual specification for a live actor in <c>EntityStore</c>.</summary>
internal sealed record ActorVisualSpec(
    ActorVisualKind Kind,
    string AssetKey,
    int? Archive = null,
    int? Record = null,
    int? MobileId = null,
    string? Gender = null,
    bool OnBase = true,
    string? CrowdCharacterHint = null)
{
    internal static ActorVisualSpec NoneSpec { get; } = new(ActorVisualKind.None, string.Empty, OnBase: true);

    internal static ActorVisualSpec Billboard(int archive, int record, bool onBase = true, string? assetKey = null, string? crowdHint = null) =>
        new(ActorVisualKind.Billboard, assetKey ?? $"sprite/texture-{archive}-{record}", Archive: archive, Record: record, OnBase: onBase, CrowdCharacterHint: crowdHint);

    internal static ActorVisualSpec SplatCivilian(int archive, int record, string? gender = null, string? crowdHint = null) =>
        new(ActorVisualKind.SplatCharacter, $"sprite/texture-{archive}-{record}", Archive: archive, Record: record, Gender: gender, OnBase: true, CrowdCharacterHint: crowdHint);

    internal static ActorVisualSpec DirectionalMobile(int mobileId, string? gender = null, bool onBase = true) =>
        new(ActorVisualKind.DirectionalMobile, $"actor/mobile-{mobileId}", MobileId: mobileId, Gender: gender, OnBase: onBase);

    internal static ActorVisualSpec PortraitOnly(string assetKey) =>
        new(ActorVisualKind.PortraitOnly, assetKey, OnBase: false);
}

/// <summary>
/// Tier-2 rendering component attached to every live actor in <c>EntityStore</c> (<c>Scene/</c>).
/// <see cref="DaggerfallSiteAppearance"/> reads this directly instead of branching on gameplay kind
/// or holding a parallel <c>npcVisuals</c> dictionary; mutating <see cref="Spec"/> supports in-place
/// visual swaps (such as guard promotion) on the same entity and durable ID.
/// </summary>
internal sealed class ActorRenderComponent(ActorVisualSpec spec)
{
    internal ActorVisualSpec Spec { get; private set; } = spec ?? throw new ArgumentNullException(nameof(spec));

    internal void SetVisual(ActorVisualSpec spec) => Spec = spec ?? throw new ArgumentNullException(nameof(spec));
}
