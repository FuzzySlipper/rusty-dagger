using Daggerfall.Import.Arena2;

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
/// One clip of the numeric sound archive: where it sits, what it is, and how it stands.
/// </summary>
/// <param name="Ordinal">Its position in the archive's own directory order, which is its stable identity.</param>
/// <param name="NumericId">The numeric record identity the archive carries.</param>
/// <param name="ByteLength">How many sample bytes it holds.</param>
/// <param name="Disposition">How the clip stands.</param>
/// <param name="UsageCandidate">What the donor names it, when the donor names it at all.</param>
/// <param name="Reason">Why the disposition holds, naming the conversion for an unsupported clip.</param>
public sealed record DaggerfallSoundClip(
    int Ordinal,
    uint NumericId,
    int ByteLength,
    DaggerfallSoundClipDisposition Disposition,
    string UsageCandidate,
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
        }
    }
}

/// <summary>
/// Builds the published catalog from the numeric sound archive.
/// </summary>
/// <remarks>
/// The archive carries 459 numeric records and no names; the donor's own clip enum is where a name
/// comes from, and only the clips this product has a use for are named here. Everything else is
/// published as readable with no consumer rather than dropped, because a clip that is absent from the
/// catalog cannot be counted and one that says it has no consumer can.
/// </remarks>
public static class DaggerfallSoundCatalogBuilder
{
    /// <summary>The donor-named clips this product already publishes, by archive ordinal.</summary>
    private static readonly Dictionary<int, string> Admitted = new()
    {
        [106] = "audio.melee.dagger.swing",
        [108] = "audio.melee.hit.1",
        [109] = "audio.melee.hit.2",
        [110] = "audio.melee.hit.3",
        [111] = "audio.melee.hit.4",
        [112] = "audio.melee.hit.5",
    };

    public static DaggerfallSoundCatalog Build(SoundArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        List<DaggerfallSoundClip> clips = new(archive.Count);
        for (int ordinal = 0; ordinal < archive.Count; ordinal++)
        {
            Arena2PcmClip clip = archive.GetClip(ordinal);
            bool admitted = Admitted.TryGetValue(ordinal, out string? mediaId);
            string name = DaggerfallSoundNames.For(ordinal);
            if (clip.PcmUnsigned8.Length == 0)
            {
                clips.Add(new DaggerfallSoundClip(ordinal, clip.NumericId, 0, DaggerfallSoundClipDisposition.Unsupported, name,
                    $"The archive carries a record with no sample bytes, so there is nothing to convert{(name.Length == 0 ? string.Empty : $"; the donor names clip {ordinal} '{name}'")}."));
                continue;
            }

            clips.Add(new DaggerfallSoundClip(ordinal, clip.NumericId, clip.PcmUnsigned8.Length,
                admitted ? DaggerfallSoundClipDisposition.Admitted : DaggerfallSoundClipDisposition.ReadableNoConsumer,
                name,
                admitted
                    ? $"A published artifact carries clip {ordinal} as '{mediaId}'{(name.Length == 0 ? string.Empty : $"; the donor names it '{name}'")}."
                    : $"The clip decodes and no published artifact carries it{(name.Length == 0 ? "; the donor names it nothing" : $"; the donor names it '{name}', which no consumer here uses yet")}."));
        }

        DaggerfallSoundCatalog catalog = new(DaggerfallSoundCatalog.CurrentSchemaVersion, clips, [archive.Source]);
        catalog.Validate();
        return catalog;
    }
}
