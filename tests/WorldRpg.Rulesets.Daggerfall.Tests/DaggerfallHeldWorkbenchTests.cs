using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;
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
        DaggerfallHeldWorkbench workbench = new(null, DaggerfallViewmodelStyle.Held, Unreached, () => { });

        foreach (DebugCommandResult result in new[] { workbench.Show("weapon.dagger.steel", "strikeDown"), workbench.Key(0), workbench.Step(1), workbench.Play(1), workbench.At(0), workbench.Read() })
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
        DaggerfallHeldWorkbench workbench = new(DaggerfallHeldArtTests.Read(DaggerfallHeldArtTests.Document()), DaggerfallViewmodelStyle.Classic, Unreached, () => { });

        Assert.Contains("viewmodel.style is classic", workbench.Page().Notice, StringComparison.Ordinal);
        Assert.Contains("held or heldRetro", workbench.Show("weapon.dagger.steel", "strikeDown").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refusals_name_the_looks_and_strikes_that_can_be_shown_before_anything_is_drawn()
    {
        JsonObject authored = DaggerfallHeldArtTests.Document();
        authored["families"]!["dagger"]!["strikes"]!.AsObject().Remove("strikeLeft");
        DaggerfallHeldWorkbench workbench = new(DaggerfallHeldArtTests.Read(authored), DaggerfallViewmodelStyle.HeldRetro, Unreached, () => { });

        Assert.Equal(["weapon.dagger.steel", "weapon.longblade", "weapon.axe", "weapon.mace", "weapon.flail", "weapon.warhammer", "weapon.staff"], workbench.Looks);
        Assert.DoesNotContain("strikeLeft", workbench.Strikes);
        Assert.Contains("Looks: weapon.dagger.steel, weapon.longblade, weapon.axe", workbench.Show("weapon.bow", "strikeDown").Message, StringComparison.Ordinal);
        DebugCommandResult noClip = workbench.Show("weapon.dagger.steel", "strikeLeft");
        Assert.False(noClip.Succeeded);
        Assert.Contains("Strikes with clips: strikeDown, strikeDownLeft, strikeRight", noClip.Message, StringComparison.Ordinal);
        foreach (DebugCommandResult unshown in new[] { workbench.Key(0), workbench.Step(-1), workbench.Play(.25F), workbench.At(.1F) })
            Assert.Equal("Choose a look first: held.motion <look> <strike>.", unshown.Message);
        Assert.Equal("Choose a look first.", workbench.Choose("strike", "strikeDown"));
        Assert.Equal((null, null, -1), workbench.Viewing);
    }

    [Fact]
    public void A_strike_is_timed_to_its_classic_frames_with_the_hit_as_the_commit_starts()
    {
        PhaseTiming timing = DaggerfallHeldViewmodel.Timing(.1F, 2, 5);

        Assert.Equal(.2F, timing.Windup, 5);
        Assert.Equal(.2F, timing.Commit, 5);
        Assert.Equal(.1F, timing.Recovery, 5);
        // A strike whose hit is its last frame still commits for a frame.
        Assert.Equal(.1F, DaggerfallHeldViewmodel.Timing(.1F, 4, 5).Commit, 5);
    }

    [Fact]
    public void Every_page_value_is_bound_to_where_the_held_definition_holds_it_and_matches_it_as_loaded()
    {
        JsonObject authored = DaggerfallHeldArtTests.Document();
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(authored);
        PhaseTiming timing = DaggerfallHeldViewmodel.Timing(.1F, 2, 5);

        foreach (var (family, strike) in new[] { ("dagger", "strikeDown"), ("longsword", "strikeUp"), ("halberd", "strikeLeft") })
            foreach (TunableControl control in DaggerfallHeldTunables.For(art.Document, family, strike, timing, new HashSet<string>(), () => { })
                .SelectMany(group => group.Tunables).Select(tunable => tunable.Control))
            {
                TunableSource source = Assert.IsType<TunableSource>(control.Source);
                Assert.Equal(DaggerfallHeldArt.DocumentPath, source.Path);
                JsonNode saved = Assert.IsAssignableFrom<JsonNode>(Resolve(authored, source.Pointer));
                Assert.True(Math.Abs(control.Value - saved.GetValue<float>()) <= control.Step / 2, $"{control.Id} ({source.Pointer}) is {control.Value} loaded but {saved} authored.");
            }
    }

    [Fact]
    public void A_value_set_on_the_page_changes_the_loaded_art_and_redraws()
    {
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(DaggerfallHeldArtTests.Document());
        int redraws = 0;
        List<TunableGroup> groups = DaggerfallHeldTunables.For(art.Document, "dagger", "strikeDown", DaggerfallHeldViewmodel.Timing(.1F, 2, 5), new HashSet<string>(), () => redraws++);
        Tunable Control(string id) => groups.SelectMany(group => group.Tunables).Single(tunable => tunable.Control.Id == id);
        DaggerfallHeldFamily dagger = art.Document.Families["dagger"];

        Control("clip.dagger.strikeDown.strike").Set(.2F);
        Control("clip.dagger.strikeDown.fade").Set(.25F);
        Control("look.hold.y").Set(-80);
        Control("idle.seconds").Set(3);
        Control("arms.shoulder.x").Set(.2F);

        Assert.Equal(.2F, dagger.Strikes["strikeDown"].Marks.Strike);
        Assert.Equal(.25F, dagger.Strikes["strikeDown"].Fade);
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
        PhaseTiming timing = DaggerfallHeldViewmodel.Timing(.1F, 2, 5);
        // The workbench's page as it shows one strike, then another; the first one's marks stay bound once edited.
        HashSet<string> edited = [];
        string strike = "strikeDown";
        TunablesPage Page() => new(DaggerfallHeldWorkbench.Title, DaggerfallHeldTunables.For(art.Document, "dagger", strike, timing, edited, () => { }));
        Source source = new(authored.ToJsonString());
        TunablesDebugModule module = new(source, Page, (_, _) => null);

        Assert.True(module.Set("clip.dagger.strikeDown.strike", .15F).Succeeded);
        strike = "strikeLeft";
        Assert.True(module.Set("clip.dagger.strikeLeft.recover", .5F).Succeeded);
        strike = "strikeUp";

        string diff = module.Diff().Message;
        string sha = JsonNode.Parse(diff)!["files"]![0]!["sha256"]!.GetValue<string>();
        Assert.True(module.Save(DaggerfallHeldArt.DocumentPath, sha).Succeeded);
        JsonNode saved = source.Saved();
        Assert.Equal(.15F, Resolve(saved, "/families/dagger/strikes/strikeDown/marks/strike")!.GetValue<float>(), 4);
        Assert.Equal(.5F, Resolve(saved, "/families/dagger/strikes/strikeLeft/marks/recover")!.GetValue<float>(), 4);
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
