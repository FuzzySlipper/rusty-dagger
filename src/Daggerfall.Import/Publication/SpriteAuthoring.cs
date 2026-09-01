using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

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
    SpriteAuthoredValues AuthoredValues);

/// <summary>The source family that supplies the semantic meaning of a sprite set.</summary>
public enum SpriteInspectionKind
{
    DungeonBillboard,
    DungeonActor,
    DungeonCorpse,
    ClassicWeapon,
    ClassicEffect,
}

/// <summary>Closure and provenance facts retained by the canonical publication.</summary>
public sealed record SpriteInspectionClosure(
    string RelativePath,
    ContentDigest ContentDigest,
    long ByteLength,
    IReadOnlyList<string> DependsOnPaths,
    IReadOnlyList<ImportPublicationSource> PublicationSources);

/// <summary>Generated atlas facts. These are inspection-only and never editable through an overlay.</summary>
public sealed record SpriteInspectionAtlas(int Width, int Height);

/// <summary>One regenerated atlas frame and its optional dungeon source-layout facts.</summary>
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
    int? Orientation = null);

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
    NormalizedVector2? Pivot,
    NormalizedVector2? DisplaySize,
    float? FramesPerSecond,
    bool? Loop,
    IReadOnlyList<int>? Sequence);

/// <summary>Stable, typed inspection catalog over the generated dungeon and classic media sidecars.</summary>
public sealed record SpriteInspectionCatalog(IReadOnlyList<SpriteInspectionEntry> Entries)
{
    public SpriteInspectionEntry Require(string id) => Entries.SingleOrDefault(entry => StringComparer.Ordinal.Equals(entry.Id, id))
        ?? throw new InvalidOperationException($"Sprite set '{id}' is not present in this publication.");
}

/// <summary>
/// Stable identity for authored sprite values. It retains source and generated
/// structural facts while deliberately excluding tunable presentation values
/// and sidecar bytes that change when those values are reapplied.
/// </summary>
public static class SpriteAuthoringBasis
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ContentDigest Compute(CanonicalImportManifest publication, SpriteInspectionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(catalog);
        BasisDocument document = new(
            publication.ImporterId,
            publication.ImporterVersion,
            publication.Sources.OrderBy(source => source.SourcePath, StringComparer.Ordinal).Select(source => new BasisSource(source.SourcePath, source.ContentHash, source.ByteLen)).ToArray(),
            catalog.Entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).Select(entry => new BasisEntry(
                entry.Id, entry.Kind, entry.Closure.RelativePath, entry.Closure.ContentDigest, entry.Closure.ByteLength,
                entry.Atlas.Width, entry.Atlas.Height,
                entry.Frames.OrderBy(frame => frame.FrameIndex).Select(frame => new BasisFrame(frame.Id, frame.FrameIndex, frame.X, frame.Y, frame.Width, frame.Height, frame.SourceWidth, frame.SourceHeight, frame.Mirrored, frame.SourceRecord, frame.SourceFrame, frame.Orientation)).ToArray(),
                entry.States.OrderBy(state => state.Name, StringComparer.Ordinal).Select(state => new BasisState(state.Name, state.FrameStart, state.FramesPerOrientation, state.FrameIndices.Order().ToArray(), state.IsPreferredRest)).ToArray(),
                entry.Actions.OrderBy(action => action.Name, StringComparer.Ordinal).Select(action => new BasisAction(
                    action.Name,
                    action.SourceRecordOrdinal,
                    entry.Kind == SpriteInspectionKind.ClassicWeapon ? action.FrameIndices.Order().ToArray() : null,
                    action.SourceSequence?.Select(step => step.Value).ToArray(),
                    action.AlternateChance)).ToArray())).ToArray());
        return ContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(document, Json));
    }

    private sealed record BasisDocument(string ImporterId, int ImporterVersion, IReadOnlyList<BasisSource> Sources, IReadOnlyList<BasisEntry> Entries);
    private sealed record BasisSource(string Path, ContentDigest Digest, long ByteLength);
    private sealed record BasisEntry(string Id, SpriteInspectionKind Kind, string Path, ContentDigest Digest, long ByteLength, int AtlasWidth, int AtlasHeight, IReadOnlyList<BasisFrame> Frames, IReadOnlyList<BasisState> States, IReadOnlyList<BasisAction> Actions);
    private sealed record BasisFrame(string Id, int Index, int X, int Y, int Width, int Height, int SourceWidth, int SourceHeight, bool Mirrored, int? SourceRecord, int? SourceFrame, int? Orientation);
    private sealed record BasisState(string Name, int FrameStart, int FramesPerOrientation, IReadOnlyList<int> Frames, bool PreferredRest);
    private sealed record BasisAction(string Name, int? SourceRecordOrdinal, IReadOnlyList<int>? StructuralFrameIndices, IReadOnlyList<sbyte>? SourceSequence, byte? AlternateChance);
}

/// <summary>
/// Creates a typed sprite inspection catalog from a validated publication.
/// It intentionally projects canonical sidecar records instead of exposing
/// mutable manifest dictionaries or reproducing Studio display calculations.
/// </summary>
public static class SpriteInspectionCatalogBuilder
{
    public static SpriteInspectionCatalog Create(
        CanonicalImportManifest publication,
        DungeonMediaManifestSidecar dungeon,
        ClassicMediaManifestSidecar classic)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(publication);
            ArgumentNullException.ThrowIfNull(dungeon);
            ArgumentNullException.ThrowIfNull(classic);
            publication.Validate();
            ValidateSidecars(dungeon, classic);

            IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure = publication.Artifacts
                .ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
            ValidateMediaClosure(dungeon.Media, closure, "dungeon");
            ValidateMediaClosure(classic.Media, closure, "classic");
            List<SpriteInspectionEntry> entries = [];
            AddDungeonBillboards(entries, dungeon, closure, publication.Sources);
            AddDungeonActors(entries, dungeon, closure, publication.Sources);
            AddClassicWeapon(entries, classic, closure, publication.Sources);
            AddClassicEffects(entries, classic, closure, publication.Sources);
            if (entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            {
                throw new InvalidOperationException("Sprite inspection entries must have unique media IDs.");
            }

            return new(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        {
            throw new FormatException("The sprite inspection input violates the canonical publication contract.", exception);
        }
    }

    internal static void ValidateSidecars(DungeonMediaManifestSidecar dungeon, ClassicMediaManifestSidecar classic)
    {
        Arena2MediaBundlePublication.ValidatePersistedSidecars(dungeon, classic);
    }

    private static void AddDungeonBillboards(
        ICollection<SpriteInspectionEntry> entries,
        DungeonMediaManifestSidecar sidecar,
        IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure,
        IReadOnlyList<ImportPublicationSource> sources)
    {
        foreach (DungeonBillboardMediaManifest billboard in sidecar.Billboards.OrderBy(value => value.MediaId, StringComparer.Ordinal))
        {
            NormalizedMediaDescriptor descriptor = RequireDescriptor(sidecar.Media, billboard.MediaId, "dungeon billboard");
            entries.Add(new(
                descriptor.Id,
                $"Billboard {billboard.SpriteResourceId}",
                SpriteInspectionKind.DungeonBillboard,
                ToClosure(descriptor, closure, sources),
                new(descriptor.AtlasWidth, descriptor.AtlasHeight),
                Frames(descriptor, billboard.Frames),
                [],
                [new("billboard", billboard.Playback?.FramesPerSecond, billboard.Playback?.Loops, descriptor.Sequence ?? descriptor.Frames.Select(frame => frame.FrameIndex).ToArray(), SourceFramesPerSecond: billboard.SourcePlayback?.FramesPerSecond)],
                Authored(descriptor)));
        }
    }

    private static void AddDungeonActors(
        ICollection<SpriteInspectionEntry> entries,
        DungeonMediaManifestSidecar sidecar,
        IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure,
        IReadOnlyList<ImportPublicationSource> sources)
    {
        foreach (DungeonActorMediaManifest actor in sidecar.Actors.OrderBy(value => value.MediaId, StringComparer.Ordinal))
        {
            NormalizedMediaDescriptor descriptor = RequireDescriptor(sidecar.Media, actor.MediaId, "dungeon actor");
            IReadOnlyList<DungeonMediaFrameLayout> layouts = actor.States.SelectMany(state => state.Frames).ToArray();
            IReadOnlyList<SpriteInspectionState> states = actor.States.OrderBy(state => state.State).Select(state => new SpriteInspectionState(
                state.State.ToString(), state.SourcePlayback.FramesPerSecond, state.Playback.FramesPerSecond, state.Playback.Loops,
                state.FrameStart, state.FramesPerOrientation, state.Frames.OrderBy(frame => frame.AtlasFrameIndex).Select(frame => frame.AtlasFrameIndex).ToArray(),
                state.State == actor.PreferredRestState)).ToArray();
            entries.Add(new(
                descriptor.Id,
                $"Actor {actor.SourceName}",
                SpriteInspectionKind.DungeonActor,
                ToClosure(descriptor, closure, sources),
                new(descriptor.AtlasWidth, descriptor.AtlasHeight),
                Frames(descriptor, layouts),
                states,
                [new("primary-attack-source", null, null, [], Sequence(actor.SourceAttackSequence.PrimaryFrames)),
                 .. actor.SourceAttackSequence.Alternates.Select((alternate, index) => new SpriteInspectionAction($"primary-attack-alternate-{index}", null, null, [], Sequence(alternate.Frames), alternate.Chance))],
                Authored(descriptor)));

            if (actor.Corpse is not null)
            {
                NormalizedMediaDescriptor corpse = RequireDescriptor(sidecar.Media, actor.Corpse.MediaId, "dungeon corpse");
                entries.Add(new(
                    corpse.Id,
                    $"Corpse {actor.SourceName}",
                    SpriteInspectionKind.DungeonCorpse,
                    ToClosure(corpse, closure, sources),
                    new(corpse.AtlasWidth, corpse.AtlasHeight),
                    Frames(corpse, [actor.Corpse.Frame]),
                    [], [], Authored(corpse)));
            }
        }
    }

    private static void AddClassicWeapon(
        ICollection<SpriteInspectionEntry> entries,
        ClassicMediaManifestSidecar sidecar,
        IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure,
        IReadOnlyList<ImportPublicationSource> sources)
    {
        if (sidecar.WeaponActions.Count == 0)
        {
            return;
        }

        NormalizedMediaDescriptor descriptor = sidecar.Media.Resources.SingleOrDefault(resource => resource.Kind == NormalizedMediaKind.WeaponSprite)
            ?? throw new FormatException("Classic media does not contain a weapon sprite descriptor.");
        entries.Add(new(
            descriptor.Id,
            "Classic dagger weapon",
            SpriteInspectionKind.ClassicWeapon,
            ToClosure(descriptor, closure, sources),
            new(descriptor.AtlasWidth, descriptor.AtlasHeight),
            Frames(descriptor, null), [],
            sidecar.WeaponActions.OrderBy(action => action.Action).Select(action => new SpriteInspectionAction(
                action.Action.ToString(), action.Timing.FramesPerSecond, action.Timing.Loop,
                Enumerable.Range(action.FrameStart, action.FrameCount).ToArray(),
                SourceRecordOrdinal: action.SourceRecordOrdinal)).ToArray(),
            Authored(descriptor)));
    }

    private static void AddClassicEffects(
        ICollection<SpriteInspectionEntry> entries,
        ClassicMediaManifestSidecar sidecar,
        IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure,
        IReadOnlyList<ImportPublicationSource> sources)
    {
        foreach (IGrouping<string, ClassicEffectManifest> group in sidecar.Effects.GroupBy(effect => effect.MediaId, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            NormalizedMediaDescriptor descriptor = RequireDescriptor(sidecar.Media, group.Key, "classic effect");
            entries.Add(new(
                descriptor.Id,
                $"Classic effect {string.Join(", ", group.OrderBy(effect => effect.Effect).Select(effect => effect.Effect))}",
                SpriteInspectionKind.ClassicEffect,
                ToClosure(descriptor, closure, sources),
                new(descriptor.AtlasWidth, descriptor.AtlasHeight), Frames(descriptor, null), [],
                group.OrderBy(effect => effect.Effect).Select(effect => new SpriteInspectionAction(
                    effect.Effect.ToString(), effect.Timing.FramesPerSecond, effect.Timing.Loop,
                    descriptor.Sequence ?? descriptor.Frames.Select(frame => frame.FrameIndex).ToArray(),
                    SourceRecordOrdinal: effect.SourceRecordOrdinal)).ToArray(),
                Authored(descriptor)));
        }
    }

    private static SpriteInspectionClosure ToClosure(NormalizedMediaDescriptor descriptor, IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure, IReadOnlyList<ImportPublicationSource> sources)
    {
        if (!closure.TryGetValue(descriptor.RelativePath, out ImportPublicationManifestArtifact? artifact)
            || artifact.ContentHash != descriptor.ContentDigest || artifact.ByteLen != descriptor.ByteLength)
        {
            throw new FormatException($"Sprite '{descriptor.Id}' does not agree with the publication closure.");
        }

        return new(descriptor.RelativePath, descriptor.ContentDigest, descriptor.ByteLength, artifact.DependsOnPaths.ToArray(), sources.OrderBy(source => source.SourcePath, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateMediaClosure(NormalizedMediaManifest media, IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure, string family)
    {
        foreach (NormalizedMediaDescriptor descriptor in media.Resources)
        {
            if (!closure.TryGetValue(descriptor.RelativePath, out ImportPublicationManifestArtifact? artifact)
                || artifact.ContentHash != descriptor.ContentDigest || artifact.ByteLen != descriptor.ByteLength)
            {
                throw new FormatException($"The {family} media descriptor '{descriptor.Id}' does not agree with the publication closure.");
            }
        }
    }

    private static IReadOnlyList<SpriteInspectionFrame> Frames(NormalizedMediaDescriptor descriptor, IReadOnlyList<DungeonMediaFrameLayout>? layouts)
    {
        Dictionary<int, DungeonMediaFrameLayout> layoutByIndex = layouts is null
            ? []
            : layouts.ToDictionary(layout => layout.AtlasFrameIndex);
        return descriptor.Frames.OrderBy(frame => frame.FrameIndex).Select(frame =>
        {
            layoutByIndex.TryGetValue(frame.FrameIndex, out DungeonMediaFrameLayout? layout);
            return new SpriteInspectionFrame(frame.Id, frame.FrameIndex, frame.X, frame.Y, frame.Width, frame.Height,
                frame.SourceWidth, frame.SourceHeight, frame.Mirrored, layout?.SourceRecord, layout?.SourceFrame, layout?.Orientation);
        }).ToArray();
    }

    private static SpriteAuthoredValues Authored(NormalizedMediaDescriptor descriptor) => new(
        descriptor.DisplayName, descriptor.Pivot, descriptor.DisplaySize, descriptor.FramesPerSecond, descriptor.Loop, descriptor.Sequence?.ToArray());

    private static IReadOnlyList<SpriteSourceSequenceStep> Sequence(IReadOnlyList<sbyte> source) => source
        .Select(value => new SpriteSourceSequenceStep(value, value == -1))
        .ToArray();

    private static NormalizedMediaDescriptor RequireDescriptor(NormalizedMediaManifest manifest, string id, string subject) => manifest.Resources.SingleOrDefault(resource => StringComparer.Ordinal.Equals(resource.Id, id))
        ?? throw new FormatException($"The {subject} references unknown media '{id}'.");

}

/// <summary>Dedicated tracked input for future source regeneration; it is not a generated sidecar.</summary>
public sealed record SpriteAuthoredOverlayDocument(int SchemaVersion, ContentDigest AuthoringBasisDigest, IReadOnlyList<SpriteAuthoredOverlay> Overlays)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Typed authored overlay values. Generated frames, UVs, bytes, dimensions, paths, and hashes have no fields here.</summary>
public sealed record SpriteAuthoredOverlay(
    string Id,
    string? DisplayName = null,
    NormalizedVector2? Pivot = null,
    NormalizedVector2? DisplaySize = null,
    float? FramesPerSecond = null,
    bool? Loop = null,
    IReadOnlyList<int>? Sequence = null);

/// <summary>
/// Strict JSON and safe filesystem operations for authored sprite overlays.
/// The authoring root is separate from the generated publication root: callers
/// later feed validated overlays to a regeneration request through
/// <see cref="ToMediaOverlays"/> rather than placing them in a publication.
/// </summary>
public static class SpriteAuthoredOverlayStore
{
    private const int MaximumOverlayBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SpriteAuthoredOverlayDocument Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumOverlayBytes)
        {
            throw new FormatException("A sprite overlay document is empty or exceeds its byte quota.");
        }

        try
        {
            return JsonSerializer.Deserialize<SpriteAuthoredOverlayDocument>(bytes, Json)
                ?? throw new FormatException("The sprite overlay document is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("The sprite overlay document is not a supported strict JSON document.", exception);
        }
    }

    public static void Validate(SpriteAuthoredOverlayDocument document, SpriteInspectionCatalog catalog, ContentDigest authoringBasisDigest)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);
        if (document.SchemaVersion != SpriteAuthoredOverlayDocument.CurrentSchemaVersion)
        {
            throw new FormatException("The sprite overlay schema version is not supported.");
        }

        document.AuthoringBasisDigest.Validate();
        if (document.AuthoringBasisDigest != authoringBasisDigest)
        {
            throw new FormatException("The sprite overlay was authored against a different structural authoring basis.");
        }

        ArgumentNullException.ThrowIfNull(document.Overlays);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (SpriteAuthoredOverlay overlay in document.Overlays)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            NormalizedImportDocument.RequireLogicalId(overlay.Id, nameof(overlay.Id));
            if (!ids.Add(overlay.Id))
            {
                throw new FormatException($"The sprite overlay contains duplicate ID '{overlay.Id}'.");
            }

            SpriteInspectionEntry entry = catalog.Require(overlay.Id);
            if (entry.Kind == SpriteInspectionKind.ClassicWeapon
                && (overlay.FramesPerSecond is not null || overlay.Loop is not null || overlay.Sequence is not null))
            {
                throw new FormatException("Classic weapon timing and sequence are action-specific and cannot be authored as resource-wide values.");
            }
            ValidateValues(overlay, entry.Frames.Count);
        }
    }

    /// <summary>Converts validated overlay data to the existing normalized-media input seam for a later regeneration command.</summary>
    public static IReadOnlyList<AuthoredMediaOverlay> ToMediaOverlays(SpriteAuthoredOverlayDocument document, SpriteInspectionCatalog catalog, ContentDigest authoringBasisDigest)
    {
        Validate(document, catalog, authoringBasisDigest);
        return document.Overlays.Select(overlay => new AuthoredMediaOverlay(
            overlay.Id, true, overlay.DisplayName, overlay.Pivot, overlay.DisplaySize, overlay.FramesPerSecond, overlay.Loop, overlay.Sequence?.ToArray())).ToArray();
    }

    public static string ResolveRelativePath(string rootDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        NormalizedImportDocument.RequireLogicalPath(relativePath, nameof(relativePath));
        string root = Path.GetFullPath(rootDirectory);
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The overlay path escaped the publication directory.", nameof(relativePath));
        }

        return candidate;
    }

    /// <summary>
    /// Proves that authored source files cannot reside in or contain the exact
    /// generated publication tree. This check is lexical over normalized
    /// absolute paths so it also works when discard is deliberately invoked
    /// without opening a publication directory.
    /// </summary>
    public static void ValidateRootSeparation(string publicationDirectory, string authoringDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoringDirectory);
        string publication = TrimTrailingSeparator(Path.GetFullPath(publicationDirectory));
        string authoring = TrimTrailingSeparator(Path.GetFullPath(authoringDirectory));
        if (StringComparer.Ordinal.Equals(publication, authoring)
            || IsNestedWithin(publication, authoring)
            || IsNestedWithin(authoring, publication))
        {
            throw new ArgumentException("The authored sprite source root must be distinct from, and neither contain nor be contained by, the generated publication root.");
        }
    }

    /// <summary>Overlay files are JSON leaves relative to a separately selected authored source root.</summary>
    public static void ValidateOverlayRelativePath(string relativePath)
    {
        NormalizedImportDocument.RequireLogicalPath(relativePath, nameof(relativePath));
        if (!relativePath.StartsWith("sprites/", StringComparison.Ordinal)
            || !relativePath.EndsWith(".json", StringComparison.Ordinal))
        {
            throw new ArgumentException("A sprite overlay must be a relative JSON sidecar under sprites/ in the authored source root.", nameof(relativePath));
        }
    }

    public static void Write(string publicationDirectory, string authoringDirectory, string relativePath, SpriteAuthoredOverlayDocument document, SpriteInspectionCatalog catalog, ContentDigest authoringBasisDigest)
    {
        Validate(document, catalog, authoringBasisDigest);
        ValidateRootSeparation(publicationDirectory, authoringDirectory);
        ValidateOverlayRelativePath(relativePath);
        string target = ResolveRelativePath(authoringDirectory, relativePath);
        string? directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException("The overlay target has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        byte[] bytes = [.. JsonSerializer.SerializeToUtf8Bytes(document, Json), (byte)'\n'];
        if (bytes.Length > MaximumOverlayBytes)
        {
            throw new FormatException("The serialized sprite overlay exceeds its byte quota.");
        }
        string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        string backup = target + ".bak";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(target))
            {
                if (File.Exists(backup))
                {
                    throw new IOException("The sprite overlay recovery path already exists; inspect or move it before replacing the overlay.");
                }

                File.Replace(temporary, target, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, target);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Moves an overlay out of the separate authored root without reading or changing any publication.</summary>
    public static bool Discard(string publicationDirectory, string authoringDirectory, string relativePath)
    {
        ValidateRootSeparation(publicationDirectory, authoringDirectory);
        ValidateOverlayRelativePath(relativePath);
        string target = ResolveRelativePath(authoringDirectory, relativePath);
        if (!File.Exists(target))
        {
            return false;
        }

        string discarded = target + ".discarded";
        if (File.Exists(discarded))
        {
            throw new IOException("The sprite overlay recovery path already exists; inspect or move it before discarding again.");
        }

        File.Move(target, discarded);
        return true;
    }

    private static string TrimTrailingSeparator(string path)
    {
        string root = Path.GetPathRoot(path) ?? string.Empty;
        return path.Length == root.Length ? path : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsNestedWithin(string candidate, string root)
    {
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static void ValidateValues(SpriteAuthoredOverlay overlay, int frameCount)
    {
        if (overlay.DisplayName is not null && (string.IsNullOrWhiteSpace(overlay.DisplayName) || overlay.DisplayName.Any(char.IsControl)))
        {
            throw new FormatException("A sprite overlay display name must be non-empty plain text.");
        }

        overlay.Pivot?.Validate(nameof(overlay.Pivot));
        overlay.DisplaySize?.Validate(nameof(overlay.DisplaySize));
        if (overlay.DisplaySize is { X: <= 0F } or { Y: <= 0F }
            || overlay.FramesPerSecond is <= 0F
            || (overlay.FramesPerSecond is not null && !float.IsFinite(overlay.FramesPerSecond.Value))
            || overlay.Sequence is { Count: 0 }
            || overlay.Sequence?.Any(index => index < 0 || index >= frameCount) == true)
        {
            throw new FormatException("A sprite overlay contains invalid authored presentation values.");
        }
    }
}

/// <summary>Strict reader for the fixed generated sidecars required by sprite inspection.</summary>
public static class SpritePublicationReader
{
    private const int MaximumSidecarBytes = 32 * 1024 * 1024;
    private const long MaximumMediaArtifactBytes = 16L * 1024 * 1024;
    private const long MaximumMediaArtifactTotalBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static SpritePublicationSnapshot Read(string publicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationDirectory);
        byte[] manifestBytes = ReadRequired(publicationDirectory, ImportPublicationManifestSerializer.ManifestRelativePath);
        CanonicalImportManifest manifest = Deserialize<CanonicalImportManifest>(manifestBytes, "publication manifest");
        byte[] dungeonBytes = ReadRequired(publicationDirectory, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath);
        byte[] classicBytes = ReadRequired(publicationDirectory, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath);
        DungeonMediaManifestSidecar dungeon = Deserialize<DungeonMediaManifestSidecar>(dungeonBytes, "dungeon sprite sidecar");
        ClassicMediaManifestSidecar classic = Deserialize<ClassicMediaManifestSidecar>(classicBytes, "classic sprite sidecar");
        SpriteInspectionCatalog catalog;
        try
        {
            manifest.Validate();
            ValidateManifestArtifact(manifest, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath, dungeonBytes);
            ValidateManifestArtifact(manifest, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, classicBytes);
            catalog = SpriteInspectionCatalogBuilder.Create(manifest, dungeon, classic);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        {
            throw new FormatException("The sprite publication metadata violates the canonical contract.", exception);
        }

        VerifyMediaArtifacts(publicationDirectory, [.. dungeon.Media.Resources, .. classic.Media.Resources]);
        return new(catalog, SpriteAuthoringBasis.Compute(manifest, catalog), manifest);
    }

    /// <summary>Builds the same authoring identity from an in-memory generated plan before publication.</summary>
    public static SpritePublicationSnapshot FromPlan(ImportPublicationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        try
        {
            IReadOnlyDictionary<string, ImportPublicationArtifact> artifacts = plan.Artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
            DungeonMediaManifestSidecar dungeon = Deserialize<DungeonMediaManifestSidecar>(artifacts[Arena2MediaBundlePublication.DungeonMediaManifestRelativePath].Bytes.Span, "generated dungeon sprite sidecar");
            ClassicMediaManifestSidecar classic = Deserialize<ClassicMediaManifestSidecar>(artifacts[Arena2MediaBundlePublication.ClassicMediaManifestRelativePath].Bytes.Span, "generated classic sprite sidecar");
            SpriteInspectionCatalog catalog = SpriteInspectionCatalogBuilder.Create(plan.Manifest, dungeon, classic);
            return new(catalog, SpriteAuthoringBasis.Compute(plan.Manifest, catalog), plan.Manifest);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or NullReferenceException or OverflowException)
        {
            throw new FormatException("The generated import plan does not contain a valid sprite authoring basis.", exception);
        }
    }

    private static byte[] ReadRequired(string root, string relativePath)
    {
        string path = SpriteAuthoredOverlayStore.ResolveRelativePath(root, relativePath);
        FileInfo file = new(path);
        if (!file.Exists || file.Length is <= 0 or > MaximumSidecarBytes)
        {
            throw new FormatException($"Required sprite publication file '{relativePath}' is missing or outside its byte quota.");
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.LongLength != file.Length)
        {
            throw new IOException($"Sprite publication file '{relativePath}' changed while it was being read.");
        }

        return bytes;
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> bytes, string subject)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Json)
                ?? throw new FormatException($"The {subject} is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException($"The {subject} is not a supported strict JSON document.", exception);
        }
    }

    private static void ValidateManifestArtifact(CanonicalImportManifest manifest, string relativePath, ReadOnlySpan<byte> bytes)
    {
        ImportPublicationManifestArtifact artifact = manifest.Artifacts.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.RelativePath, relativePath))
            ?? throw new FormatException($"The publication manifest does not contain '{relativePath}'.");
        if (artifact.ByteLen != bytes.Length || artifact.ContentHash != ContentDigest.Compute(bytes))
        {
            throw new FormatException($"The publication file '{relativePath}' does not match its manifest digest.");
        }
    }

    private static void VerifyMediaArtifacts(string root, IReadOnlyList<NormalizedMediaDescriptor> descriptors)
    {
        long total = 0;
        foreach (NormalizedMediaDescriptor descriptor in descriptors)
        {
            if (descriptor.ByteLength > MaximumMediaArtifactBytes || (total = checked(total + descriptor.ByteLength)) > MaximumMediaArtifactTotalBytes)
            {
                throw new FormatException("Sprite publication media exceeds the explicit artifact verification quota.");
            }

            string path = SpriteAuthoredOverlayStore.ResolveRelativePath(root, descriptor.RelativePath);
            FileInfo file = new(path);
            if (!file.Exists || file.Length != descriptor.ByteLength)
            {
                throw new FormatException($"Published media artifact '{descriptor.RelativePath}' is missing or has the wrong length.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != file.Length)
            {
                throw new IOException($"Published media artifact '{descriptor.RelativePath}' changed while it was being read.");
            }

            if (ContentDigest.Compute(bytes) != descriptor.ContentDigest)
            {
                throw new FormatException($"Published media artifact '{descriptor.RelativePath}' does not match its descriptor digest.");
            }
        }
    }
}

/// <summary>One validated inspection catalog and the digest that guards its authored overlay.</summary>
public sealed record SpritePublicationSnapshot(SpriteInspectionCatalog Catalog, ContentDigest AuthoringBasisDigest, CanonicalImportManifest Manifest);
