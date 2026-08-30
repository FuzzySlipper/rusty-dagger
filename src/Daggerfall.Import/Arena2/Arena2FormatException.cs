namespace Daggerfall.Import.Arena2;

/// <summary>Identifies malformed or unsupported Arena2 source data.</summary>
public sealed class Arena2FormatException : IOException
{
    /// <summary>Creates a format error at a source byte position.</summary>
    public Arena2FormatException(string source, int offset, string message)
        : base($"{source} at byte {offset}: {message}")
    {
        SourceName = source;
        Offset = offset;
    }

    /// <summary>Logical source identity supplied by the caller.</summary>
    public string SourceName { get; }

    /// <summary>Zero-based byte offset where parsing failed.</summary>
    public int Offset { get; }
}
