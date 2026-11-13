using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The career passives' owner, constructed over a character state without the session.</summary>
public sealed class DaggerfallCareerPassivesTests
{
    private const long Player = 1;

    private sealed class Fixture
    {
        internal readonly DaggerfallCharacterState Character;
        internal readonly StatsComponent Stats;
        internal readonly Dictionary<long, DaggerfallActorDefinition> Actors = [];
        internal readonly DaggerfallCareerPassives Passives;

        internal Fixture(DaggerfallCustomCareerTrait[] advantages, DaggerfallCustomCareerTrait[]? disadvantages = null)
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
            Stats = new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
            Character = new DaggerfallCharacterState(definitions, Stats, player);
            var choices = DaggerfallCustomCareerChoices.Default(definitions, Character.Career) with
            { Name = "Test career", HitPointsPerLevel = 12, Advantages = advantages, Disadvantages = disadvantages ?? [] };
            Character.BeginChoices();
            Character.ReplacePending(new("Passive tester", "breton", DaggerfallCharacterGender.Male, 0,
                DaggerfallCharacterReflexes.Average, "custom", choices));
            Character.CommitChoices();
            Passives = new DaggerfallCareerPassives(Character, definitions, Actors, Player);
        }
    }

    [Fact]
    public void The_player_has_the_committed_careers_specials_and_an_actor_its_definitions_class()
    {
        var f = new Fixture([new("acute-hearing")]);
        Assert.True(f.Passives.SpecialsFor(Player).AcuteHearing);
        Assert.Equal(DaggerfallCareerSpecials.None, f.Passives.SpecialsFor(2));
        DaggerfallActorDefinition sorcerer = TestPayload.Definitions.Actors.Values.First(actor => actor.Career is not null);
        f.Actors[2] = sorcerer;
        Assert.Equal(TestPayload.Definitions.Catalogs.RequireCareer(sorcerer.Career!).Specials, f.Passives.SpecialsFor(2));
    }

    [Fact]
    public void Acute_hearing_reaches_further_and_further_still_with_its_talent()
    {
        Assert.Equal(16F, new Fixture([]).Passives.EnemyAudibleRange(acuteHearingTalent: true));
        var f = new Fixture([new("acute-hearing")]);
        Assert.Equal(20F, f.Passives.EnemyAudibleRange(acuteHearingTalent: false));
        Assert.Equal(24F, f.Passives.EnemyAudibleRange(acuteHearingTalent: true));
    }

    [Fact]
    public void Spell_absorption_follows_the_light_where_the_player_is()
    {
        var f = new Fixture([new("spell-absorption", "light")]);
        Assert.True(f.Passives.AbsorbsSpells(Player, light: true));
        Assert.False(f.Passives.AbsorbsSpells(Player, light: false));
    }

    [Fact]
    public void Light_powered_magery_loses_its_maximum_in_the_dark_and_regains_it_in_the_light()
    {
        var f = new Fixture([], [new("light-powered-magery", "unable")]);
        EntityId entity = new(7);
        Stat maximum = f.Stats.GetTrack(TrackId.Parse("magicka")).Maximum;
        f.Passives.RefreshMagery(f.Stats, entity, dark: true);
        Assert.Equal(0, maximum.Value);
        var penalty = Assert.Single(maximum.Sources, source => source.Identity == new IntrinsicSourceIdentity(entity, SourceInstanceId.Parse("daggerfall.passive-magery")));
        f.Passives.RefreshMagery(f.Stats, entity, dark: true);
        Assert.Same(penalty, Assert.Single(maximum.Sources, source => source.Identity == penalty.Identity));
        f.Passives.RefreshMagery(f.Stats, entity, dark: false);
        Assert.DoesNotContain(maximum.Sources, source => source.Identity == penalty.Identity);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(0, 4, 1)]
    [InlineData(0, 5, 2)]
    [InlineData(1, 4, 1)]
    [InlineData(1, 2, 0)]
    [InlineData(3, 10_000, 720)]
    public void A_passive_round_falls_on_every_fourth_magic_round_within_the_catch_up(long roundBefore, long minutes, long count)
    {
        var f = new Fixture([new("regenerate-health", "general")]);
        DaggerfallPassiveRounds rounds = f.Passives.Rounds(roundBefore, minutes, 2 * 24 * 60, vampire: false, travelling: false,
            () => throw new InvalidOperationException("No holy harm applies."), dark: false, sunlight: true, swimming: false);
        Assert.Equal(new DaggerfallPassiveRounds(count, true, 0), rounds);
    }

    [Fact]
    public void Sunlight_and_holy_places_harm_a_vampire_except_the_sun_on_a_journey()
    {
        var f = new Fixture([]);
        Assert.Equal(DaggerfallPassiveRounds.None, f.Passives.Rounds(0, 8, 2880, vampire: false, travelling: false, () => true, false, true, false));
        Assert.Equal(new DaggerfallPassiveRounds(2, false, 24), f.Passives.Rounds(0, 8, 2880, vampire: true, travelling: false, () => true, false, true, false));
        Assert.Equal(new DaggerfallPassiveRounds(2, false, 12), f.Passives.Rounds(0, 8, 2880, vampire: true, travelling: true, () => true, false, true, false));
        Assert.Equal(DaggerfallPassiveRounds.None, f.Passives.Rounds(0, 8, 2880, vampire: true, travelling: true, () => false, false, true, false));
        // Sun damage that applies out of the sun still counts its rounds, harmlessly.
        Assert.Equal(new DaggerfallPassiveRounds(2, false, 0), f.Passives.Rounds(0, 8, 2880, vampire: true, travelling: false, () => false, true, false, false));
    }
}
