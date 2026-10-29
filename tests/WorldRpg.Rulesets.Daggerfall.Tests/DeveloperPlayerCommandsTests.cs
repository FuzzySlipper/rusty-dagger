using Rusty.Engine.Debugging;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer player commands give items, gold, skills, a level and vitals through the game's own owners: an item is
/// made by the item factory and held through the equipment rules, gold is received as currency, a skill is set on the
/// permanent stat progression reads, and a level opens the ordinary level-up.
/// </summary>
public sealed class DeveloperPlayerCommandsTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    [Fact]
    public void Hold_wields_a_named_weapon_drawn_in_the_main_hand()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        session.State.WeaponDrawn = false;

        DebugCommandResult held = session.Hold("Steel Dagger");

        Assert.True(held.Succeeded, held.Message);
        Assert.Contains("Steel Dagger", held.Message, StringComparison.Ordinal);
        Assert.True(session.State.WeaponDrawn);
        var hand = Assert.Single(session.State.Equipment.Read().Assignments, assignment => assignment.Slot.Value == "right-hand");
        Assert.Equal("template-113-steel", hand.Item.Definition.Value);
    }

    [Fact]
    public void Give_makes_items_by_label_template_name_or_index_and_refuses_what_it_cannot_name()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        int Count(string definition) => session.State.Inventory.Read().UniqueItems.Count(item => item.Definition.Value == definition);

        Assert.True(session.Give("ebony dagger", 3).Succeeded);
        Assert.Equal(3, Count("template-113-ebony"));
        Assert.True(session.Give("113", 1).Succeeded);
        DebugCommandResult arrows = session.Give("Arrow", 20);
        Assert.True(arrows.Succeeded, arrows.Message);
        Assert.Contains("×20", arrows.Message, StringComparison.Ordinal);

        Assert.Contains("No item is named", session.Give("Moonblade", 1).Message, StringComparison.Ordinal);
        Assert.Contains("takes no material", session.Give("leather Dagger", 1).Message, StringComparison.Ordinal);
        // The factory's own refusals are reported, not thrown: arrows are weapons but take no material.
        Assert.Contains("cannot be made", session.Give("ebony Arrow", 1).Message, StringComparison.Ordinal);
        Assert.False(session.Give("Dagger", 0).Succeeded);
        Assert.Contains("113 Dagger", session.Items("dagger").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gold_skill_level_and_heal_go_through_currency_progression_and_vitals()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;

        ulong gold = session.State.Currency.Read().Gold;
        Assert.True(session.Money(500).Succeeded);
        Assert.Equal(gold + 500, session.State.Currency.Read().Gold);

        Assert.True(session.Skill(DaggerfallSkills.ShortBlade, 150).Succeeded);
        Assert.Equal(100, session.State.SkillUses.PermanentSkillValue(DaggerfallSkills.ShortBlade));
        Assert.Contains("Unknown skill", session.Skill("juggling", 5).Message, StringComparison.Ordinal);

        foreach (var (_, track) in session.State.Actors.Player.Stats.Tracks) track.SetCurrent(1, clamp: true);
        Assert.True(session.Heal().Succeeded);
        Assert.All(session.State.Actors.Player.Stats.Tracks, pair => Assert.Equal(pair.Value.Maximum.Value, pair.Value.Current));

        int level = session.State.Progression.Level;
        DebugCommandResult levelled = session.LevelUp();
        Assert.True(levelled.Succeeded, levelled.Message);
        Assert.NotNull(session.State.LevelUps.Read());
        Assert.True(session.State.SkillUses.CalculatedPlayerLevel > level);
        // The open level-up holds further developer changes until it is allocated.
        Assert.False(session.LevelUp().Succeeded);

    }
}
