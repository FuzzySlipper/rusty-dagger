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
                    Presentation.SetOutcome("Character identity committed.");
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
        return new DaggerfallCharacterCreationChoices(action.Name, action.Race, gender, face,
            (DaggerfallCharacterReflexes)reflexes, action.Career);
    }
}
