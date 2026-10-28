using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>Icons, element-selected casting hands and world billboards, joined to every stock spell.</summary>
internal static class SpellReferenceExport
{
    internal sealed record Element(string Name, string CastingFile, int WorldArchive);
    internal static readonly Element[] Elements =
    [
        new("fire", "FIRE00C6.CIF", 375), new("cold", "FRST00C6.CIF", 376),
        new("poison", "POIS00C6.CIF", 377), new("shock", "SHOK00C6.CIF", 378), new("magic", "MJIC00C6.CIF", 379),
    ];
    internal static readonly string[] Targets = ["caster-only", "by-touch", "single-target-at-range", "area-around-caster", "area-at-range"];
    private const int IconCount = 69;
    private const int IconColumns = 20;

    public static void Run(string arena2, string output)
    {
        ReferenceExport export = new(arena2, output);
        Arena2SpellCatalog catalog = Arena2MagicReader.ReadSpells(export.Read("SPELLS.STD"), "arena2/SPELLS.STD");
        IndexedImg icons = ImgDecoder.DecodeHeaderless(export.Read("ICON00I0.IMG"), "arena2/ICON00I0.IMG");
        IndexedImg selectors = ImgDecoder.DecodeHeaderless(export.Read("MASK04I0.IMG"), "arena2/MASK04I0.IMG");
        int dimension = icons.Width / IconColumns;
        List<object> iconLinks = [];
        for (int icon = 0; icon < IconCount; icon++)
        {
            string directory = $"icons/icon-{icon:000}";
            ReferenceImage image = Icon(export, directory, icons, icon, dimension);
            object[] spells = catalog.Spells.Select((spell, ordinal) => (spell, ordinal)).Where(entry => entry.spell.Icon == icon)
                .Select(entry => (object)new { entry.ordinal, entry.spell.Index, entry.spell.Name, directory = SpellDirectory(entry.spell, entry.ordinal) }).ToArray();
            export.Subject(directory, $"Spell icon {icon:000}", new { kind = "spell-icon", icon, directory, spells, images = new[] { image } }, [image],
                "A selectable presentation icon. The icon does not choose the spell's element, casting hands, or world effect.");
            iconLinks.Add(new { icon, directory, stockSpellCount = spells.Length });
        }

        foreach ((Element element, int index) in Elements.Select((element, index) => (element, index)))
        {
            string directory = "elements/" + element.Name;
            List<ReferenceImage> images = ElementImages(export, directory, element, world: true, allWorldRecords: true);
            images.Add(Crop(export, directory, "element-icon.png", "element-icon", selectors, 24, index * 16, 16, 16, opaque: true));
            export.Subject(directory, element.Name + " casting and world effects", new { kind = "element", classicElement = index,
                directory, element, casting = CastingSequence, worldRecords = "0 = travelling projectile, 1 = impact; other supplied records retain source identities.", images }, images,
                "One source hand is drawn twice; the right hand is mirrored at runtime. All frames here retain source orientation.");
        }

        for (int target = 0; target < Targets.Length; target++)
        {
            string directory = "targets/" + Targets[target];
            ReferenceImage image = Crop(export, directory, "target-icon.png", "target-icon", selectors, 0, target * 16, 24, 16, opaque: true);
            export.Subject(directory, Targets[target], new { kind = "target", classicTarget = target, directory,
                usesTravellingProjectile = UsesProjectile(target), images = new[] { image } }, [image]);
        }

        for (int ordinal = 0; ordinal < catalog.Spells.Count; ordinal++)
        {
            Arena2Spell spell = catalog.Spells[ordinal];
            string directory = SpellDirectory(spell, ordinal);
            List<ReferenceImage> images = [];
            if (spell.Icon >= 0 && spell.Icon < IconCount) images.Add(Icon(export, directory, icons, spell.Icon, dimension));
            else export.Warn($"{directory}: spell icon {spell.Icon} is outside the classic icon set; no substitute icon was invented.");
            Element? element = spell.Element >= 0 && spell.Element < Elements.Length ? Elements[spell.Element] : null;
            if (element is not null)
            {
                images.AddRange(ElementImages(export, directory, element, UsesProjectile(spell.RangeType), allWorldRecords: false));
                images.Add(Crop(export, directory, "element-icon.png", "element-icon", selectors, 24, spell.Element * 16, 16, 16, opaque: true));
            }
            else export.Warn($"{directory}: unknown classic element {spell.Element}; no casting/effect relationship invented.");
            string? target = spell.RangeType >= 0 && spell.RangeType < Targets.Length ? Targets[spell.RangeType] : null;
            if (target is not null)
                images.Add(Crop(export, directory, "target-icon.png", "target-icon", selectors, 0, spell.RangeType * 16, 24, 16, opaque: true));
            else export.Warn($"{directory}: unknown classic target {spell.RangeType}; no target icon invented.");
            string note = UsesProjectile(spell.RangeType)
                ? "This ranged spell shares its element's casting hands, travelling projectile (world record 0), and impact (record 1). Its icon is independent."
                : "This target type has no travelling projectile. Casting hands come from the element; selecting an icon does not assign a world sprite. Shared element effect sources remain available under elements/.";
            export.Subject(directory, spell.Name, new
            {
                kind = "spell", ordinal, spell.Index, spell.Name, directory, spell,
                iconDirectory = spell.Icon >= 0 && spell.Icon < IconCount ? $"icons/icon-{spell.Icon:000}" : null,
                elementDirectory = element is null ? null : "elements/" + element.Name,
                target, usesTravellingProjectile = UsesProjectile(spell.RangeType),
                casting = CastingSequence, note, images,
            }, images, note);
        }

        // The classic sparkle is shared feedback, not the elemental missile or a per-icon effect.
        string sharedDirectory = "shared-effects/magic-sparkle";
        List<ReferenceImage> sparkles = export.TextureImages(sharedDirectory, "world-effect", 380, 3);
        export.Subject(sharedDirectory, "Shared magic sparkle", new { kind = "shared-effect", directory = sharedDirectory,
            note = "TEXTURE.380 record 3: shared magic feedback (EnemyBlood.ShowMagicSparkles); independent of spell icon and elemental projectile.", images = sparkles }, sparkles);
        List<ReferenceImage> atlases =
        [
            Crop(export, "source-atlases", "spell-icons-atlas.png", "source-atlas", icons, 0, 0, icons.Width, icons.Height),
            Crop(export, "source-atlases", "targets-and-elements-atlas.png", "source-atlas", selectors, 0, 0, selectors.Width, selectors.Height, opaque: true),
        ];
        export.Subject("source-atlases", "Uncut source atlases", new { kind = "source-atlases", directory = "source-atlases", images = atlases }, atlases,
            "The full icon atlas retains unused padding cells; only classic icon IDs 0–68 are selectable.");
        export.WriteJson("spell-links.json", catalog.Spells.Select((spell, ordinal) => new
        {
            ordinal, spell.Index, spell.Name, spell.Icon, spell.Element, spell.RangeType, directory = SpellDirectory(spell, ordinal),
            elementDirectory = spell.Element >= 0 && spell.Element < Elements.Length ? "elements/" + Elements[spell.Element].Name : null,
            usesTravellingProjectile = UsesProjectile(spell.RangeType),
        }));
        export.Finish("Daggerfall spell references", new { spells = catalog.Spells.Count, icons = IconCount, elements = Elements.Length,
                targets = Targets.Length, emptySpellRecords = catalog.Dispositions.Count },
            new { icons = iconLinks, casting = CastingSequence, targets = Targets,
                iconPolicy = "A chosen icon is independent of element and target. Unused icons remain selectable for custom spells.",
                sourceDispositions = catalog.Dispositions },
            "Open index.html for the visual catalog; spell-links.json joins each SPELLS.STD record to its icon, element, target and directory. Duplicate classic spell identities retain their source ordinals.\n" +
            "spells/ contains self-contained reference sets with icon, casting hands, selector icons, and ranged projectile/impact frames where applicable. icons/ contains all 69 selectable icons, including those unused by stock spells. elements/ contains all five casting CIFs and every record/frame of their world texture archives. targets/ contains all five targeting icons.\n" +
            "Icon choice is independent of casting and world effects. Element 0/1/2/3/4 selects fire/cold/poison/shock/magic. Target 2/4 uses a travelling projectile (record 0) and impact (record 1). Self and touch have no travelling projectile; area around caster has no travelling projectile. No per-icon effect is invented.\n" +
            "Casting has six original source records, played as 0,1,2,3,4,5,0; the release frame is 5. The right hand mirrors the left source at runtime. Source frames remain unmirrored here. shared-effects/ retains the generic magic sparkle. source-atlases/ retains both full UI atlases and their padding.\n" +
            "Reproduce: dotnet run --project src/Daggerfall.Import.Tool -- export-spells --output local/exports/NEW_SPELLS_DIRECTORY\n" +
            "Use a fresh output directory to preserve experiments. Native images are not resized; selector icons retain their opaque source background. No generation jobs have been submitted. Extracted art stays in ignored local/.",
            ["arena2/SPELLS.STD", "arena2/ICON00I0.IMG", "arena2/MASK04I0.IMG", "arena2/*00C6.CIF", "arena2/TEXTURE.375–379", "arena2/TEXTURE.380 record 3",
                "daggerfall-unity/Assets/Scripts/Game/UserInterface/SpellIconCollection.cs: LoadClassicSpellIcons, LoadClassicSpellTargetAndElementIcons",
                "daggerfall-unity/Assets/Scripts/Utility/WeaponBasics.cs: GetMagicAnimFilename",
                "daggerfall-unity/Assets/Scripts/Game/FPSSpellCasting.cs: frameIndices, releaseFrame, UpdateSpellCast",
                "daggerfall-unity/Assets/Scripts/Game/DaggerfallMissile.cs: Start, DoCollision, GetMissileTextureArchive",
                "daggerfall-unity/Assets/Scripts/Game/MagicAndEffects/EntityEffectBroker.cs: ClassicElementIndexToElementType, ClassicTargetIndexToTargetType"]);
    }

    private static readonly object CastingSequence = new { records = new[] { 0, 1, 2, 3, 4, 5, 0 }, releaseRecord = 5,
        mirroredRightHand = true, donorFrameSeconds = 0.04, note = "Source frame duration is the donor's current presentation choice, not a claim about original executable timing." };

    internal static bool UsesProjectile(int rangeType) => rangeType is 2 or 4;
    private static string SpellDirectory(Arena2Spell spell, int ordinal) => $"spells/spell-{ordinal:000}-id-{spell.Index:000}-{ReferenceExport.Slug(spell.Name)}";

    private static List<ReferenceImage> ElementImages(ReferenceExport export, string directory, Element element, bool world, bool allWorldRecords)
    {
        List<ReferenceImage> images = [];
        IReadOnlyList<IndexedImg> casting = ImgDecoder.DecodeRecordSequence(export.Read(element.CastingFile), "arena2/" + element.CastingFile);
        for (int record = 0; record < casting.Count; record++)
        {
            IndexedImg frame = casting[record];
            images.AddRange(export.Indexed(directory, $"casting/{Path.GetFileNameWithoutExtension(element.CastingFile).ToLowerInvariant()}-record-{record:00}-frame-00",
                "casting", frame.Source, record, 0, frame.Width, frame.Height, frame.XOffset, frame.YOffset, frame.Pixels.ToArray()));
        }
        if (world)
        {
            TextureArchive texture = export.Texture(element.WorldArchive);
            int records = allWorldRecords ? texture.RecordCount : Math.Min(2, texture.RecordCount);
            for (int record = 0; record < records; record++)
                images.AddRange(export.TextureImages(directory, record switch
                {
                    0 => "world-effects/projectile", 1 => "world-effects/impact", _ => $"world-effects/source-record-{record:00}",
                }, element.WorldArchive, record));
        }
        return images;
    }

    private static ReferenceImage Icon(ReferenceExport export, string directory, IndexedImg atlas, int icon, int dimension) =>
        Crop(export, directory, "icon.png", "spell-icon", atlas, icon % IconColumns * dimension, icon / IconColumns * dimension, dimension, dimension);

    internal static ReferenceImage Crop(ReferenceExport export, string directory, string file, string role, IndexedImg atlas,
        int x, int y, int width, int height, bool opaque = false)
    {
        // An invalid rectangle would mislabel pixels as a valid icon.
        if (x < 0 || y < 0 || x + width > atlas.Width || y + height > atlas.Height)
            throw new InvalidOperationException($"{atlas.Source}: {role} rectangle {x},{y} {width}x{height} is outside {atlas.Width}x{atlas.Height}; exporting it would associate the wrong pixels with the icon.");
        byte[] indexed = new byte[width * height];
        for (int row = 0; row < height; row++)
            atlas.Pixels.Span.Slice((y + row) * atlas.Width + x, width).CopyTo(indexed.AsSpan(row * width, width));
        return export.Rgba(directory, file, role, atlas.Source, 0, 0, width, height, x, y,
            export.Palette.ToRgbaBytes(indexed, opaque ? PaletteAlphaMode.Opaque : PaletteAlphaMode.IndexZeroTransparent), []);
    }
}
