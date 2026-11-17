using System.Text;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The world variants a quests pack carries, as <c>world-variant-bake</c> bakes them with the import's normalizer: a block
/// variant keeps every placement it does not remove under the base block's id, adds its models numbered on from the block's
/// own, and is stamped with the digest of the base block's document as the world block publication writes it.
/// </summary>
public sealed class WorldVariantTests
{
    private static string PayloadPath => Path.Combine(TestData.RepositoryRoot, "content/worldrpg/packs/daggerfall.quests/payload.json");

    private static IReadOnlyList<WorldBlockVariantRecipe> Shipped => WorldBlockVariantRecipe.ReadPayload(File.ReadAllBytes(PayloadPath), PayloadPath);

    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT")]
    public void The_shipped_recipes_bake_over_their_base_block_keeping_its_placements_and_stamping_its_document()
    {
        IReadOnlyList<WorldBlockVariantRecipe> recipes = Shipped;
        Assert.NotEmpty(recipes);
        WorldBlockNormalizer normalizer = new(WorldBlockTests.Sources());
        foreach (WorldBlockVariantRecipe recipe in recipes)
        {
            WorldBlockNormalization baked = normalizer.RmbExteriorVariant(recipe);
            DaggerfallWorldBlock edited = baked.Block, source = normalizer.RmbExterior(recipe.Block).Block;
            Assert.Equal(WorldBlockNormalizer.RmbExteriorVariantKey(recipe.Block, recipe.Variant), edited.Key);
            Assert.Equal(recipe.Variant, edited.Variant);
            Assert.Equal(ContentDigest.Compute(Arena2WorldBlocksPublication.DocumentBytes(source)), edited.BaseDigest);
            Assert.Null(source.Variant);
            Assert.Null(source.BaseDigest);
            Assert.NotNull(baked.Spatial);
            Assert.Equal(edited.Spatial!.RelativePath, baked.Spatial!.RelativePath);
            Assert.NotEqual(source.Spatial!.ContentDigest, edited.Spatial.ContentDigest);

            // Every placement the variant keeps is the base block's, under the same id and pose; doors, people and
            // buildings are untouched, so a saved change to any of them still names something placed.
            DaggerfallWorldBlockModel[] kept = [.. source.Models.Where(model => !recipe.Remove.Contains(model.Id))];
            Assert.Equal(kept, edited.Models.Take(kept.Length));
            Assert.Equal(source.Doors.Select(door => door.Id), edited.Doors.Select(door => door.Id));
            Assert.Equal(source.Population.Select(person => person.Id), edited.Population.Select(person => person.Id));
            Assert.Equal(source.Buildings, edited.Buildings);

            // The added models follow the source's own, numbered on from them, at the authored pose snapped to the source grid.
            int own = source.Models.Count(model => model.BuildingIndex is null);
            DaggerfallWorldBlockModel[] added = [.. edited.Models.Skip(kept.Length)];
            Assert.Equal(recipe.Add.Count, added.Length);
            foreach ((WorldBlockVariantModel authored, int index) in recipe.Add.Select((model, index) => (model, index)))
            {
                DaggerfallWorldBlockModel model = added[index];
                Assert.Equal(RmbPlacementTrims.PlacementId(recipe.Block, null, own + index), model.Id);
                Assert.Equal(authored.Model, model.ModelId);
                Assert.Null(model.BuildingIndex);
                Assert.True(Distance(authored.Position, model.Position) < 0.025F, $"{model.Id} stands at {model.Position}, authored at {authored.Position}.");
                Assert.InRange(MathF.Abs(Turn(authored.YawDegrees - model.RotationDegrees.Y)), 0F, 0.2F);
            }
        }
    }

    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT")]
    public void A_recipe_that_would_strand_base_content_or_change_a_published_trim_is_refused_by_name()
    {
        WorldBlockNormalizer normalizer = new(WorldBlockTests.Sources());
        WorldBlockVariantRecipe shipped = Shipped[0];
        string building = $"model/{PublishedIds.Slug(shipped.Block)}/0/0";
        Assert.Contains($"removes '{building}', which is not one of the block's own models",
            Assert.Throws<InvalidOperationException>(() => normalizer.RmbExteriorVariant(shipped with { Remove = [building] })).Message);

        // The world media publication trims the source block's placements only: an added model an earlier one covers
        // (here a second copy of the block's first own model, on top of it) would need a trimmed mesh nobody published.
        DaggerfallWorldBlockModel first = normalizer.RmbExterior(shipped.Block).Block.Models.First(model => model.BuildingIndex is null);
        Assert.Contains("changes which faces of", Assert.Throws<InvalidOperationException>(() =>
            normalizer.RmbExteriorVariant(shipped with { Remove = [], Add = [new(first.ModelId, first.Position, first.RotationDegrees.Y)] })).Message);
    }

    [Fact]
    public void The_pack_refuses_a_recipe_a_quest_cannot_select_and_one_stated_twice()
    {
        static string Refusal(string json) => Assert.Throws<InvalidDataException>(() => WorldBlockVariantRecipe.ReadPayload(Encoding.UTF8.GetBytes(json), "fixture.json")).Message;
        const string add = """{ "model": "41214", "position": { "x": 1, "y": 0, "z": -1 }, "yawDegrees": 0 }""";
        Assert.Empty(WorldBlockVariantRecipe.ReadPayload("""{ "questCatalog": [] }"""u8, "fixture.json"));
        Assert.Contains("cannot select",
            Refusal($$"""{ "worldVariants": { "blocks": [{ "block": "A.RMB", "variant": "-", "remove": [], "add": [{{add}}] }], "locations": [] } }"""));
        Assert.Contains("twice",
            Refusal($$"""{ "worldVariants": { "blocks": [{ "block": "A.RMB", "variant": "v", "remove": [], "add": [{{add}}] }, { "block": "A.RMB", "variant": "v", "remove": [], "add": [{{add}}] }], "locations": [] } }"""));
        Assert.Contains("changes nothing", Refusal("""{ "worldVariants": { "blocks": [{ "block": "A.RMB", "variant": "v", "remove": [], "add": [] }], "locations": [] } }"""));
        Assert.Contains("only RMB exteriors", Refusal($$"""{ "worldVariants": { "blocks": [{ "block": "A.RDB", "variant": "v", "remove": [], "add": [{{add}}] }], "locations": [] } }"""));
        Assert.Contains("do not read", Refusal("""{ "worldVariants": { "blocks": [{ "block": "A.RMB", "variant": "v", "extra": 1 }], "locations": [] } }"""));
    }

    private static float Distance(NormalizedVector3 first, NormalizedVector3 second) =>
        MathF.Sqrt(((first.X - second.X) * (first.X - second.X)) + ((first.Y - second.Y) * (first.Y - second.Y)) + ((first.Z - second.Z) * (first.Z - second.Z)));

    private static float Turn(float degrees) => ((degrees % 360F) + 540F) % 360F - 180F;
}
