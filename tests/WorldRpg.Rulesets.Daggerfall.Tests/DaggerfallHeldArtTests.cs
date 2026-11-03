using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Checks the 3D first-person art is read from the authored held definition (content/worldrpg/first-person): weapon
/// families whose clip-led arms play a clip per classic strike, each fitted so the clip's strike mark lands on the
/// classic hit, drawn with the container's models.
/// </summary>
public sealed class DaggerfallHeldArtTests
{
    [Fact]
    public void Every_family_plays_a_clip_for_every_classic_strike_with_its_strike_mark_on_the_hit()
    {
        DaggerfallHeldArt art = Read(Document());

        Assert.Equal("dagger", art.Family("weapon.dagger.steel"));
        Assert.Equal("longsword", art.Family("weapon.longblade"));
        Assert.Equal("longsword", art.Family("weapon.warhammer"));
        Assert.Equal("halberd", art.Family("weapon.staff"));
        Assert.Null(art.Family("weapon.bow"));
        Assert.Empty(art.Document.Rig.Problems());
        // The viewmodel times a strike to the classic playback: five frames, the hit on frame 2.
        PhaseTiming timing = DaggerfallHeldViewmodel.Timing(.1F, 2, 5);
        foreach (var (name, family) in art.Document.Families)
        {
            Assert.Equal(NormalizedClassicWeapon.StrikeActions.Order(), family.Strikes.Keys.Order());
            foreach (var (action, clip) in family.Strikes)
            {
                Assert.Empty(clip.Problems());
                ArmClipFrame hit = clip.At(timing, timing.Windup);
                Assert.Equal(MotionPhase.Commit, hit.Phase);
                Assert.Equal(clip.Marks.Strike, hit.Time, 4);
                Assert.True(clip.Marks.Strike > clip.Marks.Start, $"{name} {action} winds up before its hit.");
            }
        }
    }

    [Fact]
    public void Only_a_family_held_in_both_hands_or_a_strike_it_lists_moves_the_off_hand()
    {
        DaggerfallHeldArt art = Read(Document());
        DaggerfallHeldFamily dagger = art.Document.Families["dagger"], sword = art.Document.Families["longsword"];

        Assert.False(dagger.MovesLeft(null));
        Assert.False(dagger.MovesLeft("strikeDown"));
        Assert.True(sword.MovesLeft(null));
        Assert.True(sword.MovesLeft("strikeDown"));
        Assert.True((dagger with { LeftHand = ["strikeDown"] }).MovesLeft("strikeDown"));
    }

    [Theory]
    [InlineData("strikes", "families.dagger.strikes.thrust")]
    [InlineData("marks", "families.dagger.strikes.strikeDown.marks")]
    [InlineData("weapons", "families.longsword.weapons: 'weapon.dagger.steel' is drawn by another family too")]
    [InlineData("leftHand", "families.dagger.leftHand: 'strikeUp' has no strike clip here")]
    [InlineData("idle", "families.dagger.idle.seconds")]
    public void What_cannot_be_drawn_is_refused_by_field(string broken, string field)
    {
        JsonObject document = Document();
        JsonObject dagger = document["families"]!["dagger"]!.AsObject();
        JsonObject strikes = dagger["strikes"]!.AsObject();
        switch (broken)
        {
            case "strikes": strikes["thrust"] = strikes["strikeDown"]!.DeepClone(); break;
            case "marks": strikes["strikeDown"]!["marks"]!["strike"] = 0.99; break;
            case "weapons": document["families"]!["longsword"]!["weapons"]!.AsArray().Add("weapon.dagger.steel"); break;
            case "leftHand": strikes.Remove("strikeUp"); dagger["leftHand"] = new JsonArray("strikeUp"); break;
            case "idle": dagger["idle"]!["seconds"] = 0; break;
        }

        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => Read(document));

        Assert.Contains(field, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_member_is_refused_rather_than_ignored()
    {
        JsonObject document = Document();
        document["families"]!["dagger"]!["motion"] = new JsonObject();

        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => Read(document));

        Assert.Contains("motion", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_model_the_container_does_not_carry_is_refused_by_name()
    {
        JsonObject document = Document();
        document["families"]!["dagger"]!["look"]!["model"] = "first-person/dagger.glb";

        Assert.Contains("'first-person/arms.glb' is not in first-person.rpak", Assert.Throws<InvalidDataException>(() => Read(Document(), models: [])).Message, StringComparison.Ordinal);
        Assert.Contains("'first-person/dagger.glb' is not in first-person.rpak", Assert.Throws<InvalidDataException>(() => Read(document)).Message, StringComparison.Ordinal);
        Assert.Equal("first-person/dagger.glb", Read(document, models: ["first-person/arms.glb", "first-person/arms-retro.glb", "first-person/dagger.glb"]).Document.Families["dagger"].Look.Model);
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

    // The definition read over a container that carries the given models (by default both arms).
    internal static DaggerfallHeldArt Read(JsonObject document, string[]? models = null)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallHeldArt.ContainerName);
        foreach (string model in models ?? ["first-person/arms.glb", "first-person/arms-retro.glb"])
            service.Add(DaggerfallHeldArt.ContainerName, model, [7]);
        ProductContentBundle container = new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallHeldArt.ContainerName);
        return DaggerfallHeldArt.Read(container, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
    }
}
