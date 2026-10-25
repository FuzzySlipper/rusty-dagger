namespace WorldRpg.Host;

/// <summary>How this build of the product runs.</summary>
internal static class HostBuild
{
    /// <summary>
    /// Whether the product was staged by the development loop (<c>rusty dev</c>), which builds it beside the
    /// importer from the same tree; a release and an ordinary staging are not.
    /// </summary>
#if WORLDRPG_DEVELOPMENT_LOOP
    internal static bool DevelopmentLoop { get; } = true;
#else
    internal static bool DevelopmentLoop { get; } = false;
#endif
}
