using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Character-creation actions run in the session's admitted input slice.</summary>
internal sealed partial class DaggerfallSession
{
    private void ChangeCharacter(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_mode != ProductMode.Title || _newGameInitialized)
        {
            Presentation.SetOutcome("Character creation is available from the title screen.");
            return;
        }
        try
        {
            switch (action.Kind)
            {
                case DaggerfallUiActionKind.CharacterBegin:
                    if (State.Character.Pending is not null) throw new ArgumentException("Character choices are already open.");
                    State.Character.BeginFreshChoices(_random);
                    Presentation.SetOutcome("Character choices opened.");
                    break;
                case DaggerfallUiActionKind.CharacterClassQuestions:
                    RequireCharacterDraft();
                    State.Character.BeginClassQuestions(_random, Choices(action, null));
                    Presentation.SetOutcome("Class questions opened.");
                    break;
                case DaggerfallUiActionKind.CharacterClassAnswer:
                    State.Character.AnswerClassQuestion(action.Question ?? throw new ArgumentException("Class question is incomplete."),
                        action.Answer ?? throw new ArgumentException("Class answer is incomplete."), _random);
                    Presentation.SetOutcome(State.Character.CreationMode == "character-pick" ? "Recommended career selected; review and commit character choices." : "Class answer accepted.");
                    break;
                case DaggerfallUiActionKind.CharacterClassBack:
                    State.Character.BackToClassPick();
                    Presentation.SetOutcome("Returned to character choices.");
                    break;
                case DaggerfallUiActionKind.CharacterCancel:
                    State.Character.AbandonCreation();
                    Presentation.SetOutcome("Character choices cancelled.");
                    break;
                case DaggerfallUiActionKind.CharacterUpdate:
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action, State.Character.Pending!.Background));
                    Presentation.SetOutcome("Character choices updated.");
                    break;
                case DaggerfallUiActionKind.CharacterBackgroundReroll:
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action, State.Character.Pending!.Background));
                    State.Character.RerollBackground(_random);
                    Presentation.SetOutcome("Character background rerolled.");
                    break;
                case DaggerfallUiActionKind.CharacterCommit:
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action, State.Character.Pending!.Background));
                    if (State.Character.Pending!.Background is null) throw new ArgumentException("Complete a character background before starting a new game.");
                    State.Character.CommitChoices(replaceCommitted: true);
                    Presentation.SetOutcome("Character committed. Review the final summary, then begin the new game.");
                    break;
                default:
                    throw new ArgumentException($"'{action.Action}' is not a character action.", nameof(action));
            }
        }
        catch (ArgumentException error)
        {
            Presentation.SetOutcome($"Character choice was not accepted: {error.Message}");
        }
    }

    private void RequireCharacterDraft()
    {
        if (State.Character.CreationMode == "character-generation") throw new ArgumentException("Complete or leave class questions before editing choices.");
        if (State.Character.Pending is null)
            throw new ArgumentException("Open character choices before changing them.");
    }

    /// <summary>Applies a real character-sheet level-up action while ordinary play is active.</summary>
    private void ChangeLevelUp(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            switch (action.Kind)
            {
                case DaggerfallUiActionKind.CharacterLevelAllocate:
                    State.LevelUps.Allocate(action.Attribute ?? throw new ArgumentException("Level-up attribute is incomplete.", nameof(action)));
                    Presentation.SetOutcome("Level-up point allocated.");
                    break;
                case DaggerfallUiActionKind.CharacterLevelCommit:
                    State.LevelUps.Commit();
                    Presentation.SetOutcome("Level-up committed.");
                    break;
                default:
                    throw new ArgumentException($"'{action.Action}' is not a level-up action.", nameof(action));
            }
        }
        catch (ArgumentException error)
        {
            Presentation.SetOutcome($"Level-up choice was not accepted: {error.Message}");
        }
    }

    private DaggerfallCharacterCreationChoices Choices(DaggerfallPlayerUiAction action, DaggerfallCharacterBackgroundSave? currentBackground)
    {
        if (action.Name is null || action.Race is null || action.Career is null || action.FaceIndex is not int face || action.Reflexes is not int reflexes)
            throw new ArgumentException("Character choice fields are incomplete.", nameof(action));
        DaggerfallCharacterGender gender = action.Gender switch
        {
            "male" => DaggerfallCharacterGender.Male,
            "female" => DaggerfallCharacterGender.Female,
            _ => throw new ArgumentException("Character gender must be male or female.", nameof(action)),
        };
        DaggerfallCustomCareerChoices? custom = action.Career == DaggerfallCustomCareerPolicy.CareerId
            ? new DaggerfallCustomCareerChoices(action.Name,
                List(action.PrimarySkills, "primary skills"), List(action.MajorSkills, "major skills"), List(action.MinorSkills, "minor skills"),
                action.HitPointsPerLevel ?? throw new ArgumentException("Custom class hit points per level are incomplete.", nameof(action)),
                Traits(action.Advantages, "advantages"), Traits(action.Disadvantages, "disadvantages"))
            : null;
        DaggerfallCharacterIdentity identity = new(action.Name, action.Race, gender, face, (DaggerfallCharacterReflexes)reflexes, action.Career);
        DaggerfallCharacterBackgroundSave? background = currentBackground;
        if (background is not null && background.People.RaceId != identity.RaceId)
            background = background with { People = DaggerfallBiographyPeople.Roll(_definitions, identity.RaceId, _random, background.RollSequence) };
        if (action.BackgroundAnswers is not null || action.AttributeAllocations is not null || action.SkillAllocations is not null)
        {
            if (currentBackground is null || action.BackgroundAnswers is null || action.AttributeAllocations is null || action.SkillAllocations is null)
                throw new ArgumentException("Character background fields are incomplete.", nameof(action));
            DaggerfallCareerDefinition career = action.Career == DaggerfallCustomCareerPolicy.CareerId
                ? DaggerfallCustomCareerPolicy.Compile(_definitions, custom!, State.Character.Career).Career
                : _definitions.Catalogs.TryGetCareer(action.Career, out var publishedCareer) ? publishedCareer
                    : throw new ArgumentException($"Career '{action.Career}' is not published.");
            background = action.Kind == DaggerfallUiActionKind.CharacterBackgroundReroll ? background : DaggerfallCharacterBackgroundPolicy.Update(_definitions, career, identity, background!,
                Answers(action.BackgroundAnswers), Allocations(action.AttributeAllocations, "attribute"), Allocations(action.SkillAllocations, "skill"));
        }
        return new DaggerfallCharacterCreationChoices(action.Name, action.Race, gender, face,
            (DaggerfallCharacterReflexes)reflexes, action.Career, custom, background);
    }

    private static string[] List(string? value, string field)
    {
        if (value is null) throw new ArgumentException($"Custom class {field} are incomplete.");
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static DaggerfallCustomCareerTrait[] Traits(string? value, string field)
    {
        if (value is null) throw new ArgumentException($"Custom class {field} are incomplete.");
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(entry =>
        {
            string[] pair = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            return new DaggerfallCustomCareerTrait(pair[0], pair.Length == 2 ? pair[1] : null);
        }).ToArray();
    }

    private static DaggerfallBiographyAnswerSave[] Answers(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(entry =>
    {
        string[] pair = entry.Split(':', 2, StringSplitOptions.TrimEntries);
        if (pair.Length != 2 || !int.TryParse(pair[0], out int question) || pair[1].Length != 1) throw new ArgumentException("Background answers must use question:letter.");
        return new DaggerfallBiographyAnswerSave(question, pair[1]);
    }).ToArray();

    private static DaggerfallCreationAllocationSave[] Allocations(string value, string name) => value.Length == 0 ? [] : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(entry =>
    {
        string[] pair = entry.Split(':', 2, StringSplitOptions.TrimEntries);
        if (pair.Length != 2 || !int.TryParse(pair[1], out int points)) throw new ArgumentException($"{name} allocations must use id:points.");
        return new DaggerfallCreationAllocationSave(pair[0], points);
    }).ToArray();

    private void ApplyBackground(DaggerfallCharacterBackgroundSave background)
    {
        State.Social.SetBiographyReactionModifier(background.Modifiers.Reaction);
        foreach ((int faction, int amount) in DaggerfallCharacterBackgroundPolicy.FactionReputations(_definitions, State.Character.Career, background))
            State.Social.ChangeFactionReputation(faction, amount, DaggerfallFactionReputationChange.Propagate);
        foreach ((int group, int amount) in DaggerfallCharacterBackgroundPolicy.SocialReputations(_definitions, State.Character.Career, background))
            State.Social.ChangePersonalReputation(group, amount);

    }
}
