namespace Daggerfall.Import.Normalization;

/// <summary>
/// A normalization or publication step needs an Arena2 source that is not loaded yet. The name is
/// the discovery data; the message stays human diagnostics and is never parsed for control flow.
/// </summary>
public sealed class MissingArena2SourceException : InvalidOperationException
{
    public MissingArena2SourceException(string sourceName, string message)
        : base(message) =>
        SourceName = sourceName;

    /// <summary>The admitted source leaf the step needs, such as a TEXTURE archive name.</summary>
    public string SourceName { get; }
}

/// <summary>
/// A dungeon media texture closure check found a mismatch. The lists are the discovery data;
/// the message stays human diagnostics and is never parsed for control flow.
/// </summary>
public sealed class MissingDungeonMediaTexturesException : InvalidOperationException
{
    public MissingDungeonMediaTexturesException(
        IReadOnlyList<string> missingTextureNames,
        IReadOnlyList<string> unneededTextureNames,
        string message)
        : base(message)
    {
        MissingTextureNames = missingTextureNames.ToArray();
        UnneededTextureNames = unneededTextureNames.ToArray();
    }

    /// <summary>Required archives with no supplied source, in deterministic order.</summary>
    public IReadOnlyList<string> MissingTextureNames { get; }

    /// <summary>Supplied archives no reference needs, in deterministic order.</summary>
    public IReadOnlyList<string> UnneededTextureNames { get; }
}
