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
                    State.Character.BeginChoices(_random);
                    Presentation.SetOutcome("Character choices opened.");
                    break;
                case "character-cancel":
                    State.Character.CancelChoices();
                    Presentation.SetOutcome("Character choices cancelled.");
                    break;
                case "character-update":
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action, State.Character.Pending!.Background));
                    Presentation.SetOutcome("Character choices updated.");
                    break;
                case "character-background-reroll":
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action, State.Character.Pending!.Background));
                    State.Character.RerollBackground(_random);
                    Presentation.SetOutcome("Character background rerolled.");
                    break;
                case "character-commit":
                    RequireCharacterDraft();
                    State.Character.ReplacePending(Choices(action, State.Character.Pending!.Background));
                    DaggerfallCharacterBackgroundSave? committedBackground = State.Character.CommitChoices();
                    if (committedBackground is not null) ApplyBackground(committedBackground);
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
        if (action.BackgroundAnswers is not null || action.AttributeAllocations is not null || action.SkillAllocations is not null)
        {
            if (currentBackground is null || action.BackgroundAnswers is null || action.AttributeAllocations is null || action.SkillAllocations is null)
                throw new ArgumentException("Character background fields are incomplete.", nameof(action));
            DaggerfallCareerDefinition career = action.Career == DaggerfallCustomCareerPolicy.CareerId
                ? DaggerfallCustomCareerPolicy.Compile(_definitions, custom!, State.Character.Career).Career
                : _definitions.Catalogs.RequireCareer(action.Career);
            background = action.Action == "character-background-reroll" ? currentBackground : DaggerfallCharacterBackgroundPolicy.Update(_definitions, career, identity, currentBackground,
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

        DaggerfallItemFactory factory = new(_definitions, _random);
        foreach ((DaggerfallStartingGrant grant, int ordinal) in background.StartingGrants.Select((grant, ordinal) => (grant, ordinal)))
        {
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(grant.ItemId));
            DaggerfallItemTemplateDefinition template = definition.Template ?? throw new InvalidOperationException($"Background grant '{grant.ItemId}' is not a normalized template.");
            bool appearance = template.Groups.Contains("Armor", StringComparer.Ordinal) || template.Groups.Contains("MensClothing", StringComparer.Ordinal) || template.Groups.Contains("WomensClothing", StringComparer.Ordinal);
            DaggerfallCreatedItem item = factory.Create(new DaggerfallItemCreateRequest(template.Groups[0], $"character.background.{ordinal}.{template.Index}", DaggerfallItemOwner.Player,
                Quantity: grant.Quantity, TemplateIndex: template.Index, Material: definition.Weapon?.Material ?? definition.Armor?.Material,
                Race: appearance ? State.Character.Identity.RaceId : null,
                Gender: appearance ? (State.Character.Identity.Gender == DaggerfallCharacterGender.Female ? "female" : "male") : null));
            factory.Materialize(item, State.Inventory, State.ItemInstances,
                item.Stackable ? DaggerfallInventoryStackIds.ForBiography(ordinal) : null,
                item.Stackable ? null : _uniqueItems.AllocateReference());
        }
    }
}
