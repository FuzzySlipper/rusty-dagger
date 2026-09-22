using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>What kind of cinematic a file is.</summary>
public enum DaggerfallCinematicKind
{
    Vid,
    Flc,
}

/// <summary>How a cinematic's story hook is accounted for.</summary>
public enum DaggerfallCinematicBinding
{
    /// <summary>The donor names the caller: the new-game opening or a Daedric summons.</summary>
    Bound,
    /// <summary>No donor caller names it; quests may still play it by name.</summary>
    Unresolved,
}

/// <summary>One cinematic source identity: its file, digest and story hook.</summary>
/// <param name="FileName">The source file name.</param>
/// <param name="Kind">Whether it is a VID or FLC cinematic.</param>
/// <param name="ByteLength">The file's bytes.</param>
/// <param name="Digest">The SHA-256 digest of the file.</param>
/// <param name="Binding">Whether a donor caller names it.</param>
/// <param name="Caller">The donor caller, when one names it.</param>
/// <param name="FactionId">The donor faction id, when the caller is a Daedric summons.</param>
/// <param name="Quest">The quest name, when the caller is a Daedric summons.</param>
public sealed record DaggerfallCinematicRecord(
    string FileName,
    DaggerfallCinematicKind Kind,
    long ByteLength,
    string Digest,
    DaggerfallCinematicBinding Binding,
    string Caller,
    int? FactionId,
    string Quest)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalPath(FileName, nameof(FileName));
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Binding))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A cinematic names a kind or binding the contract does not declare.");
        }

        if (ByteLength <= 0 || string.IsNullOrWhiteSpace(Digest))
        {
            throw new ArgumentException("A cinematic states no bytes or digest.", nameof(Digest));
        }

        if (Binding == DaggerfallCinematicBinding.Bound && string.IsNullOrWhiteSpace(Caller))
        {
            throw new ArgumentException($"Cinematic '{FileName}' is bound and names no caller.", nameof(Caller));
        }

        if (Binding == DaggerfallCinematicBinding.Unresolved && (!string.IsNullOrWhiteSpace(Caller) || FactionId.HasValue || !string.IsNullOrWhiteSpace(Quest)))
        {
            throw new ArgumentException($"Cinematic '{FileName}' is unresolved and still claims a hook.", nameof(Caller));
        }
    }
}

/// <summary>The normalized cinematic provenance pack: raw-media identities, never media bytes.</summary>
/// <param name="VidSource">The provenance the VID files were read from.</param>
/// <param name="FlcSource">The provenance the FLC files were read from.</param>
/// <param name="Cinematics">The cinematics in file order.</param>
public sealed record DaggerfallCinematicPack(
    DaggerfallTextSource VidSource,
    DaggerfallTextSource FlcSource,
    IReadOnlyList<DaggerfallCinematicRecord> Cinematics)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(VidSource);
        ArgumentNullException.ThrowIfNull(FlcSource);
        VidSource.Validate();
        FlcSource.Validate();
        NormalizedImportDocument.ValidateUnique(Cinematics, cinematic => cinematic.FileName, "cinematics");
        foreach (DaggerfallCinematicRecord cinematic in Cinematics)
        {
            cinematic.Validate();
        }

        if (Cinematics.Count(record => record.Kind == DaggerfallCinematicKind.Vid) != 17)
        {
            throw new InvalidOperationException("The cinematic pack carries no seventeen VID identities.");
        }

        if (Cinematics.Count(record => record.Kind == DaggerfallCinematicKind.Flc) != 16)
        {
            throw new InvalidOperationException("The cinematic pack carries no sixteen FLC identities.");
        }

        if (!Cinematics.Any(record => string.Equals(record.FileName, "DAG2.VID", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The cinematic pack carries no distinct DAG2 record.");
        }
    }
}

/// <summary>
/// Builds the cinematic provenance pack from supplied video files. Digests identify the bytes;
/// story hooks follow the donor's callers: the new-game opening names three VIDs, the Daedric
/// summons table names sixteen FLCs, and everything else stays unresolved rather than assuming
/// an ending mapping. No media bytes are published and no playback is ported.
/// </summary>
public static class DaggerfallCinematicPackBuilder
{
    /// <summary>The inventory family VID cinematic sources are documented under.</summary>
    public const string VidFamily = "CNT-025";

    /// <summary>The inventory family FLC cinematic sources are documented under.</summary>
    public const string FlcFamily = "CNT-026";

    /// <summary>The new-game opening: the three VIDs the donor plays in order.</summary>
    public static readonly IReadOnlyList<string> OpeningSequence = ["ANIM0000.VID", "ANIM0011.VID", "DAG2.VID"];

    /// <summary>One Daedric summons hook: its file, donor faction id, quest and level.</summary>
    public sealed record DaedricHook(string FileName, int FactionId, string Quest, int Level);

    /// <summary>The sixteen FLC hooks from the donor's Daedric summons table.</summary>
    public static readonly IReadOnlyList<DaedricHook> DaedricHooks =
    [
        new("HIRCINE.FLC", 4, "X0C00Y00", 155),
        new("CLAVICUS.FLC", 1, "V0C00Y00", 1),
        new("MEHRUNES.FLC", 2, "Y0C00Y00", 320),
        new("MOLAGBAL.FLC", 3, "20C00Y00", 350),
        new("SANGUINE.FLC", 5, "70C00Y00", 46),
        new("PERYITE.FLC", 6, "50C00Y00", 99),
        new("MALACATH.FLC", 7, "80C0XY00", 278),
        new("HERMAEUS.FLC", 8, "W0C00Y00", 65),
        new("SHEOGRTH.FLC", 9, "60C00Y00", 32),
        new("BOETHIAH.FLC", 10, "U0C00Y00", 302),
        new("NAMIRA.FLC", 11, "30C00Y00", 129),
        new("MERIDIA.FLC", 12, "10C00Y00", 13),
        new("VAERNIMA.FLC", 13, "90C00Y00", 190),
        new("NOCTURNA.FLC", 14, "40C00Y00", 248),
        new("MEPHALA.FLC", 15, "Z0C00Y00", 283),
        new("AZURA.FLC", 16, "T0C00Y00", 81),
    ];

    /// <summary>Builds the cinematic provenance pack.</summary>
    public static DaggerfallCinematicPack Build(
        IReadOnlyList<(string FileName, DaggerfallCinematicKind Kind, long ByteLength, string Digest)> files,
        string label,
        IReadOnlyList<SourceInventoryRow> inventory)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(inventory);
        SourceInventoryRow vid = SourceInventoryRow.RequireFamily(inventory, VidFamily);
        SourceInventoryRow flc = SourceInventoryRow.RequireFamily(inventory, FlcFamily);

        Dictionary<string, DaedricHook> hooks = DaedricHooks.ToDictionary(hook => hook.FileName, StringComparer.OrdinalIgnoreCase);
        List<DaggerfallCinematicRecord> cinematics = [];
        foreach ((string fileName, DaggerfallCinematicKind kind, long byteLength, string digest) in files.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase))
        {
            if (kind == DaggerfallCinematicKind.Vid && OpeningSequence.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                cinematics.Add(new DaggerfallCinematicRecord(fileName, kind, byteLength, digest, DaggerfallCinematicBinding.Bound, "new-game opening", null, string.Empty));
            }
            else if (kind == DaggerfallCinematicKind.Flc && hooks.TryGetValue(fileName, out DaedricHook? hook))
            {
                cinematics.Add(new DaggerfallCinematicRecord(fileName, kind, byteLength, digest, DaggerfallCinematicBinding.Bound, "Daedric summons", hook.FactionId, hook.Quest));
            }
            else
            {
                cinematics.Add(new DaggerfallCinematicRecord(fileName, kind, byteLength, digest, DaggerfallCinematicBinding.Unresolved, string.Empty, null, string.Empty));
            }
        }

        DaggerfallCinematicPack pack = new(
            new DaggerfallTextSource(DaggerfallTextKind.Resource, vid.Id, label, "en", files.Where(file => file.Kind == DaggerfallCinematicKind.Vid).Sum(file => file.ByteLength), 0, cinematics.Count(record => record.Kind == DaggerfallCinematicKind.Vid)),
            new DaggerfallTextSource(DaggerfallTextKind.Resource, flc.Id, label, "en", files.Where(file => file.Kind == DaggerfallCinematicKind.Flc).Sum(file => file.ByteLength), 0, cinematics.Count(record => record.Kind == DaggerfallCinematicKind.Flc)),
            cinematics);
        pack.Validate();
        return pack;
    }
}
