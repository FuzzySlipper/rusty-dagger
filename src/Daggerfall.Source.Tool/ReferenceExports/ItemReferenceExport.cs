using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.ReferenceExports;

/// <summary>All item templates, their reachable player records, artifacts and first-person companions.</summary>
internal static class ItemReferenceExport
{
    /// <summary>One item template, as the base pack's <c>itemTemplates</c> section states it.</summary>
    internal sealed record Template(int Index, string Name, IReadOnlyList<string> Groups, int WorldTextureArchive, int WorldTextureRecord,
        int PlayerTextureArchive, int PlayerTextureRecord, int Variants, bool IsOneHanded, bool IsIngredient, string WeaponMediaId, JsonNode Row)
    {
        internal static Template Read(JsonNode row) => new(row["index"]!.GetValue<int>(), row["name"]!.GetValue<string>(),
            [.. row["groups"]!.AsArray().Select(group => group!.GetValue<string>())],
            row["worldTextureArchive"]!.GetValue<int>(), row["worldTextureRecord"]!.GetValue<int>(),
            row["playerTextureArchive"]!.GetValue<int>(), row["playerTextureRecord"]!.GetValue<int>(), row["variants"]!.GetValue<int>(),
            row["isOneHanded"]!.GetValue<bool>(), row["isIngredient"]!.GetValue<bool>(), row["weaponMediaId"]?.GetValue<string>() ?? "", row);
    }

    private static readonly string[] Morphologies = ["argonian", "elf", "human", "khajiit"];
    /// <summary>The donor's <c>ItemHelper.artifactTextureIndexMappings</c>, as the ruleset's paper doll also carries it.</summary>
    private static readonly int[] ArtifactRecords = [12, 13, 10, 8, 19, 16, 25, 18, 21, 2, 24, 26, 0, 15, 3, 9, 23, 17, 7, 1, 22, 20, 5];
    private static readonly string[] Actions = ["idle", "strike-down", "strike-down-left", "strike-left", "strike-right", "strike-down-right", "strike-up"];

    /// <param name="basePayload">The base pack's payload, whose <c>itemTemplates</c> section names every template.</param>
    public static void Run(string arena2, string basePayload, string output)
    {
        JsonNode section = JsonNode.Parse(File.ReadAllBytes(basePayload))?["itemTemplates"]
            ?? throw new InvalidOperationException($"'{basePayload}' carries no itemTemplates section.");
        IReadOnlyList<Template> templates = [.. section["templates"]!.AsArray().Select(row => Template.Read(row!))];
        IReadOnlyList<JsonNode> magic = [.. section["magic"]!.AsArray().Select(row => row!)];
        IReadOnlyList<(int Id, string Name, int[] Values)> groups = [.. section["groupTable"]!["groups"]!.AsArray()
            .Select(group => (group!["id"]!.GetValue<int>(), group["name"]!.GetValue<string>(), group["values"]!.AsArray().Select(value => value!.GetValue<int>()).ToArray()))
            .OrderBy(group => group.Item1)];
        ReferenceExport export = new(arena2, output);
        Dictionary<int, string> directories = [];
        List<int> withoutWorldArt = [], withoutPlayerArt = [];
        foreach (Template template in templates.OrderBy(template => template.Index))
        {
            string[] names = [.. template.Groups];
            string group = names.FirstOrDefault() ?? "unassigned";
            string directory = $"{ReferenceExport.Slug(group)}/item-{template.Index:000}-{ReferenceExport.Slug(template.Name)}";
            directories[template.Index] = directory;
            List<ReferenceImage> images = export.TextureImages(directory, "world", template.WorldTextureArchive, template.WorldTextureRecord);
            string worldArt = template.WorldTextureArchive == 0 && template.WorldTextureRecord == 0 ? "no-source-texture"
                : images.Count == 0 ? "source-record-unavailable" : "exported";
            if (images.Count == 0) withoutWorldArt.Add(template.Index);
            bool worn = (group is "Armor" or "Weapons" or "MensClothing" or "WomensClothing") && template.PlayerTextureArchive != 0;
            bool cloak = (group is "MensClothing" or "WomensClothing") && template.Name.EndsWith(" Cloak", StringComparison.OrdinalIgnoreCase);
            IReadOnlyDictionary<int, IReadOnlySet<int>> player = PlayerRecords(template, group, cloak);
            foreach ((int archive, IReadOnlySet<int> records) in player.OrderBy(entry => entry.Key))
                foreach (int record in records.Order())
                    images.AddRange(export.TextureImages(directory, $"player/{Body(group, archive)}/{PlayerVariant(template, group, cloak, record)}", archive, record,
                        playerMask: true, clothingDyes: worn ? group is "MensClothing" or "WomensClothing" : null));
            if (player.Count == 0) withoutPlayerArt.Add(template.Index);
            string? weapon = WeaponFile(template.WeaponMediaId);
            if (weapon is not null)
            {
                images.AddRange(WeaponImages(export, directory, weapon, "normal"));
                if (weapon != "WEAPON09.CIF") images.AddRange(WeaponImages(export, directory, weapon.Replace("WEAPON", "WEAPO1", StringComparison.Ordinal), "enchanted"));
            }
            bool inventoryUsesWorld = template.IsIngredient || group is "UselessItems1" or "ReligiousItems" or "MiscItems"
                || template.Index == 131 || template.PlayerTextureArchive == 0 && template.PlayerTextureRecord == 0;
            string note = weapon is null
                ? "This item has no dedicated first-person animation. Armor and clothing are paper-doll layers; arrows use the bow's animation."
                : "First-person source frames include the normal and, where supplied, enchanted weapon family, in all metal palette swaps. Related items share these source files.";
            export.Subject(directory, template.Name, new
            {
                kind = "item", template.Index, template.Name, directory, groups = names, template = template.Row, worldArt,
                playerArt = player.Count == 0 ? "no-separate-player-texture; inventory uses world art" : "exported",
                inventory = new { usesWorld = inventoryUsesWorld, katanaRecordOffset = template.Index == 121 ? 1 : 0,
                    variants = inventoryUsesWorld ? 1 : Math.Max(1, template.Variants), cloakInterior = cloak,
                    note = "Inventory uses the current player/body/variant record unless usesWorld is true. A one-handed weapon uses the next player record in the right hand on the paper doll." },
                firstPersonSource = weapon, note, images,
            }, images, note);
        }

        // ItemHelper.LoadMagicItemTemplates retains artifact order; its parallel record table addresses that order.
        int[] artifactRecords = ArtifactRecords;
        JsonNode[] artifacts = [.. magic.Where(template => template["type"]!.GetValue<string>().StartsWith("ArtifactClass", StringComparison.Ordinal))];
        if (artifactRecords.Length != artifacts.Length)
            throw new InvalidOperationException($"The donor has {artifacts.Length} artifacts but {artifactRecords.Length} texture mappings; exporting them would pair names with the wrong images.");
        List<object> magicLinks = [];
        for (int i = 0; i < artifacts.Length; i++)
        {
            JsonNode artifact = artifacts[i];
            string name = artifact["name"]!.GetValue<string>();
            (int Id, string Name, int[] Values) group = groups.Single(group => group.Id == artifact["group"]!.GetValue<int>());
            Template basis = templates.Single(template => template.Index == group.Values[artifact["groupIndex"]!.GetValue<int>()]);
            string directory = $"artifacts/artifact-{i:00}-{ReferenceExport.Slug(name)}";
            List<ReferenceImage> images = [];
            foreach (int archive in PaperDollItemPublication.ArtifactArchives)
            {
                string body = archive == 432 ? "male" : "female";
                images.AddRange(export.TextureImages(directory, $"world/{body}", archive, artifactRecords[i]));
                images.AddRange(export.TextureImages(directory, $"player/{body}", archive, artifactRecords[i], playerMask: true));
                if (group.Name == "Weapons" && basis.IsOneHanded)
                    images.AddRange(export.TextureImages(directory, $"player/{body}-right-hand", archive, artifactRecords[i] + 1, playerMask: true));
            }
            string? weapon = WeaponFile(basis.WeaponMediaId);
            if (weapon is not null) images.AddRange(WeaponImages(export, directory,
                weapon == "WEAPON09.CIF" ? weapon : weapon.Replace("WEAPON", "WEAPO1", StringComparison.Ordinal), "enchanted"));
            string note = "Artifact player art is undyed. The donor also uses it as world art, explicitly as a substitute; this is not a separate classic world sprite. "
                + (weapon is null ? "This artifact has no dedicated first-person animation." : "First-person art is inherited from the enchanted base weapon family.");
            export.Subject(directory, name, new { kind = "artifact", artifactOrdinal = i, artifact, directory,
                baseTemplate = basis.Index, baseDirectory = directories[basis.Index], record = artifactRecords[i], note, images }, images, note);
            magicLinks.Add(new { Index = artifact["index"]!.GetValue<int>(), directory });
        }
        // Regular magic templates define enchantments/applicability, not an additional set of item images.
        export.WriteJson("magic-templates.json", new { templates = magic,
            note = "Regular magic prefixes/suffixes reuse the chosen base item's textures and enchanted first-person family. Artifact directories below carry their distinct player textures.", artifacts = magicLinks });

        // Retain supplied source families with no item consumer, plus bare hands and werecreature claws.
        string[] bound = templates.Select(template => WeaponFile(template.WeaponMediaId)).OfType<string>().Distinct().ToArray();
        string[] extras = export.FileNames.Where(file => Regex.IsMatch(file, @"^WEAPON\d\d\.CIF$", RegexOptions.IgnoreCase) && !bound.Contains(file, StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (string file in extras)
        {
            string name = file switch { "WEAPON10.CIF" => "Unarmed hands", "WEAPON11.CIF" => "Werecreature claws", _ => "Unassigned weapon source " + file };
            string directory = $"first-person-extra/{ReferenceExport.Slug(name)}";
            List<ReferenceImage> images = WeaponImages(export, directory, file, "source", metals: false);
            export.Subject(directory, name, new { kind = "extra-first-person", directory, source = file,
                note = "No item template is invented for this source. Unassigned files retain record identities.", images }, images);
        }
        export.Finish("Daggerfall item references", new { itemTemplates = templates.Count, artifacts = artifacts.Length,
                magicTemplates = magic.Count, extraFirstPersonFamilies = extras.Length, withoutWorldArt, withoutPlayerArt },
            new { bodyMorphologies = Morphologies, magicTemplates = "magic-templates.json", firstPersonMirror = "Original source orientation only; the other hand is mirrored at runtime.",
                sourceNotes = "World texture zero/zero means no source art. A player texture zero/zero uses world art for inventory. Dye aliases describe palette equivalence, not legal material/variant combinations." },
            "Open index.html for a visual catalog; index.csv lists every subject. Each subject has manifest.json, world/, player/, and first-person/ where applicable.\n" +
            "Every template is retained, including furniture with no 2D source art. Player records include body morphology, gender, variants, cloak interiors, one-handed alternatives, material/clothing dyes and masks. The file name retains archive, record and frame.\n" +
            "First-person frames are original unmirrored pixels; normal/enchanted families are separate. Bow record 0 contains the entire firing cycle. Shared animations are copied into each applicable item directory. Color aliases in manifests avoid duplicate unchanged dye images. Armor/clothing have no dedicated first-person sprites.\n" +
            "Artifacts have their own folders and base-item links. magic-templates.json describes ordinary magical items reusing base art. No image generation jobs have been submitted.\n" +
            "Reproduce from the repository root: dotnet run --project src/Daggerfall.Import.Tool -- export-items --output local/art/reference/NEW_ITEMS_DIRECTORY\n" +
            "Use a fresh output directory to preserve experiments. Extracted game art stays in ignored local/art/; do not commit it.",
            ["content/worldrpg/packs/daggerfall.base/payload.json: itemTemplates",
                "daggerfall-unity/Assets/Scripts/Game/Items/ItemHelper.cs: GetItemImage, GetWeaponType, artifactTextureIndexMappings",
                "daggerfall-unity/Assets/Scripts/Game/Items/ItemBuilder.cs: ApplyArmorSettings, SetRace",
                "daggerfall-unity/Assets/Scripts/Game/Items/DaggerfallUnityItem.cs: GetInventoryTextureRecord, UseWorldTexture, SetArtifact",
                "daggerfall-unity/Assets/Scripts/Utility/WeaponBasics.cs", "arena2/TEXTURE.*", "arena2/WEAPON*.CIF", "arena2/WEAPO1*.CIF"]);
    }

    /// <summary>
    /// Every player record a template can be drawn from, by archive (<c>ItemHelper.GetItemImage</c>): an armour template from
    /// 245 for a female and 249 for a male, clothing from its own archive, both across the four body morphologies, and a
    /// weapon from its own; its variants, a cloak's interior and a one-handed weapon's right-hand record.
    /// </summary>
    internal static IReadOnlyDictionary<int, IReadOnlySet<int>> PlayerRecords(Template template, string group, bool cloak)
    {
        if (template.PlayerTextureArchive == 0 && template.PlayerTextureRecord == 0) return new Dictionary<int, IReadOnlySet<int>>();
        if (group is not ("Armor" or "Weapons" or "MensClothing" or "WomensClothing"))
            return new Dictionary<int, IReadOnlySet<int>> { [template.PlayerTextureArchive] = Enumerable.Range(template.PlayerTextureRecord, Math.Max(1, template.Variants)).ToHashSet() };
        int[] archives = group switch
        {
            "Weapons" => [template.PlayerTextureArchive],
            "Armor" => [.. Enumerable.Range(245, 4), .. Enumerable.Range(249, 4)],
            _ => [.. Enumerable.Range(template.PlayerTextureArchive, 4)],
        };
        HashSet<int> records = [.. Enumerable.Range(template.PlayerTextureRecord + (cloak ? 1 : 0), Math.Max(1, template.Variants))];
        if (cloak) records.Add(template.PlayerTextureRecord);
        if (template.IsOneHanded) records.Add(template.PlayerTextureRecord + 1);
        return archives.ToDictionary(archive => archive, IReadOnlySet<int> (_) => records);
    }

    private static string Body(string group, int archive) => group switch
    {
        "Armor" => (archive >= 249 ? "male-" : "female-") + Morphologies[(archive - 245) % 4],
        "MensClothing" => "male-" + Morphologies[(archive - 239) % 4],
        "WomensClothing" => "female-" + Morphologies[(archive - 235) % 4],
        _ => "shared",
    };

    private static string PlayerVariant(Template template, string group, bool cloak, int record)
    {
        if (cloak && record == template.PlayerTextureRecord) return "cloak-interior";
        if (group == "Weapons" && template.IsOneHanded)
            return record == template.PlayerTextureRecord ? "left-hand" : "right-hand";
        return template.Variants > 0 ? $"variant-{record - template.PlayerTextureRecord - (cloak ? 1 : 0):00}" : "base";
    }

    /// <summary>The first-person weapon file a template's weapon media draws from.</summary>
    internal static string? WeaponFile(string weaponMediaId) => weaponMediaId switch
    {
        "weapon.staff" => "WEAPON01.CIF", "weapon.dagger.steel" => "WEAPON02.CIF", "weapon.longblade" => "WEAPON04.CIF",
        "weapon.mace" => "WEAPON05.CIF", "weapon.flail" => "WEAPON06.CIF", "weapon.warhammer" => "WEAPON07.CIF",
        "weapon.axe" => "WEAPON08.CIF", "weapon.bow" => "WEAPON09.CIF", _ => null,
    };

    private static List<ReferenceImage> WeaponImages(ReferenceExport export, string directory, string file, string variant, bool metals = true)
    {
        WeaponCifArchive weapon = WeaponCifArchive.Parse(export.Read(file), "arena2/" + file);
        List<ReferenceImage> images = [];
        for (int record = 0; record < weapon.RecordCount; record++)
        {
            WeaponCifRecordInfo info = weapon.GetRecordInfo(record);
            string action = file == "WEAPON09.CIF" ? "bow-cycle" : record < Actions.Length ? Actions[record] : "source-record";
            if (file == "WEAPO108.CIF") action = record switch { 3 => "strike-down-right", 4 => "strike-left", 5 => "strike-right", _ => action };
            for (int frame = 0; frame < info.FrameCount; frame++)
            {
                IndexedWeaponCifFrame decoded = weapon.DecodeFrame(record, frame);
                string role = $"first-person/{variant}/{action}";
                images.AddRange(export.Indexed(directory, $"{role}/{Path.GetFileNameWithoutExtension(file).ToLowerInvariant()}-record-{record:00}-frame-{frame:00}",
                    role, weapon.Source, record, frame, info.Width, info.Height, info.XOffset, info.YOffset, decoded.Pixels.ToArray(), metals ? false : null));
            }
        }
        return images;
    }
}
