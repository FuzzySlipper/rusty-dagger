namespace Daggerfall.Import.Arena2;

/// <summary>One numeric-BSA sound clip, retaining both directory ordinal and source ID.</summary>
public sealed record Arena2PcmClip(string Source, int Ordinal, uint NumericId, ReadOnlyMemory<byte> PcmUnsigned8);

/// <summary>Read-only numeric-BSA unsigned 8-bit PCM sound archive.</summary>
public sealed class SoundArchive
{
    /// <summary>Classic source sample rate in hertz.</summary>
    public const uint SampleRate = 11_025;

    private readonly BsaArchive archive;

    private SoundArchive(BsaArchive archive)
    {
        this.archive = archive;
    }

    /// <summary>Source identity supplied while parsing the numeric BSA.</summary>
    public string Source => archive.Source;

    /// <summary>Directory-order sound record count.</summary>
    public int Count => archive.Records.Count;

    /// <summary>Gets a clip by directory ordinal; numeric BSA IDs remain separate provenance.</summary>
    public Arena2PcmClip GetClip(int ordinal)
    {
        if (!archive.TryGetByOrdinal(ordinal, out BsaRecord? record) || record is null || !record.NumericId.HasValue)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"Sound clip ordinal must be within 0..{Count - 1}.");
        }

        return new Arena2PcmClip(Source, record.Ordinal, record.NumericId.Value, archive.GetPayload(record));
    }

    /// <summary>Returns a standard 8-bit mono PCM WAV container for an offline clip export.</summary>
    public byte[] CreateWave(int ordinal)
    {
        Arena2PcmClip clip = GetClip(ordinal);
        int dataLength = clip.PcmUnsigned8.Length;
        int riffLength;
        try
        {
            riffLength = checked(dataLength + 36);
        }
        catch (OverflowException)
        {
            throw new Arena2FormatException(Source, 0, $"sound clip {ordinal} is too large for a WAV RIFF length");
        }

        byte[] wave = new byte[checked(dataLength + 44)];
        WriteAscii(wave, 0, "RIFF");
        WriteUInt32(wave, 4, (uint)riffLength);
        WriteAscii(wave, 8, "WAVEfmt ");
        WriteUInt32(wave, 16, 16);
        WriteUInt16(wave, 20, 1);
        WriteUInt16(wave, 22, 1);
        WriteUInt32(wave, 24, SampleRate);
        WriteUInt32(wave, 28, SampleRate);
        WriteUInt16(wave, 32, 1);
        WriteUInt16(wave, 34, 8);
        WriteAscii(wave, 36, "data");
        WriteUInt32(wave, 40, (uint)dataLength);
        clip.PcmUnsigned8.Span.CopyTo(wave.AsSpan(44));
        return wave;
    }

    /// <summary>Parses a numeric BSA sound archive from immutable caller-owned bytes.</summary>
    public static SoundArchive Parse(ReadOnlySpan<byte> bytes, string source)
    {
        BsaArchive archive = BsaArchive.Parse(bytes, source);
        if (archive.Records.Any(static record => !record.NumericId.HasValue || record.Name is not null))
        {
            throw new Arena2FormatException(source, 2, "sound archive requires the numeric BSA directory variant");
        }

        return new SoundArchive(archive);
    }

    private static void WriteAscii(Span<byte> target, int offset, string text)
    {
        System.Text.Encoding.ASCII.GetBytes(text, target.Slice(offset, text.Length));
    }

    private static void WriteUInt16(Span<byte> target, int offset, ushort value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(Span<byte> target, int offset, uint value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }
}
