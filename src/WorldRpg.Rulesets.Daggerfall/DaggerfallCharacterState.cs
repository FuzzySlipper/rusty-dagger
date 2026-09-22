using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Classic player reflex choices retain the donor's numeric ordering for formula policy.</summary>
internal enum DaggerfallCharacterReflexes
{
    VeryHigh = 0,
    High = 1,
    Average = 2,
    Low = 3,
    VeryLow = 4,
}

/// <summary>The committed player identity. Only this value affects the live session and saves.</summary>
internal sealed record DaggerfallCharacterIdentity(
    string Name,
    string RaceId,
    DaggerfallCharacterGender Gender,
    int FaceIndex,
    DaggerfallCharacterReflexes Reflexes,
    string CareerId);

/// <summary>A cancellable edit of player identity; it has no gameplay effects until committed.</summary>
internal sealed record DaggerfallCharacterCreationChoices(
    string Name,
    string RaceId,
    DaggerfallCharacterGender Gender,
    int FaceIndex,
    DaggerfallCharacterReflexes Reflexes,
    string CareerId,
    DaggerfallCustomCareerChoices? CustomCareer = null)
{
    internal static DaggerfallCharacterCreationChoices From(DaggerfallCharacterIdentity identity) =>
        new(identity.Name, identity.RaceId, identity.Gender, identity.FaceIndex, identity.Reflexes, identity.CareerId);

    internal DaggerfallCharacterIdentity ToIdentity() => new(Name.Trim(), RaceId, Gender, FaceIndex, Reflexes, CareerId);
}

/// <summary>
/// Daggerfall's player-character choice owner. It resolves choices through normalized records,
/// keeps drafts separate, and applies only committed career attribute bases to Mechanics.
/// </summary>
internal sealed class DaggerfallCharacterState
{
    private readonly DaggerfallDefinitions _definitions;
    private readonly StatsComponent _stats;
    private Action? _careerCommitted;
    private DaggerfallCustomCareerDefinition? _customCareer;

    internal DaggerfallCharacterState(DaggerfallDefinitions definitions, StatsComponent stats, DaggerfallActorDefinition player, DaggerfallCharacterSave? restored = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(player);
        _definitions = definitions;
        _stats = stats;
        Identity = restored is null
            ? new DaggerfallCharacterIdentity(
                "Nameless",
                player.Race ?? throw new InvalidOperationException("The player definition must name a race."),
                DaggerfallCharacterGender.Male,
                0,
                (DaggerfallCharacterReflexes)_stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Reflexes.Value)).BaseValue,
                player.Career ?? throw new InvalidOperationException("The player definition must name a career."))
            : restored.Resolve(definitions);
        _customCareer = restored?.CustomCareer is { } custom
            ? DaggerfallCustomCareerPolicy.Compile(definitions, custom, definitions.Catalogs.RequireCareer("class00"))
            : null;
        Validate(Identity);
        // A restored Mechanics boundary already carries the player's progressed permanent bases.
        // Creation and later committed choices set the authored career bases; loading must not
        // overwrite progression merely because it revalidates the same identity.
        if (restored is null) ApplyCareerBases();
    }

    internal DaggerfallCharacterIdentity Identity { get; private set; }
    internal DaggerfallCharacterCreationChoices? Pending { get; private set; }
    internal DaggerfallCareerDefinition Career => _customCareer?.Career ?? _definitions.Catalogs.RequireCareer(Identity.CareerId);
    internal DaggerfallCustomCareerDefinition? CustomCareer => _customCareer;
    internal DaggerfallRaceDefinition Race => _definitions.Catalogs.RequireRace(Identity.RaceId);

    internal IReadOnlyList<DaggerfallCareerSkillGrant> GrantedSkills =>
    [
        .. Career.PrimarySkills.Select(skill => new DaggerfallCareerSkillGrant(skill, DaggerfallCareerSkillTier.Primary)),
        .. Career.MajorSkills.Select(skill => new DaggerfallCareerSkillGrant(skill, DaggerfallCareerSkillTier.Major)),
        .. Career.MinorSkills.Select(skill => new DaggerfallCareerSkillGrant(skill, DaggerfallCareerSkillTier.Minor)),
    ];

    /// <summary>The normalized selectable records and current draft, projected without a UI-owned choice list.</summary>
    internal DaggerfallCharacterCreationPresentation ReadCreation()
    {
        DaggerfallCharacterCreationChoices source = Pending ?? DaggerfallCharacterCreationChoices.From(Identity);
        DaggerfallCharacterCreationChoices current = source with
        {
            CustomCareer = source.CustomCareer ?? (_customCareer is { } storedCustom
                ? ToChoices(storedCustom)
                : DaggerfallCustomCareerChoices.Default(_definitions, Career)),
        };
        DaggerfallCharacterChoice[] races = _definitions.Catalogs.Races.Select(race =>
        {
            bool available = _definitions.CharacterPresentation.Races.TryGetValue(race.Id, out DaggerfallRaceLayers? layers)
                && layers.Heads(DaggerfallCharacterGender.Male).Count != 0 && layers.Heads(DaggerfallCharacterGender.Female).Count != 0;
            string? restriction = available ? null : _definitions.CharacterPresentation.RacesWithoutMedia
                .FirstOrDefault(value => value.RaceId == race.Id)?.Reason ?? "No complete character media is published.";
            return new DaggerfallCharacterChoice(race.Id, race.Id, available, restriction);
        }).ToArray();
        DaggerfallCharacterChoice[] careers = _definitions.Catalogs.Careers.Select(career => new DaggerfallCharacterChoice(
            career.Id, career.Name, true,
            _definitions.CharacterPresentation.CareersWithoutPortrait.FirstOrDefault(value => value.CareerId == career.Id)?.Reason))
            .Append(new DaggerfallCharacterChoice(DaggerfallCustomCareerPolicy.CareerId, "Custom class", true, null)).ToArray();
        DaggerfallCharacterFaceChoice[] faces = _definitions.CharacterPresentation.Races.TryGetValue(current.RaceId, out DaggerfallRaceLayers? selected)
            ? [.. selected.Heads(current.Gender).Select(face => new DaggerfallCharacterFaceChoice(face.HeadIndex, face.MediaId))] : [];
        DaggerfallCharacterReflexChoice[] reflexes = Enum.GetValues<DaggerfallCharacterReflexes>()
            .Select(value => new DaggerfallCharacterReflexChoice((int)value, value switch
            {
                DaggerfallCharacterReflexes.VeryHigh => "Very high",
                DaggerfallCharacterReflexes.High => "High",
                DaggerfallCharacterReflexes.Average => "Average",
                DaggerfallCharacterReflexes.Low => "Low",
                _ => "Very low",
            })).ToArray();
        DaggerfallCustomCareerPresentation? custom = (Pending is not null || current.CareerId == DaggerfallCustomCareerPolicy.CareerId) && current.CustomCareer is { } draft
            ? new(draft, [.. DaggerfallCustomCareerPolicy.Validate(_definitions, draft)], [.. _definitions.Catalogs.Skills.Select(skill => skill.Id)], [.. DaggerfallCustomCareerPolicy.SupportedAdvantages], [.. DaggerfallCustomCareerPolicy.SupportedDisadvantages])
            : null;
        return new DaggerfallCharacterCreationPresentation(Pending is not null, current, races, careers, faces, reflexes, custom);
    }

    internal void BeginChoices() => Pending = DaggerfallCharacterCreationChoices.From(Identity);

    internal void ReplacePending(DaggerfallCharacterCreationChoices choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        Pending = choices;
    }

    internal void CancelChoices() => Pending = null;

    internal void CommitChoices()
    {
        DaggerfallCharacterCreationChoices choices = Pending
            ?? throw new InvalidOperationException("There is no character-creation draft to commit.");
        DaggerfallCharacterIdentity committed = choices.ToIdentity();
        Validate(committed);
        DaggerfallCustomCareerDefinition? custom = committed.CareerId == DaggerfallCustomCareerPolicy.CareerId
            ? DaggerfallCustomCareerPolicy.Compile(_definitions, choices.CustomCareer ?? throw new ArgumentException("Custom class fields are incomplete."), Career)
            : null;
        Identity = committed;
        _customCareer = custom;
        Pending = null;
        ApplyCareerBases();
        _careerCommitted?.Invoke();
    }

    /// <summary>Lets the session rebase career-dependent progression after an admitted selection.</summary>
    internal void BindCareerCommitted(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _careerCommitted = callback;
    }

    internal DaggerfallCharacterSave Capture() => new(
        Identity.Name, Identity.RaceId, Identity.Gender, Identity.FaceIndex, Identity.Reflexes, Identity.CareerId, _customCareer is null ? null : ToChoices(_customCareer));

    private void Validate(DaggerfallCharacterIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Name))
            throw new ArgumentException("A character name cannot be empty.", nameof(identity));
        if (!Enum.IsDefined(identity.Gender) || !Enum.IsDefined(identity.Reflexes))
            throw new ArgumentOutOfRangeException(nameof(identity), "Character gender or reflexes are not supported.");
        _ = _definitions.Catalogs.RequireRace(identity.RaceId);
        if (identity.CareerId != DaggerfallCustomCareerPolicy.CareerId) _ = _definitions.Catalogs.RequireCareer(identity.CareerId);
        DaggerfallRaceLayers layers = _definitions.CharacterPresentation.RequireRace(identity.RaceId);
        if (!layers.Heads(identity.Gender).Any(head => head.HeadIndex == identity.FaceIndex))
            throw new ArgumentException($"Race '{identity.RaceId}' has no {identity.Gender.ToString().ToLowerInvariant()} face {identity.FaceIndex}.", nameof(identity));
    }

    private void ApplyCareerBases()
    {
        DaggerfallCareerDefinition career = Career;
        if (career.Attributes.Count != career.AttributeValues.Count)
            throw new InvalidOperationException($"Career '{career.Id}' has incompatible attribute keys and values.");
        for (int index = 0; index < career.Attributes.Count; index++)
            _stats.GetStat(StatId.Parse(career.Attributes[index])).BaseValue = career.AttributeValues[index];
        _stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Reflexes.Value)).BaseValue = (int)Identity.Reflexes;
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(_stats, career);
    }

    private static DaggerfallCustomCareerChoices ToChoices(DaggerfallCustomCareerDefinition custom) => new(
        custom.Career.Name, [.. custom.Career.PrimarySkills], [.. custom.Career.MajorSkills], [.. custom.Career.MinorSkills], custom.Career.HitPointsPerLevel,
        custom.Advantages, custom.Disadvantages);
}

/// <summary>The supplied career's skill groups are grants to progression, not invented numeric rolls.</summary>
internal enum DaggerfallCareerSkillTier { Primary, Major, Minor }
internal sealed record DaggerfallCareerSkillGrant(string SkillId, DaggerfallCareerSkillTier Tier);
internal sealed record DaggerfallCharacterChoice(string Id, string Label, bool Available, string? Restriction);
internal sealed record DaggerfallCharacterFaceChoice(int Index, string MediaId);
internal sealed record DaggerfallCharacterReflexChoice(int Value, string Label);
internal sealed record DaggerfallCharacterCreationPresentation(bool Editing, DaggerfallCharacterCreationChoices Current,
    DaggerfallCharacterChoice[] Races, DaggerfallCharacterChoice[] Careers, DaggerfallCharacterFaceChoice[] Faces, DaggerfallCharacterReflexChoice[] Reflexes,
    DaggerfallCustomCareerPresentation? Custom = null);

/// <summary>Current-schema durable identity. Definition keys are resolved before a session is built.</summary>
internal sealed record DaggerfallCharacterSave(
    string Name,
    string RaceId,
    DaggerfallCharacterGender Gender,
    int FaceIndex,
    DaggerfallCharacterReflexes Reflexes,
    string CareerId,
    DaggerfallCustomCareerChoices? CustomCareer = null)
{
    internal void Validate(DaggerfallDefinitions definitions)
    {
        DaggerfallCharacterIdentity identity = Resolve(definitions);
        if (string.IsNullOrWhiteSpace(identity.Name))
            throw new ArgumentException("Saved character identity has an empty name.");
        if (!Enum.IsDefined(identity.Gender) || !Enum.IsDefined(identity.Reflexes))
            throw new ArgumentOutOfRangeException(nameof(Gender), "Saved character identity has an unsupported gender or reflexes value.");
        _ = definitions.Catalogs.RequireRace(identity.RaceId);
        if (identity.CareerId == DaggerfallCustomCareerPolicy.CareerId)
            _ = DaggerfallCustomCareerPolicy.Compile(definitions, CustomCareer ?? throw new ArgumentException("Saved custom class has no class data."), definitions.Catalogs.RequireCareer("class00"));
        else _ = definitions.Catalogs.RequireCareer(identity.CareerId);
        if (!definitions.CharacterPresentation.RequireRace(identity.RaceId).Heads(identity.Gender).Any(head => head.HeadIndex == identity.FaceIndex))
            throw new ArgumentException($"Saved character identity names unavailable face {identity.FaceIndex} for race '{identity.RaceId}'.");
    }

    internal DaggerfallCharacterIdentity Resolve(DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return new DaggerfallCharacterIdentity(Name?.Trim() ?? string.Empty, RaceId ?? string.Empty, Gender, FaceIndex, Reflexes, CareerId ?? string.Empty);
    }
}
