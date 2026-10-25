using Rusty.Engine;
using Rusty.Engine.Testing;

namespace WorldRpg.Tests.Support;

/// <summary>
/// The containers a real import packed (<c>local/daggerfall-data.rpak</c>, <c>local/music.rpak</c>), read through
/// one shared Engine test host so a suite reads exactly what the product opens.
/// </summary>
/// <remarks>
/// The host is not thread-safe, so every Engine call runs inside one lock, one call at a time; each container is
/// opened once and stays open for the test process. Nothing here reads the container format itself.
/// </remarks>
internal static class TestDataContainers
{
    /// <summary>The data container's file name.</summary>
    public const string Data = "daggerfall-data.rpak";

    /// <summary>The music container's file name.</summary>
    public const string Music = "music.rpak";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Opened> OpenedContainers = new(StringComparer.Ordinal);
    private static EngineTestHost? host;

    /// <summary>A container's path in the repository's <c>local/</c> directory.</summary>
    public static string PathOf(string container) => Path.Combine(TestData.RepositoryRoot, "local", container);

    /// <summary>Whether a real import packed this container.</summary>
    public static bool Exists(string container) => File.Exists(PathOf(container));

    /// <summary>Every file the container carries, by container-relative path.</summary>
    public static IReadOnlyDictionary<string, ContentReferenceInfo> Entries(string container) => Open(container).Entries;

    /// <summary>Whether the container carries a file at this path.</summary>
    public static bool Contains(string container, string path) => Exists(container) && Open(container).Entries.ContainsKey(path);

    /// <summary>One file's bytes, read afresh.</summary>
    public static byte[] Read(string container, string path)
    {
        Opened opened = Open(container);
        if (!opened.Entries.ContainsKey(path)) throw new FileNotFoundException($"{container} carries no '{path}'.", path);
        lock (Gate) return Host().Call(_ => opened.Bundle.ReadBytes(path).ToArray());
    }

    private static Opened Open(string container)
    {
        lock (Gate)
        {
            if (OpenedContainers.TryGetValue(container, out Opened? opened)) return opened;
            string path = PathOf(container);
            if (!File.Exists(path))
                throw new FileNotFoundException($"No {container} at {path}; run `{TestData.RegenerationCommand}` with local/arena2 supplied.", path);
            ProductContentBundle bundle = Host().Call(engine => ProductContentBundle.OpenContainer(engine.Content, path));
            opened = new(bundle, bundle.Entries.ToArray().ToDictionary(entry => entry.Path, StringComparer.Ordinal));
            OpenedContainers.Add(container, opened);
            return opened;
        }
    }

    private static EngineTestHost Host() => host ??= EngineTestHost.Create(new EngineTestHostOptions());

    private sealed record Opened(ProductContentBundle Bundle, IReadOnlyDictionary<string, ContentReferenceInfo> Entries);
}
