using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.ReferenceExports;

internal sealed record NpcArt(string Label, IReadOnlyList<ReferenceImage> Frames, bool Identical = false);
internal sealed record NpcLink(string Label, string Href);
internal sealed record NpcEntry(string Id, string Name, string Kind, IReadOnlyList<NpcArt> Art,
    IReadOnlyList<NpcPlacement> Placements, IReadOnlyDictionary<string, string> Facts, IReadOnlyList<NpcLink> Links,
    string[] Aliases, IReadOnlyList<NpcEntry>? Merged = null, ReferenceImage? PreferredImage = null)
{
    public ReferenceImage? Thumbnail => PreferredImage ?? Art.SelectMany(art => art.Frames).FirstOrDefault();
}

internal static class NpcReferenceExport
{
    private const string SpecialFaces = "FACES.CIF";
    private const string CommonFaces = "TFAC00I0.RCI";
    private const int PlaceholderFace = 410;
    private const int PortraitSide = 64;
    private const int PortraitCellBytes = PortraitSide * PortraitSide;
    private const int CityWatchArchive = 399, FirstRider = 491, LastRider = 495;

    // These have NPC-related metadata, but the images depict a barn door, a standing light and a brazier.
    private static readonly HashSet<NpcSprite> Props = [new(171, 1), new(197, 6), new(210, 0)];

    // The retained figure from the noble test archive has no world placement.
    private static readonly NpcSprite TestNoblewoman = new(185, 1);

    // These records share an NPC identity but place different poses in the world.
    private static readonly Dictionary<NpcSprite, string> SpritePoses = new()
    {
        [new(176, 0)] = "Kneeling", [new(176, 1)] = "Standing",
        [new(178, 2)] = "Kneeling", [new(178, 3)] = "Standing",
    };

    // Daggerfall Unity's MobilePersonNPC face banks, matched to MobilePersonBillboard's outfits.
    private static readonly Dictionary<int, int> FaceStarts = new()
    {
        [381] = 336, [382] = 312, [383] = 336, [384] = 312, [395] = 144, [396] = 144, [397] = 120, [398] = 96,
        [387] = 240, [388] = 264, [389] = 168, [390] = 192, [392] = 72, [393] = 0, [451] = 48, [452] = 0,
        [385] = 192, [386] = 216, [391] = 288, [394] = 240, [453] = 72, [454] = 72, [455] = 24, [456] = 72,
    };

    private sealed record SpriteCaption(string Caption, string Gender, int Face);

    public static void Run(string arena2, string basePayload, string characters, string output, string merges)
    {
        IReadOnlyDictionary<string, NpcReferenceId> ids = NpcReferenceIds.Read();
        ReferenceExport export = new(arena2, output);
        const string factionSource = "arena2/FACTION.TXT";
        Dictionary<int, ClassicFaction> factions = FactionReader.Read(Encoding.UTF8.GetString(export.Read("FACTION.TXT")), factionSource)
            .ToDictionary(faction => faction.Id);
        Dictionary<NpcSprite, SpriteCaption> captions = ReadCaptions(export);
        HashSet<int> archives = [.. PeopleReferenceExport.StaticArchives];
        HashSet<NpcSprite> listed = [.. captions.Keys, TestNoblewoman];
        ILookup<NpcSprite, NpcPlacement> placements = NpcReferenceWorld.ReadPlacements(export, archives, listed)
            .Where(placement => IsPerson(placement.Sprite)).ToLookup(placement => placement.Sprite);
        ILookup<int, NpcPlacement> factionPlacements = placements.SelectMany(group => group).ToLookup(placement => placement.Faction);
        NpcReferenceWorld world = new(export, basePayload, placements.SelectMany(group => group).Select(placement => placement.Block));

        var factionSprites = factions.Values.SelectMany(faction => faction.Flats.Where(id => id > 0)
            .Select(id => (Sprite: NpcSprite.Decode(id), Faction: faction))).ToLookup(value => value.Sprite, value => value.Faction);
        HashSet<NpcSprite> spriteKeys = [.. archives.SelectMany(archive => Enumerable.Range(0, export.Texture(archive).RecordCount)
            .Select(record => new NpcSprite(archive, record))), .. listed, .. placements.Select(group => group.Key), .. factionSprites.Select(group => group.Key)];

        HashSet<string> files = export.FileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<NpcSprite, NpcArt> spriteArt = [];
        foreach (NpcSprite sprite in spriteKeys.Where(IsPerson).OrderBy(sprite => sprite.Number))
        {
            string file = $"TEXTURE.{sprite.Archive:000}";
            if (!files.Contains(file))
            {
                export.Warn($"{sprite.Id} refers to absent {file}, record {sprite.Record}.");
                continue;
            }
            IReadOnlyList<ReferenceImage> images = export.TextureImages("art", "sprite", sprite.Archive, sprite.Record);
            string label = images.Count == 1 ? "Billboard sprite" : "Billboard animation";
            if (SpritePoses.TryGetValue(sprite, out string? pose)) label = $"{pose} · {label}";
            if (images.Count > 0) spriteArt.Add(sprite, new(label, images));
        }

        var portraits = new PortraitBank(export);
        List<NpcEntry> entries = [];
        // Faction ids and sprite ids are independent. For example, two named NPCs can use one sprite.
        ILookup<NpcSprite, ClassicFaction> namedBySprite = factions.Values.Where(IsNamedPerson)
            .SelectMany(faction => faction.Flats.Where(id => id > 0).Select(NpcSprite.Decode)
                .Concat(factionPlacements[faction.Id].Select(placement => placement.Sprite)).Distinct()
                .Where(spriteArt.ContainsKey).Select(sprite => (Sprite: sprite, Faction: faction)))
            .ToLookup(value => value.Sprite, value => value.Faction);
        var namedSprites = namedBySprite.SelectMany(group => group.Select(faction => (faction.Id, Sprite: group.Key)))
            .ToLookup(value => value.Id, value => value.Sprite);
        foreach (ClassicFaction faction in factions.Values.Where(IsNamedPerson).OrderBy(faction => faction.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (faction.Id == 304) continue; // Skakmat's portrait is left over from cut content.
            string sourceId = FactionId(faction);
            if (FindId(sourceId) is not { } id) continue;
            NpcSprite[] sprites = [.. namedSprites[faction.Id].OrderBy(sprite => sprite.Number)];
            List<NpcArt> art = [.. sprites.Select(sprite => spriteArt[sprite])];
            string portraitSource = faction.Face <= 60 ? SpecialFaces : CommonFaces;
            if (faction.Face >= 0 && portraits.Get(portraitSource, faction.Face) is { } portrait)
                art.Add(new("Dialogue portrait", [portrait]));
            if (art.Count == 0) continue;

            Dictionary<string, string> facts = new()
            {
                ["Role"] = faction.Type == 0 ? "Daedric quest patron" : "Named character",
                ["Faction ID"] = faction.Id == faction.FiledId ? $"{faction.Id}" : $"{faction.Id} (filed as {faction.FiledId})",
            };
            if (faction.Region >= 0) facts["Faction region"] = world.Region(faction.Region);
            if (factions.TryGetValue(faction.Parent, out ClassicFaction? parent)) facts["Affiliation"] = parent.Name;
            if (!factionPlacements[faction.Id].Any()) facts["World placements"] = "No fixed block placements found for this identity.";

            NpcReferenceId[] shared = [.. sprites.Where(sprite => ids.ContainsKey(sprite.Id)).Select(sprite => ids[sprite.Id]).Distinct()];
            entries.Add(new(id, faction.Name, "Named NPC", art, [.. factionPlacements[faction.Id]], facts,
                [.. shared.Select(reference => new NpcLink($"Shared billboard {reference.Id}", $"../{reference.Id}/index.html"))],
                [sourceId, .. sprites.Where(sprite => !ids.ContainsKey(sprite.Id)).SelectMany(SpriteAliases)]));
        }

        var spriteGroups = spriteArt.Keys.ToLookup(sprite => ids.GetValueOrDefault(sprite.Id)?.PrimarySource ?? sprite.Id);
        foreach (var group in spriteGroups)
        {
            NpcSprite[] sources = [.. group.OrderBy(sprite => sprite.Id == group.Key ? 0 : 1)];
            NpcArt primary = spriteArt[sources[0]];
            ClassicFaction[] named = [.. sources.SelectMany(source => namedBySprite[source]).DistinctBy(faction => faction.Id)];
            NpcPlacement[] uses = [.. sources.SelectMany(source => placements[source])
                .Select(use => use with { Pose = SpritePoses.GetValueOrDefault(use.Sprite) })];
            ClassicFaction[] genericFactions = [.. sources.SelectMany(source => factionSprites[source])
                .Concat(uses.Select(use => factions.GetValueOrDefault(use.Faction)).OfType<ClassicFaction>())
                .Where(faction => !IsNamedPerson(faction) && faction.Id != 0).DistinctBy(faction => faction.Id).OrderBy(faction => faction.Name, StringComparer.Ordinal)];

            // Artwork that only belongs to named NPCs is shown on those pages. A registered billboard
            // keeps its page even if new source data changes its known placements or affiliations.
            if (!ids.ContainsKey(group.Key) && named.Length > 0 && genericFactions.Length == 0 &&
                uses.All(use => named.Any(faction => faction.Id == use.Faction))) continue;
            if (FindId(group.Key) is not { } id) continue;

            NpcReferenceId reference = ids[group.Key];
            List<NpcArt> art = [primary];
            foreach (NpcSprite source in sources.Skip(1))
            {
                NpcArt variant = spriteArt[source];
                string label = SpritePoses.ContainsKey(source) ? variant.Label :
                    variant.Frames.Count == 1 ? "Alternate billboard" : "Alternate animation";
                art.Add(variant with
                {
                    Label = $"{label} · {source.Archive:000}-{source.Record}",
                    Identical = art.Any(existing => !existing.Identical && SameFrames(export.Root, existing, variant)),
                });
            }

            Dictionary<string, string> facts = new();
            string kind = art.Any(animation => animation.Frames.Count > 1) ? "Animated billboard" : "Billboard";
            SpriteCaption[] sourceCaptions = [.. sources.Select(source => captions.GetValueOrDefault(source)).OfType<SpriteCaption>()];
            if (sourceCaptions.Length > 0)
            {
                facts["Source caption"] = string.Join(", ", sourceCaptions.Select(caption => caption.Caption).Distinct());
                facts["Appearance"] = string.Join(", ", sourceCaptions.Select(caption => caption.Gender).Distinct());
                ReferenceImage[] faces = [.. sourceCaptions.Select(caption => caption.Face).Distinct()
                    .Where(face => face >= 0 && face != PlaceholderFace).Select(face => portraits.Get(CommonFaces, face)).OfType<ReferenceImage>()];
                if (faces.Length > 0) art.Add(new(faces.Length == 1 ? "Sprite’s dialogue portrait" : "Sprites’ dialogue portraits", faces));
            }
            List<int> filedFaces = [];
            foreach (string portraitSource in reference.PortraitSources)
            {
                if (!int.TryParse(portraitSource.AsSpan("npc-face-".Length), out int record))
                {
                    export.Warn($"{id} has an invalid portrait key '{portraitSource}'.");
                    continue;
                }
                if (portraits.Get(SpecialFaces, record) is not { } face) continue;
                string label = art.Any(item => item.Frames[0].Role == "portrait") ? "Alternate faction portrait" : "Faction portrait";
                art.Add(new(label, [face]));
                filedFaces.Add(record);
            }
            if (filedFaces.Count > 0)
                facts["Portrait faction records"] = string.Join(", ", factions.Values.Where(faction => filedFaces.Contains(faction.Face))
                    .Select(faction => $"{faction.Name} (#{faction.Id})"));
            if (genericFactions.Length > 0) facts["Factions using this sprite"] = string.Join(", ", genericFactions.Select(faction => faction.Name));
            if (uses.Length == 0) facts["World placements"] = "No fixed block placements found for this sprite.";

            var related = named.Where(faction => ids.ContainsKey(FactionId(faction)))
                .Select(faction => new NpcLink(faction.Name, $"../{ids[FactionId(faction)].Id}/index.html"));
            string name = reference.Name ?? sourceCaptions.FirstOrDefault()?.Caption ?? (named.Length == 1 ? named[0].Name : kind);
            entries.Add(new(id, name, kind, art, uses, facts,
                [.. related], [.. reference.Aliases, .. reference.PortraitSources, .. sources.SelectMany(SpriteAliases)]));
        }

        var characterLinks = ReadCharacterLinks(characters);
        ILookup<int, EnemyClass> enemyClasses = EnemyClasses(basePayload).ToLookup(enemy => enemy.Archive);
        foreach (var source in DirectionalSpriteReferenceExport.Sources()
            .Where(source => source.Family is "townspeople" or "guard" || source.Archive is 473 or (>= FirstRider and <= LastRider))
            .Concat(enemyClasses.Select(set => new DirectionalSpriteReferenceExport.Source(set.Key,
                $"{string.Join(", ", set.Select(enemy => enemy.Name))} ({set.First().Gender})", "enemy-class", Gender: set.First().Gender)))
            .OrderBy(source => source.Archive))
        {
            int archive = source.Archive;
            string sourceId = $"npc-{archive:000}", alias = $"mobile-{archive:000}";
            if (FindId(sourceId) is not { } id) continue;
            List<NpcArt> art = ReadDirectional(export, source);
            Dictionary<string, string> facts = new() { ["Reference alias"] = alias };
            // A mobile's character stages are named by its mobile id, and its female set's by that id and "-female".
            string[] mobileAliases = [];
            string kind;
            switch (source.Family)
            {
                case "townspeople":
                    kind = "Wandering townsfolk";
                    facts["Display appearance"] = $"{source.DisplayRace} · {source.Gender} · outfit {source.Outfit}";
                    facts["World use"] = "Procedurally populated streets. Name and dialogue portrait vary by instance.";
                    int start = FaceStarts[archive];
                    art.Add(new("Dialogue portrait variants", [.. portraits.Range(CommonFaces, start, 24)]));
                    break;
                case "guard":
                    kind = "Wandering guard";
                    mobileAliases = ["mobile-146"];
                    facts["World use"] = "Civilian city watch patrol variant. Dialogue portraits come from the corresponding male outfit 1 face banks.";
                    art.Add(new("Dialogue portrait variants", [.. new[] { 385, 387, 381 }.SelectMany(outfit =>
                        portraits.Range(CommonFaces, FaceStarts[outfit], 24))]));
                    break;
                case "enemy-class":
                    kind = "Enemy class";
                    EnemyClass[] classes = [.. enemyClasses[archive]];
                    mobileAliases = [.. classes.Select(enemy => enemy.Gender == "female" ? $"mobile-{enemy.MobileId}-female" : $"mobile-{enemy.MobileId}")];
                    facts["Classes"] = string.Join(", ", classes.Select(enemy => enemy.Name));
                    facts["Mobile IDs"] = string.Join(", ", classes.Select(enemy => enemy.MobileId));
                    facts["World use"] = "Hostile class in dungeons and wilderness encounters. Its behaviour is keyed by its mobile IDs; this record is its art.";
                    break;
                case "supplemental" when archive != 473:
                    kind = "Mounted figure";
                    facts["World use"] = "Source-only directional set; no gameplay consumer is established.";
                    break;
                default:
                    kind = "Named NPC";
                    facts["World use"] = "King Lysandus’s spectre in the main quest. Lysandus’ Revenge names his tomb and Woodborne Hall.";
                    break;
            }
            string[] stages = [.. mobileAliases.Select(mobile => mobile["mobile-".Length..])];
            string[] matching = [.. characterLinks.Keys.Where(key => key == $"{archive:000}" || key.StartsWith($"{archive:000}-", StringComparison.Ordinal)
                || stages.Contains(key)).Order(StringComparer.Ordinal)];
            var links = matching.Select(key => new NpcLink($"Character pipeline {key}",
                Path.GetRelativePath(Path.Combine(export.Root, id), characterLinks[key]).Replace('\\', '/')));
            string[] aliases = sourceId == id ? [alias, .. mobileAliases, .. matching] : [alias, sourceId, .. mobileAliases, .. matching];
            entries.Add(new(id, source.Name, kind, art, [], facts, [.. links], aliases));
        }

        NpcReferencePages.Write(export, world, factions, NpcReferenceMerges.Apply(export, entries, merges));

        string? FindId(string source)
        {
            if (ids.TryGetValue(source, out NpcReferenceId? reference)) return reference.Id;
            export.Warn($"No NPC number reserved for {source}. Add an entry to NpcReferenceIds.txt to include it.");
            return null;
        }
    }

    internal static bool SameFrames(string root, NpcArt first, NpcArt second) =>
        first.Frames.Count == second.Frames.Count && first.Frames.Zip(second.Frames).All(pair =>
            File.ReadAllBytes(Path.Combine(root, "art", pair.First.File)).AsSpan()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(root, "art", pair.Second.File))));

    private sealed record EnemyClass(int Archive, int MobileId, string Name, string Gender);

    /// <summary>The human enemy classes by the sprite set each draws: a class has a male and a female set, and classes share sets.
    /// The City Watch draws the guard's townsfolk set, so it is the guard's record.</summary>
    private static IEnumerable<EnemyClass> EnemyClasses(string basePayload)
    {
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(basePayload));
        foreach (JsonElement mobile in payload.RootElement.GetProperty("mobiles").GetProperty("mobiles").EnumerateArray()
            .Where(mobile => mobile.GetProperty("affinity").GetString() == "Human"))
        {
            int id = mobile.GetProperty("donorId").GetInt32();
            string name = mobile.GetProperty("donorName").GetString()!;
            foreach ((string property, string gender) in new[] { ("maleTexture", "male"), ("femaleTexture", "female") })
                if (mobile.GetProperty(property).GetInt32() is var archive and not CityWatchArchive)
                    yield return new(archive, id, name, gender);
        }
    }

    private static string[] SpriteAliases(NpcSprite sprite) => [sprite.Id, $"{sprite.Archive:000}-{sprite.Record}"];

    private static bool IsNamedPerson(ClassicFaction faction) => faction.Id > 0 && faction.Type is 0 or 4;
    private static string FactionId(ClassicFaction faction) => $"npc-faction-{faction.Id:000}";
    private static bool IsPerson(NpcSprite sprite) => sprite.Archive > 0 && sprite.Archive != RdbSourceClassification.EditorFlatArchive && !Props.Contains(sprite);

    private static Dictionary<NpcSprite, SpriteCaption> ReadCaptions(ReferenceExport export)
    {
        Dictionary<NpcSprite, SpriteCaption> captions = [];
        string text = Encoding.UTF8.GetString(export.Read("FLATS.CFG")).Replace("\r", "", StringComparison.Ordinal);
        foreach (string block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] rows = block.Split('\n', StringSplitOptions.TrimEntries);
            string[] pair = rows[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length < 6 || pair.Length != 2 || !int.TryParse(pair[0], out int archive) ||
                !int.TryParse(pair[1], out int record) || !int.TryParse(rows[5], out int face))
            {
                export.Warn($"Skipped malformed FLATS.CFG section '{rows[0]}'.");
                continue;
            }
            captions.TryAdd(new(archive, record), new(rows[1], rows[2].Contains('2') ? "Female" : "Male", face));
        }
        return captions;
    }

    private static Dictionary<string, string> ReadCharacterLinks(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory).Where(path => File.Exists(Path.Combine(path, "index.html")))
                .ToDictionary(path => Path.GetFileName(path), path => Path.GetFullPath(Path.Combine(path, "index.html")), StringComparer.Ordinal)
            : new(StringComparer.Ordinal);

    private static List<NpcArt> ReadDirectional(ReferenceExport export, DirectionalSpriteReferenceExport.Source source)
    {
        TextureArchive texture = export.Texture(source.Archive);
        List<NpcArt> art = [];
        for (int record = 0; record < texture.RecordCount; record++)
        {
            string label = source.Family != "supplemental" && record < 5
                ? $"Walk · {DirectionalSpriteReferenceExport.Directions[record]}"
                : $"Source record {record}";
            if ((source.Family == "townspeople" && record == 5) || (source.Family == "guard" && record == 15)) label = "Idle · front";
            if (source.Archive == 473) label = $"Spectral sprite · record {record}";
            List<ReferenceImage> frames = [];
            if (Arena2SpectralTextures.IsSpectralArchive((ushort)source.Archive))
            {
                TextureRecordInfo info = texture.GetRecordInfo(record);
                for (int frame = 0; frame < info.FrameCount; frame++)
                {
                    IndexedTextureFrame decoded = texture.DecodeFrame(record, frame);
                    frames.Add(export.Rgba("art", $"sprite/texture-{source.Archive:000}-record-{record:000}-frame-{frame:000}-source.png",
                        "sprite", texture.Source, record, frame, decoded.Width, decoded.Height, info.XOffset, info.YOffset,
                        Arena2SpectralTextures.ToRgbaBytes(decoded.Pixels.Span, export.Palette, texture.Source), ["source"]));
                }
            }
            else frames = export.TextureImages("art", "sprite", source.Archive, record);
            if (frames.Count > 0) art.Add(new(label, frames));
        }
        return art;
    }

    private sealed class PortraitBank(ReferenceExport export)
    {
        private readonly Dictionary<string, byte[]> raw = new(StringComparer.Ordinal);
        private readonly Dictionary<(string File, int Record), ReferenceImage> images = [];

        public IEnumerable<ReferenceImage> Range(string file, int start, int count)
        {
            for (int index = start; index < start + count; index++)
                if (Get(file, index) is { } image) yield return image;
        }

        public ReferenceImage? Get(string file, int record)
        {
            if (images.TryGetValue((file, record), out ReferenceImage? image)) return image;
            if (record < 0 || record >= Count(file))
            {
                export.Warn($"{file} portrait {record} is absent.");
                return null;
            }

            ReadOnlySpan<byte> pixels = Raw(file).AsSpan(record * PortraitCellBytes, PortraitCellBytes);
            image = export.Rgba("art", $"portrait/{file.ToLowerInvariant()}-{record:000}.png", "portrait", $"arena2/{file}", record, 0,
                PortraitSide, PortraitSide, 0, 0, export.Palette.ToRgbaBytes(pixels, PaletteAlphaMode.IndexZeroTransparent), ["source"]);
            images.Add((file, record), image);
            return image;
        }

        private int Count(string file) => Raw(file).Length / PortraitCellBytes;
        private byte[] Raw(string file)
        {
            if (!raw.TryGetValue(file, out byte[]? bytes)) raw[file] = bytes = export.Read(file);
            return bytes;
        }
    }
}
