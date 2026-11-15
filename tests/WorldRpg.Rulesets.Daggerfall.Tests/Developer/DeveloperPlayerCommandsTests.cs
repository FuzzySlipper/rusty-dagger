using WorldRpg.Kit;
using Rusty.Engine.Debugging;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
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

        DebugCommandResult held = session.Commands<IDeveloperPlayerCommands>().Hold("Steel Dagger");

        Assert.True(held.Succeeded, held.Message);
        Assert.Contains("Steel Dagger", held.Message, StringComparison.Ordinal);
        Assert.True(session.State.WeaponDrawn);
        var hand = Assert.Single(session.State.Equipment.Read().Assignments, assignment => assignment.Slot.Value == "right-hand");
        Assert.Equal("template-113-steel", hand.Item.Definition.Value);
    }

    [Fact]
    public void Hold_magic_wields_an_enchanted_weapon_drawn()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        session.State.WeaponDrawn = false;

        DebugCommandResult held = session.Commands<IDeveloperPlayerCommands>().HoldMagic();

        Assert.True(held.Succeeded, held.Message);
        Assert.True(session.State.WeaponDrawn);
        var hand = Assert.Single(session.State.Equipment.Read().Assignments, assignment => assignment.Slot.Value == "right-hand");
        Assert.Contains("magic-item", hand.Item.Definition.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Equip_goes_through_the_ordinary_rules_and_tries_a_shield_on_the_off_arm()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;

        // The fixture's class forbids shields, so the rules refuse it by name and it stays in the pack.
        DebugCommandResult refused = session.Commands<IDeveloperPlayerCommands>().Equip("steel 110");

        Assert.False(refused.Succeeded);
        Assert.Contains("cannot use round shields", refused.Message, StringComparison.Ordinal);
        Assert.Contains(session.State.Inventory.Read().UniqueItems, item => item.Definition.Value.StartsWith("template-110", StringComparison.Ordinal));
        Assert.False(session.Commands<IDeveloperPlayerCommands>().Equip("no such item").Succeeded);
    }

    [Fact]
    public void Spell_teaches_a_named_spell_once_and_refuses_what_it_cannot_tell_apart()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        var spell = TestPayload.Definitions.Magic.Spells.First(pair => !session.State.Character.KnownSpells.Contains(pair.Key));

        Assert.True(session.Commands<IDeveloperPlayerCommands>().Spell(spell.Value.Name).Succeeded);
        Assert.Contains(spell.Key, session.State.Character.KnownSpells);
        Assert.Contains("already known", session.Commands<IDeveloperPlayerCommands>().Spell(spell.Value.Name).Message, StringComparison.Ordinal);
        Assert.False(session.Commands<IDeveloperPlayerCommands>().Spell("no such spell").Succeeded);
        Assert.Contains("several spells", session.Commands<IDeveloperPlayerCommands>().Spell("a").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ride_gives_and_mounts_a_horse_and_puts_the_weapon_away_and_refuses_what_is_no_mount()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        session.State.WeaponDrawn = true;

        Assert.Contains("is no mount", session.Commands<IDeveloperPlayerCommands>().Ride("dragon").Message, StringComparison.Ordinal);
        Assert.Contains("is no mount", session.Commands<IDeveloperPlayerCommands>().Ride("ship").Message, StringComparison.Ordinal);

        DebugCommandResult mounted = session.Commands<IDeveloperPlayerCommands>().Ride("horse");
        Assert.True(mounted.Succeeded, mounted.Message);
        Assert.Equal(DaggerfallTransportMode.Horse, session.State.Transport.Mode);
        Assert.False(session.State.WeaponDrawn);

        Assert.True(session.Commands<IDeveloperPlayerCommands>().Ride("foot").Succeeded);
        Assert.True(session.State.Transport.IsOnFoot);
    }

    [Fact]
    public void A_horse_takes_the_step_along_its_own_heading_and_holds_the_view_in_range()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        float start = session.State.PlayerControl.YawRadians;
        Assert.True(session.Commands<IDeveloperPlayerCommands>().Ride("horse").Succeeded);
        run.Step();

        session.State.PlayerControl.YawRadians = start + 2f;
        run.Step();

        float heading = run.Spatial.StepRequests[^1].Command.HeadingYawRadians;
        Assert.InRange(MathF.Abs(MathF.IEEERemainder(heading - start, MathF.Tau)), 0f, .2f);
        float past = MathF.IEEERemainder(session.State.PlayerControl.YawRadians - heading, MathF.Tau);
        Assert.Equal(DaggerfallTuning.Defaults.Transport.HorseRide.LookRangeDegrees * MathF.PI / 180f, past, .001f);
        Assert.Contains("Horse at 0.0 m/s", session.Commands<IDeveloperPlayerCommands>().RideState().Message, StringComparison.Ordinal);

        Assert.True(session.Commands<IDeveloperPlayerCommands>().Ride("foot").Succeeded);
        Assert.Contains("rides nothing", session.Commands<IDeveloperPlayerCommands>().RideState().Message, StringComparison.Ordinal);
        run.Step();
        Assert.Equal(session.State.PlayerControl.YawRadians, run.Spatial.StepRequests[^1].Command.HeadingYawRadians);
    }

    [Fact]
    public void Morph_curses_and_changes_the_player_and_changes_them_back()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;

        Assert.Contains("no beast form", session.Commands<IDeveloperPlayerCommands>().Morph("").Message, StringComparison.Ordinal);
        Assert.Contains("no beast form", session.Commands<IDeveloperPlayerCommands>().Morph("vampire").Message, StringComparison.Ordinal);

        DebugCommandResult changed = session.Commands<IDeveloperPlayerCommands>().Morph("wereboar");
        Assert.True(changed.Succeeded, changed.Message);
        Assert.Equal(DaggerfallRacialKind.Wereboar, session.State.RacialOverrides.Current!.State.Kind);
        Assert.True(session.State.RacialOverrides.Current!.State.BeastForm);
        Assert.Contains("already a wereboar", session.Commands<IDeveloperPlayerCommands>().Morph("werewolf").Message, StringComparison.Ordinal);

        Assert.True(session.Commands<IDeveloperPlayerCommands>().Morph("").Succeeded);
        Assert.False(session.State.RacialOverrides.Current!.State.BeastForm);
    }

    [Fact]
    public void Give_makes_items_by_label_template_name_or_index_and_refuses_what_it_cannot_name()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        int Count(string definition) => session.State.Inventory.Read().UniqueItems.Count(item => item.Definition.Value == definition);

        Assert.True(session.Commands<IDeveloperPlayerCommands>().Give("ebony dagger", 3).Succeeded);
        Assert.Equal(3, Count("template-113-ebony"));
        Assert.True(session.Commands<IDeveloperPlayerCommands>().Give("113", 1).Succeeded);
        DebugCommandResult arrows = session.Commands<IDeveloperPlayerCommands>().Give("Arrow", 20);
        Assert.True(arrows.Succeeded, arrows.Message);
        Assert.Contains("×20", arrows.Message, StringComparison.Ordinal);

        Assert.Contains("No item is named", session.Commands<IDeveloperPlayerCommands>().Give("Moonblade", 1).Message, StringComparison.Ordinal);
        Assert.Contains("takes no material", session.Commands<IDeveloperPlayerCommands>().Give("leather Dagger", 1).Message, StringComparison.Ordinal);
        // The factory's own refusals are reported, not thrown: arrows are weapons but take no material.
        Assert.Contains("cannot be made", session.Commands<IDeveloperPlayerCommands>().Give("ebony Arrow", 1).Message, StringComparison.Ordinal);
        Assert.False(session.Commands<IDeveloperPlayerCommands>().Give("Dagger", 0).Succeeded);
        Assert.Contains("113 Dagger", session.Commands<IDeveloperPlayerCommands>().Items("dagger").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gold_skill_level_and_heal_go_through_currency_progression_and_vitals()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;

        ulong gold = session.State.Currency.Read().Gold;
        Assert.True(session.Commands<IDeveloperPlayerCommands>().Money(500).Succeeded);
        Assert.Equal(gold + 500, session.State.Currency.Read().Gold);

        Assert.True(session.Commands<IDeveloperPlayerCommands>().Skill(DaggerfallSkills.ShortBlade, 150).Succeeded);
        Assert.Equal(100, session.State.SkillUses.PermanentSkillValue(DaggerfallSkills.ShortBlade));
        Assert.Contains("Unknown skill", session.Commands<IDeveloperPlayerCommands>().Skill("juggling", 5).Message, StringComparison.Ordinal);

        foreach (var (_, track) in session.State.Actors.Player.Stats.Tracks) track.SetCurrent(1, clamp: true);
        Assert.True(session.Commands<IDeveloperPlayerCommands>().Heal().Succeeded);
        Assert.All(session.State.Actors.Player.Stats.Tracks, pair => Assert.Equal(pair.Value.Maximum.Value, pair.Value.Current));

        int level = session.State.Progression.Level;
        DebugCommandResult levelled = session.Commands<IDeveloperPlayerCommands>().LevelUp();
        Assert.True(levelled.Succeeded, levelled.Message);
        Assert.NotNull(session.State.LevelUps.Read());
        Assert.True(session.State.SkillUses.CalculatedPlayerLevel > level);
        // The open level-up holds further developer changes until it is allocated.
        Assert.False(session.Commands<IDeveloperPlayerCommands>().LevelUp().Succeeded);

    }
}
