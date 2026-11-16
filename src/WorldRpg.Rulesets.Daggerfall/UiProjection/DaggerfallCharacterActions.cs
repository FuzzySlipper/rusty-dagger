using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The character screen's actions (creation at the title, level-up in play) and the controls screen's, each read from
/// its action and asked of Character's owner, then worded for the player.
/// </summary>
/// <param name="quickEntry">Whether the bundle begins with a quick-start character.</param>
/// <param name="creationOpen">Whether the title still offers creation: the entry screen, before a new game began.</param>
internal sealed class DaggerfallCharacterActions(DaggerfallCharacterCreation creation, DaggerfallCharacterState character,
    DaggerfallLevelUpState levelUps, DaggerfallPlayerControls controls, bool quickEntry, Func<bool> creationOpen,
    PresentationState presentation)
{
    /// <summary>The entry screen's begin: a quick-entry bundle commits its quick-start character.</summary>
    internal void Begin()
    {
        if (quickEntry) creation.QuickStart();
    }

    internal void ChangeCharacter(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!creationOpen())
        {
            presentation.SetOutcome("Character creation is available from the title screen.");
            return;
        }
        try
        {
            switch (action.Kind)
            {
                case DaggerfallUiActionKind.CharacterBegin:
                    creation.Begin();
                    presentation.SetOutcome("Character choices opened.");
                    break;
                case DaggerfallUiActionKind.CharacterClassQuestions:
                    creation.OpenClassQuestions(SheetFields(action));
                    presentation.SetOutcome("Class questions opened.");
                    break;
                case DaggerfallUiActionKind.CharacterClassAnswer:
                    creation.AnswerClassQuestion(action.Question ?? throw new ArgumentException("Class question is incomplete."),
                        action.Answer ?? throw new ArgumentException("Class answer is incomplete."));
                    presentation.SetOutcome(character.CreationMode == "character-pick" ? "Recommended career selected; review your choices, then Start." : "Class answer accepted.");
                    break;
                case DaggerfallUiActionKind.CharacterClassBack:
                    character.BackToClassPick();
                    presentation.SetOutcome("Returned to character choices.");
                    break;
                case DaggerfallUiActionKind.CharacterCancel:
                    character.AbandonCreation();
                    presentation.SetOutcome("Character choices cancelled.");
                    break;
                case DaggerfallUiActionKind.CharacterUpdate:
                    creation.Update(SheetFields(action));
                    presentation.SetOutcome("Character choices updated.");
                    break;
                case DaggerfallUiActionKind.CharacterBackgroundReroll:
                    creation.RerollBackground(SheetFields(action));
                    presentation.SetOutcome("Character background rerolled.");
                    break;
                case DaggerfallUiActionKind.CharacterCommit:
                    creation.Commit(SheetFields(action));
                    presentation.SetOutcome("Character committed. Review the final summary, then begin the new game.");
                    break;
                case DaggerfallUiActionKind.CharacterQuickStart:
                    creation.QuickStart(SheetFields(action));
                    presentation.SetOutcome("Quick start: character committed; beginning the new game.");
                    break;
                default:
                    throw new ArgumentException($"'{action.Action}' is not a character action.", nameof(action));
            }
        }
        catch (ArgumentException error)
        {
            presentation.SetOutcome($"Character choice was not accepted: {DaggerfallRejectionText.For(error)}");
        }
    }

    private static DaggerfallCharacterSheetFields SheetFields(DaggerfallPlayerUiAction action) => new(
        action.Name, action.Race, action.Gender, action.FaceIndex, action.Reflexes, action.Career,
        action.PrimarySkills, action.MajorSkills, action.MinorSkills, action.HitPointsPerLevel, action.Advantages, action.Disadvantages,
        action.BackgroundAnswers, action.AttributeAllocations, action.SkillAllocations);

    /// <summary>Applies a real character-sheet level-up action while ordinary play is active.</summary>
    internal void ChangeLevelUp(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            switch (action.Kind)
            {
                case DaggerfallUiActionKind.CharacterLevelAllocate:
                    levelUps.Allocate(action.Attribute ?? throw new ArgumentException("Level-up attribute is incomplete.", nameof(action)));
                    presentation.SetOutcome("Level-up point allocated.");
                    break;
                case DaggerfallUiActionKind.CharacterLevelCommit:
                    levelUps.Commit();
                    presentation.SetOutcome("Level-up committed.");
                    break;
                default:
                    throw new ArgumentException($"'{action.Action}' is not a level-up action.", nameof(action));
            }
        }
        catch (ArgumentException error)
        {
            presentation.SetOutcome($"Level-up choice was not accepted: {DaggerfallRejectionText.For(error)}");
        }
    }

    /// <summary>Each controls action asks the controls owner for the edit it names.</summary>
    internal void ChangeControls(DaggerfallPlayerUiAction action)
    {
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.ActionDrivenTime: controls.ChangePlayerTime(action.Enabled!.Value); break;
            case DaggerfallUiActionKind.ControlsLook: controls.Change(settings => settings.SetLook(action.Sensitivity!.Value, action.Invert!.Value)); break;
            case DaggerfallUiActionKind.ControlsUiScale: controls.Change(settings => settings.SetUiScale(action.Scale)); break;
            case DaggerfallUiActionKind.ControlsGroundCover: controls.Change(settings => settings.SetGroundCover(action.Enabled!.Value)); break;
            case DaggerfallUiActionKind.ControlsClutterDistance: controls.Change(settings => settings.SetClutterDistance(action.Scale!.Value)); break;
            case DaggerfallUiActionKind.ControlsLampShadows: controls.Change(settings => settings.SetLampShadows(
                new(Enum.Parse<DaggerfallLampShadowLamps>(action.Item!, ignoreCase: true), action.Scale!.Value, action.Enabled!.Value))); break;
            case DaggerfallUiActionKind.ControlsSurfaceRelief: controls.Change(settings => settings.SetSurfaceRelief(action.Scale!.Value)); break;
            case DaggerfallUiActionKind.ControlsRebind: controls.Change(settings => settings.Rebind(action.Item!, [action.Key!], action.Confirm)); break;
            case DaggerfallUiActionKind.ControlsRebindButton:
                controls.Change(settings => settings.RebindButton(action.Item!, checked((int)action.Amount!.Value), controls.PadActions, action.Confirm)); break;
            case DaggerfallUiActionKind.ControlsVolume: controls.Change(settings => settings.SetVolume(action.Item!, action.Scale!.Value)); break;
            case DaggerfallUiActionKind.ControlsReset: controls.Reset(); break;
            default: throw new ArgumentException($"'{action.Action}' is not a controls action.", nameof(action));
        }
    }
}
