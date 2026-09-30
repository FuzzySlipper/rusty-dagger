using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A fact that reads the Host's staged product manifest, which only a Host build produces. Without it the
/// fact skips at discovery and names the build that stages it, rather than failing on a missing build
/// output or passing without reading what it checks. The gate stages the Host before running this suite,
/// so there the fact always runs.
/// </summary>
internal sealed class StagedProductFactAttribute : FactAttribute
{
    /// <summary>The staged manifest, relative to the repository root.</summary>
    internal const string ManifestPath = "src/WorldRpg.Host/obj/Rusty.Engine/Product/product.json";

    public StagedProductFactAttribute()
    {
        if (!File.Exists(Path.Combine(TestData.RepositoryRoot, ManifestPath)))
            Skip = $"The staged product manifest '{ManifestPath}' is absent; run `rusty build --project src/WorldRpg.Host/WorldRpg.Host.csproj` to stage it.";
    }
}
