namespace Daggerfall.Import.Arena2;

/// <summary>What the envelope decode could establish about one quest binary.</summary>
public enum QuestBinaryEnvelopeDisposition
{
    /// <summary>The envelope's observed invariants hold.</summary>
    WellFormed,

    /// <summary>The file is shorter than the envelope header.</summary>
    Truncated,

    /// <summary>The file is long enough but its header is not the observed shape.</summary>
    Malformed,
}

/// <summary>
/// The envelope of one classic quest-logic binary: the fixed header block, the terminal
/// marker where the file carries one, and the length. The payload is deliberately not
/// interpreted: the classic binary format has no reader in the donor and its record
/// semantics are not established, so this records the boundaries that hold and claims
/// nothing about what runs inside them.
/// </summary>
/// <remarks>
/// The observed envelope, verified across all 306 supplied binaries: the first fifteen
/// bytes are zero and byte fifteen is a variant value; 272 of the 306 end with the
/// <c>0xFFFF</c> terminal marker in their last six bytes. The marker is reported, not
/// required: the other 34 files end without it and no evidence establishes that they are
/// damaged, so their absence is an observed variant rather than a defect.
/// </remarks>
public sealed record QuestBinaryEnvelope(
    string Path,
    int Length,
    byte HeaderVariant,
    bool HasTerminalMarker,
    QuestBinaryEnvelopeDisposition Disposition,
    string Note)
{
    /// <summary>The envelope header block's size in bytes.</summary>
    public const int HeaderLength = 16;

    /// <summary>The number of leading zero bytes the supplied corpus shares.</summary>
    public const int DefinedHeaderBytes = 15;

    /// <summary>The terminal marker value the classic envelope uses where it carries one.</summary>
    public const ushort TerminalMarker = 0xffff;

    /// <summary>The size of the terminal marker record in bytes.</summary>
    public const int TerminalMarkerLength = 6;

    /// <summary>Decodes one quest binary's envelope.</summary>
    public static QuestBinaryEnvelope Decode(ReadOnlySpan<byte> bytes, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (bytes.Length < HeaderLength)
        {
            return new QuestBinaryEnvelope(path, bytes.Length, HeaderVariant: 0, HasTerminalMarker: false, QuestBinaryEnvelopeDisposition.Truncated, $"The file is {bytes.Length} bytes where the envelope header is {HeaderLength}.");
        }

        for (int index = 0; index < DefinedHeaderBytes; index++)
        {
            if (bytes[index] != 0)
            {
                return new QuestBinaryEnvelope(path, bytes.Length, bytes[15], HasTerminalMarker: false, QuestBinaryEnvelopeDisposition.Malformed, $"byte {index} of the envelope header is 0x{bytes[index]:X2} where the supplied corpus is zero.");
            }
        }

        CheckedLittleEndianReader reader = new(bytes, path);
        reader.Seek(bytes.Length - TerminalMarkerLength);
        bool marker = reader.ReadUInt16() == TerminalMarker;
        return new QuestBinaryEnvelope(
            path,
            bytes.Length,
            bytes[DefinedHeaderBytes],
            marker,
            QuestBinaryEnvelopeDisposition.WellFormed,
            marker
                ? $"Header variant {bytes[DefinedHeaderBytes]}, terminal marker present."
                : $"Header variant {bytes[DefinedHeaderBytes]}, no terminal marker in the last {TerminalMarkerLength} bytes.");
    }
}
