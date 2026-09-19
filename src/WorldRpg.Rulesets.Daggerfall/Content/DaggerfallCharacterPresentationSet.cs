namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Which paper-doll layer a published reference names.</summary>
internal enum DaggerfallCharacterLayerKind
{
    /// <summary>The race's paper-doll background.</summary>
    Background,

    /// <summary>A body without clothing, which the equipment layers draw over.</summary>
    BodyUnclothed,

    /// <summary>A body with clothing already drawn.</summary>
    BodyClothed,

    /// <summary>One of the race and gender's selectable heads.</summary>
    Head,
}

/// <summary>Whether a layer belongs to the male or the female paper doll.</summary>
internal enum DaggerfallCharacterGender
{
    Male,
    Female,
}

/// <summary>
/// One published presentation layer: the media identity a character sheet or social view resolves,
/// the source file it came from, and the palette it is read with.
/// </summary>
/// <param name="Kind">Which layer this is.</param>
/// <param name="Gender">Which paper doll the layer belongs to, or null for a background.</param>
/// <param name="HeadIndex">Which head this is within its race and gender, or -1 when it is not a head.</param>
/// <param name="MediaId">The published identity a consumer references.</param>
/// <param name="SourceFile">The supplied source file the artifact comes from.</param>
/// <param name="Palette">The palette the canvas is read with.</param>
internal sealed record DaggerfallCharacterLayerDefinition(
    DaggerfallCharacterLayerKind Kind,
    DaggerfallCharacterGender? Gender,
    int HeadIndex,
    string MediaId,
    string SourceFile,
    string Palette,
    string Consumer);

/// <summary>
/// One race's published layers, resolved by gender and role rather than by position.
/// </summary>
/// <param name="RaceId">The catalog race identity the layers belong to.</param>
/// <param name="DonorRaceId">The donor's own race value.</param>
/// <param name="Layers">Every layer the pack publishes for the race.</param>
internal sealed record DaggerfallRaceLayers(string RaceId, int DonorRaceId, IReadOnlyList<DaggerfallCharacterLayerDefinition> Layers)
{
    /// <summary>The race's background, which every paper doll draws first.</summary>
    internal DaggerfallCharacterLayerDefinition Background =>
        Single(DaggerfallCharacterLayerKind.Background, null);

    /// <summary>The body a gender wears its equipment over.</summary>
    internal DaggerfallCharacterLayerDefinition Body(DaggerfallCharacterGender gender, bool clothed) =>
        Single(clothed ? DaggerfallCharacterLayerKind.BodyClothed : DaggerfallCharacterLayerKind.BodyUnclothed, gender);

    /// <summary>The heads a gender can choose from, in the order the source supplies them.</summary>
    internal IReadOnlyList<DaggerfallCharacterLayerDefinition> Heads(DaggerfallCharacterGender gender) =>
        [.. Layers.Where(layer => layer.Kind == DaggerfallCharacterLayerKind.Head && layer.Gender == gender).OrderBy(layer => layer.HeadIndex)];

    private DaggerfallCharacterLayerDefinition Single(DaggerfallCharacterLayerKind kind, DaggerfallCharacterGender? gender) =>
        Layers.SingleOrDefault(layer => layer.Kind == kind && layer.Gender == gender)
        ?? throw new InvalidOperationException($"Race '{RaceId}' publishes no {kind} layer for {(gender is null ? "any gender" : gender.ToString()!.ToLowerInvariant())}.");
}

/// <summary>One published faction face a social or escort view resolves by faction index.</summary>
/// <param name="Index">The donor's faction face index.</param>
/// <param name="MediaId">The published identity.</param>
/// <param name="SourceFile">The supplied source file whose cells are the faces.</param>
/// <param name="Palette">The palette the cells are read with.</param>
internal sealed record DaggerfallFactionFaceDefinition(int Index, string MediaId, string SourceFile, string Palette, string Consumer);

/// <summary>One career's class portrait, resolved by career identity.</summary>
/// <param name="CareerId">The catalog career identity.</param>
/// <param name="MediaId">The published identity of the portrait's first frame.</param>
/// <param name="SourceFile">The supplied source file, an animation whose frames are the portrait.</param>
/// <param name="Palette">The palette the frames carry.</param>
/// <param name="FrameCount">How many frames the portrait animates through.</param>
internal sealed record DaggerfallCareerPortraitDefinition(string CareerId, string MediaId, string SourceFile, string Palette, int FrameCount, string Consumer);

/// <summary>One career the pack records as having no portrait, and why.</summary>
/// <param name="CareerId">The catalog career identity.</param>
/// <param name="Reason">Why it has no portrait.</param>
internal sealed record DaggerfallCareerWithoutPortrait(string CareerId, string Reason);

/// <summary>One race the pack records as having no presentation media, and why.</summary>
/// <param name="RaceId">The catalog race identity.</param>
/// <param name="DonorRaceId">The donor's own race value.</param>
/// <param name="Reason">Why the race has no layers.</param>
internal sealed record DaggerfallRaceWithoutMedia(string RaceId, int DonorRaceId, string Reason);

/// <summary>
/// The published character presentation references, resolved by race, gender and layer.
/// </summary>
/// <remarks>
/// This is the consumer side of the character-media publication: a character sheet or social view
/// asks for a race's background, body or heads and gets the published media identity instead of
/// reconstructing a file name from a donor naming convention. Races the corpus cannot draw are
/// carried with their reason rather than being absent, so a caller cannot mistake "no media" for
/// "not looked up".
/// </remarks>
internal sealed class DaggerfallCharacterPresentationSet(
    IReadOnlyDictionary<string, DaggerfallRaceLayers> races,
    IReadOnlyList<DaggerfallFactionFaceDefinition> factionFaces,
    IReadOnlyDictionary<string, DaggerfallCareerPortraitDefinition> careers,
    IReadOnlyList<DaggerfallCareerWithoutPortrait> careersWithoutPortrait,
    IReadOnlyList<DaggerfallRaceWithoutMedia> racesWithoutMedia,
    IReadOnlyList<string> files)
{
    /// <summary>Every race the pack publishes layers for, by catalog race identity.</summary>
    internal IReadOnlyDictionary<string, DaggerfallRaceLayers> Races { get; } = races;

    /// <summary>
    /// The published faction faces, ordered by the donor's faction index, which is what a social or
    /// escort view resolves rather than a race layer.
    /// </summary>
    internal IReadOnlyList<DaggerfallFactionFaceDefinition> FactionFaces { get; } = factionFaces;

    /// <summary>Every career the pack publishes a portrait for, by catalog career identity.</summary>
    internal IReadOnlyDictionary<string, DaggerfallCareerPortraitDefinition> Careers { get; } = careers;

    /// <summary>Careers the pack records as having no portrait, with the reason.</summary>
    internal IReadOnlyList<DaggerfallCareerWithoutPortrait> CareersWithoutPortrait { get; } = careersWithoutPortrait;

    /// <summary>Races the pack records as having no presentation media, with the reason.</summary>
    internal IReadOnlyList<DaggerfallRaceWithoutMedia> RacesWithoutMedia { get; } = racesWithoutMedia;

    /// <summary>Every supplied character file the publication accounted for.</summary>
    internal IReadOnlyList<string> Files { get; } = files;

    /// <summary>Resolves one career's portrait, naming the career when the pack publishes none.</summary>
    internal DaggerfallCareerPortraitDefinition RequirePortrait(string careerId) =>
        Careers.TryGetValue(careerId, out DaggerfallCareerPortraitDefinition? portrait)
            ? portrait
            : throw new InvalidOperationException($"Daggerfall character presentation publishes no portrait for career '{careerId}'.");

    /// <summary>Resolves one race's layers, naming the race when the pack does not publish them.</summary>
    internal DaggerfallRaceLayers RequireRace(string raceId) =>
        Races.TryGetValue(raceId, out DaggerfallRaceLayers? race)
            ? race
            : throw new InvalidOperationException($"Daggerfall character presentation publishes no layers for race '{raceId}'.");
}
