using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer first-person workbench's answers: why nothing can be viewed without the art or under the classic
/// style, the refusals that name what can be shown, and the page's values, each bound to the authored held definition
/// where Save writes it and applied to the loaded art. Drawing the viewed arms needs the Engine's animation services
/// and the container's models, which window runs and scripts/fp-arms/sheet.sh cover.
/// </summary>
public sealed class DaggerfallHeldWorkbenchTests
{
    [Fact]
    public void Without_the_first_person_container_every_command_and_the_page_say_how_to_build_it()
    {
        DaggerfallHeldWorkbench workbench = new(null, DaggerfallAttackTuning.Default, DaggerfallViewmodelStyle.Held, Unreached, () => { });

        foreach (DebugCommandResult result in new[] { workbench.Show("weapon.dagger.steel", "fastA"), workbench.Key(0), workbench.Step(1), workbench.Play(1), workbench.At(0), workbench.Read() })
        {
            Assert.False(result.Succeeded);
            Assert.Contains("first-person.rpak is not installed", result.Message, StringComparison.Ordinal);
            Assert.Contains("scripts/fp-arms/build.sh", result.Message, StringComparison.Ordinal);
        }
        TunablesPage page = workbench.Page();
        Assert.Empty(page.Groups);
        Assert.Contains("first-person.rpak is not installed", page.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void The_classic_style_draws_sprites_so_the_page_asks_for_a_held_style()
    {
        DaggerfallHeldWorkbench workbench = new(DaggerfallHeldArtTests.Read(DaggerfallHeldArtTests.Document()), DaggerfallAttackTuning.Default, DaggerfallViewmodelStyle.Classic, Unreached, () => { });

        Assert.Contains("viewmodel.style is classic", workbench.Page().Notice, StringComparison.Ordinal);
        Assert.Contains("held or heldRetro", workbench.Show("weapon.dagger.steel", "fastA").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refusals_name_the_looks_and_strikes_that_can_be_shown_before_anything_is_drawn()
    {
        JsonObject authored = DaggerfallHeldArtTests.Document();
        authored["families"]!["dagger"]!["strikes"]!.AsObject().Remove("fastC");
        foreach (string item in new[] { "113", "114" }) authored["items"]!.AsObject().Remove(item); // an item's family must have every strike
        DaggerfallHeldWorkbench workbench = new(DaggerfallHeldArtTests.Read(authored), DaggerfallAttackTuning.Default, DaggerfallViewmodelStyle.HeldRetro, Unreached, () => { });

        Assert.Equal(["weapon.dagger.steel", "weapon.longblade", "weapon.axe", "weapon.flail", "weapon.warhammer", "weapon.mace", "weapon.staff"], workbench.Looks.Take(7));
        Assert.Contains("item.120", workbench.Looks);
        Assert.DoesNotContain("fastC", workbench.Strikes);
        Assert.Contains("Looks: weapon.dagger.steel, weapon.longblade, weapon.axe", workbench.Show("weapon.bow", "fastA").Message, StringComparison.Ordinal);
        DebugCommandResult noClip = workbench.Show("weapon.dagger.steel", "fastC");
        Assert.False(noClip.Succeeded);
        Assert.Contains("Strikes with clips: fastA, fastB, fastD", noClip.Message, StringComparison.Ordinal);
        foreach (DebugCommandResult unshown in new[] { workbench.Key(0), workbench.Step(-1), workbench.Play(.25F), workbench.At(.1F) })
            Assert.Equal("Choose a look first: held.motion <look> <strike>.", unshown.Message);
        Assert.Equal("Choose a look first.", workbench.Choose("strike", "fastA"));
        Assert.Equal((null, null, -1), workbench.Viewing);
    }

    [Fact]
    public void Every_page_value_is_bound_to_where_the_held_definition_holds_it_and_matches_it_as_loaded()
    {
        JsonObject authored = DaggerfallHeldArtTests.Document();
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(authored);
        PhaseTiming timing = new PhaseTiming(.2F, .2F, .1F);

        foreach (var (family, strike) in new[] { ("dagger", "fastA"), ("longsword", "fastB"), ("sns", "fastD"), ("halberd", "fastC") })
            foreach (TunableControl control in DaggerfallHeldTunables.For(art.Document, new(family, null, DaggerfallHeldDrawing.Steel), strike, timing, new HashSet<string>(), () => { })
                .SelectMany(group => group.Tunables).Select(tunable => tunable.Control))
            {
                TunableSource source = Assert.IsType<TunableSource>(control.Source);
                Assert.Equal(DaggerfallHeldArt.DocumentPath, source.Path);
                JsonNode saved = Assert.IsAssignableFrom<JsonNode>(Resolve(authored, source.Pointer));
                Assert.True(Math.Abs(control.Value - saved.GetValue<float>()) <= control.Step / 2, $"{control.Id} ({source.Pointer}) is {control.Value} loaded but {saved} authored.");
            }
    }

    [Fact]
    public void An_arm_set_needs_a_look_shown_and_a_set_the_art_has()
    {
        DaggerfallHeldWorkbench workbench = new(DaggerfallHeldArtTests.Read(DaggerfallHeldArtTests.Document()), DaggerfallAttackTuning.Default, DaggerfallViewmodelStyle.Held, Unreached, () => { });

        Assert.Contains("Choose a look first", workbench.Arms("female-cloth").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_set_on_the_page_changes_the_loaded_art_and_redraws()
    {
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(DaggerfallHeldArtTests.Document());
        int redraws = 0;
        List<TunableGroup> groups = DaggerfallHeldTunables.For(art.Document, new("dagger", null, DaggerfallHeldDrawing.Steel), "fastA", new PhaseTiming(.2F, .2F, .1F), new HashSet<string>(), () => redraws++);
        Tunable Control(string id) => groups.SelectMany(group => group.Tunables).Single(tunable => tunable.Control.Id == id);
        DaggerfallHeldFamily dagger = art.Document.Families["dagger"];

        Control("clip.dagger.fastA.strike").Set(.2F);
        Control("clip.dagger.fastA.fade").Set(.25F);
        Control("look.dagger.hold.y").Set(-80);
        Control("look.dagger.idle").Set(3);
        Control("arms.shoulder.x").Set(.2F);

        Assert.Equal(.2F, dagger.Strikes["fastA"].Marks.Strike);
        Assert.Equal(.25F, dagger.Strikes["fastA"].Fade);
        Assert.Equal(-80, dagger.Look.Hold.Y);
        Assert.Equal(3, dagger.Idle.Seconds);
        Assert.Equal(.2F, art.Document.Arms.Shoulder.X);
        Assert.Equal(5, redraws);
    }

    [Fact]
    public void Marks_set_on_two_strikes_are_still_saved_after_the_viewer_moves_to_another()
    {
        JsonObject authored = DaggerfallHeldArtTests.Document();
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(authored);
        PhaseTiming timing = new PhaseTiming(.2F, .2F, .1F);
        // The workbench's page as it shows one strike, then another; the first one's marks stay bound once edited.
        HashSet<string> edited = [];
        string strike = "fastA";
        TunablesPage Page() => new(DaggerfallHeldWorkbench.Title, DaggerfallHeldTunables.For(art.Document, new("dagger", null, DaggerfallHeldDrawing.Steel), strike, timing, edited, () => { }));
        Source source = new(authored.ToJsonString());
        TunablesDebugModule module = new(source, Page, (_, _) => null);

        Assert.True(module.Set("clip.dagger.fastA.strike", .15F).Succeeded);
        strike = "fastC";
        Assert.True(module.Set("clip.dagger.fastC.recover", .5F).Succeeded);
        strike = "fastB";

        string diff = module.Diff().Message;
        string sha = JsonNode.Parse(diff)!["files"]![0]!["sha256"]!.GetValue<string>();
        Assert.True(module.Save(DaggerfallHeldArt.DocumentPath, sha).Succeeded);
        JsonNode saved = source.Saved();
        Assert.Equal(.15F, Resolve(saved, "/families/dagger/strikes/fastA/marks/strike")!.GetValue<float>(), 4);
        Assert.Equal(.5F, Resolve(saved, "/families/dagger/strikes/fastC/marks/recover")!.GetValue<float>(), 4);
    }

    [Fact]
    public void A_look_and_idle_set_on_one_family_are_still_saved_after_the_viewer_moves_to_another()
    {
        JsonObject authored = DaggerfallHeldArtTests.Document();
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(authored);
        PhaseTiming timing = new PhaseTiming(.2F, .2F, .1F);
        HashSet<string> edited = [];
        string family = "dagger";
        TunablesPage Page() => new(DaggerfallHeldWorkbench.Title, DaggerfallHeldTunables.For(art.Document, new(family, null, DaggerfallHeldDrawing.Steel), "fastA", timing, edited, () => { }));
        Source source = new(authored.ToJsonString());
        TunablesDebugModule module = new(source, Page, (_, _) => null);

        Assert.True(module.Set("look.dagger.idle", 3).Succeeded);
        Assert.True(module.Set("look.dagger.grip.x", .05F).Succeeded);
        family = "longsword";
        Assert.True(module.Set("look.longsword.hold.y", -80).Succeeded);
        family = "halberd";

        string diff = module.Diff().Message;
        string sha = JsonNode.Parse(diff)!["files"]![0]!["sha256"]!.GetValue<string>();
        Assert.True(module.Save(DaggerfallHeldArt.DocumentPath, sha).Succeeded);
        JsonNode saved = source.Saved();
        Assert.Equal(3, Resolve(saved, "/families/dagger/idle/seconds")!.GetValue<float>(), 4);
        Assert.Equal(.05F, Resolve(saved, "/families/dagger/look/grip/0")!.GetValue<float>(), 4);
        Assert.Equal(-80, Resolve(saved, "/families/longsword/look/hold/1")!.GetValue<float>(), 2);
    }

    [Fact]
    public void An_items_own_grip_and_hold_are_on_the_page_when_it_is_viewed_and_still_saved_after_it_is_left()
    {
        JsonObject authored = DaggerfallHeldArtTests.Document();
        authored["items"]!["120"]!["grip"] = new JsonArray(0.01, 0.02, 0.03);
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(authored);
        PhaseTiming timing = new PhaseTiming(.2F, .2F, .1F);
        HashSet<string> edited = [];
        DaggerfallHeldDrawing shown = art.Look("item.120")!;
        TunablesPage Page() => new(DaggerfallHeldWorkbench.Title, DaggerfallHeldTunables.For(art.Document, shown, "fastA", timing, edited, () => { }));
        Source source = new(authored.ToJsonString());
        TunablesDebugModule module = new(source, Page, (_, _) => null);

        Assert.Equal("sns", shown.Family);
        Assert.True(module.Set("item.120.grip.y", .05F).Succeeded);
        Assert.Equal(.05F, art.Document.Items[120].Grip!.Value.Y);
        shown = art.Look("weapon.dagger.steel")!;

        string sha = JsonNode.Parse(module.Diff().Message)!["files"]![0]!["sha256"]!.GetValue<string>();
        Assert.True(module.Save(DaggerfallHeldArt.DocumentPath, sha).Succeeded);
        Assert.Equal(.05F, Resolve(source.Saved(), "/items/120/grip/1")!.GetValue<float>(), 4);
    }

    // The developer content source over one held definition: reads it and applies the RFC 6902 replacements Save sends.
    private sealed class Source(string json) : IContentSourceService
    {
        private readonly List<JsonArray> edits = [];

        public ReadOnlyMemory<byte> Read(ContentSourceReadRequest request) =>
            request.Path == DaggerfallHeldArt.DocumentPath ? System.Text.Encoding.UTF8.GetBytes(json) : throw new FileNotFoundException(request.Path);

        public ContentSha256 Edit(ContentSourceEditRequest request)
        {
            edits.Add(JsonNode.Parse(request.Edits)!.AsArray());
            return default;
        }

        internal JsonNode Saved()
        {
            JsonNode document = JsonNode.Parse(json)!;
            foreach (JsonNode? edit in edits.SelectMany(batch => batch))
            {
                Assert.Equal("replace", edit!["op"]!.GetValue<string>());
                string pointer = edit["path"]!.GetValue<string>();
                int split = pointer.LastIndexOf('/');
                JsonNode parent = Resolve(document, pointer[..split])!;
                string token = pointer[(split + 1)..];
                if (parent is JsonArray items) items[int.Parse(token)] = edit["value"]!.DeepClone();
                else parent[token] = edit["value"]!.DeepClone();
            }
            return document;
        }
    }

    // The workbench refuses before it reaches the site's appearance.
    private static DaggerfallSiteAppearance Unreached() => throw new InvalidOperationException("The workbench drew before refusing.");

    // The node an RFC 6901 pointer names, or null.
    private static JsonNode? Resolve(JsonNode root, string pointer)
    {
        JsonNode? node = root;
        foreach (string token in pointer.Split('/').Skip(1).Select(token => token.Replace("~1", "/").Replace("~0", "~")))
            node = node switch
            {
                JsonObject members => members[token],
                JsonArray items when int.TryParse(token, out int index) && index < items.Count => items[index],
                _ => null,
            };
        return node;
    }
}
