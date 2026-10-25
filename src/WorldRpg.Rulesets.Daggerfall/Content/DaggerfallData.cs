using Rusty.Engine;
using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The content the import tool derives from the player's Daggerfall files, as one composition reads it: the
/// site closures, the product-wide world and classic media, the sky, the cinematics and the per-block world,
/// each by its content-root-relative path.
/// </summary>
/// <remarks>
/// <para>
/// The derived content is the data container (<see cref="DataContainerName"/>), which the host opens beside the
/// product for its life; its paths are the content-root-relative paths the publications name. The product's
/// own tracked content (the authored UI art among it) is read first, so a reader names a path and never which of
/// the two holds it. A root such as the world blocks is a path prefix inside the container, opened as a whole by
/// a use that lists or references several files below it.
/// </para>
/// <para>
/// The ruleset's own tracked content (the pack and bundle descriptors, the authored base payload and the
/// tuning) is not read through this: the composition resolves it. A renderer or audio owner that already holds
/// an artifact's path and digest resolves it through the Engine content service instead, which finds it in the
/// open container as in the product's content.
/// </para>
/// </remarks>
internal sealed class DaggerfallData
{
    /// <summary>The data container's file name, installed beside the product.</summary>
    internal const string DataContainerName = "daggerfall-data.rpak";

    private readonly ProductContent _content;
    private readonly ProductContentBundle? _data;
    private readonly Dictionary<string, ContentReferenceInfo[]> _roots = new(StringComparer.Ordinal);

    /// <summary>Reads the product's content and, when it was opened, the data container.</summary>
    internal DaggerfallData(ProductContent content, ProductContentBundle? data)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _data = data;
    }

    /// <summary>The derived content a composition resolved over: its product content and the opened data container.</summary>
    internal static DaggerfallData For(ResolvedGameComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return new(composition.Content, composition.Installed.FirstOrDefault(installed => installed.Name == DataContainerName)?.Container);
    }

    /// <summary>Where the derived content is read from, for diagnostics that name it.</summary>
    internal string Source => _data?.Id ?? $"the product content ({DataContainerName} is not open)";

    /// <summary>Finds an optional file by its exact content-root-relative path.</summary>
    internal bool TryReadFile(string path, out ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_content.TryReadFile(path, out ProductContentFile file) || (_data is not null && _data.TryReadFile(path, out file)))
        {
            bytes = file.Bytes;
            return true;
        }

        bytes = default;
        return false;
    }

    /// <summary>Reads a required file, or throws <see cref="FileNotFoundException"/> naming its path and where it was looked for.</summary>
    internal ReadOnlyMemory<byte> ReadBytes(string path) => TryReadFile(path, out ReadOnlyMemory<byte> bytes)
        ? bytes
        : throw new FileNotFoundException($"'{path}' is neither in the product content nor in {Source}.", path);

    /// <summary>
    /// Opens one generated root for a use that reads or references several files below it. The caller disposes
    /// it once that use has what it needs; references opened from it outlive it.
    /// </summary>
    /// <exception cref="FileNotFoundException">The data container is not open, or holds nothing below the root.</exception>
    internal DaggerfallDataRoot Open(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (_data is null) throw new FileNotFoundException($"'{root}' is read from {DataContainerName}, which is not open.", root);
        ContentReferenceInfo[] entries;
        lock (_roots)
        {
            if (!_roots.TryGetValue(root, out entries!))
            {
                string prefix = root + "/";
                entries = [.. _data.Entries.ToArray()
                    .Where(entry => entry.Path.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(entry => entry with { Path = entry.Path[prefix.Length..] })];
                _roots.Add(root, entries);
            }
        }

        return entries.Length == 0
            ? throw new FileNotFoundException($"{_data.Id} holds nothing below '{root}'; import the Daggerfall data again.", root)
            : new DaggerfallDataRoot(root, _data, entries);
    }
}

/// <summary>One generated root inside the data container: its files by root-relative path.</summary>
internal sealed class DaggerfallDataRoot : IDisposable
{
    private readonly ProductContentBundle _container;
    private readonly ContentReferenceInfo[] _entries;

    internal DaggerfallDataRoot(string root, ProductContentBundle container, ContentReferenceInfo[] entries)
    {
        Root = root;
        _container = container;
        _entries = entries;
    }

    /// <summary>The root's content-root-relative path.</summary>
    internal string Root { get; }

    /// <summary>Where the root is read from, for diagnostics that name it.</summary>
    internal string Source => $"{_container.Id}:{Root}";

    /// <summary>Every file below the root, by root-relative path, with its digest and length.</summary>
    internal ReadOnlyMemory<ContentReferenceInfo> Entries => _entries;

    /// <summary>Reads a required file below the root, or throws <see cref="FileNotFoundException"/> naming it.</summary>
    internal ReadOnlyMemory<byte> ReadBytes(string path) => _container.ReadBytes($"{Root}/{path}");

    /// <summary>Retains one file below the root for an Engine service; the reference outlives this root.</summary>
    internal ContentReference OpenReference(string path) => _container.OpenReference($"{Root}/{path}");

    /// <summary>Ends this use of the root; the container stays open for the product's life.</summary>
    public void Dispose()
    {
    }
}
