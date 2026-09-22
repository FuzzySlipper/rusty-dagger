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
        if (action.Action != "character-cancel" && _mode != ProductMode.Title)
        {
            Presentation.SetOutcome("Character creation is available from the title screen.");
            return;
        }
        try
        {
            switch (action.Action)
            {
                case "character-begin":
                    State.Character.BeginChoices();
                    Presentation.SetOutcome("Character choices opened.");
                    break;
                case "character-cancel":
                    State.Character.CancelChoices();
                    Presentation.SetOutcome("Character choices cancelled.");
                    break;
                case "character-update":
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action));
                    Presentation.SetOutcome("Character choices updated.");
                    break;
                case "character-commit":
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action));
                    State.Character.CommitChoices();
                    int unequipped = _equipmentMoves.UnequipForbidden();
                    Presentation.SetOutcome(unequipped == 0 ? "Character identity committed." : $"Character identity committed; removed {unequipped} forbidden equipped item(s).");
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
        if (State.Character.Pending is null)
            throw new ArgumentException("Open character choices before changing them.");
    }

    /// <summary>Applies a real character-sheet level-up action while ordinary play is active.</summary>
    private void ChangeLevelUp(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            switch (action.Action)
            {
                case "character-level-allocate":
                    State.LevelUps.Allocate(action.Attribute ?? throw new ArgumentException("Level-up attribute is incomplete.", nameof(action)));
                    Presentation.SetOutcome("Level-up point allocated.");
                    break;
                case "character-level-commit":
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

    private static DaggerfallCharacterCreationChoices Choices(DaggerfallPlayerUiAction action)
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
        return new DaggerfallCharacterCreationChoices(action.Name, action.Race, gender, face,
            (DaggerfallCharacterReflexes)reflexes, action.Career, custom);
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
}
