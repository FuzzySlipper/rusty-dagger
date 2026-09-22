using System;
using System.IO;
using System.Linq;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCharacterStateTests
{
    [Fact]
    public void Character_action_requires_one_complete_typed_choice()
    {
        DaggerfallPlayerUiAction? action = DaggerfallUiAction.Parse("""{"action":"character-commit","name":"Aubk-i","race":"khajiit","gender":"female","faceIndex":3,"reflexes":1,"career":"class08"}"""u8);
        Assert.NotNull(action);
        Assert.Equal(("Aubk-i", "khajiit", "female", 3, 1, "class08"), (action!.Name, action.Race, action.Gender, action.FaceIndex, action.Reflexes, action.Career));
        Assert.Null(DaggerfallUiAction.Parse("""{"action":"character-commit","name":"Aubk-i","race":"khajiit","gender":"female","faceIndex":3,"career":"class08"}"""u8));
    }

    [Fact]
    public void Cancelling_a_draft_preserves_the_committed_identity()
    {
        DaggerfallCharacterState character = Create(out _, out _);
        character.BeginChoices();
        character.ReplacePending(new DaggerfallCharacterCreationChoices("Aubk-i", "khajiit", DaggerfallCharacterGender.Female, 3,
            DaggerfallCharacterReflexes.High, "class08"));
        character.CancelChoices();

        Assert.Equal("Nameless", character.Identity.Name);
        Assert.Equal("breton", character.Identity.RaceId);
        Assert.Equal("class00", character.Career.Id);
        Assert.Null(character.Pending);
    }

    [Fact]
    public void A_committed_predefined_career_applies_its_authored_attributes_and_granted_skills()
    {
        DaggerfallCharacterState character = Create(out DaggerfallDefinitions definitions, out StatsComponent stats);
        character.BeginChoices();
        character.ReplacePending(new DaggerfallCharacterCreationChoices("Aubk-i", "khajiit", DaggerfallCharacterGender.Female, 3,
            DaggerfallCharacterReflexes.VeryHigh, "class01"));
        character.CommitChoices();

        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class01");
        Assert.Equal("Aubk-i", character.Identity.Name);
        Assert.Equal("khajiit", character.Identity.RaceId);
        Assert.Equal("class01", character.Career.Id);
        Assert.Equal(career.AttributeValues[0], stats.GetStat(StatId.Parse("strength")).BaseValue);
        Assert.Equal(career.AttributeValues[1], stats.GetStat(StatId.Parse("intelligence")).BaseValue);
        Assert.Equal(0, stats.GetStat(StatId.Parse("reflexes")).BaseValue);
        Assert.Equal(career.PrimarySkills, character.GrantedSkills.Where(grant => grant.Tier == DaggerfallCareerSkillTier.Primary).Select(grant => grant.SkillId));
        Assert.Equal(25 + career.HitPointsPerLevel, stats.GetStat(StatId.Parse("health-maximum")).BaseValue);
        Assert.Equal(DaggerfallFormulaPolicy.SpellPoints(career.AttributeValues[1], career.SpellPointMultiplierMilli),
            stats.GetStat(StatId.Parse("magicka-maximum")).BaseValue);
    }

    [Fact]
    public void An_invalid_draft_cannot_replace_the_live_identity()
    {
        DaggerfallCharacterState character = Create(out _, out _);
        character.BeginChoices();
        character.ReplacePending(new DaggerfallCharacterCreationChoices(" ", "breton", DaggerfallCharacterGender.Male, 0,
            DaggerfallCharacterReflexes.Average, "class00"));

        Assert.Throws<ArgumentException>(() => character.CommitChoices());
        Assert.Equal("Nameless", character.Identity.Name);
        Assert.NotNull(character.Pending);
    }

    [Fact]
    public void Committed_identity_round_trips_through_the_current_save_shape()
    {
        DaggerfallCharacterState original = Create(out DaggerfallDefinitions definitions, out _);
        original.BeginChoices();
        original.ReplacePending(new DaggerfallCharacterCreationChoices("Aubk-i", "redguard", DaggerfallCharacterGender.Female, 4,
            DaggerfallCharacterReflexes.Low, "class16"));
        original.CommitChoices();

        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent restoredStats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        restoredStats.GetStat(StatId.Parse("strength")).BaseValue = 91;
        DaggerfallCharacterState restored = new(definitions, restoredStats, player, original.Capture());
        Assert.Equal(original.Identity, restored.Identity);
        Assert.Equal("class16", restored.Career.Id);
        Assert.Equal(91, restoredStats.GetStat(StatId.Parse("strength")).BaseValue);
    }

    [Fact]
    public void Custom_class_uses_the_predefined_career_shape_and_round_trips_its_derived_vitals()
    {
        DaggerfallCharacterState character = Create(out DaggerfallDefinitions definitions, out StatsComponent stats);
        character.BeginChoices();
        DaggerfallCustomCareerChoices custom = new("Nightblade",
            ["mysticism", "alteration", "thaumaturgy"], ["illusion", "destruction", "restoration"],
            ["medical", "short-blade", "blunt-weapon", "dragonish", "daedric", "dodging"], 12,
            [new DaggerfallCustomCareerTrait("increased-magery", "1.5"), new DaggerfallCustomCareerTrait("immunity", "disease")],
            [new DaggerfallCustomCareerTrait("forbidden-material", "steel")]);
        character.ReplacePending(new DaggerfallCharacterCreationChoices("Aubk-i", "khajiit", DaggerfallCharacterGender.Female, 3,
            DaggerfallCharacterReflexes.High, DaggerfallCustomCareerPolicy.CareerId, custom));
        character.CommitChoices();

        Assert.Equal(DaggerfallCustomCareerPolicy.CareerId, character.Career.Id);
        Assert.Equal(3, character.Career.PrimarySkills.Count);
        Assert.Equal(3, character.Career.MajorSkills.Count);
        Assert.Equal(6, character.Career.MinorSkills.Count);
        Assert.Equal(12, character.Career.HitPointsPerLevel);
        Assert.Equal(1500, character.Career.SpellPointMultiplierMilli);
        Assert.Equal(37, stats.GetStat(StatId.Parse("health-maximum")).BaseValue);
        Assert.Equal(DaggerfallFormulaPolicy.SpellPoints(60, 1500), stats.GetStat(StatId.Parse("magicka-maximum")).BaseValue);
        Assert.Equal(["forbidden-material:steel"], character.CustomCareer!.ForbiddenEquipment);
        Assert.True(DaggerfallCustomCareerPolicy.Forbids(definitions.RequireItem(new DaggerfallItemId("iron-helm")), ["forbidden-armor:plate"], out string equipmentReason));
        Assert.Contains("plate", equipmentReason, StringComparison.Ordinal);

        DaggerfallCharacterSave save = character.Capture();
        Assert.NotNull(save.CustomCareer);
        Assert.Equal("Nightblade", save.CustomCareer!.Name);
        save.Validate(definitions);
    }

    [Fact]
    public void Custom_class_reports_duplicate_skills_conflicting_traits_and_out_of_range_difficulty()
    {
        DaggerfallCharacterState character = Create(out DaggerfallDefinitions definitions, out _);
        DaggerfallCustomCareerChoices invalid = new("Broken",
            ["mysticism", "mysticism", "thaumaturgy"], ["illusion", "destruction", "restoration"],
            ["medical", "short-blade", "blunt-weapon", "dragonish", "daedric", "dodging"], 30,
            [new DaggerfallCustomCareerTrait("immunity", "disease"), new DaggerfallCustomCareerTrait("regenerate-health", "general"), new DaggerfallCustomCareerTrait("spell-absorption", "general")],
            [new DaggerfallCustomCareerTrait("critical-weakness", "disease")]);
        List<string> errors = DaggerfallCustomCareerPolicy.Validate(definitions, invalid);

        Assert.Contains(errors, error => error.Contains("only once", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("cannot both target", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Classic difficulty", StringComparison.Ordinal));
        character.BeginChoices();
        character.ReplacePending(new DaggerfallCharacterCreationChoices("Broken", "breton", DaggerfallCharacterGender.Male, 0,
            DaggerfallCharacterReflexes.Average, DaggerfallCustomCareerPolicy.CareerId, invalid));
        Assert.Throws<ArgumentException>(() => character.CommitChoices());
    }

    private static DaggerfallCharacterState Create(out DaggerfallDefinitions definitions, out StatsComponent stats)
    {
        definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        return new DaggerfallCharacterState(definitions, stats, player);
    }

    private static string RepositoryRoot()
    {
        string? explicitRoot = Environment.GetEnvironmentVariable("DAGGER_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot) && File.Exists(Path.Combine(explicitRoot, "AGENTS.md"))) return explicitRoot;
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
