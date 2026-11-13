using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The character sheet's fields as the creation screen sends them: identity and career, a custom class's skill
/// lists and traits, and the background's answers and allocations in their written form
/// (<c>question:letter</c>, <c>id:points</c>), each comma-separated.
/// </summary>
internal sealed record DaggerfallCharacterSheetFields(
    string? Name, string? Race, string? Gender, int? FaceIndex, int? Reflexes, string? Career,
    string? PrimarySkills, string? MajorSkills, string? MinorSkills, int? HitPointsPerLevel,
    string? Advantages, string? Disadvantages,
    string? BackgroundAnswers, string? AttributeAllocations, string? SkillAllocations);

/// <summary>
/// Character creation from the title screen: the choices opened, the class questions, the draft's edits and its
/// background, the commit, and the quick start. A refused choice throws <see cref="ArgumentException"/> naming
/// why; the screen's lines are the caller's.
/// </summary>
internal sealed class DaggerfallCharacterCreation(DaggerfallCharacterState character, DaggerfallDefinitions definitions, IRandomService random)
{
    /// <summary>The name a quick-started character takes when the draft has none.</summary>
    internal const string QuickStartName = "Adventurer";

    internal void Begin()
    {
        if (character.Pending is not null) throw new ArgumentException("Character choices are already open.");
        character.BeginFreshChoices(random);
    }

    internal void OpenClassQuestions(DaggerfallCharacterSheetFields fields)
    {
        RequireDraft();
        character.BeginClassQuestions(random, Choices(fields, null, reroll: false));
    }

    internal void AnswerClassQuestion(int question, int answer) => character.AnswerClassQuestion(question, answer, random);

    internal void Update(DaggerfallCharacterSheetFields fields)
    {
        RequireDraft();
        character.ReplacePending(Choices(fields, character.Pending!.Background, reroll: false));
    }

    internal void RerollBackground(DaggerfallCharacterSheetFields fields)
    {
        RequireDraft();
        character.ReplacePending(Choices(fields, character.Pending!.Background, reroll: true));
        character.RerollBackground(random);
    }

    internal void Commit(DaggerfallCharacterSheetFields fields)
    {
        RequireDraft();
        character.ReplacePending(Choices(fields, character.Pending!.Background, reroll: false));
        if (character.Pending!.Background is null) throw new ArgumentException("Complete a character background before starting a new game.");
        character.CommitChoices(replaceCommitted: true);
    }

    /// <summary>
    /// A testing convenience: the sheet's identity and career, a background completed without asking, committed at
    /// once; the entry screen begins the new game in the same update.
    /// </summary>
    internal void QuickStart(DaggerfallCharacterSheetFields fields)
    {
        if (character.Pending is null) character.BeginFreshChoices(random);
        if (character.CreationMode == "character-generation") character.BackToClassPick();
        DaggerfallCharacterSheetFields named = string.IsNullOrWhiteSpace(fields.Name) ? fields with { Name = QuickStartName } : fields;
        character.ReplacePending(character.WithCompletedBackground(Choices(named, null, reroll: false), random));
        character.CommitChoices(replaceCommitted: true);
    }

    /// <summary>Quick-starts the character the draft stands at, for a start that begins play with one press.</summary>
    internal void QuickStart()
    {
        if (character.Pending is null) character.BeginFreshChoices(random);
        if (character.CreationMode == "character-generation") character.BackToClassPick();
        DaggerfallCharacterCreationChoices draft = character.Pending!;
        character.ReplacePending(character.WithCompletedBackground(
            string.IsNullOrWhiteSpace(draft.Name) ? draft with { Name = QuickStartName } : draft, random));
        character.CommitChoices(replaceCommitted: true);
    }

    private void RequireDraft()
    {
        if (character.CreationMode == "character-generation") throw new ArgumentException("Complete or leave class questions before editing choices.");
        if (character.Pending is null)
            throw new ArgumentException("Open character choices before changing them.");
    }

    private DaggerfallCharacterCreationChoices Choices(DaggerfallCharacterSheetFields fields, DaggerfallCharacterBackgroundSave? currentBackground, bool reroll)
    {
        if (fields.Name is null || fields.Race is null || fields.Career is null || fields.FaceIndex is not int face || fields.Reflexes is not int reflexes)
            throw new ArgumentException("Character choice fields are incomplete.", nameof(fields));
        DaggerfallCharacterGender gender = fields.Gender switch
        {
            "male" => DaggerfallCharacterGender.Male,
            "female" => DaggerfallCharacterGender.Female,
            _ => throw new ArgumentException("Character gender must be male or female.", nameof(fields)),
        };
        DaggerfallCustomCareerChoices? custom = fields.Career == DaggerfallCustomCareerPolicy.CareerId
            ? new DaggerfallCustomCareerChoices(fields.Name,
                List(fields.PrimarySkills, "primary skills"), List(fields.MajorSkills, "major skills"), List(fields.MinorSkills, "minor skills"),
                fields.HitPointsPerLevel ?? throw new ArgumentException("Custom class hit points per level are incomplete.", nameof(fields)),
                Traits(fields.Advantages, "advantages"), Traits(fields.Disadvantages, "disadvantages"))
            : null;
        DaggerfallCharacterIdentity identity = new(fields.Name, fields.Race, gender, face, (DaggerfallCharacterReflexes)reflexes, fields.Career);
        var selected = new DaggerfallCharacterCreationChoices(fields.Name, fields.Race, gender, face,
            (DaggerfallCharacterReflexes)reflexes, fields.Career, custom, currentBackground);
        // A custom class the player is still fixing (a skill chosen twice, a trait too many) is kept as
        // drawn, so its eligibility reasons describe it; its background waits, unchanged, until the class
        // can be compiled again, since the tiers it would be rolled and spent against do not yet exist.
        // Its biography's people still follow the race, as they do for any draft, so the draft stays one
        // its presentation can describe.
        if (character.HasIneligibleCustomClass(selected))
            return currentBackground is not null && currentBackground.People.RaceId != identity.RaceId
                ? selected with { Background = currentBackground with { People = DaggerfallBiographyPeople.Roll(definitions, identity.RaceId, random, currentBackground.RollSequence) } }
                : selected;
        // The visible allocation fields belonged to the previous career. Publish fresh matching
        // rolls before accepting allocations for a different class or custom skill tier layout.
        if (currentBackground is not null && character.BackgroundCareerChanged(selected) && !reroll)
            return character.WithFreshBackground(selected, random);
        DaggerfallCharacterBackgroundSave? background = currentBackground;
        if (background is not null && background.People.RaceId != identity.RaceId)
            background = background with { People = DaggerfallBiographyPeople.Roll(definitions, identity.RaceId, random, background.RollSequence) };
        if (fields.BackgroundAnswers is not null || fields.AttributeAllocations is not null || fields.SkillAllocations is not null)
        {
            if (currentBackground is null || fields.BackgroundAnswers is null || fields.AttributeAllocations is null || fields.SkillAllocations is null)
                throw new ArgumentException("Character background fields are incomplete.", nameof(fields));
            DaggerfallCareerDefinition career = fields.Career == DaggerfallCustomCareerPolicy.CareerId
                ? DaggerfallCustomCareerPolicy.Compile(definitions, custom!, character.Career).Career
                : definitions.Catalogs.TryGetCareer(fields.Career, out var publishedCareer) ? publishedCareer
                    : throw new ArgumentException($"Career '{fields.Career}' is not published.");
            background = reroll ? background : DaggerfallCharacterBackgroundPolicy.Update(definitions, career, identity, background!,
                Answers(fields.BackgroundAnswers), Allocations(fields.AttributeAllocations, "attribute"), Allocations(fields.SkillAllocations, "skill"));
        }
        return new DaggerfallCharacterCreationChoices(fields.Name, fields.Race, gender, face,
            (DaggerfallCharacterReflexes)reflexes, fields.Career, custom, background);
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
}
