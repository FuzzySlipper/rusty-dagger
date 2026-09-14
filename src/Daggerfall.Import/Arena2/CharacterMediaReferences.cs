using System.Security.Cryptography;
namespace Daggerfall.Import.Arena2;

/// <summary>
/// One canvas a character-media publisher would emit, with the palette it needs and the companions
/// it belongs with.
/// </summary>
/// <param name="MediaId">The stable identity of this canvas as a published artifact.</param>
/// <param name="Path">The supplied source file, which is the artifact's source identity.</param>
/// <param name="Family">The documented family the file belongs to.</param>
/// <param name="Key">The identity key the inventory recorded for the file.</param>
/// <param name="Consumer">The consumer the inventory recorded, or empty when none is bound yet.</param>
/// <param name="Binding">Whether a published consumer binds the file, or it is still required-pending.</param>
/// <param name="CanvasIndex">The canvas's index within its file, which is its record or cell.</param>
/// <param name="Palette">The palette file the classic reader pairs with this canvas.</param>
/// <param name="Companions">The other presentation layers this canvas belongs with.</param>
/// <param name="Reason">Where the palette and companion facts come from, and what is still unknown.</param>
public sealed record CharacterCanvasReference(
    string MediaId,
    string Path,
    string Family,
    string Key,
    string Consumer,
    MediaBinding Binding,
    int CanvasIndex,
    string Palette,
    IReadOnlyList<string> Companions,
    string Reason);

/// <summary>One supplied file whose format nothing here reads, kept explicit rather than dropped.</summary>
/// <param name="Path">The supplied source file.</param>
/// <param name="Family">The documented family it belongs to.</param>
/// <param name="Key">The identity key the inventory recorded.</param>
/// <param name="Binding">Whether a published consumer binds it, or it is still required-pending.</param>
/// <param name="DonorReader">The donor class that reads this format, or empty when none is known.</param>
/// <param name="Reason">Why the file supplies no canvas, naming the reader that would be needed.</param>
public sealed record CharacterMediaUnavailable(
    string Path,
    string Family,
    string Key,
    MediaBinding Binding,
    string DonorReader,
    string Reason);

/// <summary>
/// Everything a character-media publication has to account for: the canvases it can emit, and the
/// supplied files it cannot, each with the reason.
/// </summary>
/// <param name="Canvases">One reference per readable canvas.</param>
/// <param name="Unavailable">One entry per supplied file whose format nothing here reads.</param>
public sealed record CharacterMediaReferenceSet(
    IReadOnlyList<CharacterCanvasReference> Canvases,
    IReadOnlyList<CharacterMediaUnavailable> Unavailable);

/// <summary>
/// Derives the palette — and, where the classic naming rule establishes them, the companion layers —
/// of every readable character-media canvas, accounts for every supplied file it cannot publish, and
/// refuses a canvas whose palette is not supplied.
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

    /// <summary>The published consumer that binds a character canvas: the ruleset's character sheet.</summary>
    /// <remarks>
    /// <c>MediaBinding.Admitted</c> means a published consumer binds the file, not that bytes exist, so
    /// the name is the consumer's own: <c>DaggerfallCharacterPresentation</c> resolves an actor's race
    /// layers and career portrait through <c>DaggerfallCharacterPresentationSet</c> on every sheet read
    /// and publishes them in the sheet projection. Naming it here is what tells the publication which
    /// files are bound rather than leaving every file pending while its artifacts sit unread.
    /// </remarks>
    public const string CharacterSheetConsumer = "the character sheet";

    /// <summary>The palette a <c>NITE*</c> file is read with.</summary>
    public const string NightskyPalette = "NIGHTSKY.COL";

    /// <summary>The palettes a complete character-media publication needs.</summary>
    public static readonly string[] RequiredPalettes = [ArtPalette, NightskyPalette];

    /// <summary>
    /// The donor class that reads each format this repository has no reader for. Naming the reader is
    /// what makes an unavailable family a recorded gap rather than an omission: the canvases are
    /// absent because a specific class is missing here, not because nobody looked.
    /// </summary>
    private static readonly Dictionary<string, string> DonorReaders = new(StringComparer.Ordinal)
    {
        ["CEL"] = "Assets/Scripts/API/FlcFile.cs",
        ["BSS"] = "Assets/Scripts/API/BssFile.cs",
    };

    /// <summary>The prefix of a palette identity that names a container's own colours rather than a file.</summary>
    private const string PaletteIdentityPrefix = "embedded-palette-sha256:";

    /// <summary>The prefix of a palette identity that names a container's own colours rather than a file.</summary>
    public const string EmbeddedPalettePrefix = "embedded-palette-sha256:";

    /// <summary>
    /// The published identity of a palette a container carries itself, which has no file name to state.
    /// </summary>
    /// <remarks>
    /// A digest of the palette's own colours, so the reference and the emitted artifact state the same fact
    /// about the same bytes rather than each describing the colours in its own words.
    /// </remarks>
    public static string EmbeddedPaletteIdentity(Arena2Palette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        byte[] colors = new byte[palette.Colors.Length * 3];
        for (int index = 0; index < palette.Colors.Length; index++)
        {
            Rgb24 color = palette.Colors.Span[index];
            colors[(index * 3) + 0] = color.Red;
            colors[(index * 3) + 1] = color.Green;
            colors[(index * 3) + 2] = color.Blue;
        }

        return $"{EmbeddedPalettePrefix}{Convert.ToHexString(SHA256.HashData(colors))[..16].ToLowerInvariant()}";
    }

    /// <summary>
    /// The palette a supplied file carries itself, as a published identity, or null when it carries none.
    /// </summary>
    private static string? ContainerPalette(string path, ReadOnlySpan<byte> bytes)
    {
        if (!path.EndsWith(".CEL", StringComparison.OrdinalIgnoreCase) || bytes.IsEmpty) return null;
        Arena2Palette? palette = FlcDecoder.ReadContainerPalette(bytes, System.IO.Path.GetFileName(path));
        return palette is null ? null : EmbeddedPaletteIdentity(palette);
    }

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
    /// <param name="sources">
    /// The supplied corpus by file name, when the caller has it: a container that carries its own palette is
    /// painted in that one, and stating the family's palette instead would describe colours the canvas does
    /// not have. A caller without the bytes states the family's palette, which is what the classic reader
    /// pairs for every family that carries none.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A canvas needs a palette or a companion layer the corpus does not supply, naming the file and
    /// the missing reference rather than painting it with a default or publishing it alone.
    /// </exception>
    public static CharacterMediaReferenceSet Derive(
        CharacterMediaInventory inventory,
        IReadOnlySet<string> suppliedPalettes,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(suppliedPalettes);
        HashSet<string> supplied = new(StringComparer.OrdinalIgnoreCase);
        foreach (CharacterMediaRecord record in inventory.Files)
        {
            supplied.Add(System.IO.Path.GetFileName(record.Path));
        }

        List<CharacterCanvasReference> references = [];
        List<CharacterMediaUnavailable> unavailable = [];
        foreach (CharacterMediaRecord file in inventory.Files)
        {
            // A file nothing reads has no canvas to publish, and it is not dropped either: it is
            // accounted for with the reader its format would need, so a family cannot go missing
            // because it was inconvenient.
            if (file.Decode == Arena2CanvasKind.Unread)
            {
                string donorReader = DonorReaders.GetValueOrDefault(file.Family, string.Empty);
                unavailable.Add(new CharacterMediaUnavailable(
                    file.Path,
                    file.Family,
                    file.Key,
                    file.Binding,
                    donorReader,
                    donorReader.Length == 0
                        ? $"No reader in this repository opened this file, and no donor reader is recorded for the '{file.Family}' format; the file supplies no canvas here rather than an approximation."
                        : $"The donor reads this format with {donorReader}, and nothing in this repository opened this file, so its canvases are unavailable rather than approximated."));
                continue;
            }

            // A container that carries its own palette is painted in that one; the classic reader pairs a
            // palette with the family for everything else. Stating the family's palette for an animation
            // would describe colours the artifact does not have.
            string palette = PaletteFor(file.Path);
            if (sources is not null && sources.TryGetValue(System.IO.Path.GetFileName(file.Path), out ReadOnlyMemory<byte> fileBytes))
            {
                palette = ContainerPalette(file.Path, fileBytes.Span) ?? palette;
            }

            // A supplied palette is required for a file the classic reader pairs one with; a container that
            // carries its own needs no supplied file, so its identity is not looked up among them.
            if (!palette.StartsWith(EmbeddedPalettePrefix, StringComparison.Ordinal) && !suppliedPalettes.Contains(palette))
            {
                throw new InvalidOperationException(
                    $"'{System.IO.Path.GetFileName(file.Path)}' is read with '{palette}', which the caller does not supply, so its {file.CanvasCount} canvas(es) would be published with the wrong colours or with none.");
            }

            (IReadOnlyList<string> companions, string reason) = Companions(file);

            // A paper-doll layer without the layers it is drawn with is not a publishable layer: the
            // donor's naming establishes which files belong together, so a missing companion is a gap
            // that names itself rather than a canvas published on its own.
            foreach (string companion in companions)
            {
                if (!supplied.Contains(companion))
                {
                    throw new InvalidOperationException(
                        $"'{System.IO.Path.GetFileName(file.Path)}' is drawn with '{companion}', which the corpus does not supply, so publishing it alone would publish an incomplete paper doll.");
                }
            }
            for (int index = 0; index < file.CanvasCount; index++)
            {
                references.Add(new CharacterCanvasReference(
                    MediaId(file, index),
                    file.Path,
                    file.Family,
                    file.Key,
                    file.Consumer,
                    file.Binding,
                    index,
                    palette,
                    companions,
                    reason));
            }
        }

        return new CharacterMediaReferenceSet(references, unavailable);
    }

    /// <summary>
    /// The stable identity of one canvas as a published artifact.
    /// </summary>
    /// <remarks>
    /// The identity is derived from the file's own name rather than assigned by position, so adding
    /// a family or reordering the corpus cannot silently rename an artifact a consumer already
    /// references. A paper-doll file names its race, gender and layer; anything else in these
    /// families names its family and its own number, and a canvas index distinguishes the canvases a
    /// multi-record or grid file carries.
    /// </remarks>
    public static string MediaId(CharacterMediaRecord file, int canvasIndex)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegative(canvasIndex);
        string name = System.IO.Path.GetFileNameWithoutExtension(file.Path).ToUpperInvariant();
        string family = file.Family.ToLowerInvariant();
        // The one fixed-cell grid in these families is the faction face table: sixty-one cells a
        // social or escort view indexes by faction, which is a different role from a paper-doll head
        // and is named as one.
        // A class portrait is an animation: one identity per frame, named for the class it depicts so
        // a career resolves its portrait rather than reconstructing a file name.
        if (file.Family == "CEL")
        {
            return $"character.portrait.{name.ToLowerInvariant()}.{canvasIndex}";
        }

        if (name == "FACES")
        {
            return $"character.faction-face.{canvasIndex:00}";
        }

        if (TryRaceAndGender(name, out int race, out bool female))
        {
            string gender = female ? "female" : "male";
            bool body = name.StartsWith("BODY", StringComparison.Ordinal);
            string layer = name[^2..] switch
            {
                "I0" when body => "body-unclothed",
                "I1" when body => "body-clothed",
                "I0" => "head",
                _ => "layer",
            };
            return $"character.{layer}.{gender}.{race:00}.{canvasIndex}";
        }

        return $"character.{family}.{name.ToLowerInvariant()}.{canvasIndex}";
    }

    /// <summary>
    /// The same references with the binding a consumer established: the canvases a published consumer
    /// binds become <see cref="MediaBinding.Admitted"/> and the rest stay required-pending.
    /// </summary>
    /// <remarks>
    /// The emission pass publishes every readable canvas because the reference set is the subject, but
    /// publishing bytes is not binding them: a reference is bound when a consumer resolves it. Rewriting
    /// the set with that fact - rather than deriving the flag from what the pass published - is what keeps
    /// "a consumer binds this" a statement about a consumer.
    /// </remarks>
    /// <param name="set">The derived reference set, before any consumer is named.</param>
    /// <param name="admitted">The source files a published consumer binds, by file name.</param>
    /// <param name="consumer">The consumer that binds them, named as it is in the published references.</param>
    /// <param name="paintedPalettes">
    /// The palette each published canvas was actually painted in, by media identity. A derived reference
    /// states the palette the classic reader would pair with the file, and a container that carries its own
    /// palette is painted in that one instead, so the reference would name colours the artifact does not
    /// have unless it is rewritten with what the emission used.
    /// </param>
    public static CharacterMediaReferenceSet WithBoundFiles(
        CharacterMediaReferenceSet set,
        IReadOnlySet<string> admitted,
        string consumer,
        IReadOnlyDictionary<string, string>? paintedPalettes = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        return set with
        {
            Canvases = [.. set.Canvases.Select(canvas =>
            {
                bool bound = admitted.Contains(System.IO.Path.GetFileName(canvas.Path));
                return canvas with
                {
                    Binding = bound ? MediaBinding.Admitted : MediaBinding.RequiredPending,
                    Consumer = bound ? consumer : canvas.Consumer,
                    Palette = paintedPalettes is not null && paintedPalettes.TryGetValue(canvas.MediaId, out string? painted)
                        ? painted
                        : canvas.Palette,
                };
            })],
            Unavailable = [.. set.Unavailable.Select(file =>
            {
                bool bound = admitted.Contains(System.IO.Path.GetFileName(file.Path));
                return file with { Binding = bound ? MediaBinding.Admitted : MediaBinding.RequiredPending };
            })],
        };
    }

    /// <summary>
    /// The supplied files a character sheet resolves: every race's background and paper-doll layers, and
    /// every class portrait the corpus supplies.
    /// </summary>
    /// <remarks>
    /// A published reference is <c>MediaBinding.Admitted</c> when a published consumer binds the file, so the
    /// set comes from the consumer's domain rather than from the publisher's: the sheet resolves a race
    /// through <c>DaggerfallCharacterPresentationSet.RequireRace</c> for any of the eight races the catalogs
    /// publish, taking that race's background, both genders' bodies and its head CIFs, and a career's
    /// portrait for any career the corpus depicts. The pack publishes all eight races' layers for exactly
    /// that reason, so binding only the race one actor happens to declare would leave seven races' live
    /// references labelled unclaimed.
    /// <para>
    /// What stays unbound is what no consumer resolves: the faction face grid, whose cells nothing here
    /// slices the pixels of, and the story and compass families, which have no character-sheet role.
    /// </para>
    /// </remarks>
    /// <param name="inventory">The supplied files, which are the candidate bindings.</param>
    public static IReadOnlySet<string> FilesBoundByCharacterSheet(CharacterMediaInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        HashSet<string> bound = new(StringComparer.OrdinalIgnoreCase);
        foreach (CharacterMediaRecord file in inventory.Files)
        {
            // A file is bound when the sheet has a layer that resolves it: a paper-doll name encodes a race
            // and a layer, a background names the scene a race is drawn in, and a class portrait is the
            // family the sheet's portrait lookup draws from.
            string name = System.IO.Path.GetFileNameWithoutExtension(file.Path).ToUpperInvariant();
            if (file.Family == "CEL" || TryRaceAndGender(name, out _, out _) || IsRaceBackground(name))
            {
                bound.Add(file.Path);
            }
        }

        return bound;
    }

    /// <summary>
    /// Whether a file name is a race background. The name is the donor's <c>SCBG{race}I0</c>, and the
    /// presentation builder resolves one per catalog race, so the numbering a background carries is the same
    /// zero-based race the paper-doll files use.
    /// </summary>
    private static bool IsRaceBackground(string nameWithoutExtension)
    {
        const string Prefix = "SCBG";
        return nameWithoutExtension.Length == 8
            && nameWithoutExtension.StartsWith(Prefix, StringComparison.Ordinal)
            && nameWithoutExtension[6] == 'I'
            && nameWithoutExtension[7] == '0'
            && int.TryParse(nameWithoutExtension.AsSpan(4, 2), out int race)
            && race is >= 0 and <= 7;
    }

    /// <summary>
    /// The other layers one file's canvases belong with, by the donor's paper-doll naming.
    /// </summary>
    private static (IReadOnlyList<string> Companions, string Reason) Companions(CharacterMediaRecord file)
    {
        // The same normalisation the identity uses, so a lower-case corpus name cannot be one thing
        // to the identity and another to the companion rule.
        string name = System.IO.Path.GetFileNameWithoutExtension(file.Path).ToUpperInvariant();
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

    /// <summary>
    /// Reads the race and gender a paper-doll file name encodes, when it encodes them.
    /// </summary>
    /// <param name="nameWithoutExtension">The file's stem, which the caller has upper-cased.</param>
    private static bool TryRaceAndGender(string nameWithoutExtension, out int race, out bool female)
    {
        race = 0;
        female = false;
        // The paper-doll names are exactly a layer prefix, a two-digit race, and a one-digit variant:
        // a longer or oddly-suffixed name is not one of them, and treating it as one would give a
        // canvas an identity claiming a race and layer the donor does not have.
        if (nameWithoutExtension.Length != 8) return false;
        string prefix = nameWithoutExtension[..4];
        bool body = prefix == "BODY";
        if (!body && prefix != "FACE") return false;
        if (nameWithoutExtension[6] != 'I') return false;
        char variant = nameWithoutExtension[7];
        if (body ? variant is not ('0' or '1') : variant != '0') return false;
        if (!int.TryParse(nameWithoutExtension.AsSpan(4, 2), out int number)) return false;
        female = number >= 10;
        race = female ? number - 10 : number;
        // The donor's media covers the eight playable races: values at or above eighteen would name a
        // race and gender pair the paper-doll art does not have.
        return race is >= 0 and <= 7;
    }
}
