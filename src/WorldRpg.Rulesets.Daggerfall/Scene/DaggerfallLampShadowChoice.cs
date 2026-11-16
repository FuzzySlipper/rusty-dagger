namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Which lamps may cast a shadow: none, the big lamps, the main lamps or every lamp.</summary>
internal enum DaggerfallLampShadowLamps { Off, Big, Main, All }

/// <summary>
/// The player's lamp shadow preference, a video setting: which lamps may cast, the side of the sectors near the player in
/// which they ask, as a share of the tuned side (0 for none, so every lamp of the site may ask), and whether lamps
/// outdoors cast too. The tuning holds what each choice means (the reaches and the side).
/// </summary>
internal readonly record struct DaggerfallLampShadowChoice(DaggerfallLampShadowLamps Lamps, float SectorShare, bool Outdoors)
{
    /// <summary>The main lamps, in half-block sectors, indoors: what Patch chose to start from (#10867).</summary>
    internal static DaggerfallLampShadowChoice Default { get; } = new(DaggerfallLampShadowLamps.Main, 1f, false);

    /// <summary>The sector sides a player may choose, as shares of the tuned side; 0 is none.</summary>
    internal static IReadOnlyList<float> SectorShares { get; } = [0f, .5f, 1f, 2f, 4f];

    internal DaggerfallLampShadowChoice Validate() => Enum.IsDefined(Lamps) && SectorShares.Contains(SectorShare) ? this
        : throw new ArgumentOutOfRangeException(nameof(SectorShare), this,
            $"Lamp shadows are {string.Join(", ", Enum.GetNames<DaggerfallLampShadowLamps>())}, in sectors of {string.Join(", ", SectorShares)} times the tuned side.");
}
