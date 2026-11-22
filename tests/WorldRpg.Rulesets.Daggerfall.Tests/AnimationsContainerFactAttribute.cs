using WorldRpg.Tests.Support;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A fact about the animations container the game ships beside it, which no checkout has until it pulls or builds it.
/// Without it the fact skips at discovery and names how to get it.
/// </summary>
internal sealed class AnimationsContainerFactAttribute : FactAttribute
{
    public AnimationsContainerFactAttribute()
    {
        if (!TestDataContainers.Exists(TestDataContainers.Animations))
            Skip = $"No {TestDataContainers.Animations} in local/data; scripts/data-sync.sh pull (or link) brings it, or authoring/tools/people/splat-people.py builds it.";
    }
}
