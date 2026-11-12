using System.Text.Json.Nodes;
using Daggerfall.Source.Tool.Commands;
using Daggerfall.Source.Tool.Donor;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The drift check is the one reader of Daggerfall Unity: it names every value where our tables and a donor checkout
/// disagree, and never the commit a table was taken at.
/// </summary>
public sealed class DonorDriftTests
{
    [Fact]
    public void Names_each_differing_value_by_its_path_and_ignores_the_commit_a_table_names()
    {
        JsonNode ours = JsonNode.Parse("""{ "source": { "path": "a", "commit": "1" }, "rows": [1, 2, 3], "name": "x" }""")!;
        JsonNode donor = JsonNode.Parse("""{ "source": { "path": "a", "commit": "2" }, "rows": [1, 5], "extra": true, "name": "x" }""")!;

        IReadOnlyList<string> differences = DonorDriftCommand.Differences(ours, donor);

        Assert.Equal(3, differences.Count);
        Assert.Contains(differences, difference => difference.StartsWith("$.rows[1]: ours 2, the donor 5", StringComparison.Ordinal));
        Assert.Contains(differences, difference => difference.StartsWith("$.rows[2]: only ours has it", StringComparison.Ordinal));
        Assert.Contains(differences, difference => difference.StartsWith("$.extra: only the donor has it", StringComparison.Ordinal));
        Assert.Empty(DonorDriftCommand.Differences(ours, ours.DeepClone()));
    }

    [DonorFact("Assets/Scripts/Utility/EnemyBasics.cs", "Assets/StreamingAssets/Quests")]
    public void Our_tables_hold_what_the_named_daggerfall_unity_checkout_says() =>
        Assert.All(DonorTakes.All, take => Assert.True(
            DonorDriftCommand.Differences(take.Ours(TestData.RepositoryRoot), take.Donor(TestData.DonorRoot!)).Count == 0,
            $"{take.Label} differs from the donor; run daggerfall-source-tool donor-drift for the values."));
}
