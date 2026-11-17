namespace Daggerfall.Import.Normalized;

/// <summary>
/// Which source family a text key belongs to. The kind is part of the key rather than a property of
/// the pack because one key space has to address text a later source supplies: a book, a biography,
/// a rumor and a generated name are each addressed by the identity their own source gives them.
/// </summary>
public enum DaggerfallTextKind
{
    /// <summary>A record of the classic text resource, addressed by the key its directory carries.</summary>
    Resource,

    /// <summary>A supplied book, addressed by the file identity the classic message field names.</summary>
    Book,

    /// <summary>A supplied biography, addressed by its class and biography indices.</summary>
    Biography,

    /// <summary>A rumor record, addressed by its own position and region.</summary>
    Rumor,

    /// <summary>A generated name, addressed by the bank and index its table states.</summary>
    Name,

    /// <summary>A localized managed donor string, addressed by its Internal_Strings.csv key.</summary>
    Internal,
}


/// <summary>
/// One supplied text source: the language its bytes are written in, the bytes it carries, and what
/// its directory declared.
/// </summary>
/// <param name="Kind">The source family whose keys this source's records belong to.</param>
/// <param name="Path">The logical source path, which is also the identity its records name.</param>
/// <param name="Language">The language tag the source's text is written in.</param>
/// <param name="ByteLength">The source's byte length.</param>
/// <param name="DeclaredLength">The byte length the source's own directory declares, where it declares one.</param>
/// <param name="Records">How many records the source declares.</param>
public sealed record DaggerfallTextSource(
    DaggerfallTextKind Kind,
    string Path,
    string Language,
    long ByteLength,
    int DeclaredLength,
    int Records)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A published text source names a source family the contract does not declare.");
        }

        NormalizedImportDocument.RequireLogicalPath(Path, nameof(Path));

        // The language is the caller's assertion about bytes that declare none, so a failure names the
        // source and the value it was given rather than a parameter name the caller never wrote.
        if (string.IsNullOrWhiteSpace(Language) || Language.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException($"Text source '{Path}' must state a language tag such as 'en'; it states '{Language}'.");
        }
        if (ByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, $"Text source '{Path}' must retain a positive byte length.");
        }

        if (DeclaredLength < 0 || Records < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Records), Records, $"Text source '{Path}' cannot declare a negative length or record count.");
        }
    }
}

