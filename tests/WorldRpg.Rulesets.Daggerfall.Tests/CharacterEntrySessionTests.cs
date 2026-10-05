using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CharacterEntrySessionTests
{
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
