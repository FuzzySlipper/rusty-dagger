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

    [Fact]
    public void LogicalSourceRequiresPositiveCallerByteLengthAndSerializesSourcePath()
    {
        ContentDigest digest = new("1111111111111111111111111111111111111111111111111111111111111111");
        LogicalSourceRecord invalid = new("arena2/MAPS.BSA", digest, 0);

        Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
        string json = JsonSerializer.Serialize(invalid with { ByteLength = 12 }, PublishedJson.Section);
        Assert.Contains("\"sourcePath\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sourceUri\"", json, StringComparison.Ordinal);
    }
}
