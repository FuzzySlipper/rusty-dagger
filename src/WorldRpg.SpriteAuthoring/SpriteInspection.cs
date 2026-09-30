using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldRpg.SpriteAuthoring;

/// <summary>One explicitly inspectable generated sprite set.</summary>
public sealed record SpriteInspectionEntry(
    string Id,
    string Label,
    SpriteInspectionKind Kind,
    SpriteInspectionClosure Closure,
    SpriteInspectionAtlas Atlas,
    IReadOnlyList<SpriteInspectionFrame> Frames,
    IReadOnlyList<SpriteInspectionState> States,
    IReadOnlyList<SpriteInspectionAction> Actions,
    SpriteAuthoredValues AuthoredValues,
    IReadOnlyList<SpriteInspectionFrame>? GeneratedFrames = null,
    SpriteVector2? SourceWorldSize = null);

/// <summary>The media family that supplies the semantic meaning of a sprite set.</summary>
public enum SpriteInspectionKind
{
    DungeonBillboard,
    DungeonActor,
    DungeonCorpse,
    ClassicWeapon,
    ClassicEffect,
}

/// <summary>Closure and provenance facts retained by the generated publication.</summary>
public sealed record SpriteInspectionClosure(
    string RelativePath,
    SpriteContentDigest ContentDigest,
    long ByteLength,
    IReadOnlyList<string> DependsOnPaths,
    IReadOnlyList<SpriteInspectionSource> PublicationSources);

/// <summary>One portable source fact the publication was generated from.</summary>
public sealed record SpriteInspectionSource(string SourcePath, SpriteContentDigest ContentHash, long ByteLen);

/// <summary>Generated atlas facts. These are inspection-only and never editable through an overlay.</summary>
public sealed record SpriteInspectionAtlas(int Width, int Height);

/// <summary>One regenerated atlas frame and its optional source-layout facts.</summary>
public sealed record SpriteInspectionFrame(
    string Id,
    int FrameIndex,
    int X,
    int Y,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    bool Mirrored,
    int? SourceRecord = null,
    int? SourceFrame = null,
    int? Orientation = null,
    SpriteVector2? SourceWorldSize = null);

/// <summary>One source-labelled state layout. It describes data only; it does not play or render.</summary>
public sealed record SpriteInspectionState(
    string Name,
    float SourceFramesPerSecond,
    float FramesPerSecond,
    bool Loops,
    int FrameStart,
    int FramesPerOrientation,
    IReadOnlyList<int> FrameIndices,
    bool IsPreferredRest);

/// <summary>One named action or source sequence, including retained damage-marker values where present.</summary>
public sealed record SpriteInspectionAction(
    string Name,
    float? FramesPerSecond,
    bool? Loops,
    IReadOnlyList<int> FrameIndices,
    IReadOnlyList<SpriteSourceSequenceStep>? SourceSequence = null,
    byte? AlternateChance = null,
    float? SourceFramesPerSecond = null,
    int? SourceRecordOrdinal = null);

/// <summary>One source attack-sequence value. A negative-one value is a retained damage beat, not a frame index.</summary>
public sealed record SpriteSourceSequenceStep(sbyte Value, bool IsDamageMarker);

/// <summary>Only authored presentation values that may later be supplied to media normalization.</summary>
public sealed record SpriteAuthoredValues(
    string? DisplayName,
    SpriteVector2? Pivot,
    SpriteVector2? DisplaySize,
    float? FramesPerSecond,
    bool? Loop,
    IReadOnlyList<int>? Sequence);

/// <summary>Stable, typed inspection catalog over a generated publication's sprite media.</summary>
public sealed record SpriteInspectionCatalog(IReadOnlyList<SpriteInspectionEntry> Entries)
{
    public SpriteInspectionEntry Require(string id) => Entries.SingleOrDefault(entry => StringComparer.Ordinal.Equals(entry.Id, id))
        ?? throw new InvalidOperationException($"Sprite set '{id}' is not present in this publication.");
}

/// <summary>
/// The inspection catalog a generated publication yields for authoring tools,
/// with the structural authoring-basis digest that guards every overlay
/// authored against it. The offline importer produces it; an authoring tool
/// consumes it without reading source-shaped publication sidecars.
/// </summary>
public sealed record SpriteInspectionDocument(SpriteContentDigest AuthoringBasisDigest, SpriteInspectionCatalog Catalog);

/// <summary>Strict JSON for <see cref="SpriteInspectionDocument"/>.</summary>
public static class SpriteInspectionDocumentSerializer
{
    private const int MaximumDocumentBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static byte[] Serialize(SpriteInspectionDocument document)
    {
        Validate(document);
        byte[] bytes = [.. JsonSerializer.SerializeToUtf8Bytes(document, Json), (byte)'\n'];
        if (bytes.Length > MaximumDocumentBytes)
        {
            throw new FormatException("The serialized sprite inspection document exceeds its byte quota.");
        }

        return bytes;
    }

    public static SpriteInspectionDocument Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumDocumentBytes)
        {
            throw new FormatException("A sprite inspection document is empty or exceeds its byte quota.");
        }

        SpriteInspectionDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SpriteInspectionDocument>(bytes, Json)
                ?? throw new FormatException("The sprite inspection document is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("The sprite inspection document is not a supported strict JSON document.", exception);
        }

        Validate(document);
        return document;
    }

    /// <summary>Rejects a document whose catalog could not have come from a valid generated publication.</summary>
    public static void Validate(SpriteInspectionDocument document)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(document);
            document.AuthoringBasisDigest.Validate();
            ArgumentNullException.ThrowIfNull(document.Catalog);
            ArgumentNullException.ThrowIfNull(document.Catalog.Entries);
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (SpriteInspectionEntry entry in document.Catalog.Entries)
            {
                ArgumentNullException.ThrowIfNull(entry);
                SpriteLogicalNames.RequireId(entry.Id, nameof(entry.Id));
                if (!ids.Add(entry.Id))
                {
                    throw new FormatException($"The sprite inspection document repeats entry '{entry.Id}'.");
                }

                ValidateEntry(entry);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new FormatException("The sprite inspection document violates the inspection contract.", exception);
        }
    }

    private static void ValidateEntry(SpriteInspectionEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Label) || !Enum.IsDefined(entry.Kind))
        {
            throw new FormatException($"Sprite '{entry.Id}' needs a label and a known kind.");
        }

        ArgumentNullException.ThrowIfNull(entry.Closure);
        SpriteLogicalNames.RequirePath(entry.Closure.RelativePath, nameof(entry.Closure.RelativePath));
        entry.Closure.ContentDigest.Validate();
        ArgumentNullException.ThrowIfNull(entry.Closure.DependsOnPaths);
        ArgumentNullException.ThrowIfNull(entry.Closure.PublicationSources);
        foreach (SpriteInspectionSource source in entry.Closure.PublicationSources)
        {
            ArgumentNullException.ThrowIfNull(source);
            SpriteLogicalNames.RequirePath(source.SourcePath, nameof(source.SourcePath));
            source.ContentHash.Validate();
        }

        ArgumentNullException.ThrowIfNull(entry.Atlas);
        ArgumentNullException.ThrowIfNull(entry.Frames);
        ArgumentNullException.ThrowIfNull(entry.States);
        ArgumentNullException.ThrowIfNull(entry.Actions);
        ArgumentNullException.ThrowIfNull(entry.AuthoredValues);
        if (entry.Closure.ByteLength <= 0 || entry.Atlas.Width <= 0 || entry.Atlas.Height <= 0 || entry.Frames.Count == 0
            || entry.Frames.Any(frame => frame is null) || entry.States.Any(state => state?.FrameIndices is null)
            || entry.Actions.Any(action => action?.FrameIndices is null)
            || entry.Frames.Select(frame => frame.FrameIndex).Distinct().Count() != entry.Frames.Count)
        {
            throw new FormatException($"Sprite '{entry.Id}' has an empty or malformed atlas, frame, state, or action record.");
        }
    }
}
