using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

/// <summary>One presentation layer a race is drawn from, as published pack data.</summary>
/// <param name="Race">The catalog race identity the layer belongs to.</param>
/// <param name="DonorRaceId">The donor's own race value, which is one-based.</param>
/// <param name="Layer">The role: a background, a body, or a numbered head.</param>
/// <param name="MediaId">The stable identity of the canvas, as the publication derives it.</param>
/// <param name="SourceFile">The supplied source file the artifact comes from.</param>
/// <param name="Palette">The palette the canvas is read with.</param>
/// <param name="Binding">Whether a published consumer binds it, or it is still required-pending.</param>
public sealed record DaggerfallCharacterLayer(
    string Race,
    int DonorRaceId,
    string Layer,
    string MediaId,
    string SourceFile,
    string Palette,
    MediaBinding Binding,
    string Consumer);

/// <summary>What the publication did with one supplied character file.</summary>
public enum CharacterFileOutcome
{
    /// <summary>A layer in this section is drawn from the file's canvases.</summary>
    Referenced,

    /// <summary>The file reads and no layer in this section uses it.</summary>
    Unreferenced,

    /// <summary>Nothing in this repository reads the file's format.</summary>
    Unreadable,
}

/// <summary>
/// One supplied character file's place in the publication, so a file cannot disappear between the
/// inventory and the pack without a record of which one and why.
/// </summary>
/// <param name="Path">The supplied source file.</param>
/// <param name="Family">The documented family it belongs to.</param>
/// <param name="CanvasCount">The canvases it carries, or zero when nothing reads it.</param>
/// <param name="Outcome">Whether a layer uses it, it is unused, or it cannot be read.</param>
/// <param name="Reason">Why the outcome holds, naming the reader for an unreadable format.</param>
public sealed record DaggerfallCharacterFile(string Path, string Family, int CanvasCount, CharacterFileOutcome Outcome, string Reason);

/// <summary>
/// One published faction face: the identity a social or escort view resolves by faction index.
/// </summary>
/// <param name="Index">The donor's faction face index.</param>
/// <param name="MediaId">The published identity.</param>
/// <param name="SourceFile">The supplied source file, whose cells are the faces.</param>
/// <param name="Palette">The palette the cells are read with.</param>
/// <param name="Binding">Whether a consumer binds it, or it is still required-pending.</param>
/// <param name="Consumer">The consumer the inventory recorded, which states that none binds it yet when none does.</param>
public sealed record DaggerfallFactionFace(int Index, string MediaId, string SourceFile, string Palette, MediaBinding Binding, string Consumer);

/// <summary>
/// One career's portrait: the class art the character-creation and sheet views draw.
/// </summary>
/// <param name="CareerId">The catalog career identity.</param>
/// <param name="MediaId">The published identity of the portrait's first frame.</param>
/// <param name="SourceFile">The supplied source file, an animation whose frames are the portrait.</param>
/// <param name="Palette">The palette the frames carry.</param>
/// <param name="FrameCount">How many frames the portrait animates through.</param>
/// <param name="Binding">Whether a consumer binds it, or it is still required-pending.</param>
/// <param name="Consumer">The consumer the inventory recorded, which states that none binds it yet when none does.</param>
public sealed record DaggerfallCareerPortrait(string CareerId, string MediaId, string SourceFile, string Palette, int FrameCount, MediaBinding Binding, string Consumer);

/// <summary>A career the corpus supplies no portrait for, kept explicit.</summary>
/// <param name="CareerId">The catalog career identity.</param>
/// <param name="Reason">Why it has no portrait.</param>
public sealed record DaggerfallCareerWithoutPortrait(string CareerId, string Reason);

/// <summary>A race the corpus supplies no presentation media for, kept explicit.</summary>
/// <param name="Race">The catalog race identity.</param>
/// <param name="DonorRaceId">The donor's own race value.</param>
/// <param name="Reason">Why it has no layers, naming what would have to supply them.</param>
public sealed record DaggerfallRaceWithoutMedia(string Race, int DonorRaceId, string Reason);

/// <summary>
/// The character presentation section of the pack: every race's paper-doll layers as data a
/// character or social consumer resolves, plus the races the corpus supplies no media for.
/// </summary>
/// <param name="SchemaVersion">Shape version of this section.</param>
/// <param name="Layers">Every published layer, ordered by race then layer.</param>
/// <param name="RacesWithoutMedia">Races no supplied media draws.</param>
/// <param name="Sources">Inventory identities this section was built from.</param>
public sealed record DaggerfallCharacterPresentation(
    int SchemaVersion,
    IReadOnlyList<DaggerfallCharacterLayer> Layers,
    IReadOnlyList<DaggerfallFactionFace> Faces,
    IReadOnlyList<DaggerfallCareerPortrait> Careers,
    IReadOnlyList<DaggerfallCareerWithoutPortrait> CareersWithoutPortrait,
    IReadOnlyList<DaggerfallCharacterFile> Files,
    IReadOnlyList<DaggerfallRaceWithoutMedia> RacesWithoutMedia,
    IReadOnlyList<string> Sources)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Checks the section against the canvases the publication actually emitted.
    /// </summary>
    /// <remarks>
    /// The rule is the binding's own meaning rather than "everything must resolve": a reference a published
    /// consumer binds has to resolve to an artifact, or the pack would claim a live consumer draws art that
    /// was never emitted, while a reference that stays required-pending is a stated gap and is allowed to
    /// have no artifact. Every reference is checked, not only the layers: a face or a career whose media
    /// never got published is the same dangling reference, and one a reader would only discover in a session.
    /// </remarks>
    /// <param name="publishedMediaIds">The media identities the publication emitted.</param>
    public void Validate(IReadOnlySet<string> publishedMediaIds)
    {
        ArgumentNullException.ThrowIfNull(publishedMediaIds);
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Character presentation schema must be {CurrentSchemaVersion}.");
        }

        foreach (DaggerfallCharacterLayer layer in Layers)
        {
            NormalizedImportDocument.RequireLogicalId(layer.Race, nameof(layer.Race));
            NormalizedImportDocument.RequireLogicalId(layer.MediaId, nameof(layer.MediaId));
            ArgumentException.ThrowIfNullOrWhiteSpace(layer.Layer);
            ArgumentException.ThrowIfNullOrWhiteSpace(layer.SourceFile);
            ArgumentException.ThrowIfNullOrWhiteSpace(layer.Palette);
            RequireBoundReference(publishedMediaIds, layer.Binding, layer.MediaId, $"Race '{layer.Race}' layer '{layer.Layer}'");
        }

        foreach (DaggerfallFactionFace face in Faces)
        {
            NormalizedImportDocument.RequireLogicalId(face.MediaId, nameof(face.MediaId));
            ArgumentException.ThrowIfNullOrWhiteSpace(face.SourceFile);
            ArgumentException.ThrowIfNullOrWhiteSpace(face.Palette);
            RequireBoundReference(publishedMediaIds, face.Binding, face.MediaId, $"Faction face {face.Index}");
        }

        foreach (DaggerfallCareerPortrait portrait in Careers)
        {
            NormalizedImportDocument.RequireLogicalId(portrait.MediaId, nameof(portrait.MediaId));
            ArgumentException.ThrowIfNullOrWhiteSpace(portrait.SourceFile);
            ArgumentException.ThrowIfNullOrWhiteSpace(portrait.Palette);
            RequireBoundReference(publishedMediaIds, portrait.Binding, portrait.MediaId, $"Career '{portrait.CareerId}' portrait");
        }
    }

    /// <summary>
    /// Refuses a reference a consumer binds whose canvas was never published, and says nothing about one
    /// that stays pending: a pending reference is a stated gap, not a dangling one.
    /// </summary>
    private static void RequireBoundReference(IReadOnlySet<string> publishedMediaIds, MediaBinding binding, string mediaId, string subject)
    {
        if (binding == MediaBinding.Admitted && !publishedMediaIds.Contains(mediaId))
        {
            throw new InvalidOperationException(
                $"{subject} names media '{mediaId}', which is bound by a published consumer but that no published canvas carries; the reference would resolve to nothing.");
        }
    }

}

/// <summary>Compares a source file name the way the inventory admits one: without regard to case.</summary>
internal sealed class FileAndCanvas : IEqualityComparer<(string File, int Canvas)>
{
    public bool Equals((string File, int Canvas) left, (string File, int Canvas) right) =>
        left.Canvas == right.Canvas && string.Equals(left.File, right.File, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string File, int Canvas) value) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.File), value.Canvas);
}

/// <summary>
/// Builds the character presentation section from the character-media inventory and the catalog's
/// races.
/// </summary>
/// <remarks>
/// The donor's race values are one-based (Breton is 1) while its paper-doll file names are zero-based
/// (<c>BODY00I0.IMG</c> is Breton's male body), so the layer a race is drawn from is its donor value
/// minus one. Values outside the eight playable races have no paper-doll subclass at all — the donor's
/// list continues with vampire and werewolf forms that own no media — and those races are recorded as
/// having none rather than being mapped onto the next index, which would draw one race with another's
/// art.
/// </remarks>
public static class DaggerfallCharacterPresentationBuilder
{
    /// <summary>Race values the donor supplies paper-doll media for: the eight playable races.</summary>
    public const int FirstDonorRaceId = 1;

    /// <summary>The last donor race value with paper-doll media.</summary>
    public const int LastDonorRaceId = 8;

    /// <summary>Heads the donor's face CIF supplies per race and gender.</summary>
    public const int HeadsPerRaceAndGender = 10;

    /// <summary>
    /// The consumer a reference publishes: the one the inventory recorded, or the inventory's own
    /// label for a file no consumer binds yet.
    /// </summary>
    /// <remarks>
    /// A published artifact states who claims it, and "nobody yet" is a statement rather than an empty
    /// field: an empty one cannot be told apart from a builder that forgot to carry the fact, which is
    /// what the requirement lane found when the records dropped the consumer entirely.
    /// </remarks>
    private static string ConsumerOf(CharacterCanvasReference canvas) =>
        canvas.Consumer is { Length: > 0 } consumer ? consumer : CharacterMediaInventory.UnstatedConsumer;

    /// <param name="inventory">The supplied files, as the inventory enumerated them.</param>
    /// <param name="suppliedPalettes">The palette files the caller supplies, by file name.</param>
    /// <param name="races">The catalog races a layer is keyed by.</param>
    /// <param name="careers">The catalog careers a portrait is matched to, by identity and name.</param>
    /// <param name="unpublishable">
    /// The supplied files whose canvases the publication could not emit, by file name, with the reason. A
    /// file here is recorded as unreadable even though its container was read: a format this repository
    /// parses but cannot take pixels from supplies no canvas, and calling it merely unused would say the
    /// opposite of what the generated media index states.
    /// </param>
    public static DaggerfallCharacterPresentation Build(
        CharacterMediaInventory inventory,
        IReadOnlySet<string> suppliedPalettes,
        IReadOnlyList<DaggerfallRaceKey> races,
        IReadOnlyDictionary<string, string> careers,
        IReadOnlyDictionary<string, string>? unpublishable = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(races);
        ArgumentNullException.ThrowIfNull(careers);
        IReadOnlyDictionary<string, string> unread = unpublishable ?? new Dictionary<string, string>(StringComparer.Ordinal);
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, suppliedPalettes);
        // Looked up by source file and canvas index rather than by a second derivation of the media
        // id: the derivation owns naming, and a builder that re-derived it would be a second place
        // for the two to disagree.
        Dictionary<(string File, int Canvas), CharacterCanvasReference> canvases = new(new FileAndCanvas());
        foreach (CharacterCanvasReference reference in set.Canvases)
        {
            canvases.Add((System.IO.Path.GetFileName(reference.Path), reference.CanvasIndex), reference);
        }

        List<DaggerfallCharacterLayer> layers = [];
        List<DaggerfallRaceWithoutMedia> without = [];
        foreach (DaggerfallRaceKey race in races.OrderBy(race => race.DonorRaceId))
        {
            if (race.DonorRaceId is < FirstDonorRaceId or > LastDonorRaceId)
            {
                without.Add(new DaggerfallRaceWithoutMedia(race.Id, race.DonorRaceId,
                    $"The donor's paper-doll media covers race values {FirstDonorRaceId} to {LastDonorRaceId}; value {race.DonorRaceId} has no subclass and therefore no art, so none is claimed for it."));
                continue;
            }

            int media = race.DonorRaceId - 1;
            List<(string Layer, string File, int Canvas)> expected =
            [
                ("background", $"SCBG{media:00}I0.IMG", 0),
                ("body.male.unclothed", $"BODY{media:00}I0.IMG", 0),
                ("body.male.clothed", $"BODY{media:00}I1.IMG", 0),
                ("body.female.unclothed", $"BODY{media + 10:00}I0.IMG", 0),
                ("body.female.clothed", $"BODY{media + 10:00}I1.IMG", 0),
            ];
            for (int head = 0; head < HeadsPerRaceAndGender; head++)
            {
                expected.Add(($"head.male.{head}", $"FACE{media:00}I0.CIF", head));
                expected.Add(($"head.female.{head}", $"FACE{media + 10:00}I0.CIF", head));
            }

            foreach ((string layer, string file, int canvasIndex) in expected)
            {
                // A layer the corpus does not supply is a source gap, not a publishable layer: the
                // pack says which layers a race has rather than listing identities that resolve to
                // nothing.
                if (!canvases.TryGetValue((file, canvasIndex), out CharacterCanvasReference? canvas))
                {
                    without.Add(new DaggerfallRaceWithoutMedia(race.Id, race.DonorRaceId,
                        $"Layer '{layer}' needs '{file}' canvas {canvasIndex}, which the supplied corpus does not carry."));
                    continue;
                }

                layers.Add(new DaggerfallCharacterLayer(race.Id, race.DonorRaceId, layer, canvas.MediaId, canvas.Path, canvas.Palette, canvas.Binding, ConsumerOf(canvas)));
            }
        }

        // Every supplied file is accounted for: the ones a layer draws from, the ones that read and
        // nothing uses, and the ones no reader here can open. A file that is in none of those three
        // would be a family going missing quietly.
        // The faction faces are the section's non-racial layer family: a social or escort view
        // resolves them by faction index, and the donor reads them from one fixed-cell grid.
        List<DaggerfallFactionFace> faces = [.. set.Canvases
            .Where(canvas => System.IO.Path.GetFileName(canvas.Path).Equals("FACES.CIF", StringComparison.OrdinalIgnoreCase))
            .OrderBy(canvas => canvas.CanvasIndex)
            .Select(canvas => new DaggerfallFactionFace(canvas.CanvasIndex, canvas.MediaId, System.IO.Path.GetFileName(canvas.Path), canvas.Palette, canvas.Binding, ConsumerOf(canvas)))];

        // A career's portrait is the class animation named for it, which is how the classic corpus
        // stores the three it supplies; a career the corpus does not depict says so rather than
        // borrowing another class's art.
        Dictionary<string, List<CharacterCanvasReference>> portraits = new(StringComparer.OrdinalIgnoreCase);
        foreach (CharacterCanvasReference canvas in set.Canvases.Where(canvas => canvas.Path.EndsWith(".CEL", StringComparison.OrdinalIgnoreCase)))
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(canvas.Path);
            if (!portraits.TryGetValue(fileName, out List<CharacterCanvasReference>? frames))
            {
                frames = [];
                portraits.Add(fileName, frames);
            }

            frames.Add(canvas);
        }

        List<DaggerfallCareerPortrait> careerPortraits = [];
        List<DaggerfallCareerWithoutPortrait> careersWithout = [];
        foreach ((string career, string name) in careers.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            // The corpus names a class portrait for the class, while a catalog career is identified by
            // its record position and carries the class name as its name: the name is what matches,
            // and a space in it is not part of the file name.
            if (portraits.TryGetValue(name.Replace(" ", string.Empty, StringComparison.Ordinal), out List<CharacterCanvasReference>? frames))
            {
                CharacterCanvasReference first = frames.OrderBy(frame => frame.CanvasIndex).First();
                careerPortraits.Add(new DaggerfallCareerPortrait(career, first.MediaId, System.IO.Path.GetFileName(first.Path), first.Palette, frames.Count, first.Binding, ConsumerOf(first)));
                continue;
            }

            careersWithout.Add(new DaggerfallCareerWithoutPortrait(career,
                $"The corpus supplies no class portrait named for '{name}'; the three it supplies depict the classes they are named for, and none of them is this one."));
        }

        HashSet<string> referenced =
        [
            .. layers.Select(layer => System.IO.Path.GetFileName(layer.SourceFile)),
            .. faces.Select(face => face.SourceFile),
            .. careerPortraits.Select(portrait => portrait.SourceFile),
        ];
        Dictionary<string, CharacterMediaUnavailable> unavailable = set.Unavailable.ToDictionary(entry => System.IO.Path.GetFileName(entry.Path), StringComparer.Ordinal);
        List<DaggerfallCharacterFile> files = [];
        foreach (CharacterMediaRecord file in inventory.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            string name = System.IO.Path.GetFileName(file.Path);
            if (unavailable.TryGetValue(name, out CharacterMediaUnavailable? unreadable))
            {
                files.Add(new DaggerfallCharacterFile(name, file.Family, 0, CharacterFileOutcome.Unreadable, unreadable.Reason));
                continue;
            }

            // A file whose container was read but whose canvases could not be emitted carries no canvas
            // here, which is what the outcome says; its count stays so the gap names what it lost.
            if (unread.TryGetValue(name, out string? refusal))
            {
                files.Add(new DaggerfallCharacterFile(name, file.Family, file.CanvasCount, CharacterFileOutcome.Unreadable, refusal));
                continue;
            }

            files.Add(referenced.Contains(name)
                ? new DaggerfallCharacterFile(name, file.Family, file.CanvasCount, CharacterFileOutcome.Referenced,
                    "A layer in this section is drawn from this file's canvases.")
                : new DaggerfallCharacterFile(name, file.Family, file.CanvasCount, CharacterFileOutcome.Unreferenced,
                    $"The file reads and no layer here uses it: the '{file.Family}' family has no paper-doll role in this section yet."));
        }

        return new DaggerfallCharacterPresentation(
            DaggerfallCharacterPresentation.CurrentSchemaVersion,
            [.. layers.OrderBy(layer => layer.DonorRaceId).ThenBy(layer => layer.Layer, StringComparer.Ordinal)],
            faces,
            careerPortraits,
            careersWithout,
            files,
            without,
            [inventory.Source, .. set.Unavailable.Select(entry => entry.Path).Order(StringComparer.Ordinal)]);
    }
}
