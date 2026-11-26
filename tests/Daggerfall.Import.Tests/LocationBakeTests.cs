using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Daggerfall.Source.Tool.Commands;
using WorldRpg.Tests.Support;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class LocationBakeTests
{
    private static readonly string BasePack = Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "packs", "daggerfall.base");

    [CorpusFact]
    public void A_recipe_that_keeps_the_classic_grid_lays_the_location_out_as_the_import_does()
    {
        (BsaArchive maps, DaggerfallLocationExteriorBuilder builder, PakMap climate) = Sources();
        LocationOverrideRecipe classic = new(17, 1045, null, 1, 1, [new(0, 0, "MANRAS01.RMB")], [], false);

        DaggerfallLocationMap baked = LocationBakeCommand.Bake(maps, builder, climate, classic);

        DaggerfallLocationMap imported = DaggerfallLocationBuilder.Build(maps, Archive("BLOCKS.BSA"), Archive("ARCH3D.BSA"), climate)
            .Locations.Single(location => location.Region == 17 && location.Index == 1045);
        Assert.Equal(JsonSerializer.Serialize(imported, PublishedJson.Section), JsonSerializer.Serialize(baked, PublishedJson.Section));
        Assert.Equal(imported.Exterior!.GroundTiles, baked.Exterior!.GroundTiles);
    }

    [CorpusFact]
    public void The_committed_location_overrides_are_what_the_bake_makes_of_their_recipes()
    {
        (BsaArchive maps, DaggerfallLocationExteriorBuilder builder, PakMap climate) = Sources();
        IReadOnlyList<LocationOverrideRecipe> recipes = LocationBakeCommand.Read(Path.Combine(BasePack, "payload.json"));
        Assert.NotEmpty(recipes);

        foreach (LocationOverrideRecipe recipe in recipes)
        {
            DaggerfallLocationMap location = LocationBakeCommand.Bake(maps, builder, climate, recipe);
            string folder = Path.Combine(BasePack, LocationBakeCommand.Folder);
            Assert.Equal(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(location, PublishedJson.Section)),
                File.ReadAllText(Path.Combine(folder, LocationBakeCommand.RecordPath(recipe.Region, recipe.Index))));
            Assert.Equal(JsonSerializer.Serialize(new DaggerfallLocationDetail(location.Exterior!.Buildings, location.Exterior.BuildingReferences), PublishedJson.SectionCompact),
                File.ReadAllText(Path.Combine(folder, $"{recipe.Region}/{recipe.Index}.json")));
            Assert.Equal(location.Exterior.GroundTiles, File.ReadAllBytes(Path.Combine(folder, $"{recipe.Region}/{recipe.Index}.ground")));
        }
    }

    [Fact]
    public void A_recipe_must_fill_its_grid_and_add_buildings_inside_it()
    {
        LocationOverrideRecipe holed = new(17, 1045, null, 2, 1, [new(0, 0, "MANRAS01.RMB")], [], false);
        LocationOverrideRecipe stray = new(17, 1045, null, 1, 1, [new(0, 0, "MANRAS01.RMB")], [new(1, 0, 16, 129, 1, 20)], false);

        Assert.Contains("must place one block in each of its 2x1 cells", Assert.Throws<InvalidDataException>(() => LocationBakeCommand.Bake(null!, null!, null!, holed)).Message);
        Assert.Contains("adds a city building outside its grid", Assert.Throws<InvalidDataException>(() => LocationBakeCommand.Bake(null!, null!, null!, stray)).Message);
    }

    private static (BsaArchive Maps, DaggerfallLocationExteriorBuilder Builder, PakMap Climate) Sources() =>
        (Archive("MAPS.BSA"), new(Archive("BLOCKS.BSA"), Archive("ARCH3D.BSA")),
            PakDecoder.Decode(File.ReadAllBytes(TestData.Corpus("CLIMATE.PAK")), "arena2/CLIMATE.PAK"));

    private static BsaArchive Archive(string name) => BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus(name)), $"arena2/{name}");
}
