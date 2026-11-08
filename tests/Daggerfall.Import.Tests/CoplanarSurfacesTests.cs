using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>Faces placed in one plane and facing keep one drawn copy of what they share.</summary>
public sealed class CoplanarSurfacesTests
{
    private static readonly NormalizedVector3 Front = new(0F, 0F, 1F);

    [Fact]
    public void A_later_face_keeps_only_what_an_earlier_one_in_its_plane_does_not_cover()
    {
        CoplanarSurfaces surfaces = new();
        Assert.Single(surfaces.Add(Wall(0F, 4F), Uvs(0F, 4F), Front));

        // The second hedge piece runs from 2 to 6 m: only its 4 to 6 m stretch is new, and it keeps its own
        // texture coordinates there, interpolated where it was cut.
        var (polygon, uvs) = Assert.Single(surfaces.Add(Wall(2F, 6F), Uvs(10F, 14F), Front));
        Assert.Equal(4F, polygon.Min(point => point.X), 4);
        Assert.Equal(6F, polygon.Max(point => point.X), 4);
        for (int index = 0; index < polygon.Count; index++)
            Assert.Equal(10F + (polygon[index].X - 2F), uvs[index].X, 4);

        // Wholly covered, a third piece draws nothing.
        Assert.Empty(surfaces.Add(Wall(0.5F, 5.5F), Uvs(0F, 5F), Front));
    }

    [Fact]
    public void Faces_that_only_touch_face_apart_or_lie_in_another_plane_are_kept_whole()
    {
        CoplanarSurfaces surfaces = new();
        surfaces.Add(Wall(0F, 4F), Uvs(0F, 4F), Front);

        Assert.Equal(Wall(4F, 8F), Assert.Single(surfaces.Add(Wall(4F, 8F), Uvs(0F, 4F), Front)).Polygon);
        Assert.Equal(Wall(1F, 3F), Assert.Single(surfaces.Add(Wall(1F, 3F), Uvs(0F, 2F), new(0F, 0F, -1F))).Polygon);
        NormalizedVector3[] behind = [.. Wall(1F, 3F).Select(point => point with { Z = -0.8F })];
        Assert.Equal(behind, Assert.Single(surfaces.Add(behind, Uvs(0F, 2F), Front)).Polygon);
    }

    private static NormalizedVector3[] Wall(float from, float to) =>
        [new(from, 0F, 0F), new(to, 0F, 0F), new(to, 1.6F, 0F), new(from, 1.6F, 0F)];

    private static NormalizedVector2[] Uvs(float from, float to) =>
        [new(from, 1F), new(to, 1F), new(to, 0F), new(from, 0F)];
}
