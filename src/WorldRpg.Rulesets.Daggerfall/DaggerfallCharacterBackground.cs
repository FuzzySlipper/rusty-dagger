using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One chosen answer and the durable normalized question it answers.</summary>
internal sealed record DaggerfallBiographyAnswerSave(int Question, string Letter);
internal sealed record DaggerfallCreationAllocationSave(string Id, int Points);
internal sealed record DaggerfallStartingGrant(string ItemId, int TemplateIndex, ulong Quantity, string SourceEffect);
/// <summary>The six donor BIOG modifier slots, preserved independently even where a gameplay admission has not landed.</summary>
internal sealed record DaggerfallBiographyModifiersSave(int PoisonResistance, int Fatigue, int Reaction, int DiseaseResistance, int MagicResistance, int AvoidHit);

/// <summary>
/// The character-creation rolls and selections.  Keeping the roll rather than its random seed makes
/// a reopened draft and a restored biography deterministic without replaying Engine random draws.
/// </summary>
internal sealed record DaggerfallCharacterBackgroundSave(
    int BiographyClassIndex,
    int RollSequence,
    DaggerfallBiographyAnswerSave[] Answers,
    DaggerfallCreationAllocationSave[] RolledAttributes,
    DaggerfallCreationAllocationSave[] AttributeAllocations,
    int AttributeBonusPool,
    DaggerfallCreationAllocationSave[] RolledSkills,
    DaggerfallCreationAllocationSave[] SkillAllocations,
    DaggerfallStartingGrant[] StartingGrants,
    DaggerfallBiographyModifiersSave Modifiers,
    string[] Biography);

internal sealed record DaggerfallCharacterBackgroundPresentation(
    int BiographyClassIndex, string[] Biography, DaggerfallBiographyQuestionPresentation[] Questions,
    DaggerfallCreationAttributePresentation[] Attributes, int AttributeBonusPool, int RemainingAttributePoints,
    DaggerfallCreationSkillPresentation[] Skills, int PrimarySkillPoints, int MajorSkillPoints, int MinorSkillPoints,
    DaggerfallStartingGrant[] StartingGrants, DaggerfallBiographyModifiersSave Modifiers, string[] UnsupportedEffects);
internal sealed record DaggerfallBiographyQuestionPresentation(int Number, string Text, DaggerfallBiographyAnswerPresentation[] Answers, string? SelectedLetter);
internal sealed record DaggerfallBiographyAnswerPresentation(string Letter, string Text);
internal sealed record DaggerfallCreationAttributePresentation(string Id, string Label, int Rolled, int Allocated, int Value, bool CanAllocate);
internal sealed record DaggerfallCreationSkillPresentation(string Id, string Tier, int Rolled, int Allocated, int BiographyBonus, int Value, bool CanAllocate);

/// <summary>Classic creation policy interpreted from the normalized BIOG and catalog records.</summary>
internal static class DaggerfallCharacterBackgroundPolicy
{
    private const int AttributeRollMinimum = 0, AttributeRollMaximum = 10;
    private const int AttributePoolMinimum = 6, AttributePoolMaximum = 14;
    private const int TierPool = 6;
    private static readonly string[] WeaponMaterials = ["iron", "steel", "silver", "elven", "dwarven", "mithril", "adamantium", "ebony", "orcish", "daedric"];

    internal static DaggerfallCharacterBackgroundSave Roll(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career, DaggerfallCharacterIdentity identity, IRandomService random, int rollSequence)
    {
        ArgumentNullException.ThrowIfNull(definitions); ArgumentNullException.ThrowIfNull(career); ArgumentNullException.ThrowIfNull(random);
        if (rollSequence <= 0) throw new ArgumentOutOfRangeException(nameof(rollSequence));
        DaggerfallBiographyDefinition biography = BiographyFor(definitions, career);
        DaggerfallCreationAllocationSave[] attributes = career.Attributes.Select((id, index) =>
            new DaggerfallCreationAllocationSave(id, checked(career.AttributeValues[index] + Draw(random, rollSequence, $"attribute.{id}", AttributeRollMinimum, AttributeRollMaximum)))).ToArray();
        List<DaggerfallCreationAllocationSave> skills = [];
        AddRolls(skills, career.PrimarySkills, 28, "primary", random, rollSequence);
        AddRolls(skills, career.MajorSkills, 18, "major", random, rollSequence);
        AddRolls(skills, career.MinorSkills, 13, "minor", random, rollSequence);
        DaggerfallBiographyAnswerSave[] answers = biography.Questions
            .Select(question => new DaggerfallBiographyAnswerSave(question.Number, question.Answers[0].Letter)).ToArray();
        return Build(definitions, career, identity, biography, rollSequence, answers, attributes, [], Draw(random, rollSequence, "attribute-pool", AttributePoolMinimum, AttributePoolMaximum), skills.ToArray(), [], requireComplete: false);
    }

    internal static DaggerfallCharacterBackgroundSave Update(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career,
        DaggerfallCharacterIdentity identity, DaggerfallCharacterBackgroundSave current, IReadOnlyList<DaggerfallBiographyAnswerSave> answers,
        IReadOnlyList<DaggerfallCreationAllocationSave> attributes, IReadOnlyList<DaggerfallCreationAllocationSave> skills)
    {
        ArgumentNullException.ThrowIfNull(current);
        DaggerfallBiographyDefinition biography = BiographyFor(definitions, career);
        if (current.BiographyClassIndex != biography.ClassIndex)
            throw new ArgumentException("Reroll the background after changing career so it uses that career's questionnaire.");
        return Build(definitions, career, identity, biography, current.RollSequence, answers.ToArray(), current.RolledAttributes, attributes.ToArray(), current.AttributeBonusPool,
            current.RolledSkills, skills.ToArray(), requireComplete: false);
    }

    internal static DaggerfallCharacterBackgroundSave RequireComplete(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career, DaggerfallCharacterIdentity identity, DaggerfallCharacterBackgroundSave value) =>
        Build(definitions, career, identity, BiographyFor(definitions, career), value.RollSequence, value.Answers, value.RolledAttributes, value.AttributeAllocations,
            value.AttributeBonusPool, value.RolledSkills, value.SkillAllocations, requireComplete: true);

    internal static DaggerfallCharacterBackgroundPresentation Present(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career, DaggerfallCharacterIdentity identity, DaggerfallCharacterBackgroundSave value)
    {
        DaggerfallCharacterBackgroundSave background = Build(definitions, career, identity, BiographyFor(definitions, career), value.RollSequence, value.Answers, value.RolledAttributes,
            value.AttributeAllocations, value.AttributeBonusPool, value.RolledSkills, value.SkillAllocations, requireComplete: false);
        DaggerfallBiographyDefinition biography = BiographyFor(definitions, career);
        Dictionary<string, int> attributeAllocations = Values(background.AttributeAllocations);
        Dictionary<string, int> skillAllocations = Values(background.SkillAllocations);
        Dictionary<string, int> skillBonuses = SkillBonuses(definitions, biography, background.Answers);
        DaggerfallCreationAttributePresentation[] attributes = background.RolledAttributes.Select(value => new DaggerfallCreationAttributePresentation(
            value.Id, Label(value.Id), value.Points, attributeAllocations.GetValueOrDefault(value.Id), checked(value.Points + attributeAllocations.GetValueOrDefault(value.Id)),
            Remaining(background.AttributeBonusPool, background.AttributeAllocations) > 0 && value.Points + attributeAllocations.GetValueOrDefault(value.Id) < DaggerfallFormulaPolicy.MaxStatValue())).ToArray();
        DaggerfallCreationSkillPresentation[] skills = background.RolledSkills.Select(value =>
        {
            string tier = Tier(career, value.Id);
            int remaining = RemainingTier(tier, background.SkillAllocations, career);
            int allocated = skillAllocations.GetValueOrDefault(value.Id);
            return new DaggerfallCreationSkillPresentation(value.Id, tier, value.Points, allocated, skillBonuses.GetValueOrDefault(value.Id),
                checked(value.Points + allocated + skillBonuses.GetValueOrDefault(value.Id)), remaining > 0);
        }).ToArray();
        return new(background.BiographyClassIndex, background.Biography,
            biography.Questions.Select(question => new DaggerfallBiographyQuestionPresentation(question.Number, Text(definitions, question.TextKeys),
                question.Answers.Select(answer => new DaggerfallBiographyAnswerPresentation(answer.Letter, Text(definitions, [answer.TextKey]))).ToArray(),
                background.Answers.Single(answer => answer.Question == question.Number).Letter)).ToArray(),
            attributes, background.AttributeBonusPool, Remaining(background.AttributeBonusPool, background.AttributeAllocations), skills,
            RemainingTier("primary", background.SkillAllocations, career), RemainingTier("major", background.SkillAllocations, career), RemainingTier("minor", background.SkillAllocations, career), background.StartingGrants,
            background.Modifiers, UnsupportedEffects(biography, background.Answers, background.Modifiers));
    }

    internal static void ApplyStats(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career, DaggerfallCharacterBackgroundSave background, StatsComponent stats)
    {
        Dictionary<string, int> attributes = Values(background.RolledAttributes);
        foreach ((string id, int allocation) in Values(background.AttributeAllocations)) attributes[id] = checked(attributes[id] + allocation);
        foreach ((string id, int value) in attributes) stats.GetStat(StatId.Parse(id)).BaseValue = value;
        Dictionary<string, int> bonuses = SkillBonuses(definitions, BiographyFor(definitions, career), background.Answers);
        foreach (DaggerfallCreationAllocationSave skill in background.RolledSkills)
            stats.GetStat(StatId.Parse(skill.Id)).BaseValue = checked(skill.Points + Values(background.SkillAllocations).GetValueOrDefault(skill.Id) + bonuses.GetValueOrDefault(skill.Id));
        // BIOG can award any skill, not only a career's three trained groups.
        foreach ((string id, int bonus) in bonuses.Where(entry => !background.RolledSkills.Any(skill => skill.Id == entry.Key)))
        {
            Stat stat = stats.GetStat(StatId.Parse(id));
            stat.BaseValue = checked(stat.BaseValue + bonus);
        }
    }

    internal static IEnumerable<(int Faction, int Amount)> FactionReputations(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career, DaggerfallCharacterBackgroundSave background) =>
        Effects(BiographyFor(definitions, career), background.Answers).Where(effect => effect.Kind == DaggerfallBiographyEffectKind.FactionReputation)
            .Select(effect => (Number(effect.First), Number(effect.Second)));
    internal static IEnumerable<(int Group, int Amount)> SocialReputations(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career, DaggerfallCharacterBackgroundSave background) =>
        Effects(BiographyFor(definitions, career), background.Answers).Where(effect => effect.Kind == DaggerfallBiographyEffectKind.SocialReputation)
            .Select(effect => (Number(effect.First), Number(effect.Second)));

    private static DaggerfallCharacterBackgroundSave Build(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career, DaggerfallCharacterIdentity identity, DaggerfallBiographyDefinition biography, int rollSequence,
        DaggerfallBiographyAnswerSave[] answers, DaggerfallCreationAllocationSave[] rolledAttributes, DaggerfallCreationAllocationSave[] attributeAllocations,
        int attributePool, DaggerfallCreationAllocationSave[] rolledSkills, DaggerfallCreationAllocationSave[] skillAllocations, bool requireComplete)
    {
        if (rollSequence <= 0) throw new ArgumentOutOfRangeException(nameof(rollSequence));
        _ = DaggerfallFormulaPolicy.CreationBonusPool(attributePool);
        RequireChoices(biography, answers);
        RequireExact(career.Attributes, rolledAttributes, "rolled attributes"); RequireExact(career.SkillReferences, rolledSkills, "rolled skills");
        RequireAllocations(career.Attributes, attributeAllocations, attributePool, "attribute");
        RequireAllocations(career.SkillReferences, skillAllocations, TierPool * 3, "skill");
        Dictionary<string, int> rolled = Values(rolledAttributes), allocated = Values(attributeAllocations);
        foreach ((string attribute, int index) in career.Attributes.Select((value, index) => (value, index)))
            if (rolled[attribute] < career.AttributeValues[index] || rolled[attribute] > career.AttributeValues[index] + AttributeRollMaximum || rolled[attribute] + allocated.GetValueOrDefault(attribute) > DaggerfallFormulaPolicy.MaxStatValue())
                throw new ArgumentException($"Creation attribute '{attribute}' is outside the classic roll or maximum.");
        foreach (string tier in new[] { "primary", "major", "minor" })
        {
            int minimum = tier switch { "primary" => 28, "major" => 18, "minor" => 13, _ => throw new InvalidOperationException("Unknown creation skill tier.") };
            foreach (DaggerfallCreationAllocationSave skill in rolledSkills.Where(value => Tier(career, value.Id) == tier))
                if (skill.Points is < 0 or > int.MaxValue || skill.Points < minimum || skill.Points > minimum + 3)
                    throw new ArgumentException($"Creation {tier} skill '{skill.Id}' is outside the classic roll.");
            int points = skillAllocations.Where(value => Tier(career, value.Id) == tier).Sum(value => value.Points);
            if (points > TierPool || (requireComplete && points != TierPool)) throw new ArgumentException($"Allocate exactly {TierPool} {tier} skill points before committing.");
        }
        int attributePoints = attributeAllocations.Sum(value => value.Points);
        if (attributePoints > attributePool || (requireComplete && attributePoints != attributePool)) throw new ArgumentException("Allocate all attribute bonus points before committing.");
        DaggerfallStartingGrant[] grants = Effects(biography, answers).Where(effect => effect.Kind is DaggerfallBiographyEffectKind.Item or DaggerfallBiographyEffectKind.Gold)
            .Select(effect => effect.Kind == DaggerfallBiographyEffectKind.Item ? Grant(definitions, effect) : GoldGrant(definitions, effect)).ToArray();
        DaggerfallBiographyModifiersSave modifiers = Modifiers(biography, answers);
        string[] prose = Biography(definitions, biography, answers, identity);
        return new(biography.ClassIndex, rollSequence, answers.OrderBy(answer => answer.Question).ToArray(), rolledAttributes.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(),
            attributeAllocations.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(), attributePool, rolledSkills.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(),
            skillAllocations.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(), grants, modifiers, prose);
    }

    private static void AddRolls(List<DaggerfallCreationAllocationSave> values, IEnumerable<string> skills, int minimum, string scope, IRandomService random, int rollSequence)
    { foreach (string skill in skills) values.Add(new DaggerfallCreationAllocationSave(skill, checked(minimum + Draw(random, rollSequence, $"{scope}.{skill}", 0, 3)))); }
    private static int Draw(IRandomService random, int rollSequence, string key, int minimum, int maximum) => checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, "daggerfall.character-creation.v1", $"{rollSequence}.{key}", minimum, maximum)).Value);
    private static Dictionary<string, int> Values(IEnumerable<DaggerfallCreationAllocationSave> values) => values.ToDictionary(value => value.Id, value => value.Points, StringComparer.Ordinal);
    private static int Remaining(int pool, IEnumerable<DaggerfallCreationAllocationSave> values) => checked(pool - values.Sum(value => value.Points));
    private static int RemainingTier(string tier, IEnumerable<DaggerfallCreationAllocationSave> values, DaggerfallCareerDefinition career) => checked(TierPool - values.Where(value => Tier(career, value.Id) == tier).Sum(value => value.Points));
    private static string Tier(DaggerfallCareerDefinition career, string skill) => career.PrimarySkills.Contains(skill) ? "primary" : career.MajorSkills.Contains(skill) ? "major" : career.MinorSkills.Contains(skill) ? "minor" : throw new ArgumentException($"'{skill}' is not trained by the selected career.");
    private static int Number(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    private static string Label(string id) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));
    private static string Text(DaggerfallDefinitions definitions, IEnumerable<DaggerfallTextKey> keys) => string.Join(" ", keys.SelectMany(key => definitions.Text.Require(key).TextRuns)).Trim();
    private static IEnumerable<DaggerfallBiographyEffectDefinition> Effects(DaggerfallBiographyDefinition biography, IEnumerable<DaggerfallBiographyAnswerSave> answers) =>
        answers.Select(answer => biography.Questions.Single(question => question.Number == answer.Question).Answers.Single(value => value.Letter == answer.Letter)).SelectMany(answer => answer.Effects);
    private static string[] Biography(DaggerfallDefinitions definitions, DaggerfallBiographyDefinition biography, DaggerfallBiographyAnswerSave[] answers, DaggerfallCharacterIdentity identity)
    {
        DaggerfallTextContext context = BiographyContext(definitions, biography, answers, identity);
        DaggerfallTextRenderResult rendered = definitions.TextPresentation.Resolve(biography.BackstoryKey, context);
        if (!rendered.IsComplete)
            throw new ArgumentException($"Biography '{biography.BackstoryKey}' cannot be rendered from its selected answers: {string.Join(", ", rendered.Diagnostics.Select(diagnostic => diagnostic.Detail))}.");
        string[] lines = rendered.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Any(line => line.Contains('%', StringComparison.Ordinal)))
            throw new ArgumentException($"Biography '{biography.BackstoryKey}' still contains an unresolved macro.");
        return lines;
    }

    private static DaggerfallTextContext BiographyContext(DaggerfallDefinitions definitions, DaggerfallBiographyDefinition biography, IEnumerable<DaggerfallBiographyAnswerSave> answers, DaggerfallCharacterIdentity identity)
    {
        string?[] primary = new string?[12], secondary = new string?[12], tertiary = new string?[12];
        DaggerfallTextPlayerContext player = identity.Gender == DaggerfallCharacterGender.Female
            ? new(Name: identity.Name, Race: identity.RaceId, PlayerPronoun: "she", PlayerObjectPronoun: "her", PlayerReflexivePronoun: "herself", PlayerPossessiveAdjective: "her", PlayerPossessivePronoun: "hers")
            : new(Name: identity.Name, Race: identity.RaceId, PlayerPronoun: "he", PlayerObjectPronoun: "him", PlayerReflexivePronoun: "himself", PlayerPossessiveAdjective: "his", PlayerPossessivePronoun: "his");
        DaggerfallTextContext fragmentContext = DaggerfallTextContext.Empty with { Player = player };
        foreach (DaggerfallBiographyAnswerSave selected in answers)
        {
            DaggerfallBiographyAnswerDefinition answer = biography.Questions.Single(question => question.Number == selected.Question).Answers.Single(answer => answer.Letter == selected.Letter);
            foreach (DaggerfallBiographyEffectDefinition effect in answer.Effects.Where(effect => effect.Kind == DaggerfallBiographyEffectKind.TextMacro && effect.MacroTargetDisposition == DaggerfallBiographyLinkDisposition.Resolved && effect.MacroTarget is not null))
            {
                int index = selected.Question - 1;
                string fragment = definitions.TextPresentation.Resolve(effect.MacroTarget!.Value, fragmentContext).Text;
                switch (effect.First)
                {
                    case "#": primary[index] = fragment; break;
                    case "!": secondary[index] = fragment; break;
                    case "?": tertiary[index] = fragment; break;
                    default: throw new ArgumentException($"Biography text macro '{effect.Text}' has an unsupported token position.");
                }
            }
        }
        return fragmentContext with { Story = new(
            QuestionOne: primary[0], QuestionTwo: primary[1], QuestionThree: primary[2], QuestionFour: primary[3], QuestionFive: primary[4], QuestionSix: primary[5], QuestionSeven: primary[6], QuestionEight: primary[7], QuestionNine: primary[8], QuestionTen: primary[9], QuestionEleven: primary[10], QuestionTwelve: primary[11],
            QuestionOneA: secondary[0], QuestionTwoA: secondary[1], QuestionThreeA: secondary[2], QuestionFourA: secondary[3], QuestionFiveA: secondary[4], QuestionSixA: secondary[5], QuestionSevenA: secondary[6], QuestionEightA: secondary[7], QuestionNineA: secondary[8], QuestionTenA: secondary[9], QuestionElevenA: secondary[10], QuestionTwelveA: secondary[11],
            QuestionOneB: tertiary[0], QuestionTwoB: tertiary[1], QuestionThreeB: tertiary[2], QuestionFourB: tertiary[3], QuestionFiveB: tertiary[4], QuestionSixB: tertiary[5], QuestionSevenB: tertiary[6], QuestionEightB: tertiary[7], QuestionNineB: tertiary[8], QuestionTenB: tertiary[9], QuestionElevenB: tertiary[10], QuestionTwelveB: tertiary[11]) };
    }
    private static Dictionary<string, int> SkillBonuses(DaggerfallDefinitions definitions, DaggerfallBiographyDefinition biography, IEnumerable<DaggerfallBiographyAnswerSave> answers) =>
        Effects(biography, answers).Where(effect => effect.Kind == DaggerfallBiographyEffectKind.Skill).GroupBy(effect => definitions.Catalogs.Skills.Single(key => key.Index == Number(effect.First)).Id)
            .ToDictionary(group => group.Key, group => group.Sum(effect => Number(effect.Second)), StringComparer.Ordinal);
    private static DaggerfallBiographyModifiersSave Modifiers(DaggerfallBiographyDefinition biography, IEnumerable<DaggerfallBiographyAnswerSave> answers)
    {
        int poison = 0, fatigue = 0, reaction = 0, disease = 0, magic = 0, avoidHit = 0;
        // BIOG assigns each slot as it walks selected answers; it does not sum repeated codes.
        foreach (DaggerfallBiographyEffectDefinition effect in Effects(biography, answers).Where(effect => effect.Kind == DaggerfallBiographyEffectKind.BiographyModifier))
        {
            int value = Number(effect.Second);
            switch (effect.First)
            {
                case "RP": poison = value; break;
                case "FT": fatigue = value; break;
                case "RR": reaction = value; break;
                case "RD": disease = value; break;
                case "MR": magic = value; break;
                case "TH": avoidHit = value; break;
                default: throw new ArgumentException($"Unknown BIOG modifier '{effect.First}'.");
            }
        }
        return new(poison, fatigue, reaction, disease, magic, avoidHit);
    }
    private static string[] UnsupportedEffects(DaggerfallBiographyDefinition biography, IEnumerable<DaggerfallBiographyAnswerSave> answers, DaggerfallBiographyModifiersSave modifiers)
    {
        List<string> values = Effects(biography, answers).Where(effect => effect.Kind is DaggerfallBiographyEffectKind.Unimplemented or DaggerfallBiographyEffectKind.Invalid)
            .Select(_ => "The source includes a background effect with no gameplay consequence.").ToList();
        values.AddRange(Effects(biography, answers)
            .Where(effect => effect.Kind == DaggerfallBiographyEffectKind.TextMacro && effect.MacroTargetDisposition == DaggerfallBiographyLinkDisposition.Unresolved)
            .Select(_ => "A selected background story detail is unavailable in the current content."));
        if (modifiers.PoisonResistance != 0) values.Add("A poison-resistance background effect is recorded but is not active in current play.");
        if (modifiers.Fatigue != 0) values.Add("The source retains this fatigue background effect without a gameplay consequence.");
        if (modifiers.MagicResistance != 0) values.Add("A magic-resistance background effect is recorded but is not active in current play.");
        return values.ToArray();
    }
    private static DaggerfallStartingGrant Grant(DaggerfallDefinitions definitions, DaggerfallBiographyEffectDefinition effect)
    {
        int group = Number(effect.First), ordinal = Number(effect.Second);
        string category = group switch { 2 => "Armor", 3 => "Weapons", 7 => "Books", 10 => "ReligiousItems", 14 => "Gems", 22 => "MiscellaneousIngredients2", 25 => "Jewellery", _ => throw new ArgumentException($"Biography item group {group} has no normalized category.") };
        DaggerfallItemTemplateDefinition template = definitions.ItemTemplateCatalog.Templates.Values.Where(value => value.Groups.Contains(category, StringComparer.Ordinal)).OrderBy(value => value.Index).ElementAtOrDefault(ordinal)
            ?? throw new ArgumentException($"Biography item {effect.Text} has no normalized template.");
        string itemId = category is "Armor" or "Weapons"
            ? $"template-{template.Index}-{WeaponMaterials[Number(effect.Third)]}"
            : $"template-{template.Index}";
        _ = definitions.RequireItem(new DaggerfallItemId(itemId));
        return new(itemId, template.Index, 1, effect.Text);
    }
    private static DaggerfallStartingGrant GoldGrant(DaggerfallDefinitions definitions, DaggerfallBiographyEffectDefinition effect)
    {
        int amount = Number(effect.First);
        if (amount < 0) throw new ArgumentException("Classic creation cannot grant negative gold.");
        DaggerfallItemDefinition gold = definitions.RequireItem(new DaggerfallItemId("template-276"));
        return new(gold.Id.Value, 276, checked((ulong)amount), effect.Text);
    }
    private static void RequireChoices(DaggerfallBiographyDefinition biography, IReadOnlyList<DaggerfallBiographyAnswerSave> answers)
    {
        if (answers.Count != biography.Questions.Count || answers.Select(answer => answer.Question).Distinct().Count() != answers.Count) throw new ArgumentException("Choose one answer for every background question.");
        foreach (DaggerfallBiographyQuestionDefinition question in biography.Questions)
            if (!answers.Any(answer => answer.Question == question.Number && question.Answers.Any(value => value.Letter == answer.Letter))) throw new ArgumentException($"Background question {question.Number} has no valid answer.");
    }
    private static void RequireExact(IEnumerable<string> required, IReadOnlyList<DaggerfallCreationAllocationSave> values, string name)
    { if (values.Select(value => value.Id).Distinct().Count() != values.Count || !values.Select(value => value.Id).OrderBy(value => value).SequenceEqual(required.OrderBy(value => value))) throw new ArgumentException($"The {name} do not match the selected career."); }
    private static void RequireAllocations(IEnumerable<string> allowed, IReadOnlyList<DaggerfallCreationAllocationSave> values, int maximum, string name)
    { if (values.Any(value => value.Points <= 0) || values.Select(value => value.Id).Distinct().Count() != values.Count || values.Any(value => !allowed.Contains(value.Id)) || values.Sum(value => value.Points) > maximum) throw new ArgumentException($"The {name} allocations are invalid."); }
    private static DaggerfallBiographyDefinition BiographyFor(DaggerfallDefinitions definitions, DaggerfallCareerDefinition career)
    {
        if (career.Id.StartsWith("class", StringComparison.Ordinal) && int.TryParse(career.Id.AsSpan(5), out int index) && definitions.Biographies.Biographies.SingleOrDefault(value => value.ClassIndex == index) is { } direct) return direct;
        return definitions.Biographies.Biographies.OrderByDescending(value => Affinity(career, definitions.Catalogs.RequireCareer($"class{value.ClassIndex:D2}"))).ThenBy(value => value.ClassIndex).First();
    }
    private static int Affinity(DaggerfallCareerDefinition left, DaggerfallCareerDefinition right) => left.SkillReferences.Count(right.SkillReferences.Contains);
}
