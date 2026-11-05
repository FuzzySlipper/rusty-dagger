using WorldRpg.Rulesets.Daggerfall.Facts;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Checks the 3D first-person art is read from the authored held definition (content/worldrpg/first-person): weapon
/// families whose clip-led arms play a clip per fast attack, each fitted so the clip's strike mark lands as the
/// rules' windup ends, drawn with the container's models.
/// </summary>
public sealed class DaggerfallHeldArtTests
{
    [Fact]
    public void Every_family_plays_a_clip_for_every_fast_attack_with_its_strike_mark_on_the_hit()
    {
        DaggerfallHeldArt art = Read(Document());

        Assert.Equal("dagger", art.Family("weapon.dagger.steel"));
        Assert.Equal("longsword", art.Family("weapon.longblade"));
        Assert.Equal("longsword", art.Family("weapon.warhammer"));
        Assert.Equal("sns", art.Family("weapon.mace"));
        Assert.Equal("longsword", art.Family("weapon.flail"));
        Assert.Equal("halberd", art.Family("weapon.staff"));
        Assert.Equal("bow", art.Family("weapon.bow"));
        Assert.Null(art.Family("weapon.unknown"));
        Assert.Empty(art.Document.Rig.Problems());
        foreach (var (name, family) in art.Document.Families)
        {
            // The viewmodel times an attack by its family's weapon class.
            DaggerfallAttackTiming fast = DaggerfallAttackTuning.Default.For(family.Class).Fast;
            PhaseTiming timing = new((float)fast.Windup, (float)fast.Commit, (float)fast.Recovery);
            if (family.Bow is null)
            {
                Assert.All(DaggerfallAttackIds.Fast, id => Assert.Contains(id, family.Strikes.Keys));
                Assert.Contains(DaggerfallAttackIds.Cast, family.Strikes.Keys);
                Assert.True(family.MovesLeft(DaggerfallAttackIds.Cast), $"{name}'s cast is the left hand's.");
            }
            else Assert.Equal([DaggerfallAttackIds.Kick, DaggerfallAttackIds.Shot, DaggerfallAttackIds.ShotDraw], family.Strikes.Keys.Order());
            Assert.NotNull(family.PowerHold);
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
        Assert.False(dagger.MovesLeft("fastA"));
        Assert.True(sword.MovesLeft(null));
        Assert.True(sword.MovesLeft("fastA"));
        Assert.False(mace.MovesLeft("fastA"));
        Assert.True((dagger with { LeftHand = ["fastA"] }).MovesLeft("fastA"));
    }

    [Theory]
    [InlineData("strikes", "families.dagger.strikes.thrust")]
    [InlineData("hold", "families.dagger.strikes.powerHold: is not an attack with a timed clip")]
    [InlineData("powerHold", "families.dagger.powerHold: must name a clip")]
    [InlineData("marks", "families.dagger.strikes.fastA.marks")]
    [InlineData("weapons", "families.longsword.weapons: 'weapon.dagger.steel' is drawn by another family too")]
    [InlineData("leftHand", "families.dagger.leftHand: 'fastB' has no strike clip here")]
    [InlineData("idle", "families.dagger.idle.seconds")]
    public void What_cannot_be_drawn_is_refused_by_field(string broken, string field)
    {
        JsonObject document = Document();
        JsonObject dagger = document["families"]!["dagger"]!.AsObject();
        JsonObject strikes = dagger["strikes"]!.AsObject();
        switch (broken)
        {
            case "strikes": strikes["thrust"] = strikes["fastA"]!.DeepClone(); break;
            case "hold": strikes["powerHold"] = strikes["fastA"]!.DeepClone(); break;
            case "powerHold": dagger["powerHold"] = new JsonObject { ["clip"] = "FPP_Dag_Idle" }; break;
            case "marks": strikes["fastA"]!["marks"]!["strike"] = 0.99; break;
            case "weapons": document["families"]!["longsword"]!["weapons"]!.AsArray().Add("weapon.dagger.steel"); break;
            case "leftHand": strikes.Remove("fastB"); dagger["leftHand"] = new JsonArray("fastB"); break;
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
        DaggerfallHeldDrawing longsword = art.Drawing("weapon.longblade", 120, "steel")!, dagger = art.Drawing("weapon.dagger.steel", null, "steel")!;

        Assert.Equal(120, longsword.Item);
        Assert.Equal("first-person/weapons/longsword.glb", art.Document.Items[120].Model);
        Assert.All(art.Document.Items.Values, item => Assert.NotNull(item.Model));
        Assert.Null(dagger.Item);
        Assert.Equal("first-person/weapons/dagger.glb", art.Document.Families[dagger.Family].Look.Model);
        Assert.NotEqual(art.Drawing("weapon.longblade", 120, "ebony"), longsword);
        Assert.Null(art.Drawing("weapon.unknown", null, "steel"));
    }

    [Theory]
    [InlineData("family", "items.120.family: names no family")]
    [InlineData("clip", "items.120.family: sns has no clip for fastB")]
    [InlineData("model", "'first-person/weapons/lost.glb' is not in first-person.rpak")]
    public void An_item_that_cannot_be_drawn_is_refused_by_field(string broken, string field)
    {
        JsonObject document = Document();
        switch (broken)
        {
            case "family": document["items"]!["120"]!["family"] = "greatsword"; break;
            case "clip": document["families"]!["sns"]!["strikes"]!.AsObject().Remove("fastB"); break;
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
        foreach (string model in ArmModels()) service.Add(DaggerfallHeldArt.ContainerName, model, [7]);
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

        Assert.Contains("'first-person/arms-male-leather.glb' is not in first-person.rpak", Assert.Throws<InvalidDataException>(() => Read(Document(), models: [])).Message, StringComparison.Ordinal);
        Assert.Contains("'first-person/dagger.glb' is not in first-person.rpak", Assert.Throws<InvalidDataException>(() => Read(document)).Message, StringComparison.Ordinal);
        Assert.Equal("first-person/dagger.glb", Read(document, models: [.. ArmModels(), "first-person/dagger.glb", .. Weapons()]).Document.Families["dagger"].Look.Model);
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
            Assert.Equal(new DaggerfallHeldDrawing("sns", 120, DaggerfallHeldDrawing.Steel) { Arms = "male-leather" }, wielded);
            presentation.ViewHeld(new("longsword", null, DaggerfallHeldDrawing.Steel));

            presentation.EndHeldView();

            Assert.Equal(wielded, presentation.DrawnHeld!.Drawing);
        }
    }

    [Fact]
    public void A_family_with_a_cast_clip_plays_it_to_the_rules_timing_and_draws_no_sprite_hand()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, AppearanceFake appearance, _) = Viewing(releases);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            int atlases = appearance.AtlasRequests.Count;

            presentation.React(new PlayerCastMotionFact(1, new(.3d, .02d, .3d), 1, 1));

            Assert.Equal(DaggerfallAttackIds.Cast, presentation.DrawnHeld!.Playing);
            Assert.Equal(atlases, appearance.AtlasRequests.Count); // Cold has no effect published here, and no hand is drawn.

            presentation.React(new PlayerAttackRestFact(1, 2));

            Assert.Null(presentation.DrawnHeld!.Playing);
        }
    }

    [Fact]
    public void A_cast_burns_its_element_on_the_palm_until_the_spell_leaves()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, AppearanceFake appearance, _) = Viewing(releases);
        using (presentation)
        {
            int Drawn()
            {
                presentation.Publish(EmptyActors());
                return appearance.Snapshots.Last().Count(fact => fact.Layer == RenderLayer.Viewmodel && fact.Visible);
            }
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            int arms = Drawn();

            presentation.React(new PlayerCastMotionFact(0, new(.3d, .02d, .3d), 1, 1));
            Assert.Equal(arms + 1, Drawn());

            presentation.DrawnHeld!.Step(.25F);
            Assert.Equal(arms + 1, Drawn());

            presentation.DrawnHeld!.Step(.1F);
            Assert.Equal(arms, Drawn());
            Assert.Equal(DaggerfallAttackIds.Cast, presentation.DrawnHeld!.Playing);
        }
    }

    [Fact]
    public void A_shield_plays_the_family_its_clips_were_made_for_and_binds_to_the_off_hand_until_it_comes_off()
    {
        List<string> releases = [];
        (int Template, string Metal)? worn = (110, "ebony");
        (DaggerfallSiteAppearance presentation, AppearanceFake appearance, EngineContextFake engine) = Viewing(releases, shieldOf: _ => worn);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            DaggerfallHeldViewmodel shielded = presentation.DrawnHeld!;
            Assert.Equal(new DaggerfallHeldDrawing("sns", 120, DaggerfallHeldDrawing.Steel) { Arms = "male-leather", Shield = 110, ShieldMetal = "ebony" }, shielded.Drawing);

            presentation.Publish(EmptyActors());
            presentation.Publish(EmptyActors());

            MeshJointAttachment attachment = Assert.Single(appearance.Attachments);
            Assert.Equal((shielded.ShieldObject, "hand_l"), (attachment.ChildObjectId, attachment.Joint));
            AppearanceFact shield = Assert.Single(appearance.Snapshots.Last(), fact => fact.ObjectId == shielded.ShieldObject);
            Assert.True(shield.Visible);
            Assert.True(shield.HasParentObject);

            worn = null;
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);

            Assert.Null(presentation.DrawnHeld!.Drawing.Shield);
            presentation.Publish(EmptyActors());
            Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.ObjectId == shielded.ShieldObject);
        }
    }

    [Theory]
    [InlineData("weapon.bow", 129)]
    [InlineData("item.129", 129)]
    [InlineData("item.130", 130)]
    public void Every_look_of_a_bow_opens_in_the_workbench_as_an_item_with_a_model_and_a_draw(string look, int template)
    {
        List<string> releases = [];
        DaggerfallHeldArt art = Read(Document());
        (DaggerfallSiteAppearance presentation, _, _) = Viewing(releases);
        using (presentation)
        {
            DaggerfallHeldDrawing drawing = art.Look(look)!;
            Assert.Equal(("bow", template), (drawing.Family, drawing.Item));
            Assert.NotNull(presentation.ViewHeld(drawing));
        }
    }

    [Theory]
    [InlineData(129)]
    [InlineData(130)]
    public void A_bow_rides_the_off_hand_with_its_arrow_on_the_string_from_the_draw_to_the_release(int template)
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, AppearanceFake appearance, _) = Viewing(releases, template: template);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            DaggerfallHeldViewmodel bow = presentation.DrawnHeld!;
            Assert.Equal("bow", bow.Drawing.Family);
            AppearanceFact Arrow() => Assert.Single(appearance.Snapshots.Last(), fact => fact.ObjectId == bow.ArrowObject);

            presentation.Publish(EmptyActors());
            presentation.Publish(EmptyActors());

            Assert.Equal([(bow.WeaponObject, "hand_l"), (bow.ArrowObject, "nock")], appearance.Attachments.Select(attachment => (attachment.ChildObjectId, attachment.Joint)));
            AppearanceFact held = Assert.Single(appearance.Snapshots.Last(), fact => fact.ObjectId == bow.WeaponObject);
            Assert.True(held.Visible);
            Assert.True(held.HasParentObject);
            Assert.False(Arrow().Visible);

            bow.Attack(DaggerfallAttackIds.ShotDraw, new(.6d, 0d, 0d), 5);
            bow.Step(.1F);
            presentation.Publish(EmptyActors());
            Assert.True(Arrow().Visible);

            bow.Hold();
            bow.Step(1);
            presentation.Publish(EmptyActors());
            Assert.True(Arrow().Visible);

            bow.Attack(DaggerfallAttackIds.Shot, new(.02d, .02d, .45d), 5);
            bow.Step(.01F);
            presentation.Publish(EmptyActors());
            Assert.True(Arrow().Visible);

            bow.Step(.02F);
            presentation.Publish(EmptyActors());
            Assert.False(Arrow().Visible);
        }
    }

    [Theory]
    [InlineData("Male", "Cloth", "male-cloth")]
    [InlineData("Male", "Leather", "male-leather")]
    [InlineData("Male", "Mail", "male-chainmail")]
    [InlineData("Female", "Cloth", "female-cloth")]
    [InlineData("Female", "Leather", "female-leather")]
    [InlineData("Female", "Mail", "female-leather")]
    public void A_players_gender_and_arm_armour_choose_the_arm_set(string gender, string wear, string set)
    {
        Assert.Equal(set, Read(Document()).Arms(Enum.Parse<DaggerfallCharacterGender>(gender), Enum.Parse<DaggerfallArmWear>(wear)));
    }

    [Theory]
    [InlineData(new string[0], "Cloth")]
    [InlineData(new[] { "leather" }, "Leather")]
    [InlineData(new[] { "chain" }, "Mail")]
    [InlineData(new[] { "leather", "iron", "leather" }, "Mail")]
    public void The_heaviest_armour_on_the_arms_shows(string[] materials, string wear)
    {
        Assert.Equal(Enum.Parse<DaggerfallArmWear>(wear), DaggerfallArmWearPolicy.Heaviest(materials));
    }

    [Theory]
    [InlineData("worn", "armsWorn.female.mail: 'female-plate' is no arm set")]
    [InlineData("beast", "armsBeast.wereboar: 'boar-hide' is no arm set")]
    [InlineData("bow", "items.130.drawn: a bow needs how much of its draw clip")]
    [InlineData("no-bow", "families.bow.bow: needs an item of the family")]
    [InlineData("rig", "rig.model: 'first-person/lost.glb' is no arm set's model")]
    [InlineData("model", "'first-person/lost.glb' is not in first-person.rpak")]
    public void Arms_that_cannot_be_drawn_are_refused_by_field(string broken, string field)
    {
        JsonObject document = Document();
        switch (broken)
        {
            case "worn": document["armsWorn"]!["female"]!["mail"] = "female-plate"; break;
            case "beast": document["armsBeast"]!["wereboar"] = "boar-hide"; break;
            case "bow": document["items"]!["130"]!.AsObject().Remove("drawn"); break;
            case "no-bow": foreach (string bow in new[] { "129", "130" }) document["items"]!.AsObject().Remove(bow); break;
            case "rig": document["rig"]!["model"] = "first-person/lost.glb"; break;
            case "model": document["armSets"]!["male-cloth"]!["retroModel"] = "first-person/lost.glb"; break;
        }

        Assert.Contains(field, Assert.Throws<InvalidDataException>(() => Read(document)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Werewolf", "claws")]
    [InlineData("Wereboar", "claws-boar")]
    public void A_transformed_player_draws_the_claws_on_the_beast_arms_and_changes_back(string form, string arms)
    {
        List<string> releases = [];
        DaggerfallRacialKind? beast = null;
        DaggerfallHeldArt art = Read(Document());
        (DaggerfallSiteAppearance presentation, _, EngineContextFake engine) = Viewing(releases,
            arms: () => beast is { } kind ? art.Arms(kind) : "male-leather", beast: () => beast);
        using (presentation)
        {
            void Update() => presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            Update();
            string armed = presentation.DrawnHeld!.Drawing.Family;
            Assert.NotEqual("claws", armed);

            beast = Enum.Parse<DaggerfallRacialKind>(form);
            Update();
            Assert.Equal("claws", presentation.DrawnHeld!.Drawing.Family);
            Assert.Equal(arms, presentation.DrawnHeld!.Drawing.Arms);
            Assert.Null(presentation.DrawnHeld!.Drawing.Item);
            Assert.Contains($"first-person/arms-{arms}.glb", engine.Animation.OpenedPaths);

            beast = null;
            Update();
            Assert.Equal(armed, presentation.DrawnHeld!.Drawing.Family);
            Assert.Equal("male-leather", presentation.DrawnHeld!.Drawing.Arms);
        }
    }

    [Fact]
    public void Arms_open_the_set_worn_and_a_change_waits_for_rest_and_holds_the_pose()
    {
        List<string> releases = [];
        string worn = "male-leather";
        (DaggerfallSiteAppearance presentation, _, EngineContextFake engine) = Viewing(releases, arms: () => worn);
        using (presentation)
        {
            void Update() => presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            Update();
            Assert.Contains("first-person/arms-male-leather.glb", engine.Animation.OpenedPaths);
            DaggerfallHeldViewmodel first = presentation.DrawnHeld!;
            first.Step(.75F);
            presentation.Publish(EmptyActors());
            presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 1, 1));
            first.Step(.3F);
            presentation.Publish(EmptyActors());

            worn = "male-chainmail";
            Update();
            Assert.Same(first, presentation.DrawnHeld);

            first.Rest(0);
            first.Step(.01F);
            presentation.Publish(EmptyActors());
            Update();
            Assert.Same(first, presentation.DrawnHeld);

            first.Step(1);
            presentation.Publish(EmptyActors());
            Update();
            DaggerfallHeldViewmodel dressed = presentation.DrawnHeld!;
            presentation.Publish(EmptyActors());

            Assert.NotSame(first, dressed);
            Assert.Equal("male-chainmail", dressed.Drawing.Arms);
            Assert.Contains("first-person/arms-male-chainmail.glb", engine.Animation.OpenedPaths);
            Assert.Equal(first.IdleSeconds, dressed.IdleSeconds);
            Assert.Equal(1, dressed.Clip.Led);
        }
    }

    [Fact]
    public void A_guard_raises_holds_and_lowers_on_its_clips_and_a_shield_brings_its_own()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, AppearanceFake appearance, _) = Viewing(releases, shieldOf: _ => (110, DaggerfallHeldDrawing.Steel));
        using (presentation)
        {
            string Drawn()
            {
                presentation.Publish(EmptyActors());
                return presentation.DrawnHeld!.Clip.Playing!.Value.Clip;
            }
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            string idle = Drawn();

            presentation.React(new PlayerGuardChangedFact(true, 1, 1));
            presentation.DrawnHeld!.Step(.1F);
            Assert.Equal("FPP_sns_BlockShieldStart", Drawn());
            presentation.DrawnHeld!.Step(.3F);
            Assert.Equal("FPP_sns_BlockShieldIdle", Drawn());

            presentation.React(new PlayerGuardChangedFact(false, 1, 2));
            presentation.DrawnHeld!.Step(.1F);
            Assert.Equal("FPP_sns_BlockShieldStop", Drawn());
            presentation.DrawnHeld!.Step(.3F);
            Assert.Equal(idle, Drawn());
        }
    }

    [Fact]
    public void A_shield_with_no_entry_leaves_the_weapon_as_it_was()
    {
        DaggerfallHeldArt art = Read(Document());
        DaggerfallHeldDrawing dagger = art.Drawing("weapon.dagger.steel", 113, DaggerfallHeldDrawing.Steel)!;

        Assert.Equal(dagger, art.WithShield(dagger, 999, DaggerfallHeldDrawing.Steel));
        Assert.Equal("sns", art.WithShield(dagger, 109, DaggerfallHeldDrawing.Steel).Family);
    }

    [Fact]
    public void A_cast_with_the_weapon_sheathed_brings_up_the_bare_arms_for_the_cast_alone()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, _, _) = Viewing(releases);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: false);
            Assert.Null(presentation.DrawnHeld);

            presentation.React(new PlayerCastMotionFact(0, new(.3d, .02d, .3d), 1, 1));

            Assert.Equal("unarmed", presentation.DrawnHeld!.Drawing.Family);
            Assert.Equal(DaggerfallAttackIds.Cast, presentation.DrawnHeld!.Playing);

            presentation.React(new PlayerAttackRestFact(1, 2));

            Assert.Null(presentation.DrawnHeld);
        }
    }

    [Fact]
    public void A_weapon_drawn_during_a_sheathed_cast_takes_over_from_its_arms()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, _, _) = Viewing(releases);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: false);
            presentation.React(new PlayerCastMotionFact(0, new(.3d, .02d, .3d), 1, 1));

            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);

            Assert.Equal("sns", presentation.DrawnHeld!.Drawing.Family);
        }
    }

    [Fact]
    public void A_cast_cut_short_takes_its_element_off_the_palm()
    {
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, AppearanceFake appearance, _) = Viewing(releases);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            presentation.Publish(EmptyActors());
            int arms = appearance.Snapshots.Last().Count(fact => fact.Layer == RenderLayer.Viewmodel && fact.Visible);
            presentation.React(new PlayerCastMotionFact(0, new(.3d, .02d, .3d), 1, 1));

            presentation.React(new PlayerAttackRestFact(1, 2));

            presentation.Publish(EmptyActors());
            Assert.Equal(arms, appearance.Snapshots.Last().Count(fact => fact.Layer == RenderLayer.Viewmodel && fact.Visible));
        }
    }

    [Fact]
    public void A_cast_with_no_clip_rests_the_held_arms_it_cuts_into()
    {
        JsonObject document = Document();
        document["families"]!["sns"]!["strikes"]!.AsObject().Remove("cast");
        document["families"]!["sns"]!.AsObject().Remove("leftHand");
        List<string> releases = [];
        (DaggerfallSiteAppearance presentation, _, _) = Viewing(releases, document);
        using (presentation)
        {
            presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
            presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 1, 1));
            Assert.Equal("fastA", presentation.DrawnHeld!.Playing);

            presentation.React(new PlayerCastMotionFact(0, new(.3d, .02d, .3d), 1, 2));

            Assert.Null(presentation.DrawnHeld!.Playing);
        }
    }

    // A site drawing the authored art through arms whose model has the rig's joints and every clip, whatever the hands
    // wield standing for a longsword (template 120).
    private static (DaggerfallSiteAppearance Presentation, AppearanceFake Appearance, EngineContextFake Engine) Viewing(List<string> releases, JsonObject? document = null,
        Func<WorldRpg.Kit.Inventory.EquipmentRead, (int Template, string Metal)?>? shieldOf = null, Func<string>? arms = null, Func<DaggerfallRacialKind?>? beast = null, int template = 120)
    {
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        content.Add("weapon/claws.png", Hash);
        content.Add("effect/fire-missile.png", Hash);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(Hash, releases).Service, appearance, PerceptionFake.Create().Service);
        DaggerfallHeldArt art = Read(document ?? Document());
        engine.Animation.Joints = Joints(art.Document.Rig);
        engine.Animation.Clips = [.. art.Document.Families.Values.SelectMany(family => family.Strikes.Values.Select(clip => clip.Clip).Append(family.Idle.Clip).Concat(family.PowerHold is { } hold ? [hold.Clip] : []).Concat(family.Guard is { } guard ? GuardClips(guard) : [])).Concat(GuardClips(art.Document.Shield!.Guard)).Append("draw").Distinct()
            .Select(clip => new AnimationClipInfo(clip, clip, 1, true, 0))];
        NormalizedClassicPresentation classic = ClassicWeapon() with { Effects = [.. ClassicEffects(fireSpell: true).Effects.Where(effect => effect.Name == "fireMissile")], UnarmedVisual = "weapon.unarmed" };
        classic = classic with { Weapons = new Dictionary<string, NormalizedClassicWeapon>(classic.Weapons) { ["weapon.werecreature"] = classic.Weapons["weapon.dagger.steel"] with { ResourceId = "weapon.werecreature", TexturePath = "weapon/claws.png" } } };
        DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: classic));
        if (beast is not null) presentation.UsePlayerBeastForm(beast);
        presentation.UseHeldArt(engine.Context, art, DaggerfallViewmodelStyle.Held, new Dictionary<string, DaggerfallDyeTint>(), _ => (template, DaggerfallHeldDrawing.Steel), shieldOf ?? (_ => null), _ => arms?.Invoke() ?? "male-leather");
        return (presentation, appearance, engine);
    }

    private static string[] GuardClips(DaggerfallHeldGuard guard) => [guard.Raise.Clip, guard.Hold.Clip, guard.Lower.Clip];

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

    // Both models of every arm set.
    private static string[] ArmModels() => Document()["armSets"]!.AsObject()
        .SelectMany(set => new[] { set.Value!["model"]!.GetValue<string>(), set.Value!["retroModel"]!.GetValue<string>() }).ToArray();

    // The definition read over a container that carries the given models (by default what scripts/fp-arms/build.sh
    // packs: every arm set and the committed weapon models).
    internal static DaggerfallHeldArt Read(JsonObject document, string[]? models = null)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallHeldArt.ContainerName);
        foreach (string model in models ?? [.. ArmModels(), .. Weapons()])
            service.Add(DaggerfallHeldArt.ContainerName, model, [7]);
        ProductContentBundle container = new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallHeldArt.ContainerName);
        return DaggerfallHeldArt.Read(container, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
    }
}
