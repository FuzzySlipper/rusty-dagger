namespace Daggerfall.Import.Arena2;

/// <summary>
/// One canvas a character-media publisher would emit, with the palette it needs and the companions
/// it belongs with.
/// </summary>
/// <param name="Path">The supplied source file, which is the artifact's source identity.</param>
/// <param name="Family">The documented family the file belongs to.</param>
/// <param name="Key">The identity key the inventory recorded for the file.</param>
/// <param name="Consumer">The consumer the inventory recorded, or empty when none is bound yet.</param>
/// <param name="CanvasIndex">The canvas's index within its file, which is its record or cell.</param>
/// <param name="Palette">The palette file the classic reader pairs with this canvas.</param>
/// <param name="Companions">The other presentation layers this canvas belongs with.</param>
/// <param name="Reason">Where the palette and companion facts come from, and what is still unknown.</param>
public sealed record CharacterCanvasReference(
    string Path,
    string Family,
    string Key,
    string Consumer,
    int CanvasIndex,
    string Palette,
    IReadOnlyList<string> Companions,
    string Reason);

/// <summary>
/// Derives the palette — and, where the classic naming rule establishes them, the companion layers —
/// of every readable character-media canvas, and refuses a canvas whose palette is not supplied.
/// </summary>
/// <remarks>
/// The inventory records no palette per canvas and no cross-check derives one, which it states as a
/// known boundary. This is the derivation, and it is deliberately a refusal rather than a default: a
/// canvas painted with the wrong palette is a silently wrong artifact, while a canvas whose palette
/// is missing is a gap that names itself.
/// <para>
/// The palette rule is the classic reader's <c>ImgFile.PaletteName</c>: a <c>NITE*</c> file is read
/// with <c>NIGHTSKY.COL</c> and everything else in these families with <c>ART_PAL.COL</c>. Companion
/// layers follow the donor's paper-doll naming — a body is <c>BODY{race}I0/I1</c> for a male and
/// <c>BODY{race+10}I0/I1</c> for a female, a head is <c>FACE{race}I0</c> and <c>FACE{race+10}I0</c>,
/// and the background is the race's <c>SCBG{race}I0</c> — so a canvas's companions are the other
/// layers a character of the same race and gender is drawn from.
/// </para>
/// </remarks>
public static class CharacterMediaReferences
{
    /// <summary>The palette the classic reader pairs with these families, with no name rule.</summary>
    public const string ArtPalette = "ART_PAL.COL";

    /// <summary>The palette a <c>NITE*</c> file is read with.</summary>
    public const string NightskyPalette = "NIGHTSKY.COL";

    /// <summary>The palettes a complete character-media publication needs.</summary>
    public static readonly string[] RequiredPalettes = [ArtPalette, NightskyPalette];

    /// <summary>The classic reader's palette rule for one of these files.</summary>
    public static string PaletteFor(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return System.IO.Path.GetFileName(path).StartsWith("NITE", StringComparison.OrdinalIgnoreCase) ? NightskyPalette : ArtPalette;
    }

    /// <summary>
    /// Derives one reference per readable canvas.
    /// </summary>
    /// <param name="inventory">The character-media inventory to publish from.</param>
    /// <param name="suppliedPalettes">The palette files the caller supplies, by file name.</param>
    /// <exception cref="InvalidOperationException">
    /// A canvas needs a palette the caller does not supply, or its family's companion layers are
    /// never derived. Both name the file and the missing reference rather than defaulting.
    /// </exception>
    public static IReadOnlyList<CharacterCanvasReference> Derive(
        CharacterMediaInventory inventory,
        IReadOnlySet<string> suppliedPalettes)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(suppliedPalettes);
        List<CharacterCanvasReference> references = [];
        foreach (CharacterMediaRecord file in inventory.Files)
        {
            // A file nothing reads has no canvas to publish; the inventory already carries it with
            // its format named, and this refuses to invent one.
            if (file.Decode == Arena2CanvasKind.Unread) continue;

            string palette = PaletteFor(file.Path);
            if (!suppliedPalettes.Contains(palette))
            {
                throw new InvalidOperationException(
                    $"'{System.IO.Path.GetFileName(file.Path)}' is read with '{palette}', which the caller does not supply, so its {file.CanvasCount} canvas(es) would be published with the wrong colours or with none.");
            }

            (IReadOnlyList<string> companions, string reason) = Companions(file);
            for (int index = 0; index < file.CanvasCount; index++)
            {
                references.Add(new CharacterCanvasReference(file.Path, file.Family, file.Key, file.Consumer, index, palette, companions, reason));
            }
        }

        return references;
    }

    /// <summary>
    /// The other layers one file's canvases belong with, by the donor's paper-doll naming.
    /// </summary>
    private static (IReadOnlyList<string> Companions, string Reason) Companions(CharacterMediaRecord file)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(file.Path);
        string palette = PaletteFor(file.Path);
        string paletteFact = $"Read with '{palette}' by the classic reader's palette rule for this family.";

        // The paper-doll layer for a body or a head is the same race and gender: bodies carry the
        // race number for a male and the race number plus ten for a female, and heads do the same.
        if (TryRaceAndGender(name, out int race, out bool female))
        {
            string bodyUnclothed = $"BODY{race + (female ? 10 : 0):00}I0.IMG";
            string bodyClothed = $"BODY{race + (female ? 10 : 0):00}I1.IMG";
            string head = $"FACE{race + (female ? 10 : 0):00}I0.CIF";
            string background = $"SCBG{race:00}I0.IMG";
            string[] layers = [bodyUnclothed, bodyClothed, head, background];
            return ([.. layers.Where(layer => !string.Equals(layer, System.IO.Path.GetFileName(file.Path), StringComparison.OrdinalIgnoreCase))],
                $"{paletteFact} Companions are the other paper-doll layers for race {race} {(female ? "female" : "male")} by the donor's naming rule.");
        }

        return ([], $"{paletteFact} This family has no paper-doll companion rule, so no companion is claimed rather than one being guessed.");
    }

    /// <summary>Reads the race and gender a paper-doll file name encodes, when it encodes them.</summary>
    private static bool TryRaceAndGender(string nameWithoutExtension, out int race, out bool female)
    {
        race = 0;
        female = false;
        if (nameWithoutExtension.Length < 6) return false;
        string prefix = nameWithoutExtension[..4];
        if (prefix is not ("BODY" or "FACE")) return false;
        if (!int.TryParse(nameWithoutExtension.AsSpan(4, 2), out int number) || number is < 0 or > 15) return false;
        female = number >= 10;
        race = female ? number - 10 : number;
        return true;
    }
}
