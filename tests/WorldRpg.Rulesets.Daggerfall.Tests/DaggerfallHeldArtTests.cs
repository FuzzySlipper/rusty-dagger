using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

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
        Assert.Equal("sns", art.Family("weapon.mace"));
        Assert.Equal("longsword", art.Family("weapon.flail"));
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
        DaggerfallHeldFamily dagger = art.Document.Families["dagger"], sword = art.Document.Families["longsword"], mace = art.Document.Families["sns"];

        Assert.False(dagger.MovesLeft(null));
        Assert.False(dagger.MovesLeft("strikeDown"));
        Assert.True(sword.MovesLeft(null));
        Assert.True(sword.MovesLeft("strikeDown"));
        Assert.False(mace.MovesLeft("strikeDown"));
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

    [Theory]
    [InlineData(116, "weapon.longblade", "sns")] // shortsword, one-handed
    [InlineData(121, "weapon.longblade", "sns")] // katana, one-handed
    [InlineData(127, "weapon.axe", "sns")] // battle axe, one-handed
    [InlineData(114, "weapon.longblade", "dagger")] // tanto
    [InlineData(122, "weapon.longblade", "longsword")] // claymore, two-handed
    [InlineData(123, "weapon.longblade", "longsword")] // dai-katana, two-handed
    [InlineData(128, "weapon.axe", "longsword")] // war axe, two-handed
    public void An_item_plays_the_family_its_hands_call_for_whatever_its_classic_visual(int template, string visual, string family)
    {
        DaggerfallHeldDrawing drawing = Read(Document()).Drawing(visual, template, "ebony")!;

        Assert.Equal(family, drawing.Family);
        Assert.Equal("ebony", drawing.Metal);
    }

    [Fact]
    public void An_item_draws_its_own_model_and_a_weapon_without_an_entry_draws_its_familys()
    {
        DaggerfallHeldArt art = Read(Document());
        DaggerfallHeldDrawing longsword = art.Drawing("weapon.longblade", 120, "steel")!, dagger = art.Drawing("weapon.dagger.steel", 113, "steel")!;

        Assert.Equal(120, longsword.Item);
        Assert.Equal("first-person/weapons/longsword.glb", art.Document.Items[120].Model);
        Assert.All(art.Document.Items.Values, item => Assert.NotNull(item.Model));
        Assert.Null(dagger.Item);
        Assert.Equal("first-person/weapons/dagger.glb", art.Document.Families[dagger.Family].Look.Model);
        Assert.NotEqual(art.Drawing("weapon.longblade", 120, "ebony"), longsword);
        Assert.Null(art.Drawing("weapon.bow", 129, "steel"));
    }

    [Theory]
    [InlineData("family", "items.120.family: names no family")]
    [InlineData("clip", "items.120.family: sns has no clip for strikeUp")]
    [InlineData("model", "'first-person/weapons/lost.glb' is not in first-person.rpak")]
    public void An_item_that_cannot_be_drawn_is_refused_by_field(string broken, string field)
    {
        JsonObject document = Document();
        switch (broken)
        {
            case "family": document["items"]!["120"]!["family"] = "greatsword"; break;
            case "clip": document["families"]!["sns"]!["strikes"]!.AsObject().Remove("strikeUp"); break;
            case "model": document["items"]!["120"]!["model"] = "first-person/weapons/lost.glb"; break;
        }

        Assert.Contains(field, Assert.Throws<InvalidDataException>(() => Read(document)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_models_metal_slot_is_found_by_name_and_a_tint_brightens_as_the_paper_doll_does()
    {
        string weapons = Path.Combine(TestData.RepositoryRoot, "content/worldrpg/first-person/weapons");
        BundleContentFake service = new();
        service.Container(DaggerfallHeldArt.ContainerName);
        foreach (string file in Directory.GetFiles(weapons, "*.glb")) service.Add(DaggerfallHeldArt.ContainerName, $"first-person/weapons/{Path.GetFileName(file)}", File.ReadAllBytes(file));
        service.Add(DaggerfallHeldArt.ContainerName, "first-person/arms.glb", [7]);
        service.Add(DaggerfallHeldArt.ContainerName, "first-person/arms-retro.glb", [7]);
        ProductContentBundle container = new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallHeldArt.ContainerName);
        DaggerfallHeldArt art = DaggerfallHeldArt.Read(container, System.Text.Encoding.UTF8.GetBytes(Document().ToJsonString()));

        Assert.Equal(0u, art.MetalSlot("first-person/weapons/dagger.glb"));
        Assert.Equal(1u, art.MetalSlot("first-person/weapons/saber.glb"));

        Color white = DaggerfallHeldArt.MetalColor(new("#ffffff", 1));
        Color bright = DaggerfallHeldArt.MetalColor(new("#808080", 1.5));
        Assert.Equal(1F, white.R, 4);
        Assert.Equal(1F, white.A, 4);
        Assert.InRange(bright.R, .5F, .55F);
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
        Assert.Equal("first-person/dagger.glb", Read(document, models: ["first-person/arms.glb", "first-person/arms-retro.glb", "first-person/dagger.glb", .. Weapons()]).Document.Families["dagger"].Look.Model);
    }

    [Fact]
    public void The_flail_alone_hangs_a_chain_from_its_handle_to_its_ball()
    {
        DaggerfallHeldArt art = Read(Document());

        Assert.Equal([125], art.Document.Items.Where(item => item.Value.Chain is not null).Select(item => item.Key));
        HeldChainDefinition chain = art.Document.Items[125].Chain!;
        Assert.Equal("handle", chain.Handle);
        Assert.Equal(["link0", "link1", "link2", "link3", "link4"], chain.Links);
        Assert.Equal("ball", chain.Weight);
        // A chain's weight is what swings it: heavier than a link, or the links would lead it.
        Assert.True(chain.WeightMass > chain.LinkMass);
        // It leans right, so the ball hangs out of the view at rest, and a strike has something to let go of.
        Assert.True(chain.Gravity.X > 0 && chain.Gravity.Y < 0);
        Assert.True(chain.HeldDamping > 0);
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

    [Fact]
    public void Leaving_the_workbench_with_the_weapon_sheathed_draws_nothing_and_releases_the_preview()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, AppearanceFake appearance, EngineContextFake engine) = Viewing(releases);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: false);
            presentation.ViewHeld(new("sns", 120, DaggerfallHeldDrawing.Steel));

            presentation.EndHeldView();

            Assert.Null(presentation.DrawnHeld);
            presentation.Publish(EmptyActors());
            Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
            presentation.BeginAdmittedUpdate();
            Assert.Equal(2, engine.Animation.ReleasedAppearances); // The arms and the look.
        }
    }

    [Fact]
    public void Leaving_the_workbench_with_a_weapon_drawn_gives_the_hands_back_what_they_wield()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, _, _) = Viewing(releases);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            DaggerfallHeldDrawing wielded = presentation.DrawnHeld!.Drawing;
            Assert.Equal(new DaggerfallHeldDrawing("sns", 120, DaggerfallHeldDrawing.Steel), wielded);
            presentation.ViewHeld(new("longsword", null, DaggerfallHeldDrawing.Steel));

            presentation.EndHeldView();

            Assert.Equal(wielded, presentation.DrawnHeld!.Drawing);
        }
    }

    // A site drawing the authored art through arms whose model has the rig's joints and every clip, whatever the hands
    // wield standing for a longsword (template 120).
    private static (DaggerfallSiteAppearance Presentation, AppearanceFake Appearance, EngineContextFake Engine) Viewing(List<string> releases)
    {
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(Hash, releases).Service, appearance, PerceptionFake.Create().Service);
        DaggerfallHeldArt art = Read(Document());
        engine.Animation.Joints = Joints(art.Document.Rig);
        engine.Animation.Clips = [.. art.Document.Families.Values.SelectMany(family => family.Strikes.Values.Select(clip => clip.Clip).Append(family.Idle.Clip)).Distinct()
            .Select(clip => new AnimationClipInfo(clip, clip, 1, true, 0))];
        DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UseHeldArt(engine.Context, art, DaggerfallViewmodelStyle.Held, new Dictionary<string, DaggerfallDyeTint>(), _ => (120, DaggerfallHeldDrawing.Steel));
        return (presentation, appearance, engine);
    }

    // Both arms' joints under one root: the upper arm, the forearm and its twist, the hand and its digits.
    private static AnimationJointInfo[] Joints(ArmRig rig)
    {
        List<AnimationJointInfo> joints = [new("root", uint.MaxValue)];
        uint Add(string name, uint parent)
        {
            joints.Add(new(name, parent));
            return (uint)joints.Count - 1;
        }
        foreach (ArmJoints arm in new[] { rig.Main, rig.Off })
        {
            uint lower = Add(arm.Lower, Add(arm.Upper, 0));
            foreach (var twist in arm.Twist) Add(twist.Joint, lower);
            uint hand = Add(arm.Hand, lower);
            foreach (string[] digit in arm.Fingers.Append(arm.Thumb)) digit.Aggregate(hand, (parent, joint) => Add(joint, parent));
        }
        return [.. joints];
    }

    // The authored held definition.
    internal static JsonObject Document() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestData.RepositoryRoot, "content", DaggerfallHeldArt.DocumentPath)))!.AsObject();

    private static string[] Weapons() => Directory.GetFiles(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/first-person/weapons"), "*.glb")
        .Select(path => $"first-person/weapons/{Path.GetFileName(path)}").ToArray();

    // The definition read over a container that carries the given models (by default what scripts/fp-arms/build.sh
    // packs: both arms and the committed weapon models).
    internal static DaggerfallHeldArt Read(JsonObject document, string[]? models = null)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallHeldArt.ContainerName);
        foreach (string model in models ?? ["first-person/arms.glb", "first-person/arms-retro.glb", .. Weapons()])
            service.Add(DaggerfallHeldArt.ContainerName, model, [7]);
        ProductContentBundle container = new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallHeldArt.ContainerName);
        return DaggerfallHeldArt.Read(container, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
    }
}
