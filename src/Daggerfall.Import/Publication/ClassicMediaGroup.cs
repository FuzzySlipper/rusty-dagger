using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>
/// The product-wide classic media group: the classic media publication's artifacts other than its audio
/// clips, and the generated inventory that indexes them.
/// </summary>
/// <remarks>
/// The group publishes no audio. The product-wide world media publication carries the clips every site's
/// classic sidecar maps, under the one audio bundle the Host declares for it, and the session opens clips
/// only from that bundle, so copies of the same WAV bodies here would be bytes nothing opens. The images
/// this group publishes are, in turn, referenced by the world media publication and the site closures
/// rather than copied into them.
/// The sound catalog is built from the same publication as an importer-side disposition report of the
/// numeric archive; nothing at runtime reads it, so it is reported by the command rather than published.
/// </remarks>
public sealed class ClassicMediaGroup
{
    /// <summary>The generator the group's inventory names.</summary>
    public const string Generator = "daggerfall-import-tool classic-media";

    /// <summary>The group-relative path of the generated inventory.</summary>
    public const string InventoryRelativePath = "media/classic-media-inventory.json";

    private ClassicMediaGroup(Arena2ClassicMediaPublication publication, DaggerfallSoundCatalog catalog, IReadOnlyList<ImportPublicationArtifact> artifacts)
    {
        Publication = publication;
        SoundCatalog = catalog;
        Artifacts = artifacts;
    }

    public Arena2ClassicMediaPublication Publication { get; }

    /// <summary>The disposition of every clip of the numeric archive, which the group reports and does not publish.</summary>
    public DaggerfallSoundCatalog SoundCatalog { get; }

    /// <summary>Every group-relative artifact the group publishes: the publication's artifacts without its audio clips.</summary>
    public IReadOnlyList<ImportPublicationArtifact> Artifacts { get; }

    /// <summary>
    /// Builds the classic media and its sound catalog. The catalog's admitted entries are the
    /// publication's own audio manifests, so "a site closure carries this clip" is a reference to bytes
    /// the same publication emits rather than a second list the catalog keeps in agreement.
    /// </summary>
    public static ClassicMediaGroup Create(Arena2ClassicMediaInputs inputs, Arena2ClassicMediaProfile profile)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(profile);
        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(inputs, profile);
        DaggerfallSoundCatalog catalog = DaggerfallSoundCatalogBuilder.Build(
            SoundArchive.Parse(inputs.DaggerSound, Arena2ClassicMediaPublication.DaggerSoundSourcePath),
            publication.SoundAdmissions);
        HashSet<string> audio = [.. publication.Audio.Select(clip => clip.MediaId)];
        return new(publication, catalog,
            [.. publication.Artifacts.Where(artifact => artifact.MediaId is not { } mediaId || !audio.Contains(mediaId))]);
    }

    /// <summary>
    /// The group's inventory. A published UI image states the semantic screen it fills, a screen drawn
    /// with the palette inside its own file states that conversion fact, and a slot's original IMG is
    /// named beside the regenerated PNG's digest: generated-byte stability cannot prove that a slot still
    /// came from the required donor input.
    /// </summary>
    /// <param name="group">The content group the artifacts are published under.</param>
    /// <param name="suppliedFiles">The file names the corpus supplies, for the families stated as unread.</param>
    public byte[] WriteInventory(string group, IEnumerable<string> suppliedFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(suppliedFiles);
        Dictionary<string, ClassicUiImageManifest> images = Publication.UiImages.ToDictionary(image => image.MediaId, StringComparer.Ordinal);
        JsonObject Entry(ImportPublicationArtifact artifact)
        {
            JsonObject entry = ArtifactInventory.Entry($"{group}/{artifact.RelativePath}", artifact.Bytes.Span, artifact.MediaId);
            if (artifact.MediaId is not { } mediaId || !images.TryGetValue(mediaId, out ClassicUiImageManifest? image)) return entry;
            string slot = image.Slot.ToString();
            entry["slot"] = char.ToLowerInvariant(slot[0]) + slot[1..];
            if (image.OwnEmbeddedPalette)
            {
                // The bytes are the file's trailing palette scaled by four, which is why dropping the
                // scale darkens every colour in the artifact.
                entry["palette"] = new JsonObject
                {
                    ["source"] = "embedded-in-source-file",
                    ["channelScale"] = 4,
                    ["donorAnchor"] = "ImgFile.ReadPalette",
                };
            }

            LogicalSourceRecord source = Publication.Sources.Single(source => string.Equals(source.SourcePath, $"arena2/{image.SourceFile}", StringComparison.Ordinal));
            entry["source"] = new JsonObject
            {
                ["path"] = source.SourcePath,
                ["byteLength"] = source.ByteLength,
                ["sha256"] = source.ContentDigest.Value,
            };
            return entry;
        }

        string[] supplied = [.. suppliedFiles.Order(StringComparer.OrdinalIgnoreCase)];
        UnreadableMediaFamily[] unreadable = [.. UnpublishedFamilies
            .Select(family => family with { Files = [.. supplied.Where(file => file.EndsWith(family.Family, StringComparison.OrdinalIgnoreCase))] })
            .Where(family => family.Files.Count != 0)];
        return ArtifactInventory.Write(Generator, Artifacts.Select(Entry), unreadable);
    }

    /// <summary>
    /// The families this group reads and publishes no canvas from. The reason says which half is missing:
    /// these containers are read here, so the gap is a missing publisher rather than a missing decoder. The
    /// character group publishes the class portraits and compass frames from them through its own command,
    /// so the reason says which group is speaking. No family entry covers the CIF files: most are weapon,
    /// armour and painting grammars this repository reads and publishes, and the face grammar it refuses is
    /// refused by name when a face is read.
    /// </summary>
    private static readonly UnreadableMediaFamily[] UnpublishedFamilies =
    [
        new(".CEL", "class-question animation", [], "no publisher in this group: the FLC container and its frames are read, and the character group publishes the class portraits from them, but nothing here emits an artifact from these files and nothing plays them back, so this group carries no canvas for them", "Assets/Scripts/API/FlcFile.cs"),
        new(".BSS", "compass sprite bank", [], "no publisher in this group: the BSS container is decoded and its frames are published through the character group, but this classic-media group emits no artifact from these files", "Assets/Scripts/API/BssFile.cs"),
    ];
}
