using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Character creation's owner, constructed over a character state without the session.</summary>
public sealed class DaggerfallCharacterCreationTests
{
    private static (DaggerfallCharacterCreation Creation, DaggerfallCharacterState Character) Create()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        DaggerfallCharacterState character = new(definitions, stats, player);
        return (new DaggerfallCharacterCreation(character, definitions, RandomMinimum.Create()), character);
    }

    private static DaggerfallCharacterSheetFields Sheet(string? name = "Aubk-i", string race = "khajiit", string gender = "female", string career = "class08") =>
        new(name, race, gender, 3, 1, career, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void A_new_game_takes_the_committed_character_its_career_spells_and_full_tracks()
    {
        var (creation, character) = Create();
        Assert.False(creation.Committed);
        Assert.Throws<ArgumentException>(() => creation.NewGameChoices(newGameBegun: false));
        Assert.True(creation.OpenFresh());
        Assert.False(creation.OpenFresh());
        creation.QuickStart(Sheet());
        Assert.True(creation.Committed);
        Assert.Throws<ArgumentException>(() => creation.NewGameChoices(newGameBegun: true));

        DaggerfallCharacterCreationChoices choices = creation.NewGameChoices(newGameBegun: false);
        Assert.Equal(("Aubk-i", "khajiit", "class08"), (choices.Name, choices.RaceId, choices.CareerId));
        string[] spells = creation.NewGameSpells(character.Career, custom: false);
        Assert.Equal(TestPayload.Definitions.NewGame.Careers.Single(value => value.Career == "class08").Spells, spells);

        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        DaggerfallCharacterState fresh = new(definitions, stats, player);
        DaggerfallCharacterCreation newGame = new(fresh, definitions, RandomMinimum.Create());
        newGame.CommitNewCharacter(choices);
        Assert.Equal(("khajiit", "class08"), (fresh.Race.Id, fresh.Career.Id));
        foreach (var (_, track) in stats.Tracks) track.SetCurrent(0, clamp: true);
        newGame.CompleteNewCharacter(spells, stats);
        Assert.All(stats.Tracks, entry => Assert.Equal(entry.Value.Maximum.Value, entry.Value.Current));
        Assert.All(spells, spell => Assert.Contains(spell, fresh.KnownSpells));
    }

    [Theory]
    [InlineData("destruction", true)]
    [InlineData("long-blade", false)]
    public void A_custom_class_starts_with_magic_only_from_a_primary_or_major_school_of_magic(string primary, bool magic)
    {
        var (creation, character) = Create();
        DaggerfallCareerDefinition career = character.Career with { PrimarySkills = [primary], MajorSkills = [] };
        Assert.Equal(magic ? TestPayload.Definitions.NewGame.CustomMagicSpells : [], creation.NewGameSpells(career, custom: true));
    }

    [Fact]
    public void Choices_open_once_and_an_edit_needs_an_open_draft()
    {
        var (creation, character) = Create();
        Assert.Contains("Open character choices", Assert.Throws<ArgumentException>(() => creation.Update(Sheet())).Message, StringComparison.Ordinal);
        creation.Begin();
        Assert.NotNull(character.Pending);
        Assert.Contains("already open", Assert.Throws<ArgumentException>(creation.Begin).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_edit_keeps_the_drafts_background_and_rerolls_its_people_with_the_race()
    {
        var (creation, character) = Create();
        creation.Begin();
        creation.Update(Sheet(race: "breton"));
        DaggerfallCharacterBackgroundSave breton = character.Pending!.Background!;
        Assert.Equal(("Aubk-i", "breton", "class08"), (character.Pending.Name, character.Pending.RaceId, character.Pending.CareerId));

        creation.Update(Sheet(race: "nord"));
        Assert.Equal(breton.RollSequence, character.Pending!.Background!.RollSequence);
        Assert.Equal("nord", character.Pending.Background.People.RaceId);
    }

    [Fact]
    public void Incomplete_or_malformed_sheets_are_refused_with_their_reason()
    {
        var (creation, _) = Create();
        creation.Begin();
        Assert.Contains("fields are incomplete", Assert.Throws<ArgumentException>(() => creation.Update(Sheet(name: null))).Message, StringComparison.Ordinal);
        Assert.Contains("male or female", Assert.Throws<ArgumentException>(() => creation.Update(Sheet(gender: "other"))).Message, StringComparison.Ordinal);
        // A sheet naming another career takes a fresh background before its answers are read; on the same career they are.
        creation.Update(Sheet());
        Assert.Contains("background fields are incomplete", Assert.Throws<ArgumentException>(() =>
            creation.Update(Sheet() with { BackgroundAnswers = "1:a" })).Message, StringComparison.Ordinal);
        Assert.Contains("question:letter", Assert.Throws<ArgumentException>(() =>
            creation.Update(Sheet() with { BackgroundAnswers = "1:ab", AttributeAllocations = "", SkillAllocations = "" })).Message, StringComparison.Ordinal);
        Assert.Contains("custom class primary skills".ToLowerInvariant(), Assert.Throws<ArgumentException>(() =>
            creation.Update(Sheet(career: DaggerfallCustomCareerPolicy.CareerId))).Message.ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_commit_needs_a_complete_background()
    {
        var (creation, character) = Create();
        creation.Begin();
        Assert.Throws<ArgumentException>(() => creation.Commit(Sheet()));
        Assert.NotEqual("Aubk-i", character.Identity.Name);
    }

    [Fact]
    public void Quick_start_names_a_nameless_draft_and_commits_it_with_a_completed_background()
    {
        var (creation, character) = Create();
        creation.QuickStart(Sheet(name: " "));
        Assert.Equal(DaggerfallCharacterCreation.QuickStartName, character.Identity.Name);
        Assert.Equal("khajiit", character.Identity.RaceId);
        Assert.NotNull(character.Background);

        var (fromDraft, drafted) = Create();
        fromDraft.QuickStart();
        Assert.NotNull(drafted.Background);
        Assert.Null(drafted.Pending);
    }
}
