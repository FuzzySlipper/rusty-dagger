using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class NormalizedContractTests
{
    [Fact]
    public void NormalizedContractsHaveNoEncounterOrRuntimeEntitySurface()
    {
        Type[] types = typeof(NormalizedVector3).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(NormalizedVector3).Namespace).ToArray();

        Assert.DoesNotContain(types, type => type.Name.Contains("Encounter", StringComparison.OrdinalIgnoreCase)
            || type.GetProperties().Any(property => property.Name.Contains("EntityId", StringComparison.OrdinalIgnoreCase)));
    }
}
