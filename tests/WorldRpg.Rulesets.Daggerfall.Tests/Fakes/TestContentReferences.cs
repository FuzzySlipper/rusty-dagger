using System.Runtime.CompilerServices;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The content path each reference a content fake issued names, so a graphics or audio fake handed the
/// reference can record what was opened, as the Engine resolves it from the reference.
/// </summary>
internal static class TestContentReferences
{
    private static readonly ConditionalWeakTable<ContentReference, string> Paths = [];

    internal static ContentReference Named(ContentReference reference, string path)
    {
        Paths.AddOrUpdate(reference, path);
        return reference;
    }

    /// <summary>The content path a fake-issued reference names, or null for one no content fake issued.</summary>
    internal static string? PathOf(ContentReference reference) => Paths.TryGetValue(reference, out string? path) ? path : null;
}
