using System.Collections.ObjectModel;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Runtime mode for non-blocking NPC hostility signaling (Den #11164):
/// supports evaluating TTS audio barks, near-actor comic speech bubbles, or both together without pausing play.
/// </summary>
internal enum DaggerfallHostilitySignalingMode
{
    Both = 0,
    SpeechBubbles = 1,
    AudioBarks = 2,
    Off = 3,
}

/// <summary>One authored TTS voice-bark clip carried in <c>daggerfall.people/audio/hostility/hostility-barks.json</c>.</summary>
internal sealed record DaggerfallHostilityBarkClip(
    string Id,
    string Kind,
    string Gender,
    string Voice,
    string Text,
    string Path,
    ContentSha256 Sha256,
    float DurationSeconds);

/// <summary>
/// Catalog of generated male/female TTS voice barks announcing why a vendetta kin member or hired
/// tough/bounty hunter/assassin is attacking without pausing combat (Den #11164).
/// </summary>
internal sealed class DaggerfallHostilityBarkCatalog
{
    internal const string ManifestRelativePath = "audio/hostility/hostility-barks.json";
    internal static DaggerfallHostilityBarkCatalog Empty { get; } = new([]);

    private readonly IReadOnlyList<DaggerfallHostilityBarkClip> _clips;
    private readonly IReadOnlyDictionary<(string Kind, string Gender), IReadOnlyList<DaggerfallHostilityBarkClip>> _byKindAndGender;

    internal DaggerfallHostilityBarkCatalog(IEnumerable<DaggerfallHostilityBarkClip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        _clips = Array.AsReadOnly(clips.ToArray());
        Dictionary<(string, string), List<DaggerfallHostilityBarkClip>> grouped = [];
        foreach (DaggerfallHostilityBarkClip clip in _clips)
        {
            (string, string) key = (NormalizeKey(clip.Kind), NormalizeGender(clip.Gender));
            if (!grouped.TryGetValue(key, out List<DaggerfallHostilityBarkClip>? list))
            {
                list = [];
                grouped[key] = list;
            }
            list.Add(clip);
        }
        _byKindAndGender = new ReadOnlyDictionary<(string, string), IReadOnlyList<DaggerfallHostilityBarkClip>>(
            grouped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DaggerfallHostilityBarkClip>)Array.AsReadOnly(pair.Value.ToArray())));
    }

    internal IReadOnlyList<DaggerfallHostilityBarkClip> Clips => _clips;

    /// <summary>
    /// Deterministically picks one voice-bark clip matching <paramref name="kindSlug"/> and <paramref name="gender"/>
    /// for <paramref name="actorId"/>, falling back to any gender for that kind if needed.
    /// </summary>
    internal DaggerfallHostilityBarkClip? Select(string kindSlug, string? gender, long actorId)
    {
        if (_clips.Count == 0)
            return null;
        string normalizedKind = NormalizeKey(kindSlug);
        string normalizedGender = NormalizeGender(gender);
        if (_byKindAndGender.TryGetValue((normalizedKind, normalizedGender), out IReadOnlyList<DaggerfallHostilityBarkClip>? exact)
            && exact.Count > 0)
        {
            return PickDeterministic(exact, actorId);
        }

        string alternateGender = normalizedGender == "female" ? "male" : "female";
        if (_byKindAndGender.TryGetValue((normalizedKind, alternateGender), out IReadOnlyList<DaggerfallHostilityBarkClip>? alt)
            && alt.Count > 0)
        {
            return PickDeterministic(alt, actorId);
        }

        return PickDeterministic(_clips, actorId);
    }

    internal static DaggerfallHostilityBarkCatalog Read(ReadOnlyMemory<byte>? bytes, string packId, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is not { IsEmpty: false } payload)
            return Empty;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People pack '{packId}' hostility barks", diagnostics);
            if (!root.TryGetProperty("clips", out JsonElement clipsElement) || clipsElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"People pack '{packId}' hostility barks must contain a 'clips' array.");
                return Empty;
            }
            List<DaggerfallHostilityBarkClip> clips = [];
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (JsonElement item in clipsElement.EnumerateArray())
            {
                JsonElement obj = DaggerfallBaseContent.Object(item, $"People pack '{packId}' hostility bark clip", diagnostics);
                string id = DaggerfallBaseContent.Text(obj, "id", diagnostics);
                string kind = DaggerfallBaseContent.Text(obj, "kind", diagnostics);
                string gender = DaggerfallBaseContent.Text(obj, "gender", diagnostics);
                string voice = DaggerfallBaseContent.Text(obj, "voice", diagnostics);
                string text = DaggerfallBaseContent.Text(obj, "text", diagnostics);
                string path = DaggerfallBaseContent.Text(obj, "path", diagnostics);
                string shaHex = DaggerfallBaseContent.Text(obj, "sha256", diagnostics);
                float duration = obj.TryGetProperty("durationSeconds", out JsonElement dur) && dur.ValueKind == JsonValueKind.Number
                    ? dur.GetSingle()
                    : 2.5f;
                if (!ids.Add(id))
                {
                    diagnostics.Add($"People pack '{packId}' hostility barks repeats clip id '{id}'.");
                    continue;
                }
                ContentSha256 sha = DaggerfallContentHash.Parse(shaHex, $"Hostility bark '{id}'");
                clips.Add(new DaggerfallHostilityBarkClip(id, NormalizeKey(kind), NormalizeGender(gender), voice, text, path, sha, duration));
            }
            return new DaggerfallHostilityBarkCatalog(clips);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            diagnostics.Add($"People pack '{packId}' hostility barks manifest is malformed: {ex.Message}");
            return Empty;
        }
    }

    private static DaggerfallHostilityBarkClip PickDeterministic(IReadOnlyList<DaggerfallHostilityBarkClip> pool, long actorId)
    {
        ulong hash = unchecked((ulong)actorId * 11400714819323198485UL);
        int index = (int)(hash % (ulong)pool.Count);
        return pool[index];
    }

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "kin-vendetta" : value.Trim().ToLowerInvariant();

    private static string NormalizeGender(string? gender) =>
        string.Equals(gender?.Trim(), "female", StringComparison.OrdinalIgnoreCase) ? "female" : "male";
}
