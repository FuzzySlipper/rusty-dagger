using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldRpg.SpriteWorkbench;

internal sealed record SpriteWorkbenchIntent(string Action, string? Id = null, string? Sequence = null, int? Orientation = null, int? SequenceFrameIndex = null,
    double? ElapsedSeconds = null, string? DisplayName = null, float? PivotX = null, float? PivotY = null, float? DisplaySizeX = null,
    float? DisplaySizeY = null, float? FramesPerSecond = null, bool? Loop = null, int[]? FrameSequence = null)
{
    internal const string Contract = "worldrpg.sprite-workbench.intent.v1";
    private const int MaximumPayloadBytes = 16 * 1024;
    private const int MaximumSequenceValues = 1024;

    internal static SpriteWorkbenchIntent Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadBytes) throw new FormatException("A sprite workbench intent is empty or exceeds its local byte quota.");
        try
        {
            SpriteWorkbenchIntent value = JsonSerializer.Deserialize<SpriteWorkbenchIntent>(payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new FormatException("A sprite workbench intent cannot be empty.");
            if (string.IsNullOrWhiteSpace(value.Action)) throw new FormatException("A sprite workbench intent needs an action.");
            if (value.FrameSequence is { Length: > MaximumSequenceValues }) throw new FormatException("A sprite workbench frame sequence exceeds its local entry quota.");
            return value;
        }
        catch (JsonException error)
        {
            throw new FormatException("The sprite workbench intent is not strict JSON.", error);
        }
    }
}
