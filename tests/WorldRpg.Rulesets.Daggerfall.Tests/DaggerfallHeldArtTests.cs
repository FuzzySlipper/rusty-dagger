using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Checks the 3D first-person art is read from its container: the authored rig and arms
/// (scripts/fp-arms) with the strike-down captured from the classic animation (a fixture copy of the bake).
/// </summary>
public sealed class DaggerfallHeldArtTests
{
    [Fact]
    public void The_captured_strike_loads_through_the_engine_records_and_lands_its_hit_on_the_commit()
    {
        DaggerfallHeldArt art = DaggerfallHeldArt.Read(Container(Document()));

        Assert.True(art.Draws("weapon.dagger.steel"));
        Assert.False(art.Draws("weapon.00"));
        HeldMotion motion = art.Document.Motion;
        Assert.Empty(motion.Problems());
        Assert.Empty(art.Document.Rig.Problems());
        Assert.Empty(art.Stance.Problems());
        // The viewmodel times a strike to the classic playback: five frames, the hit on frame 2.
        const float frame = .1F;
        PhaseTiming timing = new(2 * frame, 2 * frame, frame);
        Assert.Null(motion.OrderProblem("strikeDown", timing));
        MotionKey[] keys = motion.Tracks["strikeDown"];
        // The strike itself (the commit's start) is the hit frame: the capture keys the drawn hit pose there.
        Assert.Contains(keys, key => key.Phase == MotionPhase.Windup && key.At == 1F);
        Assert.Contains(keys, key => key.Phase == MotionPhase.Commit);
        Assert.Equal(2 * frame, keys.Where(key => key.Phase == MotionPhase.Windup).Max(key => key.Time(timing)), 4);
    }

    [Fact]
    public void A_track_for_an_action_the_weapon_has_not_is_refused_by_field()
    {
        JsonObject document = Document();
        JsonObject tracks = document["motion"]!["tracks"]!.AsObject();
        tracks["thrust"] = tracks["strikeDown"]!.DeepClone();

        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => DaggerfallHeldArt.Read(Container(document)));

        Assert.Contains("motion.tracks.thrust", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_model_the_container_does_not_carry_is_refused_by_name()
    {
        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => DaggerfallHeldArt.Read(Container(Document(), withModels: false)));

        Assert.Contains("'first-person/arms.glb' is not in the container", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_composition_without_the_first_person_container_draws_the_sprites()
    {
        ProductContent content = TestSessions.FullContent(TestData.RepositoryRoot, "worldrpg/imports/privateers-hold");
        ResolvedGameComposition composition = TestSessions.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition();

        Assert.Null(DaggerfallHeldArt.For(composition));
    }

    [Fact]
    public void The_viewmodel_style_is_classic_by_default_and_read_by_name()
    {
        string root = TestData.RepositoryRoot;
        string defaults = Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json");
        Assert.Equal(DaggerfallViewmodelStyle.Classic, DaggerfallTuning.Read(TestContentFiles.ReadCopy(defaults)).Viewmodel.Style);
        foreach (var (name, style) in new[] { ("held", DaggerfallViewmodelStyle.Held), ("heldRetro", DaggerfallViewmodelStyle.HeldRetro) })
            Assert.Equal(style, DaggerfallTuning.Read(Mutated(defaults, name)).Viewmodel.Style);
        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => DaggerfallTuning.Read(Mutated(defaults, "sprite")));
        Assert.Contains("viewmodel.style 'sprite'", refusal.Message, StringComparison.Ordinal);

        static byte[] Mutated(string path, string style)
        {
            JsonObject tuning = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            tuning["viewmodel"]!["style"] = style;
            return System.Text.Encoding.UTF8.GetBytes(tuning.ToJsonString());
        }
    }

    // The document build.sh assembles: the authored rig and arms, and the captured look and motion.
    private static JsonObject Document()
    {
        string root = TestData.RepositoryRoot;
        JsonObject captured = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/WorldRpg.Rulesets.Daggerfall.Tests/Fixtures/first-person-strike-down.json")))!.AsObject();
        return new JsonObject
        {
            ["weapons"] = new JsonArray("weapon.dagger.steel"),
            ["rig"] = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "scripts/fp-arms/rig.json"))),
            ["arms"] = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "scripts/fp-arms/arms.json"))),
            ["look"] = captured["look"]!.DeepClone(),
            ["motion"] = captured["motion"]!.DeepClone(),
        };
    }

    private static ProductContentBundle Container(JsonObject document, bool withModels = true)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallHeldArt.ContainerName);
        service.Add(DaggerfallHeldArt.ContainerName, DaggerfallHeldArt.DocumentPath, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        if (withModels)
            foreach (string model in new[] { "first-person/arms.glb", "first-person/dagger.glb" })
                service.Add(DaggerfallHeldArt.ContainerName, model, [7]);
        return new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallHeldArt.ContainerName);
    }
}
