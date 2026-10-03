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
    DaggerfallCustomCareerChoices? CustomCareer = null,
    DaggerfallCharacterBackgroundSave? Background = null,
    string[]? KnownSpells = null)
{
    internal static DaggerfallCharacterCreationChoices From(DaggerfallCharacterIdentity identity) =>
        new(identity.Name, identity.RaceId, identity.Gender, identity.FaceIndex, identity.Reflexes, identity.CareerId);

    internal DaggerfallCharacterIdentity ToIdentity() => new(Name.Trim(), RaceId, Gender, FaceIndex, Reflexes, CareerId);
}

/// <summary>
/// Daggerfall's player-character choice owner. It resolves choices through normalized records,
/// keeps drafts separate, and applies only committed career attribute bases to Mechanics.
/// </summary>
internal sealed partial class DaggerfallCharacterState
{
    private readonly DaggerfallDefinitions _definitions;
    private readonly StatsComponent _stats;
    private Action? _careerCommitted;
    private DaggerfallCustomCareerDefinition? _customCareer;
    private DaggerfallCharacterBackgroundSave? _background;
    private readonly HashSet<string> _knownSpells = new(StringComparer.Ordinal);
    private int _backgroundRollSequence;
    private readonly DaggerfallCharacterIdentity _initialIdentity;

    internal DaggerfallCharacterState(DaggerfallDefinitions definitions, StatsComponent stats, DaggerfallActorDefinition player, DaggerfallCharacterSave? restored = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (string known in restored?.KnownSpells ?? [])
        {
            if (!definitions.Magic.Spells.ContainsKey(known))
                throw new ArgumentException($"Saved character knows '{known}', which no published spell answers.", nameof(restored));
            _ = _knownSpells.Add(known);
        }

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
        _background = restored?.Background;
        _backgroundRollSequence = _background?.RollSequence ?? 0;
        _initialIdentity = new("Nameless", player.Race!, DaggerfallCharacterGender.Male, 0, DaggerfallCharacterReflexes.Average, player.Career!);
        Validate(Identity);
        if (_background is not null)
            _background = DaggerfallCharacterBackgroundPolicy.RequireComplete(_definitions, Career, Identity, _background);
        // A restored Mechanics boundary already carries the player's progressed permanent bases.
        // Creation and later committed choices set the authored career bases; loading must not
        // overwrite progression merely because it revalidates the same identity.
        if (restored is null) ApplyCareerBases();
    }

    internal DaggerfallCharacterIdentity Identity { get; private set; }
    internal DaggerfallCharacterCreationChoices? Pending { get; private set; }
    internal DaggerfallCareerDefinition Career => _customCareer?.Career ?? _definitions.Catalogs.RequireCareer(Identity.CareerId);
    internal DaggerfallCustomCareerDefinition? CustomCareer => _customCareer;
    internal DaggerfallCharacterBackgroundSave? Background => _background;

    /// <summary>
    /// The spells this character has learned, by the catalogue key they were learned under. Casting resolves
    /// through this list rather than through a classic identity, because a classic identity is not unique —
    /// several published spells share one — while a key names exactly one compiled definition.
    /// </summary>
    internal IReadOnlyCollection<string> KnownSpells => _knownSpells;

    /// <summary>
    /// Records that the character has learned a spell the catalogue publishes. Learning the same spell twice
    /// is the same as learning it once; a key nothing publishes is refused rather than stored as a spell that
    /// can never be cast.
    /// </summary>
    internal DaggerfallSpellDefinition[] CaptureConstructedSpells() =>
        [.. _definitions.Magic.Spells.Values.Where(spell => spell.IsPlayerCreated).OrderBy(spell => spell.Key, StringComparer.Ordinal)];

    internal bool LearnSpell(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_definitions.Magic.Spells.ContainsKey(key))
            throw new ArgumentException($"No published spell carries the key '{key}'.", nameof(key));
        return _knownSpells.Add(key);
    }

    /// <summary>
    /// Resolves a spell the character has learned to the compiled definition casting works from. This is the
    /// whole of the lookup: the catalogue is compiled into the product, so there is nothing to load, discover
    /// or reflect over — only the character's own list to check and the published definition to answer with.
    /// An unlearned key and a key nothing publishes are separate refusals, because they mean different things
    /// to a caller: the first is a cast the character cannot make, the second is data that cannot exist.
    /// </summary>
    internal DaggerfallSpellDefinition RequireKnownSpell(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_definitions.Magic.Spells.TryGetValue(key, out DaggerfallSpellDefinition? spell) || spell is null)
            throw new ArgumentException($"No published spell carries the key '{key}'.", nameof(key));
        if (!_knownSpells.Contains(key))
            throw new InvalidOperationException($"This character has not learned the spell '{key}'.");
        return spell;
    }

    /// <summary>Forgets a spell the character knows, reporting whether it knew it.</summary>
    internal event Action<string>? SpellForgotten;
    internal bool ForgetSpell(string key)
    {
        if(!_knownSpells.Remove(key)) return false;
        SpellForgotten?.Invoke(key); return true;
    }
    /// <summary>The committed BIOG text retained with this character, distinct from an editable draft.</summary>
    internal IReadOnlyList<string> History => _background?.Biography ?? [];
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
        DaggerfallCharacterCreationChoices source = Pending ?? DaggerfallCharacterCreationChoices.From(Identity) with { Background = _background };
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
            career.Id, career.Name, _definitions.NewGame.Careers.Any(value => value.Career == career.Id),
            _definitions.NewGame.Careers.All(value => value.Career != career.Id) ? "This career is not selectable for a new player character." : null))
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
        DaggerfallCharacterBackgroundPresentation? background = current.Background is { } backgroundDraft && (Pending is not null || _background is not null)
            ? DaggerfallCharacterBackgroundPolicy.Present(_definitions, CurrentCareer(current), current.ToIdentity(), backgroundDraft) : null;
        return new DaggerfallCharacterCreationPresentation(Pending is not null, current, races, careers, faces, reflexes, custom, background, CreationMode, ReadClassQuiz(), _definitions.Catalogs.ClassQuestionnaire is not null,
            Pending is null && _background is not null ? NewGameSummary() : null);
    }

    internal void BeginChoices() { _classQuestions = null; Pending = DaggerfallCharacterCreationChoices.From(Identity) with { Background = _background }; }

    /// <summary>Opening the title-screen flow captures its Engine-random rolls exactly once.</summary>
    internal void BeginChoices(Rusty.Engine.IRandomService random)
    {
        BeginChoices();
        if (_background is null)
            Pending = Pending! with { Background = DaggerfallCharacterBackgroundPolicy.Roll(_definitions, Career, Identity, random, NextBackgroundRollSequence()) };
    }

    internal void BeginFreshChoices(Rusty.Engine.IRandomService random)
    {
        AbandonCreation();
        BeginChoices();
        Pending = Pending! with { Background = DaggerfallCharacterBackgroundPolicy.Roll(_definitions, Career, Identity, random, NextBackgroundRollSequence()) };
    }

    internal void AbandonCreation()
    {
        CancelChoices(); _background = null; _customCareer = null; _knownSpells.Clear();
        Identity = _initialIdentity; ApplyCareerBases();
    }

    private string[] NewGameSummary()
    {
        var config = _definitions.NewGame;
        var items = _customCareer is null ? config.Careers.Single(value => value.Career == Career.Id).Items : config.CustomItems;
        var spells = _customCareer is null ? config.Careers.Single(value => value.Career == Career.Id).Spells
            : Career.PrimarySkills.Concat(Career.MajorSkills).Any(skill => skill is "destruction" or "restoration" or "illusion" or "alteration" or "thaumaturgy" or "mysticism")
                ? config.CustomMagicSpells : [];
        return [ $"{Identity.Name} — {Identity.RaceId}, {Identity.Gender.ToString().ToLowerInvariant()}, {Career.Name}; reflexes {Identity.Reflexes}.",
            .. Career.Attributes.Select(id => $"{id}: {_stats.GetStat(StatId.Parse(id)).ValueInt}"),
            .. GrantedSkills.Select(skill => $"{skill.SkillId}: {_stats.GetStat(StatId.Parse(skill.SkillId)).ValueInt} ({skill.Tier})"),
            .. _stats.Tracks.Select(pair => $"{pair.Key.Value}: {pair.Value.Maximum.ValueInt}"),
            "Starting clothing and spellbook.", $"{config.Gold} gold plus biography grants.",
            .. items.Select(item => $"{item.Quantity} × {item.Material} {_definitions.ItemTemplateCatalog.Templates[item.Template].Name}"),
            .. _background!.StartingGrants.Select(item => $"{item.Quantity} × {_definitions.RequireItem(new DaggerfallItemId(item.ItemId)).Template!.Name}"),
            .. spells.Select(key => $"Spell: {_definitions.Magic.Spells[key].Name}") ];
    }

    internal void RerollBackground(Rusty.Engine.IRandomService random)
    {
        DaggerfallCharacterCreationChoices current = Pending ?? throw new ArgumentException("Open character choices before rerolling the background.");

        DaggerfallCharacterIdentity identity = current.ToIdentity(); Validate(identity);
        DaggerfallCareerDefinition career = CurrentCareer(current);
        Pending = current with { Background = DaggerfallCharacterBackgroundPolicy.Roll(_definitions, career, identity, random, NextBackgroundRollSequence()) };
    }

    internal void ReplacePending(DaggerfallCharacterCreationChoices choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        Pending = choices;
    }

    internal void CancelChoices() { Pending = null; _classQuestions = null; }

    internal DaggerfallCharacterBackgroundSave? CommitChoices(bool replaceCommitted = false)
    {
        if (_classQuestions is not null) throw new ArgumentException("Complete or leave the class questions before committing character choices.");
        DaggerfallCharacterCreationChoices choices = Pending
            ?? throw new InvalidOperationException("There is no character-creation draft to commit.");
        DaggerfallCharacterIdentity committed = choices.ToIdentity();
        Validate(committed);
        DaggerfallCustomCareerDefinition? custom = committed.CareerId == DaggerfallCustomCareerPolicy.CareerId
            ? DaggerfallCustomCareerPolicy.Compile(_definitions, choices.CustomCareer ?? throw new ArgumentException("Custom class fields are incomplete."), Career)
            : null;
        DaggerfallCareerDefinition committedCareer = custom?.Career ?? _definitions.Catalogs.RequireCareer(committed.CareerId);
        if (custom is null && !_definitions.NewGame.Careers.Any(value => value.Career == committed.CareerId))
            throw new ArgumentException("This career is not selectable for a new player character.");
        DaggerfallCharacterBackgroundSave? background = choices.Background is null ? null : DaggerfallCharacterBackgroundPolicy.RequireComplete(_definitions, committedCareer, committed, choices.Background);
        if (!replaceCommitted && _background is not null && background is not null)
            throw new ArgumentException("Character creation has already been committed.");
        Identity = committed;
        _customCareer = custom;
        bool firstBackgroundCommit = _background is null && background is not null;
        _background = replaceCommitted ? background : _background ?? background;
        Pending = null;
        ApplyCareerBases();
        _careerCommitted?.Invoke();
        return firstBackgroundCommit ? background : null;
    }

    /// <summary>Lets the session rebase career-dependent progression after an admitted selection.</summary>
    internal void BindCareerCommitted(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _careerCommitted = callback;
    }

    internal DaggerfallCharacterSave Capture() => new(
        Identity.Name, Identity.RaceId, Identity.Gender, Identity.FaceIndex, Identity.Reflexes, Identity.CareerId, _customCareer is null ? null : ToChoices(_customCareer), _background,
        [.. _knownSpells.Order(StringComparer.Ordinal)]);

    private void Validate(DaggerfallCharacterIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Name))
            throw new ArgumentException("A character name cannot be empty.", nameof(identity));
        if (!Enum.IsDefined(identity.Gender) || !Enum.IsDefined(identity.Reflexes))
            throw new ArgumentOutOfRangeException(nameof(identity), "Character gender or reflexes are not supported.");
        if (!_definitions.Catalogs.TryGetRace(identity.RaceId, out _))
            throw new ArgumentException($"Race '{identity.RaceId}' is not published.");
        if (identity.CareerId != DaggerfallCustomCareerPolicy.CareerId && !_definitions.Catalogs.TryGetCareer(identity.CareerId, out _))
            throw new ArgumentException($"Career '{identity.CareerId}' is not published.");
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
        if (_background is not null) DaggerfallCharacterBackgroundPolicy.ApplyStats(_definitions, career, _background, _stats);
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(_stats, career);
    }

    private DaggerfallCareerDefinition CurrentCareer(DaggerfallCharacterCreationChoices choices) => choices.CareerId == DaggerfallCustomCareerPolicy.CareerId
        ? DaggerfallCustomCareerPolicy.Compile(_definitions, choices.CustomCareer ?? throw new ArgumentException("Custom class fields are incomplete."), Career).Career
        : _definitions.Catalogs.RequireCareer(choices.CareerId);

    private int NextBackgroundRollSequence() => _backgroundRollSequence = checked(_backgroundRollSequence + 1);

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
    DaggerfallCustomCareerPresentation? Custom = null, DaggerfallCharacterBackgroundPresentation? Background = null, string? Mode = null, DaggerfallClassQuizPresentation? ClassQuiz = null, bool ClassQuestionsAvailable = false, string[]? Summary = null);

/// <summary>Current-schema durable identity. Definition keys are resolved before a session is built.</summary>
internal sealed record DaggerfallCharacterSave(
    string Name,
    string RaceId,
    DaggerfallCharacterGender Gender,
    int FaceIndex,
    DaggerfallCharacterReflexes Reflexes,
    string CareerId,
    DaggerfallCustomCareerChoices? CustomCareer = null,
    DaggerfallCharacterBackgroundSave? Background = null,
    string[]? KnownSpells = null)
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
        if (Background is not null)
        {
            DaggerfallCareerDefinition career = identity.CareerId == DaggerfallCustomCareerPolicy.CareerId
                ? DaggerfallCustomCareerPolicy.Compile(definitions, CustomCareer!, definitions.Catalogs.RequireCareer("class00")).Career
                : definitions.Catalogs.RequireCareer(identity.CareerId);
            _ = DaggerfallCharacterBackgroundPolicy.RequireComplete(definitions, career, identity, Background);
        }
        if (!definitions.CharacterPresentation.RequireRace(identity.RaceId).Heads(identity.Gender).Any(head => head.HeadIndex == identity.FaceIndex))
            throw new ArgumentException($"Saved character identity names unavailable face {identity.FaceIndex} for race '{identity.RaceId}'.");
    }

    internal DaggerfallCharacterIdentity Resolve(DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return new DaggerfallCharacterIdentity(Name?.Trim() ?? string.Empty, RaceId ?? string.Empty, Gender, FaceIndex, Reflexes, CareerId ?? string.Empty);
    }
}
