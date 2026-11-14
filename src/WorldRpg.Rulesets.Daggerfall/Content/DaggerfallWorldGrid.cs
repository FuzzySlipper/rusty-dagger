namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The world's horizontal grid: a map pixel, which is one terrain cell, eight RMB blocks or sixteen RDB blocks, and the
/// navigation cells across it. The location assembly places blocks by it and the exterior terrain is built on it.
/// </summary>
internal static class DaggerfallWorldGrid
{
    /// <summary>One map pixel's width in world units.</summary>
    internal const float HorizontalSize = 819.2F;

    /// <summary>
    /// Exterior artifacts publish navigation cells at the source terrain's 0.8-unit grid. One wilderness map pixel is
    /// 819.2 world units, so adjacent location closures are exactly 1024 navigation cells apart in the shared Engine
    /// artifact grid.
    /// </summary>
    internal const long NavigationCellsPerExteriorCell = 1024;
}
