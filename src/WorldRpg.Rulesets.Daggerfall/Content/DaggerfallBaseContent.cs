using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Validates the ruleset-wide selected pack before scenario-specific content is interpreted.</summary>
internal static class DaggerfallBaseContent
{
    internal static void Read(ReadOnlyMemory<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("ruleset", out JsonElement ruleset)
            || ruleset.ValueKind != JsonValueKind.String
            || ruleset.GetString() != DaggerfallRuleset.Identity.Value)
            throw new InvalidOperationException("Daggerfall base pack does not identify the Daggerfall ruleset.");
    }
}
