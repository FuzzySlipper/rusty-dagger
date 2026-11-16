using WorldRpg.Tests.Support;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A fact that compares the default import with the developer's classic-art container, which only an import with
/// <c>--classic-art</c> packs. Without it the fact skips at discovery and names the import that packs it.
/// </summary>
internal sealed class ClassicArtContainerFactAttribute : FactAttribute
{
    public ClassicArtContainerFactAttribute()
    {
        if (!TestDataContainers.Exists(TestDataContainers.ClassicArt))
            Skip = $"No {TestDataContainers.ClassicArt} in local/data; import with --classic-art (the data lane does) to compare the default import with it.";
    }
}
