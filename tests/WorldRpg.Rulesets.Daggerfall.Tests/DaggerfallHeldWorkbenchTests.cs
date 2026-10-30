using System.Text.Json.Nodes;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer first-person workbench's answers: why nothing can be viewed without the art or under the classic
/// style, the refusals that name what can be shown, and the page's values, each bound to the authored held definition
/// where Save writes it and applied to the loaded art. Drawing the viewed weapon needs the Engine's animation and tween
/// services and the container's models, which window runs cover.
/// </summary>
public sealed class DaggerfallHeldWorkbenchTests
{
    [Fact]
    public void Without_the_first_person_container_every_command_and_the_page_say_how_to_build_it()
    {
        DaggerfallHeldWorkbench workbench = new(null, DaggerfallViewmodelStyle.Held, Unreached, () => { });

        foreach (DebugCommandResult result in new[] { workbench.Show("weapon.dagger.steel", "strikeDown"), workbench.Key(0), workbench.Step(1), workbench.Play(1), workbench.At(0) })
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
    public void Refusals_name_the_looks_strikes_and_hands_that_can_be_shown_before_anything_is_drawn()
    {
        DaggerfallHeldWorkbench workbench = new(DaggerfallHeldArtTests.Read(DaggerfallHeldArtTests.Document()), DaggerfallViewmodelStyle.HeldRetro, Unreached, () => { });

        Assert.Equal(["strikeDown"], workbench.Strikes);
        Assert.Contains("Looks: weapon.dagger.steel", workbench.Show("weapon.sword.steel", "strikeDown").Message, StringComparison.Ordinal);
        DebugCommandResult noMotion = workbench.Show("weapon.dagger.steel", "strikeLeft");
        Assert.False(noMotion.Succeeded);
        Assert.Contains("Strikes with motion: strikeDown", noMotion.Message, StringComparison.Ordinal);
        Assert.Equal("The hand is main or off.", workbench.Show("weapon.dagger.steel", "strikeDown", "left").Message);
        foreach (DebugCommandResult unshown in new[] { workbench.Key(0), workbench.Step(-1), workbench.Play(.25F), workbench.At(.1F), workbench.Hand("off") })
            Assert.Equal("Choose a look first: held.motion <look> <strike>.", unshown.Message);
        Assert.Equal("Choose a look first.", workbench.Choose("strike", "strikeDown"));
        Assert.Equal((null, null, false, -1), workbench.Viewing);
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
        MotionKey[] keys = art.Document.Motion.Tracks["strikeDown"];

        for (int key = -1; key < keys.Length; key++)
        {
            List<TunableGroup> groups = DaggerfallHeldTunables.For(art.Document, "strikeDown", timing, key, () => { });
            Assert.Equal(key >= 0, groups.Any(group => group.Id.StartsWith("pose.", StringComparison.Ordinal)));
            foreach (TunableControl control in groups.SelectMany(group => group.Tunables).Select(tunable => tunable.Control))
            {
                TunableSource source = Assert.IsType<TunableSource>(control.Source);
                Assert.Equal(DaggerfallHeldArt.DocumentPath, source.Path);
                JsonNode saved = Assert.IsAssignableFrom<JsonNode>(Resolve(authored, source.Pointer));
                float value = control.Options is { } options ? options.ToList().IndexOf(saved.GetValue<string>()) : saved.GetValue<float>();
                Assert.True(Math.Abs(control.Value - value) <= control.Step / 2, $"{control.Id} ({source.Pointer}) is {control.Value} loaded but {saved} authored.");
            }
        }
    }

    [Fact]
    public void A_value_set_on_the_page_changes_the_loaded_art_and_redraws()
    {
        DaggerfallHeldArt art = DaggerfallHeldArtTests.Read(DaggerfallHeldArtTests.Document());
        int redraws = 0;
        List<TunableGroup> groups = DaggerfallHeldTunables.For(art.Document, "strikeDown", DaggerfallHeldViewmodel.Timing(.1F, 2, 5), 0, () => redraws++);
        Tunable Control(string id) => groups.SelectMany(group => group.Tunables).Single(tunable => tunable.Control.Id == id);
        MotionKey first = art.Document.Motion.Tracks["strikeDown"][0];

        Control("stance.grip.x").Set(.2F);
        Control("look.hold.y").Set(-80);
        Control("key.0.phase").Set(Array.IndexOf(Enum.GetValues<MotionPhase>(), MotionPhase.Commit));
        Control("pose.position.y").Set(.05F);

        Assert.Equal(.2F, art.Stance.Grip.X);
        Assert.Equal(-80, art.Document.Look.Hold.Y);
        Assert.Equal(MotionPhase.Commit, art.Document.Motion.Tracks["strikeDown"][0].Phase);
        Assert.Equal(.05F, art.Document.Motion.Poses[first.Pose].Position.Y);
        Assert.Equal(4, redraws);
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
