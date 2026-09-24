using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Rusty.Engine;
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
    public void Background_action_requires_all_normalized_answer_and_allocation_fields()
    {
        const string complete = """{"action":"character-update","name":"Aubk-i","race":"khajiit","gender":"female","faceIndex":3,"reflexes":1,"career":"class08","backgroundAnswers":"1:a,2:b","attributeAllocations":"strength:6","skillAllocations":"medical:6"}""";
        Assert.NotNull(DaggerfallUiAction.Parse(System.Text.Encoding.UTF8.GetBytes(complete)));
        Assert.Null(DaggerfallUiAction.Parse(System.Text.Encoding.UTF8.GetBytes(complete.Replace(",\"skillAllocations\":\"medical:6\"", string.Empty, StringComparison.Ordinal))));
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
        Assert.Equal(character.CustomCareer.ForbiddenEquipment, character.Career.ForbiddenEquipment);
        Assert.True(DaggerfallCustomCareerPolicy.Forbids(definitions.RequireItem(new DaggerfallItemId("iron-helm")), ["forbidden-armor:plate"], out string equipmentReason));
        Assert.Contains("plate", equipmentReason, StringComparison.Ordinal);

        DaggerfallCharacterSave save = character.Capture();
        Assert.NotNull(save.CustomCareer);
        Assert.Equal("Nightblade", save.CustomCareer!.Name);
        save.Validate(definitions);
    }

    [Fact]
    public void Preset_careers_publish_their_classic_attack_modifier_flags_and_expert_proficiencies()
    {
        Create(out DaggerfallDefinitions definitions, out _);

        // The classic CLASS??.CFG attack-modifier byte and weapon-proficiency bits, as the
        // supplied corpus carries them: the assassin fears nothing, the archer is an archery expert.
        Assert.Equal(0x04, definitions.Catalogs.RequireCareer("class11").AttackModifierFlags);
        Assert.Equal(["archery"], definitions.Catalogs.RequireCareer("class13").ExpertProficiencies);
        Assert.All(definitions.Catalogs.Careers.Where(career => career.Id != "class11"),
            career => Assert.Equal(0, career.AttackModifierFlags));
        Assert.All(definitions.Catalogs.Careers.Where(career => career.Id != "class13"),
            career => Assert.Empty(career.ExpertProficiencies));
    }

    [Fact]
    public void A_custom_class_carries_its_bonus_phobia_and_expertise_choices_in_the_same_career_fields()
    {
        DaggerfallCharacterState character = Create(out _, out _);
        character.BeginChoices();
        DaggerfallCustomCareerChoices custom = new("Nightblade",
            ["mysticism", "alteration", "thaumaturgy"], ["illusion", "destruction", "restoration"],
            ["medical", "short-blade", "blunt-weapon", "dragonish", "daedric", "dodging"], 12,
            [new DaggerfallCustomCareerTrait("bonus-to-hit", "undead"), new DaggerfallCustomCareerTrait("expertise", "archery")],
            [new DaggerfallCustomCareerTrait("phobia", "animals")]);
        character.ReplacePending(new DaggerfallCharacterCreationChoices("Aubk-i", "khajiit", DaggerfallCharacterGender.Female, 3,
            DaggerfallCharacterReflexes.High, DaggerfallCustomCareerPolicy.CareerId, custom));
        character.CommitChoices();

        // The attack policy reads one flag byte for preset and custom careers alike: undead bonus
        // 0x01 and animal phobia 0x80, with the weapon expertise recorded as an expert proficiency.
        Assert.Equal(0x01 | 0x80, character.Career.AttackModifierFlags);
        Assert.Equal(["archery"], character.Career.ExpertProficiencies);
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

    [Fact]
    public void Background_rolls_are_allocated_once_and_the_normalized_biography_round_trips()
    {
        DaggerfallCharacterState character = Create(out DaggerfallDefinitions definitions, out StatsComponent stats);
        IRandomService random = MinimumRandom();
        character.BeginChoices(random);
        DaggerfallCharacterCreationChoices draft = Assert.IsType<DaggerfallCharacterCreationChoices>(character.Pending);
        DaggerfallCharacterBackgroundSave rolled = Assert.IsType<DaggerfallCharacterBackgroundSave>(draft.Background);
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        DaggerfallBiographyDefinition biography = definitions.Biographies.Biographies.Single(value => value.ClassIndex == rolled.BiographyClassIndex);
        DaggerfallBiographyAnswerSave[] answers = biography.Questions.Select(question =>
        {
            DaggerfallBiographyAnswerDefinition selected = question.Answers.FirstOrDefault(answer => answer.Effects.Any(effect => effect.Kind == DaggerfallBiographyEffectKind.Item)) ?? question.Answers[0];
            return new DaggerfallBiographyAnswerSave(question.Number, selected.Letter);
        }).ToArray();
        DaggerfallCharacterBackgroundSave allocated = DaggerfallCharacterBackgroundPolicy.Update(definitions, career, character.Pending!.ToIdentity(), rolled, answers,
            [new DaggerfallCreationAllocationSave(career.Attributes[0], rolled.AttributeBonusPool)],
            [new DaggerfallCreationAllocationSave(career.PrimarySkills[0], 6), new DaggerfallCreationAllocationSave(career.MajorSkills[0], 6), new DaggerfallCreationAllocationSave(career.MinorSkills[0], 6)]);
        character.ReplacePending(draft with { Background = allocated });
        DaggerfallCharacterBackgroundSave committed = Assert.IsType<DaggerfallCharacterBackgroundSave>(character.CommitChoices());

        Assert.Equal(12, committed.Answers.Length);
        Assert.NotEmpty(committed.Biography);
        Assert.NotEmpty(committed.StartingGrants);
        Assert.Contains(DaggerfallCharacterBackgroundPolicy.Present(definitions, career, character.Identity, committed).UnsupportedEffects, effect => effect.StartsWith("A poison-resistance", StringComparison.Ordinal));
        Assert.Equal(career.AttributeValues[0] + allocated.AttributeBonusPool, stats.GetStat(StatId.Parse(career.Attributes[0])).BaseValue);
        Assert.Equal(committed.Biography, Assert.IsType<DaggerfallCharacterBackgroundPresentation>(character.ReadCreation().Background).Biography);
        character.BeginChoices();
        Assert.Throws<ArgumentException>(() => character.CommitChoices());

        DaggerfallCharacterSave save = character.Capture();
        save.Validate(definitions);
        DaggerfallCharacterBackgroundSave corruptSkill = committed with
        {
            RolledSkills = committed.RolledSkills.Select((skill, index) => index == 0 ? skill with { Points = 99 } : skill).ToArray(),
        };
        Assert.Throws<ArgumentException>(() => (save with { Background = corruptSkill }).Validate(definitions));
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent restoredStats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        restoredStats.GetStat(StatId.Parse(career.Attributes[0])).BaseValue = stats.GetStat(StatId.Parse(career.Attributes[0])).BaseValue;
        DaggerfallCharacterState restored = new(definitions, restoredStats, player, save);
        Assert.Equal(committed.Biography, restored.Background!.Biography);
        Assert.Equal(committed.Biography, restored.History);
        Assert.Equal(committed.Biography, Assert.IsType<DaggerfallCharacterBackgroundPresentation>(restored.ReadCreation().Background).Biography);
    }

    [Fact]
    public void Background_reroll_uses_a_new_keyed_sequence_while_the_current_draft_stays_fixed()
    {
        DaggerfallCharacterState character = Create(out _, out _);
        IRandomService random = SequenceRandom(out SequenceRandomProxy recorder);

        character.BeginChoices(random);
        DaggerfallCharacterBackgroundSave first = Assert.IsType<DaggerfallCharacterBackgroundSave>(character.Pending!.Background);
        int callsAfterFirstRoll = recorder.Requests.Count;
        DaggerfallCharacterBackgroundPresentation presented = Assert.IsType<DaggerfallCharacterBackgroundPresentation>(character.ReadCreation().Background);
        Assert.Equal(first.RolledAttributes[0].Points, presented.Attributes.Single(attribute => attribute.Id == first.RolledAttributes[0].Id).Rolled);
        Assert.Equal(callsAfterFirstRoll, recorder.Requests.Count);

        character.RerollBackground(random);
        DaggerfallCharacterBackgroundSave second = Assert.IsType<DaggerfallCharacterBackgroundSave>(character.Pending!.Background);

        Assert.Equal(1, first.RollSequence);
        Assert.Equal(2, second.RollSequence);
        Assert.NotEqual(first.RolledAttributes[0].Points, second.RolledAttributes[0].Points);
        Assert.Contains(recorder.Requests, request => request.Key.StartsWith("1.", StringComparison.Ordinal));
        Assert.Contains(recorder.Requests, request => request.Key.StartsWith("2.", StringComparison.Ordinal));
    }

    [Fact]
    public void Biography_expands_the_selected_answer_fragments_into_the_backstory()
    {
        DaggerfallCharacterState character = Create(out DaggerfallDefinitions definitions, out _);
        character.BeginChoices(MinimumRandom());
        DaggerfallCharacterCreationChoices draft = Assert.IsType<DaggerfallCharacterCreationChoices>(character.Pending);
        DaggerfallCharacterBackgroundSave initial = Assert.IsType<DaggerfallCharacterBackgroundSave>(draft.Background);
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        DaggerfallBiographyDefinition biography = definitions.Biographies.Biographies.Single(value => value.ClassIndex == initial.BiographyClassIndex);
        DaggerfallBiographyQuestionDefinition question = biography.Questions.Single(value => value.Number == 1);
        DaggerfallBiographyAnswerDefinition original = question.Answers.Single(value => value.Letter == initial.Answers.Single(answer => answer.Question == question.Number).Letter);
        DaggerfallBiographyAnswerDefinition replacement = question.Answers.First(value => value.Letter != original.Letter);
        string originalFragment = Fragment(definitions, original, "#");
        string replacementFragment = Fragment(definitions, replacement, "#");
        DaggerfallBiographyAnswerSave[] answers = initial.Answers.Select(answer => answer.Question == question.Number
            ? new DaggerfallBiographyAnswerSave(question.Number, replacement.Letter) : answer).ToArray();

        DaggerfallCharacterBackgroundSave changed = DaggerfallCharacterBackgroundPolicy.Update(definitions, career, draft.ToIdentity(), initial, answers, [], []);

        Assert.Contains(originalFragment, initial.Biography.Single());
        Assert.Contains(replacementFragment, changed.Biography.Single());
        Assert.DoesNotContain(originalFragment, changed.Biography.Single());
        Assert.DoesNotContain(changed.Biography, line => line.Contains("%q", StringComparison.Ordinal));

        static string Fragment(DaggerfallDefinitions definitions, DaggerfallBiographyAnswerDefinition answer, string position) => definitions.Text.Require(
            answer.Effects.Single(effect => effect.Kind == DaggerfallBiographyEffectKind.TextMacro && effect.First == position).MacroTarget!.Value).TextRuns.First();
    }

    [Fact]
    public void Biography_modifier_slots_remain_distinct_and_feed_their_live_consumers()
    {
        DaggerfallCharacterState character = Create(out DaggerfallDefinitions definitions, out _);
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        character.BeginChoices(MinimumRandom());
        DaggerfallCharacterBackgroundSave rolled = Assert.IsType<DaggerfallCharacterBackgroundSave>(character.Pending!.Background);
        DaggerfallBiographyDefinition biography = definitions.Biographies.Biographies.Single(value => value.ClassIndex == rolled.BiographyClassIndex);
        DaggerfallBiographyAnswerSave[] answers = biography.Questions.Select(question => new DaggerfallBiographyAnswerSave(question.Number,
            question.Number == 12 ? question.Answers.Single(answer => answer.Effects.Any(effect => effect.First == "TH")).Letter : question.Answers[0].Letter)).ToArray();
        DaggerfallCharacterBackgroundSave selected = DaggerfallCharacterBackgroundPolicy.Update(definitions, career, character.Pending!.ToIdentity(), rolled, answers,
            [new DaggerfallCreationAllocationSave(career.Attributes[0], rolled.AttributeBonusPool)],
            [new DaggerfallCreationAllocationSave(career.PrimarySkills[0], 6), new DaggerfallCreationAllocationSave(career.MajorSkills[0], 6), new DaggerfallCreationAllocationSave(career.MinorSkills[0], 6)]);

        Assert.Equal(-5, selected.Modifiers.AvoidHit);
        Assert.Equal(0, selected.Modifiers.DiseaseResistance);
        Assert.Equal(15, DaggerfallFormulaPolicy.CalculateHitChance(60, 0, 45, 50, 45, 50, 0, selected.Modifiers.AvoidHit));
        DaggerfallSocialState social = new(definitions.Factions);
        DaggerfallFactionDefinition faction = definitions.Factions.Factions.Values.First(value => value.SocialGroup >= 0);
        int baseline = social.ReactionForFaction(faction.Id).Value;
        social.SetBiographyReactionModifier(-5);
        Assert.Equal(baseline - 5, social.ReactionForFaction(faction.Id).Value);
        Assert.DoesNotContain(DaggerfallCharacterBackgroundPolicy.Present(definitions, career, character.Pending!.ToIdentity(), selected).UnsupportedEffects, effect => effect.Contains("not active", StringComparison.Ordinal));
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

    private static IRandomService MinimumRandom() => DispatchProxy.Create<IRandomService, MinimumRandomProxy>();

    private static IRandomService SequenceRandom(out SequenceRandomProxy proxy)
    {
        IRandomService service = DispatchProxy.Create<IRandomService, SequenceRandomProxy>();
        proxy = (SequenceRandomProxy)(object)service;
        return service;
    }

    private class SequenceRandomProxy : DispatchProxy
    {
        internal List<KeyedRngRequest> Requests { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            Requests.Add(request);
            int separator = request.Key.IndexOf('.', StringComparison.Ordinal);
            int sequence = int.Parse(request.Key.AsSpan(0, separator), System.Globalization.CultureInfo.InvariantCulture);
            return new KeyedRngReceipt(Math.Min(request.Maximum, checked(request.Minimum + sequence - 1)));
        }
    }

    private class MinimumRandomProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum) : throw new NotSupportedException(method?.Name);
    }
}
