using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The content the import tool derives from the player's Daggerfall files, as one composition reads it: the
/// site closures, the product-wide world and classic media, the score, the sky, the cinematics and the
/// per-block world, each by its content-root-relative path.
/// </summary>
/// <remarks>
/// <para>
/// The Host stages this content beside the product's own files: most of it in the eager snapshot, and the
/// roots in <see cref="BundleRoots"/> as Engine bundles whose bodies are read on first use. A reader names a
/// path, or opens a root and names paths below it, and never which of the two serves it.
/// </para>
/// <para>
/// The ruleset's own tracked content (the pack and bundle descriptors, the authored base payload and the
/// tuning) is not read through this: the composition resolves it. A renderer or audio owner that already
/// holds an artifact's path and digest resolves it through the Engine content service instead, which finds
/// it wherever it is staged.
/// </para>
/// </remarks>
internal sealed class DaggerfallData
{
    /// <summary>The generated roots the Host declares as Engine bundles, each with the bundle it is staged as.</summary>
    private static readonly IReadOnlyDictionary<string, string> BundleRoots = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DaggerfallMusicBundle.LogicalRoot] = DaggerfallMusicBundle.BundleId,
        [DaggerfallSkyMedia.LogicalRoot] = DaggerfallSkyMedia.BundleId,
        [DaggerfallCinematicPresentation.Root] = DaggerfallCinematicPresentation.BundleId,
        [DaggerfallWorldMedia.AudioRoot] = DaggerfallWorldMedia.AudioBundleId,
        [DaggerfallWorldBlocks.Root] = DaggerfallWorldBlocks.BundleId,
    };

    private readonly ProductContent _content;

    internal DaggerfallData(ProductContent content) => _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>Finds an optional file by its exact content-root-relative path.</summary>
    internal bool TryReadFile(string path, out ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        bool found = _content.TryReadFile(path, out ProductContentFile file);
        bytes = found ? file.Bytes : default;
        return found;
    }

    /// <summary>Reads a required file, or throws <see cref="FileNotFoundException"/> naming its path.</summary>
    internal ReadOnlyMemory<byte> ReadBytes(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _content.ReadBytes(path);
    }

    /// <summary>
    /// Opens one generated root for a use that reads or references several files below it. The caller disposes
    /// it once that use has what it needs; references opened from it outlive it.
    /// </summary>
    /// <exception cref="ArgumentException">The root is not one this content serves as a whole.</exception>
    internal DaggerfallDataRoot Open(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!BundleRoots.TryGetValue(root, out string? bundle))
            throw new ArgumentException($"'{root}' is not a generated root the Daggerfall content serves as a whole.", nameof(root));
        return new DaggerfallDataRoot(root, bundle, _content.OpenBundle(bundle));
    }
}

/// <summary>One open generated root: its files by root-relative path, until it is disposed.</summary>
internal sealed class DaggerfallDataRoot : IDisposable
{
    private readonly ProductContentBundle _bundle;

    internal DaggerfallDataRoot(string root, string source, ProductContentBundle bundle)
    {
        Root = root;
        Source = source;
        _bundle = bundle;
    }

    /// <summary>The root's content-root-relative path.</summary>
    internal string Root { get; }

    /// <summary>Where the root is staged, for diagnostics that name it.</summary>
    internal string Source { get; }

    /// <summary>Every file below the root, by root-relative path, with its digest and length.</summary>
    internal ReadOnlyMemory<ContentReferenceInfo> Entries => _bundle.Entries;

    /// <summary>Reads a required file below the root, or throws <see cref="FileNotFoundException"/> naming it.</summary>
    internal ReadOnlyMemory<byte> ReadBytes(string path) => _bundle.ReadBytes(path);

    /// <summary>Retains one file below the root for an Engine service; the reference outlives this root.</summary>
    internal ContentReference OpenReference(string path) => _bundle.OpenReference(path);

    public void Dispose() => _bundle.Dispose();
}
