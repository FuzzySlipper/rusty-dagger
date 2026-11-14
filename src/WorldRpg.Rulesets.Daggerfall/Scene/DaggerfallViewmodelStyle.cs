namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How the first-person weapon is drawn.</summary>
internal enum DaggerfallViewmodelStyle
{
    /// <summary>The classic weapon sprites.</summary>
    Classic,
    /// <summary>The 3D arms and weapon of the first-person art, moving by its held motion.</summary>
    Held,
    /// <summary>The 3D art sampled at the classic frame time and held between samples, without blending.</summary>
    HeldRetro,
}
