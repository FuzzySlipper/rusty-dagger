using WorldRpg.Kit;
namespace WorldRpg.Host;

internal static class HostDefaults
{
    internal static readonly GameBundleId DefaultBundle = new("daggerfall.classic");

    /// <summary>The environment variable naming the bundle a launch starts under, in place of the remembered choice.</summary>
    internal const string LaunchBundleVariable = "WORLDRPG_BUNDLE";

    /// <summary>The bundle this launch names (<see cref="LaunchBundleVariable"/>), or null when it names none.</summary>
    internal static GameBundleId? LaunchBundle =>
        Environment.GetEnvironmentVariable(LaunchBundleVariable) is { Length: > 0 } id ? new GameBundleId(id.Trim()) : null;
}
