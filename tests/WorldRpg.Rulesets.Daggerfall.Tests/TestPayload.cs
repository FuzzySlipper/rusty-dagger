using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The one parsed copy of the base definitions that facts share. They are read from two payloads: the
/// authored daggerfall.base payload and the imported payload scripts/regenerate-content.sh generates.
/// Reading them parses a 75 MiB document, so parsing it inside every fact — as the suite used to,
/// hundreds of times per run — cost far more than the assertions did. The parsed value is immutable once
/// read, which is what makes sharing it across parallel test classes safe; a fact that deliberately reads
/// tampered bytes still reads its own.
/// </summary>
internal static class TestPayload
{
    private static readonly Lazy<DaggerfallDefinitions> Shared = new(() => DaggerfallBaseContent.Read(CombinedBytes));
    private static readonly Lazy<byte[]> Combined = new(() => DaggerfallBaseContent.Combine(
        File.ReadAllBytes(PayloadPath("daggerfall.base.json")), File.ReadAllBytes(PayloadPath("daggerfall.imported.json"))));

    /// <summary>The parsed base definitions.</summary>
    internal static DaggerfallDefinitions Definitions => Shared.Value;

    /// <summary>
    /// The authored and imported sections joined as the ruleset joins them, each section's bytes as its
    /// file carries them, for the facts that tamper with a section or hand the bytes to a reader.
    /// </summary>
    internal static byte[] CombinedBytes => (byte[])Combined.Value.Clone();

    /// <summary>The joined sections as text.</summary>
    internal static string CombinedText => System.Text.Encoding.UTF8.GetString(Combined.Value);

    private static string PayloadPath(string file) => System.IO.Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "payloads", file);
}
