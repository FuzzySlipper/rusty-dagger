using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How one clip of the numeric archive stands.</summary>
public enum DaggerfallSoundClipDisposition
{
    /// <summary>A published artifact carries this clip, so a consumer can reach it today.</summary>
    Admitted,

    /// <summary>The clip decodes and no published artifact carries it yet.</summary>
    ReadableNoConsumer,

    /// <summary>The clip cannot be converted, which is stated rather than omitted.</summary>
    Unsupported,
}

/// <summary>
/// One clip a published media closure carries, stated in the catalog's own vocabulary so the
/// catalog can record what the producer emitted instead of keeping a second list that has to agree
/// with it.
/// </summary>
/// <param name="Ordinal">The clip's position in the archive's own directory order, which is its stable identity.</param>
/// <param name="MediaId">The media identity a consumer asks the content store for.</param>
public sealed record DaggerfallSoundAdmission(int Ordinal, string MediaId)
{
    internal void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(MediaId, nameof(MediaId));
        if (Ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Ordinal), "A sound admission must name a clip in the archive's own order.");
        }
    }
}

/// <summary>
/// One clip of the numeric sound archive: where it sits, what it is, and how it stands.
/// </summary>
/// <param name="Ordinal">Its position in the archive's own directory order, which is its stable identity.</param>
/// <param name="NumericId">The numeric record identity the archive carries.</param>
/// <param name="ByteLength">How many sample bytes it holds.</param>
/// <param name="Disposition">How the clip stands.</param>
/// <param name="UsageCandidate">What the donor names it, when the donor names it at all.</param>
/// <param name="MediaId">The media identity a published artifact carries it under, when one does.</param>
/// <param name="Reason">Why the disposition holds, naming the conversion for an unsupported clip.</param>
public sealed record DaggerfallSoundClip(
    int Ordinal,
    uint NumericId,
    int ByteLength,
    DaggerfallSoundClipDisposition Disposition,
    string UsageCandidate,
    string? MediaId,
    string Reason);

/// <summary>
/// The published catalog of every clip the numeric sound archive carries.
/// </summary>
/// <param name="SchemaVersion">Shape version of this section.</param>
/// <param name="Clips">Every clip, in the archive's own directory order.</param>
/// <param name="Sources">The source identity the catalog was read from.</param>
public sealed record DaggerfallSoundCatalog(
    int SchemaVersion,
    IReadOnlyList<DaggerfallSoundClip> Clips,
    IReadOnlyList<string> Sources)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Sound catalog schema must be {CurrentSchemaVersion} but is {SchemaVersion}.");
        }

        // The ordinals are the stable identity a consumer keeps across releases, so they must be the
        // archive's own order without gaps or repeats: a renumbered catalog would silently repoint
        // every reference a consumer stored.
        HashSet<string> mediaIds = new(StringComparer.Ordinal);
        for (int index = 0; index < Clips.Count; index++)
        {
            if (Clips[index].Ordinal != index)
            {
                throw new InvalidOperationException($"Sound catalog clip {index} carries ordinal {Clips[index].Ordinal}, so the catalog is not in the archive's own order.");
            }

            if (string.IsNullOrWhiteSpace(Clips[index].Reason))
            {
                throw new InvalidOperationException($"Sound catalog clip {index} states no reason for being {Clips[index].Disposition}.");
            }

            bool admitted = Clips[index].Disposition == DaggerfallSoundClipDisposition.Admitted;
            bool named = Clips[index].MediaId is not null;
            if (admitted != named)
            {
                throw new InvalidOperationException($"Sound catalog clip {index} is {Clips[index].Disposition} and must {(admitted ? "name the media identity that carries it" : "name no media identity")}.");
            }

            // An admitted clip is a claim that a published artifact carries its samples, so a clip
            // with no samples cannot make it: that would send a consumer to an artifact no producer
            // can emit. The archive's one sample-less record is stated as unsupported instead.
            if (admitted && Clips[index].ByteLength <= 0)
            {
                throw new InvalidOperationException($"Sound catalog clip {index} claims a published artifact carries it but holds no sample bytes.");
            }

            // One artifact cannot stand for two clips. The builder refuses such a closure before it
            // becomes a catalog, and the record refuses it again because this is the check a persisted
            // catalog crosses: a consumer following an ordinal to a media identity would otherwise be
            // sent to the same bytes for both clips and could not tell which one it asked for.
            if (Clips[index].MediaId is { } mediaId)
            {
                NormalizedImportDocument.RequireLogicalId(mediaId, nameof(DaggerfallSoundClip.MediaId));
                if (!mediaIds.Add(mediaId))
                {
                    throw new InvalidOperationException($"Sound catalog clip {index} names media identity '{mediaId}', which an earlier clip already names.");
                }
            }
        }
    }
}

/// <summary>
/// The published JSON section for <see cref="DaggerfallSoundCatalog"/>, written and read with the
/// repository's canonical dialect so the published references a consumer follows are the same ones
/// this repository validates.
/// </summary>
public static class DaggerfallSoundCatalogJson
{
    /// <summary>
    /// The name the catalog is published under within a content group, beside the clips it describes.
    /// The group is the caller's, exactly as it is for every other artifact the publication emits: the
    /// content-root-relative name a consumer holds is the group joined to this path.
    /// </summary>
    public const string RelativePath = "media/audio/classic-sound-catalog.json";

    public static byte[] Write(DaggerfallSoundCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, PublishedJson.Section);
        return [.. bytes, (byte)'\n'];
    }

    public static DaggerfallSoundCatalog Read(ReadOnlySpan<byte> bytes)
    {
        DaggerfallSoundCatalog catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<DaggerfallSoundCatalog>(bytes, PublishedJson.Section)
                ?? throw new InvalidOperationException("The published sound catalog carries no section.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The published sound catalog is not valid JSON: {exception.Message}", exception);
        }

        catalog.Validate();
        return catalog;
    }
}

/// <summary>
/// Builds the published catalog from the numeric sound archive and the media closure that carries
/// its clips.
/// </summary>
/// <remarks>
/// The archive carries 459 numeric records and no names; the donor's own clip enum is where a name
/// comes from, and only the clips this product has a use for are named here. Everything else is
/// published as readable with no consumer rather than dropped, because a clip that is absent from the
/// catalog cannot be counted and one that says it has no consumer can. The admitted set is supplied
/// by the producer that emits the artifacts, so an admitted entry is a reference to real published
/// bytes rather than a list this catalog maintains beside them.
/// </remarks>
public static class DaggerfallSoundCatalogBuilder
{
    public static DaggerfallSoundCatalog Build(SoundArchive archive, IReadOnlyList<DaggerfallSoundAdmission> admitted)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(admitted);
        Dictionary<int, string> admissions = Admitting(archive, admitted);
        List<DaggerfallSoundClip> clips = new(archive.Count);
        for (int ordinal = 0; ordinal < archive.Count; ordinal++)
        {
            Arena2PcmClip clip = archive.GetClip(ordinal);
            bool isAdmitted = admissions.TryGetValue(ordinal, out string? mediaId);
            string name = DaggerfallSoundNames.For(ordinal);
            if (clip.PcmUnsigned8.Length == 0)
            {
                if (isAdmitted)
                {
                    throw new InvalidOperationException($"Clip {ordinal} carries no sample bytes, so no published artifact can carry it as '{mediaId}'.");
                }

                clips.Add(new DaggerfallSoundClip(ordinal, clip.NumericId, 0, DaggerfallSoundClipDisposition.Unsupported, name, null,
                    $"The archive carries a record with no sample bytes, so there is nothing to convert{(name.Length == 0 ? string.Empty : $"; the donor names clip {ordinal} '{name}'")}."));
                continue;
            }

            clips.Add(new DaggerfallSoundClip(ordinal, clip.NumericId, clip.PcmUnsigned8.Length,
                isAdmitted ? DaggerfallSoundClipDisposition.Admitted : DaggerfallSoundClipDisposition.ReadableNoConsumer,
                name,
                mediaId,
                isAdmitted
                    ? $"A published artifact carries clip {ordinal} as '{mediaId}'{(name.Length == 0 ? string.Empty : $"; the donor names it '{name}'")}."
                    : $"The clip decodes and no published artifact carries it{(name.Length == 0 ? "; the donor names it nothing" : $"; the donor names it '{name}', which no consumer here uses yet")}."));
        }

        DaggerfallSoundCatalog catalog = new(DaggerfallSoundCatalog.CurrentSchemaVersion, clips, [archive.Source]);
        catalog.Validate();
        return catalog;
    }

    /// <summary>
    /// Indexes the admitted closure by ordinal, refusing a closure that could not be published: an
    /// ordinal the archive does not carry, one clip admitted twice, or two clips sharing one media
    /// identity, which would leave a consumer unable to tell which bytes it asked for.
    /// </summary>
    private static Dictionary<int, string> Admitting(SoundArchive archive, IReadOnlyList<DaggerfallSoundAdmission> admitted)
    {
        Dictionary<int, string> result = [];
        foreach (DaggerfallSoundAdmission admission in admitted)
        {
            ArgumentNullException.ThrowIfNull(admission);
            admission.Validate();
            if (admission.Ordinal >= archive.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(admitted), $"Sound admission ordinal {admission.Ordinal} is outside the archive's 0..{archive.Count - 1}.");
            }

            if (!result.TryAdd(admission.Ordinal, admission.MediaId))
            {
                throw new ArgumentException($"The admitted sound closure names clip {admission.Ordinal} twice.", nameof(admitted));
            }
        }

        if (result.Values.Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            throw new ArgumentException("Two admitted clips cannot share one media identity.", nameof(admitted));
        }

        return result;
    }
}
