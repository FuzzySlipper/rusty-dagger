using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class RmbExteriorNormalizerTests
{
    [Fact]
    public void Charing_exterior_and_selected_real_building_interior_publish_nonempty_static_collision_navigation_closures()
    {
        DungeonLogicalSourceSet sources = Sources();
        RmbExteriorNormalizationResult exterior = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Exterior)
        {
            // This assertion is about source assembly, not a full-world navigation fidelity benchmark.
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 10F },
        });
        RmbExteriorNormalizationResult exteriorAgain = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Exterior)
        {
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 10F },
        });
        RmbExteriorNormalizationResult interior = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Interior)
        {
            Building = new RmbBuildingSelection(1, 1, 0),
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 2F },
        });

        Assert.Equal((6, 7, 42), (exterior.Layout.Width, exterior.Layout.Height, exterior.Layout.Blocks.Count));
        Assert.True(exterior.Document.Meshes.Count > 100);
        Assert.Equal(0F, exterior.Document.Bounds.Minimum.X);
        Assert.Equal(0F, exterior.Document.Bounds.Maximum.Z);
        Assert.Equal(614.4F, exterior.Document.Bounds.Maximum.X);
        Assert.Contains(exterior.Document.Meshes.SelectMany(mesh => mesh.Vertices), point => point.X == 0F && point.Y == 0F && point.Z == 0F);
        Assert.Contains(exterior.Document.Meshes.SelectMany(mesh => mesh.Vertices), point => point.X == 614.4F && point.Y == 0F && point.Z == -614.4F);
        Assert.NotEmpty(exterior.Document.Navigation!.Cells);
        Assert.Contains(exterior.Document.Navigation.Cells, cell => cell.SupportHeight == 0F);
        Assert.Equal(exterior.SpatialPublication.StaticMesh.Bytes.ToArray(), exteriorAgain.SpatialPublication.StaticMesh.Bytes.ToArray());
        Assert.Equal(exterior.SpatialPublication.CollisionNavigation.Bytes.ToArray(), exteriorAgain.SpatialPublication.CollisionNavigation.Bytes.ToArray());
        Assert.Equal("RESIAL05.RMB", interior.Layout.Blocks.Single(block => block.X == 1 && block.Y == 1).SourceName);
        Assert.Equal(new RmbBuildingSelection(1, 1, 0), interior.Building);
        Assert.NotEmpty(interior.Document.Meshes);
        Assert.NotEmpty(interior.Document.Navigation!.Cells);
        Assert.Equal(new NormalizedMarker("marker/enter", new NormalizedVector3(8F, 0F, 4.8F)), interior.Document.World.EnterMarker);
        Assert.All([exterior, interior], profile =>
        {
            Assert.True(profile.SpatialPublication.StaticMesh.Bytes.Length > 0);
            Assert.True(profile.SpatialPublication.CollisionNavigation.Bytes.Length > 0);
            Assert.NotEmpty(profile.SpatialPublication.MaterialSlots);
            profile.Validate();
        });
    }

    private static DungeonLogicalSourceSet Sources()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local/arena2");
        return new DungeonLogicalSourceSet(Directory.EnumerateFiles(arena2)
            .Where(path => Path.GetFileName(path) is "MAPS.BSA" or "BLOCKS.BSA" or "ARCH3D.BSA" or "CLIMATE.PAK"
                || Path.GetFileName(path).StartsWith("TEXTURE.", StringComparison.Ordinal))
            .Select(path => new DungeonLogicalSource($"local/arena2/{Path.GetFileName(path)}", File.ReadAllBytes(path))));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "local/arena2"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root with local Arena2 corpus was not found.");
    }
}
