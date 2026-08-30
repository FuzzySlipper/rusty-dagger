namespace Daggerfall.Import.Arena2;

/// <summary>Checked, little-endian cursor over immutable source bytes.</summary>
public ref struct CheckedLittleEndianReader
{
    private readonly ReadOnlySpan<byte> bytes;
    private readonly string source;
    private int position;

    /// <summary>Creates a checked cursor at the beginning of <paramref name="bytes"/>.</summary>
    public CheckedLittleEndianReader(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        this.bytes = bytes;
        this.source = source;
        position = 0;
    }

    /// <summary>Current zero-based byte position.</summary>
    public readonly int Position => position;

    /// <summary>Total source byte count.</summary>
    public readonly int Length => bytes.Length;

    /// <summary>Moves to an absolute source position.</summary>
    public void Seek(int offset)
    {
        if ((uint)offset > (uint)bytes.Length)
        {
            throw Error($"seek to {offset} is outside source length {bytes.Length}");
        }

        position = offset;
    }

    /// <summary>Reads one unsigned byte.</summary>
    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return bytes[position++];
    }

    /// <summary>Reads one signed 16-bit integer.</summary>
    public short ReadInt16()
    {
        return unchecked((short)ReadUInt16());
    }

    /// <summary>Reads one unsigned 16-bit integer.</summary>
    public ushort ReadUInt16()
    {
        EnsureAvailable(sizeof(ushort));
        ushort value = (ushort)(bytes[position] | (bytes[position + 1] << 8));
        position += sizeof(ushort);
        return value;
    }

    /// <summary>Reads one signed 32-bit integer.</summary>
    public int ReadInt32()
    {
        return unchecked((int)ReadUInt32());
    }

    /// <summary>Reads one unsigned 32-bit integer.</summary>
    public uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        uint value = (uint)(bytes[position]
            | (bytes[position + 1] << 8)
            | (bytes[position + 2] << 16)
            | (bytes[position + 3] << 24));
        position += sizeof(uint);
        return value;
    }

    /// <summary>Reads an exact source slice.</summary>
    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        if (length < 0)
        {
            throw Error($"negative byte count {length}");
        }

        EnsureAvailable(length);
        ReadOnlySpan<byte> value = bytes.Slice(position, length);
        position += length;
        return value;
    }

    /// <summary>Reads a NUL-terminated ASCII field with a fixed maximum width.</summary>
    public string ReadNullTerminatedAscii(int maximumLength)
    {
        ReadOnlySpan<byte> field = ReadBytes(maximumLength);
        int terminator = field.IndexOf((byte)0);
        ReadOnlySpan<byte> text = terminator >= 0 ? field[..terminator] : field;
        foreach (byte value in text)
        {
            if (value > 0x7F)
            {
                throw Error($"non-ASCII byte {value} in text field");
            }
        }

        return System.Text.Encoding.ASCII.GetString(text);
    }

    /// <summary>Throws a typed source error at the current cursor position.</summary>
    public readonly Arena2FormatException Error(string message)
    {
        return new Arena2FormatException(source, position, message);
    }

    private void EnsureAvailable(int count)
    {
        if (count > bytes.Length - position)
        {
            throw Error($"requires {count} byte(s), but only {bytes.Length - position} remain");
        }
    }
}
