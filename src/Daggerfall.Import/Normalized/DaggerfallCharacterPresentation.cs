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
    MediaBinding Binding);

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
    IReadOnlyList<DaggerfallRaceWithoutMedia> RacesWithoutMedia,
    IReadOnlyList<string> Sources)
{
    public const int CurrentSchemaVersion = 1;

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

            // A layer whose identity resolves to no canvas would be a reference to nothing, which is
            // the failure this publication exists to make impossible rather than to discover later.
            if (!publishedMediaIds.Contains(layer.MediaId))
            {
                throw new InvalidOperationException(
                    $"Race '{layer.Race}' layer '{layer.Layer}' names media '{layer.MediaId}', which no published canvas carries; the reference would resolve to nothing.");
            }
        }
    }
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

    public static DaggerfallCharacterPresentation Build(
        CharacterMediaInventory inventory,
        IReadOnlySet<string> suppliedPalettes,
        IReadOnlyList<DaggerfallRaceKey> races)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(races);
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, suppliedPalettes);
        // Looked up by source file and canvas index rather than by a second derivation of the media
        // id: the derivation owns naming, and a builder that re-derived it would be a second place
        // for the two to disagree.
        Dictionary<(string File, int Canvas), CharacterCanvasReference> canvases = [];
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

                layers.Add(new DaggerfallCharacterLayer(race.Id, race.DonorRaceId, layer, canvas.MediaId, canvas.Path, canvas.Palette, canvas.Binding));
            }
        }

        return new DaggerfallCharacterPresentation(
            DaggerfallCharacterPresentation.CurrentSchemaVersion,
            [.. layers.OrderBy(layer => layer.DonorRaceId).ThenBy(layer => layer.Layer, StringComparer.Ordinal)],
            without,
            [inventory.Source, .. set.Unavailable.Select(entry => entry.Path).Order(StringComparer.Ordinal)]);
    }
}
