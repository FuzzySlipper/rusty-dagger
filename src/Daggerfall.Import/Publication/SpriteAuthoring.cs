using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Publication;

/// <summary>
/// Stable identity for authored sprite values. It retains source and generated
/// structural facts while deliberately excluding tunable presentation values
/// and sidecar bytes that change when those values are reapplied.
/// </summary>
public static class SpriteAuthoringBasis
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static SpriteContentDigest Compute(CanonicalImportManifest publication, SpriteInspectionCatalog catalog)
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
                (entry.GeneratedFrames ?? entry.Frames).OrderBy(frame => frame.FrameIndex).Select(frame => new BasisFrame(frame.Id, frame.FrameIndex, frame.X, frame.Y, frame.Width, frame.Height, frame.SourceWidth, frame.SourceHeight, frame.Mirrored, frame.SourceRecord, frame.SourceFrame, frame.Orientation)).ToArray(),
                entry.States.OrderBy(state => state.Name, StringComparer.Ordinal).Select(state => new BasisState(state.Name, state.FrameStart, state.FramesPerOrientation, state.FrameIndices.Order().ToArray(), state.IsPreferredRest)).ToArray(),
                entry.Actions.OrderBy(action => action.Name, StringComparer.Ordinal).Select(action => new BasisAction(
                    action.Name,
                    action.SourceRecordOrdinal,
                    entry.Kind == SpriteInspectionKind.ClassicWeapon ? action.FrameIndices.Order().ToArray() : null,
                    action.SourceSequence?.Select(step => step.Value).ToArray(),
                    action.AlternateChance)).ToArray())).ToArray());
        return SpriteContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(document, Json));
    }

    private sealed record BasisDocument(string ImporterId, int ImporterVersion, IReadOnlyList<BasisSource> Sources, IReadOnlyList<BasisEntry> Entries);
    private sealed record BasisSource(string Path, ContentDigest Digest, long ByteLength);
    private sealed record BasisEntry(string Id, SpriteInspectionKind Kind, string Path, SpriteContentDigest Digest, long ByteLength, int AtlasWidth, int AtlasHeight, IReadOnlyList<BasisFrame> Frames, IReadOnlyList<BasisState> States, IReadOnlyList<BasisAction> Actions);
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
            Arena2MediaBundlePublication.ValidatePersistedSidecars(dungeon, classic);

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
                Authored(descriptor), GeneratedFrames(descriptor, billboard.Frames)));
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
                Frames(descriptor, layouts, includeSourceWorldSize: true),
                states,
                [new("primary-attack-source", null, null, [], Sequence(actor.SourceAttackSequence.PrimaryFrames)),
                 .. actor.SourceAttackSequence.Alternates.Select((alternate, index) => new SpriteInspectionAction($"primary-attack-alternate-{index}", null, null, [], Sequence(alternate.Frames), alternate.Chance))],
                Authored(descriptor), GeneratedFrames(descriptor, layouts, includeSourceWorldSize: true), Vector(actor.SourceWorldSize)));

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
                    [], [], Authored(corpse), GeneratedFrames(corpse, [actor.Corpse.Frame])));
            }
        }
    }

    private static void AddClassicWeapon(
        ICollection<SpriteInspectionEntry> entries,
        ClassicMediaManifestSidecar sidecar,
        IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure,
        IReadOnlyList<ImportPublicationSource> sources)
    {
        if (sidecar.WeaponMedia.Count == 0)
        {
            return;
        }

        foreach (ClassicWeaponMediaManifest weapon in sidecar.WeaponMedia.OrderBy(weapon => weapon.ResourceId, StringComparer.Ordinal))
        {
            NormalizedMediaDescriptor descriptor = RequireDescriptor(sidecar.Media, weapon.ResourceId, "classic weapon");
            entries.Add(new(
                descriptor.Id,
                $"Classic {descriptor.Id} weapon",
                SpriteInspectionKind.ClassicWeapon,
                ToClosure(descriptor, closure, sources),
                new(descriptor.AtlasWidth, descriptor.AtlasHeight),
                Frames(descriptor, null), [],
                weapon.Actions.OrderBy(action => action.Action).Select(action => new SpriteInspectionAction(
                    action.Action.ToString(), action.Timing.FramesPerSecond, action.Timing.Loop,
                    action.Sequence ?? Enumerable.Range(action.FrameStart, action.FrameCount).ToArray(),
                    SourceRecordOrdinal: action.SourceRecordOrdinal)).ToArray(),
                Authored(descriptor), GeneratedFrames(descriptor, null)));
        }
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
                Authored(descriptor), GeneratedFrames(descriptor, null)));
        }
    }

    private static SpriteInspectionClosure ToClosure(NormalizedMediaDescriptor descriptor, IReadOnlyDictionary<string, ImportPublicationManifestArtifact> closure, IReadOnlyList<ImportPublicationSource> sources)
    {
        if (!closure.TryGetValue(descriptor.RelativePath, out ImportPublicationManifestArtifact? artifact)
            || artifact.ContentHash != descriptor.ContentDigest || artifact.ByteLen != descriptor.ByteLength)
        {
            throw new FormatException($"Sprite '{descriptor.Id}' does not agree with the publication closure.");
        }

        return new(descriptor.RelativePath, Digest(descriptor.ContentDigest), descriptor.ByteLength, artifact.DependsOnPaths.ToArray(),
            sources.OrderBy(source => source.SourcePath, StringComparer.Ordinal).Select(source => new SpriteInspectionSource(source.SourcePath, Digest(source.ContentHash), source.ByteLen)).ToArray());
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

    private static IReadOnlyList<SpriteInspectionFrame> Frames(NormalizedMediaDescriptor descriptor, IReadOnlyList<DungeonMediaFrameLayout>? layouts, bool includeSourceWorldSize = false)
    {
        Dictionary<int, DungeonMediaFrameLayout> layoutByIndex = layouts is null
            ? []
            : layouts.ToDictionary(layout => layout.AtlasFrameIndex);
        return descriptor.Frames.OrderBy(frame => frame.FrameIndex).Select(frame =>
        {
            layoutByIndex.TryGetValue(frame.FrameIndex, out DungeonMediaFrameLayout? layout);
            return new SpriteInspectionFrame(frame.Id, frame.FrameIndex, frame.X, frame.Y, frame.Width, frame.Height,
                frame.SourceWidth, frame.SourceHeight, frame.Mirrored, layout?.SourceRecord, layout?.SourceFrame, layout?.Orientation,
                includeSourceWorldSize ? Vector(layout?.SourceWorldSize) : null);
        }).ToArray();
    }

    private static IReadOnlyList<SpriteInspectionFrame> GeneratedFrames(NormalizedMediaDescriptor descriptor, IReadOnlyList<DungeonMediaFrameLayout>? layouts, bool includeSourceWorldSize = false) =>
        Frames(descriptor with { Frames = descriptor.GeneratedFrames ?? descriptor.Frames }, layouts, includeSourceWorldSize);

    private static SpriteAuthoredValues Authored(NormalizedMediaDescriptor descriptor) => new(
        descriptor.DisplayName, Vector(descriptor.Pivot), Vector(descriptor.DisplaySize), descriptor.FramesPerSecond, descriptor.Loop, descriptor.Sequence?.ToArray());

    private static IReadOnlyList<SpriteSourceSequenceStep> Sequence(IReadOnlyList<sbyte> source) => source
        .Select(value => new SpriteSourceSequenceStep(value, value == -1))
        .ToArray();

    private static NormalizedMediaDescriptor RequireDescriptor(NormalizedMediaManifest manifest, string id, string subject) => manifest.Resources.SingleOrDefault(resource => StringComparer.Ordinal.Equals(resource.Id, id))
        ?? throw new FormatException($"The {subject} references unknown media '{id}'.");

    private static SpriteContentDigest Digest(ContentDigest digest) => new(digest.Value);

    private static SpriteVector2? Vector(NormalizedVector2? value) => value is { } vector ? new(vector.X, vector.Y) : null;
}

/// <summary>Converts a validated authored sprite overlay into the normalized-media input seam a regeneration consumes.</summary>
public static class SpriteAuthoredMediaOverlays
{
    public static IReadOnlyList<AuthoredMediaOverlay> ToMediaOverlays(SpriteAuthoredOverlayDocument document, SpriteInspectionCatalog catalog, SpriteContentDigest authoringBasisDigest)
    {
        SpriteAuthoredOverlayStore.Validate(document, catalog, authoringBasisDigest);
        return document.Overlays.Select(overlay => new AuthoredMediaOverlay(
            overlay.Id, true, overlay.DisplayName, Vector(overlay.Pivot), Vector(overlay.DisplaySize), overlay.FramesPerSecond, overlay.Loop, overlay.Sequence?.ToArray(),
            overlay.FrameRects?.Select(rect => new AuthoredMediaFrameRect(rect.FrameIndex, rect.X, rect.Y, rect.Width, rect.Height)).ToArray(),
            overlay.StateTimings?.Select(timing => new AuthoredMediaStateTiming(timing.Name, timing.FramesPerSecond, timing.Loop)).ToArray(),
            overlay.ActionTimings?.Select(timing => new AuthoredMediaActionTiming(timing.Name, timing.FramesPerSecond, timing.Loop)).ToArray())).ToArray();
    }

    private static NormalizedVector2? Vector(SpriteVector2? value) => value is { } vector ? new(vector.X, vector.Y) : null;
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

    /// <summary>
    /// Reads a generated publication from relative-path/byte entries, verifying
    /// every sidecar and media artifact against the publication manifest.
    /// </summary>
    public static SpritePublicationSnapshot Read(IReadOnlyList<SpritePublicationFile> files)
    {
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> source = Index(files);
        ReadOnlyMemory<byte> manifestBytes = Require(source, ImportPublicationManifestSerializer.ManifestRelativePath);
        CanonicalImportManifest manifest = Deserialize<CanonicalImportManifest>(manifestBytes.Span, "publication manifest");
        ReadOnlyMemory<byte> dungeonBytes = Require(source, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath);
        ReadOnlyMemory<byte> classicBytes = Require(source, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath);
        DungeonMediaManifestSidecar dungeon = Deserialize<DungeonMediaManifestSidecar>(dungeonBytes.Span, "dungeon sprite sidecar");
        ClassicMediaManifestSidecar classic = Deserialize<ClassicMediaManifestSidecar>(classicBytes.Span, "classic sprite sidecar");
        SpriteInspectionCatalog catalog;
        try
        {
            manifest.Validate();
            ValidateManifestArtifact(manifest, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath, dungeonBytes.Span);
            ValidateManifestArtifact(manifest, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, classicBytes.Span);
            catalog = SpriteInspectionCatalogBuilder.Create(manifest, dungeon, classic);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        {
            throw new FormatException("The sprite publication metadata violates the canonical contract.", exception);
        }

        VerifyMediaArtifacts(source, [.. dungeon.Media.Resources, .. classic.Media.Resources]);
        return new(catalog, SpriteAuthoringBasis.Compute(manifest, catalog), manifest);
    }

    /// <summary>Reads a generated publication from the filesystem for offline Import tooling.</summary>
    public static SpritePublicationSnapshot Read(string publicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationDirectory);
        byte[] manifestBytes = ReadRequired(publicationDirectory, ImportPublicationManifestSerializer.ManifestRelativePath);
        byte[] dungeonBytes = ReadRequired(publicationDirectory, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath);
        byte[] classicBytes = ReadRequired(publicationDirectory, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath);
        CanonicalImportManifest manifest = Deserialize<CanonicalImportManifest>(manifestBytes, "publication manifest");
        DungeonMediaManifestSidecar dungeon = Deserialize<DungeonMediaManifestSidecar>(dungeonBytes, "dungeon sprite sidecar");
        ClassicMediaManifestSidecar classic = Deserialize<ClassicMediaManifestSidecar>(classicBytes, "classic sprite sidecar");
        try
        {
            manifest.Validate();
            ValidateManifestArtifact(manifest, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath, dungeonBytes);
            ValidateManifestArtifact(manifest, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, classicBytes);
            _ = SpriteInspectionCatalogBuilder.Create(manifest, dungeon, classic);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        {
            throw new FormatException("The sprite publication metadata violates the canonical contract.", exception);
        }
        string[] paths = [
            ImportPublicationManifestSerializer.ManifestRelativePath,
            Arena2MediaBundlePublication.DungeonMediaManifestRelativePath,
            Arena2MediaBundlePublication.ClassicMediaManifestRelativePath,
            .. dungeon.Media.Resources.Select(resource => resource.RelativePath),
            .. classic.Media.Resources.Select(resource => resource.RelativePath),
        ];
        return Read(paths.Distinct(StringComparer.Ordinal)
            .Select(path => new SpritePublicationFile(path, ReadRequired(publicationDirectory, path)))
            .ToArray());
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

    private static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Index(IReadOnlyList<SpritePublicationFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Dictionary<string, ReadOnlyMemory<byte>> source = new(StringComparer.Ordinal);
        try
        {
            foreach (SpritePublicationFile file in files)
            {
                ArgumentNullException.ThrowIfNull(file);
                NormalizedImportDocument.RequireLogicalPath(file.RelativePath, nameof(file.RelativePath));
                if (!source.TryAdd(file.RelativePath, file.Bytes))
                {
                    throw new FormatException($"The sprite publication contains duplicate path '{file.RelativePath}'.");
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            throw new FormatException("The sprite publication contains an invalid path.", exception);
        }

        return source;
    }

    private static ReadOnlyMemory<byte> Require(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> source, string relativePath)
    {
        if (!source.TryGetValue(relativePath, out ReadOnlyMemory<byte> bytes)
            || bytes.Length is <= 0 or > MaximumSidecarBytes)
        {
            throw new FormatException($"Required sprite publication file '{relativePath}' is missing or outside its byte quota.");
        }

        return bytes;
    }

    private static void VerifyMediaArtifacts(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> source, IReadOnlyList<NormalizedMediaDescriptor> descriptors)
    {
        long total = 0;
        foreach (NormalizedMediaDescriptor descriptor in descriptors)
        {
            if (descriptor.ByteLength > MaximumMediaArtifactBytes || (total = checked(total + descriptor.ByteLength)) > MaximumMediaArtifactTotalBytes)
            {
                throw new FormatException("Sprite publication media exceeds the explicit artifact verification quota.");
            }

            if (!source.TryGetValue(descriptor.RelativePath, out ReadOnlyMemory<byte> bytes)
                || bytes.Length != descriptor.ByteLength)
            {
                throw new FormatException($"Published media artifact '{descriptor.RelativePath}' is missing or has the wrong length.");
            }

            if (ContentDigest.Compute(bytes.Span) != descriptor.ContentDigest)
            {
                throw new FormatException($"Published media artifact '{descriptor.RelativePath}' does not match its descriptor digest.");
            }
        }
    }
}

/// <summary>One immutable relative-path/byte entry from a generated publication.</summary>
public sealed record SpritePublicationFile(string RelativePath, ReadOnlyMemory<byte> Bytes);

/// <summary>One validated inspection catalog and the digest that guards its authored overlay.</summary>
public sealed record SpritePublicationSnapshot(SpriteInspectionCatalog Catalog, SpriteContentDigest AuthoringBasisDigest, CanonicalImportManifest Manifest)
{
    /// <summary>The product-neutral inspection document an authoring tool consumes instead of this publication's sidecars.</summary>
    public SpriteInspectionDocument ToInspectionDocument() => new(AuthoringBasisDigest, Catalog);
}
