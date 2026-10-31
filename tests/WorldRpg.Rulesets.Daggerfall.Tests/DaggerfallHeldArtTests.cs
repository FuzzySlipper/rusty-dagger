using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Checks the 3D first-person art is read from the authored held definition (content/worldrpg/first-person), whose
/// strike-down was seeded from a capture of the classic animation, drawn with the container's models.
/// </summary>
public sealed class DaggerfallHeldArtTests
{
    [Fact]
    public void The_captured_strike_loads_through_the_engine_records_and_lands_its_hit_on_the_commit()
    {
        DaggerfallHeldArt art = Read(Document());

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

        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => Read(document));

        Assert.Contains("motion.tracks.thrust", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_model_the_container_does_not_carry_is_refused_by_name()
    {
        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => Read(Document(), models: []));

        Assert.Contains("'first-person/arms.glb' is not in first-person.rpak", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_look_with_no_model_is_read_without_one_in_the_container()
    {
        JsonObject document = Document();
        document["look"]!["model"] = null;

        DaggerfallHeldArt art = Read(document, models: ["first-person/arms.glb"]);

        Assert.Null(art.Document.Look.Model);
        Assert.Empty(art.Stance.Problems());
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

    // The authored held definition.
    internal static JsonObject Document() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestData.RepositoryRoot, "content", DaggerfallHeldArt.DocumentPath)))!.AsObject();

    // The definition read over a container that carries the given models (by default the arms and a dagger).
    internal static DaggerfallHeldArt Read(JsonObject document, string[]? models = null)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallHeldArt.ContainerName);
        foreach (string model in models ?? ["first-person/arms.glb", "first-person/dagger.glb"])
            service.Add(DaggerfallHeldArt.ContainerName, model, [7]);
        ProductContentBundle container = new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallHeldArt.ContainerName);
        return DaggerfallHeldArt.Read(container, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
    }
}
