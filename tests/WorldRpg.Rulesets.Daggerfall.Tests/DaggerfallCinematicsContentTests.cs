using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Cinematic identities through the pack: the opening bound, the Daedric table bound, the rest
/// unresolved, and the DAG2 record distinct.
/// </summary>
public sealed class DaggerfallCinematicsContentTests
{
    [Fact]
    public void Resolves_bindings_and_leaves_the_rest_unresolved()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(33, definitions.Cinematics.Cinematics.Count);
        DaggerfallCinematicDefinition opening = definitions.Cinematics.Resolve("ANIM0000.VID");
        Assert.Equal(DaggerfallCinematicKind.Vid, opening.Kind);
        Assert.Equal(DaggerfallCinematicBinding.Bound, opening.Binding);
        Assert.Equal("new-game opening", opening.Caller);
        Assert.Equal(DaggerfallCinematicBinding.Bound, definitions.Cinematics.Resolve("DAG2.VID").Binding);
        Assert.Equal(DaggerfallCinematicBinding.Unresolved, definitions.Cinematics.Resolve("ANIM0001.VID").Binding);

        DaggerfallCinematicDefinition azura = definitions.Cinematics.Resolve("AZURA.FLC");
        Assert.Equal(DaggerfallCinematicKind.Flc, azura.Kind);
        Assert.Equal(DaggerfallCinematicBinding.Bound, azura.Binding);
        Assert.Equal("Daedric summons", azura.Caller);
        Assert.Equal(16, azura.FactionId);
        Assert.Equal("T0C00Y00", azura.Quest);
        Assert.All(definitions.Cinematics.Cinematics.Values, cinematic => Assert.True(cinematic.ByteLength > 0 && cinematic.Digest.Length == 64));
    }

    private static DaggerfallDefinitions Definitions()
    {
        string root = RepositoryRoot();
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
