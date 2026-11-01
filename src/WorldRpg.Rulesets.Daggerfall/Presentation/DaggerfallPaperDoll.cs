using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// One image the paper doll draws, placed in doll units (the classic doll's source pixels) from the doll's top-left
/// corner. The rect is independent of the image's pixel size: a refreshed image of any resolution is drawn scaled
/// into it, and may extend past the doll's frame, which clips it.
/// </summary>
/// <param name="Kind">What the layer is: <c>background</c>, <c>cloak-interior</c>, <c>body</c>, <c>head</c> or <c>item</c>.</param>
/// <param name="MediaId">The published image.</param>
/// <param name="X">The image's left edge in the doll.</param>
/// <param name="Y">The image's top edge in the doll.</param>
/// <param name="Width">The image's width in the doll.</param>
/// <param name="Height">The image's height in the doll.</param>
/// <param name="ClipTop">The first doll row drawn, from the image's top edge, when only part of the image is.</param>
/// <param name="ClipBottom">The doll row after the last one drawn, from the image's top edge, when only part of the image is.</param>
/// <param name="Mask">A mask image placed at <paramref name="MaskRect"/>: where it is opaque, what the doll drew beneath this layer is cleared.</param>
/// <param name="Slot">The equipment slot of an item layer.</param>
/// <param name="MaskRect">Where the mask is drawn: the classic image's rect, which a refreshed image's own rect need not be.</param>
/// <param name="Smooth">Whether the image is refreshed art, drawn smoothed; classic art is drawn with its pixels square.</param>
/// <param name="Tint">How refreshed clothing, painted neutral, is drawn in its dye.</param>
internal sealed record PaperDollLayerPresentation(string Kind, string MediaId, double X, double Y, double Width, double Height,
    double? ClipTop = null, double? ClipBottom = null, string? Mask = null, string? Slot = null, DaggerfallDollRect? MaskRect = null, bool Smooth = false,
    PaperDollTint? Tint = null);

/// <summary>
/// A dye drawn over a neutral image: the image brightened by <paramref name="Brightness"/>, then the pixels of
/// <paramref name="Mask"/> (an image the size and place of the layer) multiplied by <paramref name="Color"/>.
/// </summary>
internal sealed record PaperDollTint(string Color, double Brightness, string Mask);

/// <summary>
/// The player's paper doll: its size, the background, then every other layer in the order it is drawn.
/// </summary>
/// <param name="Width">The doll's width in source pixels.</param>
/// <param name="Height">The doll's height in source pixels.</param>
/// <param name="Background">The background, placed so the doll shows the classic crop of it.</param>
/// <param name="Layers">The cloak interior, body, head and worn items, first drawn first.</param>
/// <param name="Unplaced">Worn items the doll has no image for, by name, so a missing image is visible rather than silently dropped.</param>
internal sealed record PaperDollPresentation(int Width, int Height, PaperDollLayerPresentation Background, PaperDollLayerPresentation[] Layers, string[] Unplaced)
{
    /// <summary>Every image the doll draws, which is what the DOM needs delivered.</summary>
    internal IEnumerable<string> MediaIds => Layers.SelectMany(layer => new[] { layer.MediaId, layer.Mask, layer.Tint?.Mask }.OfType<string>())
        .Prepend(Background.MediaId);
}

/// <summary>One equipped item as the doll sees it: its slot, its meaning and its instance appearance.</summary>
/// <param name="Key">The equipped item's identity, which is the same in every slot it fills.</param>
internal sealed record DaggerfallWornItem(string Key, string Slot, DaggerfallItemDefinition Definition, DaggerfallItemInstanceMetadata? Metadata, Func<string> Label)
{
    /// <summary>
    /// The native template the item is drawn from: its own, or for an authored item the template it
    /// materializes from.
    /// </summary>
    internal DaggerfallItemTemplateDefinition? Template(DaggerfallDefinitions definitions) =>
        Definition.Template ?? (DaggerfallTemplateItemDefinitions.TemplateIndexForAuthoredItem(Definition.Id) is int index
            && definitions.ItemTemplateCatalog.Templates.TryGetValue(index, out DaggerfallItemTemplateDefinition? template) ? template : null);
}

/// <summary>
/// Daggerfall's paper doll: which images a character is drawn from, and in what order.
/// </summary>
/// <remarks>
/// After the donor's <c>PaperDollRenderer.Refresh</c>: a cloak's interior is drawn first (the second cloak's
/// if one is worn, else the first's), then the unclothed body, then - with nudity censored, the donor's
/// default - the clothed body's upper half when no chest clothing or armour is worn and its lower half when no
/// leg clothing is, split <see cref="WaistHeight"/> rows down; then the head, and then every worn piece of
/// clothing, armour and weapon by its template's draw order, ties in equipment-slot order. A vampire draws
/// its vampire head and background; a transformed lycanthrope draws only its beast background. Which texture
/// an item is drawn from follows <c>ItemBuilder</c> and <c>ItemHelper.GetItemImage</c>: armour by gender and
/// body morphology, clothing by body morphology, a variant past the template's record, a cloak's variants one
/// record later, and a one-handed weapon in the right hand from the next record. An artifact - an item whose
/// magic template is not a regular one - is drawn instead from its own texture, as <c>SetArtifact</c> sets it
/// with <c>ItemHelper.GetArtifactTextureIndices</c>: the artifact archive for the gender it was made for, at
/// the record the artifact's place among the artifact templates names, undyed.
/// <para>
/// With a refreshed art set, each layer whose classic image has a replacement draws the replacement at its own
/// doll rect (<see cref="Refreshed"/>). Refreshed clothing is painted neutral, one image for every dye: it is drawn
/// tinted in the worn dye (<see cref="PaperDollTint"/>) in place of the classic image of that dye.
/// </para>
/// </remarks>
internal static class DaggerfallPaperDollPolicy
{
    /// <summary>The row the donor splits the clothed body at.</summary>
    internal const int WaistHeight = 40;

    private const int FemaleArmorArchive = 245;
    private const int MaleArmorArchive = 249;
    private const int FemaleArtifactArchive = 433;
    private const int MaleArtifactArchive = 432;

    /// <summary>The donor's <c>artifactTextureIndexMappings</c>: each artifact's record, by its place among the artifact templates.</summary>
    private static readonly int[] ArtifactRecords = [12, 13, 10, 8, 19, 16, 25, 18, 21, 2, 24, 26, 0, 15, 3, 9, 23, 17, 7, 1, 22, 20, 5];

    /// <summary>The dye the artifact archives publish their records under.</summary>
    private const string UnchangedDye = "unchanged";

    /// <summary>The equipment slots past the jewellery, which the donor treats as worn on the body.</summary>
    private const int FirstBodySlot = 12;

    internal static PaperDollPresentation Compose(DaggerfallDefinitions definitions, DaggerfallCharacterIdentity identity,
        DaggerfallRacialOverrideView? racial, IReadOnlyList<DaggerfallWornItem> worn, DaggerfallArtReplacements? art = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(worn);
        DaggerfallCharacterPresentationSet presentation = definitions.CharacterPresentation;
        DaggerfallPaperDollFrame frame = presentation.PaperDoll;
        DaggerfallRaceLayers race = presentation.RequireRace(identity.RaceId);
        string gender = identity.Gender == DaggerfallCharacterGender.Female ? "female" : "male";
        DaggerfallRacialFormMedia? form = racial switch
        {
            { State.BeastForm: true } => presentation.RacialForms[racial.State.Kind == DaggerfallRacialKind.Werewolf ? "werewolf" : "wereboar"],
            { IsVampire: true } => presentation.RacialForms[$"vampire.{identity.RaceId}.{gender}"],
            _ => null,
        };
        PaperDollLayerPresentation background = Background(frame, form?.BodyMediaId ?? race.Background.MediaId);
        if (racial?.State.BeastForm == true)
            return new PaperDollPresentation(frame.Width, frame.Height, background, [], []);

        List<PaperDollLayerPresentation> layers = [];
        List<string> unplaced = [];
        string[] slotOrder = [.. definitions.EquipmentSlots.Keys.Select(slot => slot.Value)];
        DaggerfallWornItem[] drawn = [.. worn
            // An item held in two slots (a two-handed weapon) is drawn once, from its first slot.
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => Array.IndexOf(slotOrder, item.Slot)).First())];

        DaggerfallWornItem? cloak = drawn.FirstOrDefault(item => item.Slot == "cloak2" && IsCloak(item.Template(definitions)))
            ?? drawn.FirstOrDefault(item => item.Slot == "cloak1" && IsCloak(item.Template(definitions)));
        if (cloak is not null)
        {
            DaggerfallItemTemplateDefinition template = cloak.Template(definitions)!;
            if (Item(presentation, Archive(template, cloak, identity), template.PlayerTextureRecord, Dye(template, cloak), art, clothing: true) is { } interior)
                layers.Add(interior with { Kind = "cloak-interior", Slot = cloak.Slot });
            else unplaced.Add(cloak.Label());
        }

        DaggerfallCharacterLayerDefinition unclothed = race.Body(identity.Gender, clothed: false);
        DaggerfallCharacterLayerDefinition clothed = race.Body(identity.Gender, clothed: true);
        layers.Add(Placed("body", unclothed.MediaId, unclothed.Placement, unclothed.MediaId, art));
        bool upperClothed = drawn.Any(item => item.Slot is "chest-clothes" or "chest-armor");
        bool lowerClothed = drawn.Any(item => item.Slot == "legs-clothes");
        DaggerfallPaperDollPlacement clothedPlacement = clothed.Placement ?? throw Missing(clothed.MediaId);
        if (!upperClothed)
            layers.Add(Refreshed(Placed("body", clothed.MediaId, clothedPlacement, clothed.MediaId, null) with { ClipTop = 0, ClipBottom = Math.Min(WaistHeight, clothedPlacement.Height) }, art));
        if (!lowerClothed)
            layers.Add(Refreshed(Placed("body", clothed.MediaId, clothedPlacement, clothed.MediaId, null) with { ClipTop = Math.Min(WaistHeight, clothedPlacement.Height), ClipBottom = clothedPlacement.Height }, art));

        if (form is not null)
            layers.Add(Placed("head", form.HeadMediaId, form.HeadPlacement, form.HeadMediaId, art));
        else
        {
            DaggerfallCharacterLayerDefinition head = race.Heads(identity.Gender).Single(layer => layer.HeadIndex == identity.FaceIndex);
            layers.Add(Placed("head", head.MediaId, head.Placement, head.MediaId, art));
        }

        foreach ((DaggerfallWornItem item, DaggerfallItemTemplateDefinition template) in drawn
            .Select(item => (Item: item, Template: item.Template(definitions)))
            .Where(entry => entry.Template is not null && IsDrawn(entry.Template, Array.IndexOf(slotOrder, entry.Item.Slot)))
            .Select(entry => (entry.Item, entry.Template!))
            .OrderBy(entry => entry.Item2.DrawOrderOrEffect)
            .ThenBy(entry => Array.IndexOf(slotOrder, entry.Item.Slot)))
        {
            PaperDollLayerPresentation? drawnLayer = IsArtifact(definitions, item, out int? artifactRecord)
                ? artifactRecord is int record ? Item(presentation, ArtifactArchive(item, identity), HandRecord(template, item, record), UnchangedDye, art, clothing: false) : null
                : Item(presentation, Archive(template, item, identity), Record(template, item), Dye(template, item), art, IsClothing(template));
            if (drawnLayer is { } layer)
                layers.Add(layer with { Slot = item.Slot });
            else unplaced.Add(item.Label());
        }

        return new PaperDollPresentation(frame.Width, frame.Height, background, [.. layers], [.. unplaced]);
    }

    /// <summary>The background, placed so the doll shows the frame's crop of it.</summary>
    private static PaperDollLayerPresentation Background(DaggerfallPaperDollFrame frame, string mediaId) =>
        new("background", mediaId, frame.BackgroundX, frame.BackgroundY, frame.BackgroundWidth, frame.BackgroundHeight);

    private static PaperDollLayerPresentation Placed(string kind, string mediaId, DaggerfallPaperDollPlacement? placement, string subject, DaggerfallArtReplacements? art)
    {
        DaggerfallPaperDollPlacement place = placement ?? throw Missing(subject);
        return Refreshed(new PaperDollLayerPresentation(kind, mediaId, place.X, place.Y, place.Width, place.Height), art);
    }

    /// <summary>
    /// The layer as the refreshed art draws it, when its classic image has a replacement placed in the doll: the
    /// replacement at its own rect, smoothed, a clip moved to the same doll rows (an end of the classic image stays
    /// the end of the replacement, which may overhang it), and the mask kept at the classic image's rect.
    /// </summary>
    private static PaperDollLayerPresentation Refreshed(PaperDollLayerPresentation layer, DaggerfallArtReplacements? art)
    {
        if (art?.For(layer.MediaId) is not { Doll: { } rect } replacement) return layer;
        double? Row(double? row) => row switch
        {
            null => null,
            <= 0 => 0,
            double end when end >= layer.Height => rect.Height,
            double value => Math.Clamp(layer.Y + value - rect.Y, 0, rect.Height),
        };
        return layer with
        {
            MediaId = replacement.MediaId, X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height,
            ClipTop = Row(layer.ClipTop), ClipBottom = Row(layer.ClipBottom),
            MaskRect = layer.Mask is null ? null : new(layer.X, layer.Y, layer.Width, layer.Height), Smooth = true,
        };
    }

    private static InvalidOperationException Missing(string mediaId) =>
        new($"Paper-doll layer '{mediaId}' has no published placement.");

    private static PaperDollLayerPresentation? Item(DaggerfallCharacterPresentationSet presentation, int? archive, int record, string dye,
        DaggerfallArtReplacements? art, bool clothing)
    {
        if (archive is not int number || !presentation.PaperDollItems.TryGetValue((number, record), out DaggerfallPaperDollItemImage? image)
            || !image.Dyes.TryGetValue(dye, out string? mediaId))
            return null;
        DaggerfallPaperDollPlacement place = image.Placement;
        PaperDollLayerPresentation classic = new("item", mediaId, place.X, place.Y, place.Width, place.Height, Mask: image.MaskMediaId);
        if (!clothing) return Refreshed(classic, art);
        // The refreshed record has one neutral image for every dye, standing in for the record as published undyed.
        string neutral = $"paper-doll.{number}.{record}";
        return art?.For(neutral) is { DyeMask: { } mask } && art.Tints.TryGetValue(dye, out DaggerfallDyeTint? tint)
            ? Refreshed(classic with { MediaId = neutral }, art) with { Tint = new(tint.Multiply, tint.Brightness, mask.MediaId) }
            : classic;
    }

    /// <summary>Whether the donor draws an item of this template in this slot: clothing, armour and weapons, and jewellery worn on the body.</summary>
    private static bool IsDrawn(DaggerfallItemTemplateDefinition template, int slotIndex) =>
        template.Groups.Any(group => group is "MensClothing" or "WomensClothing" or "Armor" or "Weapons")
        || template.Groups.Contains("Jewellery", StringComparer.Ordinal) && slotIndex >= FirstBodySlot;

    private static bool IsClothing(DaggerfallItemTemplateDefinition template) =>
        template.Groups.Any(group => group is "MensClothing" or "WomensClothing");

    private static bool IsCloak(DaggerfallItemTemplateDefinition? template) =>
        template is not null && IsClothing(template) && template.Name.EndsWith(" Cloak", StringComparison.OrdinalIgnoreCase);

    /// <summary>The texture archive the item is drawn from, or null when its template draws none.</summary>
    private static int? Archive(DaggerfallItemTemplateDefinition template, DaggerfallWornItem item, DaggerfallCharacterIdentity wearer)
    {
        if (template.PlayerTextureArchive == 0) return null;
        // The item was made for a body: its own race and gender when it records them, else the wearer's.
        string race = item.Metadata?.Race ?? wearer.RaceId;
        bool female = (item.Metadata?.Gender ?? (wearer.Gender == DaggerfallCharacterGender.Female ? "female" : "male")) == "female";
        if (template.Groups.Contains("Weapons", StringComparer.Ordinal)) return template.PlayerTextureArchive;
        if (Morphology(race) is not int morphology) return null;
        if (template.Groups.Contains("Armor", StringComparer.Ordinal)) return (female ? FemaleArmorArchive : MaleArmorArchive) + morphology;
        return template.PlayerTextureArchive + morphology;
    }

    private static int Record(DaggerfallItemTemplateDefinition template, DaggerfallWornItem item) =>
        HandRecord(template, item, template.Variants > 0
            ? template.PlayerTextureRecord + (IsCloak(template) ? 1 : 0) + (item.Metadata?.Variant ?? 0)
            : template.PlayerTextureRecord);

    /// <summary>A one-handed weapon in the right hand is drawn from the record after its own.</summary>
    private static int HandRecord(DaggerfallItemTemplateDefinition template, DaggerfallWornItem item, int record) =>
        template.Groups.Contains("Weapons", StringComparer.Ordinal) && template.IsOneHanded && item.Slot == "right-hand" ? record + 1 : record;

    /// <summary>
    /// Whether the item is an artifact, and the artifact record it is drawn from: its magic template's place
    /// among the artifact templates - the magic templates that are not regular, in file order - or null when
    /// the donor's table names no record for that place.
    /// </summary>
    private static bool IsArtifact(DaggerfallDefinitions definitions, DaggerfallWornItem item, out int? record)
    {
        record = null;
        if (item.Metadata?.Enchantment is not { } key || !definitions.Magic.MagicItems.TryGetValue(key, out DaggerfallMagicItemDefinition? magic)
            || magic.Type == 0)
            return false;
        int index = definitions.Magic.MagicItems.Values.Where(value => value.Type != 0).Count(value => value.Offset < magic.Offset);
        record = index < ArtifactRecords.Length ? ArtifactRecords[index] : null;
        return true;
    }

    /// <summary>The artifact archive for the gender the item was made for, else the wearer's.</summary>
    private static int ArtifactArchive(DaggerfallWornItem item, DaggerfallCharacterIdentity wearer) =>
        (item.Metadata?.Gender ?? (wearer.Gender == DaggerfallCharacterGender.Female ? "female" : "male")) == "female"
            ? FemaleArtifactArchive
            : MaleArtifactArchive;

    /// <summary>The dye an item is drawn in: clothing's own dye, and a weapon's or armour's material.</summary>
    private static string Dye(DaggerfallItemTemplateDefinition template, DaggerfallWornItem item) =>
        IsClothing(template)
            ? item.Metadata?.Dye ?? "blue"
            : item.Metadata?.Material ?? item.Definition.Weapon?.Material ?? item.Definition.Armor?.Material ?? "steel";

    /// <summary>The donor's body morphology, which offsets clothing and armour archives: argonian, elf, human, khajiit.</summary>
    private static int? Morphology(string race) => race switch
    {
        "argonian" => 0,
        "dark-elf" or "high-elf" or "wood-elf" => 1,
        "breton" or "nord" or "redguard" => 2,
        "khajiit" => 3,
        _ => null,
    };
}
