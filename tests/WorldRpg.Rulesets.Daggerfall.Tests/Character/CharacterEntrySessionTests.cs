using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CharacterEntrySessionTests
{
    private static DaggerfallSession OpenCreation()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, inputs);
        var session = DaggerfallSession.StartNew(EngineContextFake.Create(content,
            SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases)).Context,
            new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        session.ApplyProductMode(ProductMode.Title);
        session.OpenCharacterCreation();
        return session;
    }

    [Fact]
    public void A_cleared_name_reaches_the_draft_and_the_projection_holds_the_commit_until_it_is_typed()
    {
        using var session = OpenCreation();
        var character = session.State.Character;
        string Update(string name) => JsonSerializer.Serialize(new { action = "character-update", name, race = "breton", gender = "male", faceIndex = 0, reflexes = 2, career = "class00" });
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(Update(""))]));
        Assert.Equal("Character choices updated.", session.Presentation.LastOutcome);
        Assert.Equal("", character.Pending!.Name);
        Assert.Contains("Enter a name.", character.ReadCreation().Blocked);
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui(Update("Aubk-i"))]));
        Assert.DoesNotContain("Enter a name.", character.ReadCreation().Blocked);
        // A commit still needs the name: an empty one is refused before it reaches the character.
        session.Update(new ProductUpdate(OuterUpdate(3), [Ui(Update("").Replace("character-update", "character-commit", StringComparison.Ordinal))]));
        Assert.False(session.HasCommittedCharacter);
    }

    [Fact]
    public void A_custom_class_with_a_skill_twice_reaches_the_draft_and_blocks_commit_and_quick_start_until_fixed()
    {
        using var session = OpenCreation();
        var character = session.State.Character;
        var custom = DaggerfallCustomCareerChoices.Default(TestPayload.Definitions, character.Career) with { Name = "Twice Trained" };
        string Update(string[] minor) => JsonSerializer.Serialize(new
        {
            action = "character-update", name = custom.Name, race = "breton", gender = "male", faceIndex = 0, reflexes = 2, career = "custom",
            primarySkills = string.Join(',', custom.PrimarySkills), majorSkills = string.Join(',', custom.MajorSkills),
            minorSkills = string.Join(',', minor), hitPointsPerLevel = custom.HitPointsPerLevel, advantages = "", disadvantages = "",
        });
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(Update(custom.MinorSkills))]));
        var valid = character.Pending!.Background!.RollSequence;
        Assert.Null(character.ReadCreation().QuickStartBlocked);
        // The first minor skill made the same as the first primary: the draft keeps it as drawn.
        string[] twice = [custom.PrimarySkills[0], .. custom.MinorSkills.Skip(1)];
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui(Update(twice))]));
        Assert.Equal("Character choices updated.", session.Presentation.LastOutcome);
        Assert.Equal(twice, character.Pending!.CustomCareer!.MinorSkills);
        DaggerfallCharacterCreationPresentation invalid = character.ReadCreation();
        Assert.Contains("Each trained skill may appear only once.", invalid.Custom!.Eligibility);
        Assert.Contains("Each trained skill may appear only once.", invalid.Blocked);
        Assert.NotNull(invalid.QuickStartBlocked);
        Assert.Equal(valid, character.Pending!.Background!.RollSequence);
        // Fixing it clears the reasons.
        session.Update(new ProductUpdate(OuterUpdate(3), [Ui(Update(custom.MinorSkills))]));
        DaggerfallCharacterCreationPresentation fixedDraft = character.ReadCreation();
        Assert.Empty(fixedDraft.Custom!.Eligibility);
        Assert.DoesNotContain("Each trained skill may appear only once.", fixedDraft.Blocked);
        Assert.Null(fixedDraft.QuickStartBlocked);
    }

    [Fact]
    public void Changing_race_while_a_custom_class_is_ineligible_keeps_the_draft_readable_and_fixable()
    {
        using var session = OpenCreation();
        var character = session.State.Character;
        var custom = DaggerfallCustomCareerChoices.Default(TestPayload.Definitions, character.Career) with { Name = "Twice Trained" };
        string Update(string race, string[] minor) => JsonSerializer.Serialize(new
        {
            action = "character-update", name = custom.Name, race, gender = "male", faceIndex = 0, reflexes = 2, career = "custom",
            primarySkills = string.Join(',', custom.PrimarySkills), majorSkills = string.Join(',', custom.MajorSkills),
            minorSkills = string.Join(',', minor), hitPointsPerLevel = custom.HitPointsPerLevel, advantages = "", disadvantages = "",
        });
        string[] twice = [custom.PrimarySkills[0], .. custom.MinorSkills.Skip(1)];
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(Update("breton", custom.MinorSkills))]));
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui(Update("breton", twice))]));
        // The race changes while the class is still ineligible: the biography's people follow it, so the
        // draft is still one the screen can draw, with its reasons.
        session.Update(new ProductUpdate(OuterUpdate(3), [Ui(Update("nord", twice))]));
        Assert.Equal("nord", character.Pending!.RaceId);
        Assert.Equal("nord", character.Pending!.Background!.People.RaceId);
        DaggerfallCharacterCreationPresentation invalid = character.ReadCreation();
        Assert.Contains("Each trained skill may appear only once.", invalid.Blocked);
        // Correcting the class afterwards is accepted for the new race.
        session.Update(new ProductUpdate(OuterUpdate(4), [Ui(Update("nord", custom.MinorSkills))]));
        Assert.Equal("Character choices updated.", session.Presentation.LastOutcome);
        Assert.Empty(character.ReadCreation().Custom!.Eligibility);
        Assert.Equal("nord", character.Pending!.Background!.People.RaceId);
    }

    [Fact]
    public void Switching_to_custom_career_replaces_old_background_before_allocating_and_committing()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, inputs);
        using var session = DaggerfallSession.StartNew(EngineContextFake.Create(content,
            SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases)).Context,
            new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        session.ApplyProductMode(ProductMode.Title);
        session.OpenCharacterCreation();
        var character = session.State.Character;
        var initial = character.Pending!.Background!;
        var custom = DaggerfallCustomCareerChoices.Default(TestPayload.Definitions, character.Career) with
        { Name = "Acute Listener", Advantages = [new("acute-hearing")] };
        var career = DaggerfallCustomCareerPolicy.Compile(TestPayload.Definitions, custom, character.Career).Career;
        string Payload(string action, DaggerfallCharacterBackgroundSave background, bool allocate) => JsonSerializer.Serialize(new
        {
            action, name = custom.Name, race = "breton", gender = "male", faceIndex = 0, reflexes = 2, career = "custom",
            primarySkills = string.Join(',', custom.PrimarySkills), majorSkills = string.Join(',', custom.MajorSkills),
            minorSkills = string.Join(',', custom.MinorSkills), hitPointsPerLevel = custom.HitPointsPerLevel,
            advantages = "acute-hearing", disadvantages = "",
            backgroundAnswers = string.Join(',', background.Answers.Select(answer => $"{answer.Question}:{answer.Letter}")),
            attributeAllocations = allocate ? $"{career.Attributes[0]}:{background.AttributeBonusPool}" : "",
            skillAllocations = allocate ? $"{career.PrimarySkills[0]}:6,{career.MajorSkills[0]}:6,{career.MinorSkills[0]}:6" : ""
        });
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(Payload("character-update", initial, false))]));
        Assert.Equal("Character choices updated.", session.Presentation.LastOutcome);
        var selected = character.Pending!.Background!;
        Assert.NotEqual(initial.RollSequence, selected.RollSequence);
        Assert.Equal(career.SkillReferences.Order(), selected.RolledSkills.Select(skill => skill.Id).Order());
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui(Payload("character-commit", selected, false))]));
        Assert.Contains("Allocate", session.Presentation.LastOutcome);
        Assert.False(session.HasCommittedCharacter);
        session.Update(new ProductUpdate(OuterUpdate(3), [Ui(Payload("character-commit", selected, true))]));
        Assert.True(session.HasCommittedCharacter, session.Presentation.LastOutcome);
        Assert.Contains(character.CustomCareer!.Advantages, trait => trait.Id == "acute-hearing");
    }
}
