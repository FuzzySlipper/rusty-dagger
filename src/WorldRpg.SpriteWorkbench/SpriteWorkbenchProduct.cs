using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.SpriteWorkbench;

/// <summary>Development-only Engine product for inspecting and authoring normalized sprite overlays.</summary>
public sealed class SpriteWorkbenchProduct : IEngineProduct
{
    private readonly IEngineContext engine;
    private readonly SpriteWorkbenchConfiguration configuration;
    private readonly SpritePublicationSnapshot publication;
    private readonly UiStream ui;
    private readonly Dictionary<string, Preview> previews = new(StringComparer.Ordinal);
    private Dictionary<string, SpriteAuthoredOverlay> savedOverlays = new(StringComparer.Ordinal);
    private string? selectedId;
    private string? selectedSequence;
    private int selectedOrientation;
    private bool running;
    private bool stepRequested;
    private bool shutdown;
    private ulong uiSequence;
    private SpriteAuthoredOverlay? pendingEdit;
    private string? diagnostic;
    private string saveStatus = "idle";
    private const long MaximumOverlayBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public SpriteWorkbenchProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        engine = context.Engine;
        AdmittedWorkbenchContent admitted = ReadAdmittedContent(context.Content);
        configuration = admitted.Configuration;
        publication = admitted.Publication;
        savedOverlays = LoadSavedOverlays();
        ui = engine.Ui.OpenStream(new UiStreamRequest("worldrpg.sprite-workbench", "worldrpg.sprite-workbench.snapshot.v1"));
        try
        {
            foreach (SpriteInspectionEntry entry in publication.Catalog.Entries.OrderBy(value => value.Id, StringComparer.Ordinal))
                previews.Add(entry.Id, CreatePreview(entry));
            PublishState();
        }
        catch (Exception creationError)
        {
            List<Exception> cleanupFailures = [];
            TryDisposePreviews(cleanupFailures);
            TryDispose(ui, cleanupFailures);
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Sprite workbench construction failed and cleanup reported one or more errors.", [creationError, ..cleanupFailures]);
            }

            throw;
        }
    }

    public SpriteWorkbenchSnapshot Snapshot()
    {
        SpritePlaybackReadout? playback = CurrentPlayback() is { } active ? engine.Appearance.ReadSpritePlayback(active) : null;
        return new(publication.Catalog.Entries, selectedId, selectedSequence, selectedOrientation, playback, pendingEdit, running);
    }

    public void Start() { ThrowIfShutdown(); PublishState(); }
    public void Attach() { ThrowIfShutdown(); PublishState(); }
    public void Pause() { ThrowIfShutdown(); ControlLifecycle(SpritePlaybackControl.Pause, false); }
    public void Resume() { ThrowIfShutdown(); ControlLifecycle(SpritePlaybackControl.Resume, true); }
    public void Restart() { ThrowIfShutdown(); ControlLifecycle(SpritePlaybackControl.Restart, true); }
    public void Shutdown()
    {
        if (shutdown) return;
        shutdown = true;
        List<Exception> cleanupFailures = [];
        TryPublishEmptySnapshot(cleanupFailures);
        TryDisposePreviews(cleanupFailures);
        TryDispose(ui, cleanupFailures);
        if (cleanupFailures.Count > 0) throw new AggregateException("Sprite workbench cleanup reported one or more errors.", cleanupFailures);
    }
    public void Dispose() => Shutdown();

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (shutdown) return ProductUpdateResult.None;
        foreach (ProductInputEvent input in update.Input)
        {
            if (input.ValueKind != InputValueKind.ProductPayload || !input.PayloadContract.Span.SequenceEqual("worldrpg.sprite-workbench.intent.v1"u8)) continue;
            try
            {
                Apply(SpriteWorkbenchIntent.Parse(input.PayloadData.Span));
                diagnostic = null;
            }
            // Browser payloads are untrusted product input.  A bad selection must remain a
            // visible workbench diagnostic rather than escaping the admitted update.
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                diagnostic = error.Message.Length <= 512 ? error.Message : error.Message[..512];
            }
        }
        if (CurrentPlayback() is { } playback && (running || stepRequested))
        {
            if (stepRequested)
            {
                SpritePlaybackState state = engine.Appearance.ReadSpritePlayback(playback).State;
                if (state == SpritePlaybackState.Stopped)
                    engine.Appearance.ControlSpritePlayback(new(playback, SpritePlaybackControl.Start));
                else if (state == SpritePlaybackState.Paused)
                    engine.Appearance.ControlSpritePlayback(new(playback, SpritePlaybackControl.Resume));
            }
            SpritePlaybackAdvanceLeaseReceipt receipt = engine.Appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(playback));
            // A one-shot can complete during this admitted advance.  Completion is
            // already a coherent terminal Engine state, so do not issue an invalid
            // or misleading Pause after it.
            if (stepRequested && !receipt.Readout.Completed)
                engine.Appearance.ControlSpritePlayback(new(playback, SpritePlaybackControl.Pause));
            stepRequested = false;
        }
        PublishState();
        return ProductUpdateResult.None;
    }

    private void Apply(SpriteWorkbenchIntent intent)
    {
        switch (intent.Action)
        {
            case "select": Select(intent); break;
            case "select-frame": SelectFrame(intent); break;
            case "play": Control(SpritePlaybackControl.Start, true); break;
            case "pause": Control(SpritePlaybackControl.Pause, false); break;
            case "resume": Control(SpritePlaybackControl.Resume, true); break;
            case "restart": Control(SpritePlaybackControl.Restart, true); break;
            case "stop": Control(SpritePlaybackControl.Stop, false); break;
            case "step": RequireCurrent(intent); stepRequested = true; running = false; break;
            case "sample": Sample(intent); break;
            case "edit": Edit(intent); break;
            case "save": Save(intent); break;
            case "discard": Discard(intent); break;
            default: throw new FormatException($"Unknown sprite workbench action '{intent.Action}'.");
        }
    }

    private void Select(SpriteWorkbenchIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.Id) || !previews.TryGetValue(intent.Id, out Preview? preview))
            throw new FormatException("The selected sprite does not exist.");
        if (intent.Orientation is < 0 || intent.Orientation >= preview.OrientationCount) throw new ArgumentOutOfRangeException(nameof(intent.Orientation));
        int requestedOrientation = intent.Orientation ?? 0;
        // Resolve every product-level selection fact before changing the
        // previously visible Engine playback or local selection state.
        PreviewSequence selected = preview.ResolveSequence(intent.Sequence, requestedOrientation);
        if (selected.FrameIds.Length == 0) throw new InvalidOperationException("The selected sprite sequence has no frames.");
        if (CurrentPlayback() is { } prior && engine.Appearance.ReadSpritePlayback(prior).State != SpritePlaybackState.Stopped)
            engine.Appearance.ControlSpritePlayback(new(prior, SpritePlaybackControl.Stop));
        engine.Appearance.ControlSpritePlayback(new(selected.Playback!, SpritePlaybackControl.Restart));
        engine.Appearance.ControlSpritePlayback(new(selected.Playback!, SpritePlaybackControl.Pause));
        selectedId = preview.Entry.Id;
        selectedOrientation = requestedOrientation;
        selectedSequence = selected.Name;
        pendingEdit = null;
        running = false;
        saveStatus = "selection changed";
    }

    private void SelectFrame(SpriteWorkbenchIntent intent)
    {
        Preview preview = RequireCurrent(intent);
        PreviewSequence sequence = RequireSelectedSequence(preview);
        if (intent.SequenceFrameIndex is null || intent.SequenceFrameIndex.Value < 0 || intent.SequenceFrameIndex.Value >= sequence.FrameIds.Length)
            throw new ArgumentOutOfRangeException(nameof(intent.SequenceFrameIndex), "The selected sequence entry is outside the active playback sequence.");
        uint sequenceIndex = checked((uint)intent.SequenceFrameIndex.Value);
        SpritePlayback playback = sequence.Playback ?? throw new InvalidOperationException("The selected Engine playback is unavailable.");
        if (engine.Appearance.ReadSpritePlayback(playback).State == SpritePlaybackState.Playing)
            engine.Appearance.ControlSpritePlayback(new(playback, SpritePlaybackControl.Pause));
        engine.Appearance.SelectSpritePlaybackFrame(new(playback, sequenceIndex));
        running = false;
    }

    private void Control(SpritePlaybackControl control, bool shouldRun)
    {
        SpritePlayback playback = RequirePlayback();
        engine.Appearance.ControlSpritePlayback(new(playback, control));
        running = shouldRun;
    }

    private void ControlLifecycle(SpritePlaybackControl control, bool shouldRun)
    {
        if (CurrentPlayback() is { } playback)
        {
            SpritePlaybackState state = engine.Appearance.ReadSpritePlayback(playback).State;
            if (control == SpritePlaybackControl.Pause && state == SpritePlaybackState.Playing || control == SpritePlaybackControl.Resume && state == SpritePlaybackState.Paused || control == SpritePlaybackControl.Restart)
                engine.Appearance.ControlSpritePlayback(new(playback, control));
        }
        running = shouldRun;
    }

    private void Sample(SpriteWorkbenchIntent intent)
    {
        if (intent.ElapsedSeconds is not { } elapsed || !double.IsFinite(elapsed) || elapsed < 0D) throw new ArgumentOutOfRangeException(nameof(intent.ElapsedSeconds));
        engine.Appearance.SampleSpritePlayback(new(RequirePlayback(), elapsed));
    }

    private void Edit(SpriteWorkbenchIntent intent)
    {
        Preview preview = RequireCurrent(intent);
        SpriteAuthoredOverlay candidate = new(preview.Entry.Id, intent.DisplayName,
            intent.PivotX is null && intent.PivotY is null ? null : new NormalizedVector2(RequiredFinite(intent.PivotX), RequiredFinite(intent.PivotY)),
            intent.DisplaySizeX is null && intent.DisplaySizeY is null ? null : new NormalizedVector2(RequiredFinite(intent.DisplaySizeX), RequiredFinite(intent.DisplaySizeY)),
            intent.FramesPerSecond, intent.Loop, intent.FrameSequence);
        SpriteAuthoredOverlayStore.Validate(new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, publication.AuthoringBasisDigest, [candidate]), publication.Catalog, publication.AuthoringBasisDigest);
        pendingEdit = candidate;
    }

    private void Save(SpriteWorkbenchIntent intent)
    {
        RequireCurrent(intent);
        if (pendingEdit is null) throw new InvalidOperationException("There is no selected authored sprite edit to save.");
        Dictionary<string, SpriteAuthoredOverlay> replacement = new(savedOverlays, StringComparer.Ordinal)
        {
            [pendingEdit.Id] = pendingEdit,
        };
        SpriteAuthoredOverlayStore.Write(configuration.PublicationSeparationRoot, configuration.AuthoringRoot, configuration.OverlayPath,
            new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, publication.AuthoringBasisDigest, replacement.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray()), publication.Catalog, publication.AuthoringBasisDigest);
        savedOverlays = replacement;
        pendingEdit = null;
        saveStatus = "saved";
    }

    private void Discard(SpriteWorkbenchIntent intent)
    {
        RequireCurrent(intent);
        pendingEdit = null;
        // Discard is intentionally local: the validated session cache remains the
        // saved baseline; it never polls, archives, or deletes the authored file.
        saveStatus = "unsaved edit discarded";
    }

    private Preview CreatePreview(SpriteInspectionEntry entry)
    {
        VerifyAdmittedContent(entry);
        RenderResourceInfo resource = engine.Appearance.OpenResource(new(entry.Closure.RelativePath));
        SpriteAtlas atlas = engine.Appearance.CreateSpriteAtlas(new(resource.Handle, SpriteAtlasAdapter.ToAtlasFrames(entry.Atlas.Width, entry.Atlas.Height,
            entry.Frames.Select(frame => new WorldRpg.Kit.Presentation.NormalizedSpriteFrame(checked((uint)frame.FrameIndex), frame.X, frame.Y, frame.Width, frame.Height)).ToArray())));
        Appearance? appearance = null;
        List<SpritePlayback> playbacks = [];
        try
        {
            uint initial = checked((uint)entry.Frames.First().FrameIndex);
            appearance = engine.Appearance.CreateSpriteFromAtlas(new(atlas, initial, ToVector(entry.AuthoredValues.Pivot), ToVector(entry.AuthoredValues.DisplaySize, Vector2.One), BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F)));
            List<PreviewSequence> sequences = BuildSequences(entry);
            foreach (PreviewSequence sequence in sequences)
            {
                SpritePlayback playback = engine.Appearance.CreateSpritePlayback(new(appearance, atlas,
                    SpriteAtlasAdapter.ToPlaybackFrames(sequence.FrameIds, sequence.FramesPerSecond), Array.Empty<SpritePlaybackMarker>(),
                    sequence.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1D));
                playbacks.Add(playback);
                sequence.Playback = playback;
            }
            return new(entry, atlas, appearance, sequences, entry.Frames.Select(frame => checked((uint)frame.FrameIndex)).ToHashSet());
        }
        catch (Exception creationError)
        {
            // Failed Create calls can return a wrapper whose native side was never
            // committed.  Preserve the original admission failure while making a
            // best-effort cleanup of every prior fully-created object.
            List<Exception> cleanupFailures = [];
            foreach (SpritePlayback playback in playbacks.AsEnumerable().Reverse()) TryDispose(playback, cleanupFailures);
            if (appearance is not null) TryDispose(appearance, cleanupFailures);
            TryDispose(atlas, cleanupFailures);
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Sprite preview construction failed and cleanup reported one or more errors.", [creationError, ..cleanupFailures]);
            }

            throw;
        }
    }

    private static List<PreviewSequence> BuildSequences(SpriteInspectionEntry entry)
    {
        List<PreviewSequence> values = [];
        foreach (SpriteInspectionState state in entry.States)
        {
            int count = Math.Max(1, state.FramesPerOrientation);
            int orientations = Math.Max(1, state.FrameIndices.Count / count);
            for (int orientation = 0; orientation < orientations; orientation++)
            {
                uint[] frames = state.FrameIndices.Skip(orientation * count).Take(count).Select(index => checked((uint)index)).ToArray();
                if (frames.Length > 0) values.Add(new($"state:{state.Name}@{orientation}", frames, state.FramesPerSecond, state.Loops, orientation));
            }
        }
        foreach (SpriteInspectionAction action in entry.Actions.Where(action => action.FramesPerSecond is not null && action.FrameIndices.Count > 0))
            values.Add(new($"action:{action.Name}", action.FrameIndices.Select(index => checked((uint)index)).ToArray(), action.FramesPerSecond!.Value, action.Loops ?? false, 0));
        if (values.Count == 0)
            values.Add(new("frame", entry.Frames.Select(frame => checked((uint)frame.FrameIndex)).ToArray(), 1D, true, 0));
        return values;
    }

    private void VerifyAdmittedContent(SpriteInspectionEntry entry)
    {
        using ContentReference reference = engine.Content.OpenReference(new(entry.Closure.RelativePath));
        ReadOnlySpan<ContentReferenceInfo> readout = engine.Content.ReadReferenceInfo(reference).Span;
        if (readout.Length != 1) throw new InvalidOperationException($"Engine content admission did not return one record for '{entry.Id}'.");
        ContentReferenceInfo info = readout[0];
        if (info.Path != entry.Closure.RelativePath || info.ByteLength != (ulong)entry.Closure.ByteLength || info.Sha256 != ToEngineDigest(entry.Closure.ContentDigest))
            throw new InvalidOperationException($"Engine content admission does not match normalized sprite '{entry.Id}'.");
    }

    private void PublishProjection()
    {
        UiValueBuilder builder = new();
        uint[] entries = publication.Catalog.Entries.Select(entry => builder.Object(
            ("id", builder.String(entry.Id)),
            ("label", builder.String(entry.Label)),
            ("kind", builder.String(entry.Kind.ToString())),
            ("frameCount", builder.Number(entry.Frames.Count)),
            ("stateCount", builder.Number(entry.States.Count)),
            ("actionCount", builder.Number(entry.Actions.Count)))).ToArray();
        SpritePlaybackReadout? playback = CurrentPlayback() is { } active ? engine.Appearance.ReadSpritePlayback(active) : null;
        Preview? preview = selectedId is not null && previews.TryGetValue(selectedId, out Preview? found) ? found : null;
        uint? selected = preview is null ? null : BuildSelectedProjection(builder, preview, playback);
        uint root = builder.Object(
            ("entries", builder.Array(entries)),
            ("selectedId", selectedId is null ? builder.Null() : builder.String(selectedId)),
            ("selectedSequence", selectedSequence is null ? builder.Null() : builder.String(selectedSequence)),
            ("selectedOrientation", builder.Number(selectedOrientation)),
            ("playing", builder.Number(playback?.State == SpritePlaybackState.Playing ? 1D : 0D)),
            ("frameIndex", playback is null ? builder.Null() : builder.Number(playback.Value.FrameIndex)),
            ("frameId", playback is null ? builder.Null() : builder.Number(playback.Value.FrameId)),
            ("cycle", playback is null ? builder.Null() : builder.Number(playback.Value.Cycle)),
            ("diagnostic", diagnostic is null ? builder.Null() : builder.String(diagnostic)),
            ("saveStatus", builder.String(saveStatus)),
            ("selected", selected ?? builder.Null()));
        engine.Ui.PublishProjection(new UiProjection(ui, ++uiSequence, builder.Build(root)));
    }

    private void PublishState()
    {
        PublishAppearanceSnapshot();
        PublishProjection();
    }

    private void PublishAppearanceSnapshot()
    {
        AppearanceFact[] snapshot = selectedId is not null && previews.TryGetValue(selectedId, out Preview? preview)
            ? [new AppearanceFact(configuration.PreviewPlacement.EntityId, configuration.PreviewPlacement.ToTransform(), preview.Appearance,
                configuration.PreviewPlacement.Visible, configuration.PreviewPlacement.Layer)]
            : [];
        engine.Appearance.PublishSnapshot(snapshot);
    }

    private void TryPublishEmptySnapshot(ICollection<Exception> failures)
    {
        try { engine.Appearance.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty); }
        catch (Exception error) { failures.Add(error); }
    }

    private uint BuildSelectedProjection(UiValueBuilder builder, Preview preview, SpritePlaybackReadout? playback)
    {
        SpriteInspectionEntry entry = preview.Entry;
        uint[] frames = entry.Frames.Select(frame => builder.Object(
            ("id", builder.String(frame.Id)), ("frameIndex", builder.Number(frame.FrameIndex)),
            ("x", builder.Number(frame.X)), ("y", builder.Number(frame.Y)), ("width", builder.Number(frame.Width)), ("height", builder.Number(frame.Height)),
            ("sourceWidth", builder.Number(frame.SourceWidth)), ("sourceHeight", builder.Number(frame.SourceHeight)), ("mirrored", builder.Number(frame.Mirrored ? 1 : 0)),
            ("sourceRecord", frame.SourceRecord is null ? builder.Null() : builder.Number(frame.SourceRecord.Value)),
            ("sourceFrame", frame.SourceFrame is null ? builder.Null() : builder.Number(frame.SourceFrame.Value)),
            ("orientation", frame.Orientation is null ? builder.Null() : builder.Number(frame.Orientation.Value)))).ToArray();
        uint[] states = entry.States.Select(state => builder.Object(
            ("name", builder.String(state.Name)), ("sourceFps", builder.Number(state.SourceFramesPerSecond)), ("fps", builder.Number(state.FramesPerSecond)),
            ("loop", builder.Number(state.Loops ? 1 : 0)), ("frameStart", builder.Number(state.FrameStart)), ("framesPerOrientation", builder.Number(state.FramesPerOrientation)),
            ("frameIndices", builder.Array(state.FrameIndices.Select(value => builder.Number(value)).ToArray())), ("preferredRest", builder.Number(state.IsPreferredRest ? 1 : 0)))).ToArray();
        uint[] actions = entry.Actions.Select(action => builder.Object(
            ("name", builder.String(action.Name)), ("fps", action.FramesPerSecond is null ? builder.Null() : builder.Number(action.FramesPerSecond.Value)),
            ("loop", action.Loops is null ? builder.Null() : builder.Number(action.Loops.Value ? 1 : 0)),
            ("frameIndices", builder.Array(action.FrameIndices.Select(value => builder.Number(value)).ToArray())),
            ("sourceSequence", builder.Array((action.SourceSequence ?? []).Select(step => builder.Object(("value", builder.Number(step.Value)), ("damageMarker", builder.Number(step.IsDamageMarker ? 1 : 0)))).ToArray())),
            ("alternateChance", action.AlternateChance is null ? builder.Null() : builder.Number(action.AlternateChance.Value)),
            ("sourceFps", action.SourceFramesPerSecond is null ? builder.Null() : builder.Number(action.SourceFramesPerSecond.Value)),
            ("sourceRecordOrdinal", action.SourceRecordOrdinal is null ? builder.Null() : builder.Number(action.SourceRecordOrdinal.Value)))).ToArray();
        uint[] sequences = preview.Sequences.Select(sequence => builder.Object(
            ("name", builder.String(sequence.Name)), ("orientation", builder.Number(sequence.Orientation)), ("fps", builder.Number(sequence.FramesPerSecond)),
            ("loop", builder.Number(sequence.Loops ? 1 : 0)), ("frameIds", builder.Array(sequence.FrameIds.Select(value => builder.Number(value)).ToArray())))).ToArray();
        PreviewSequence? activeSequence = preview.FindSequence(selectedSequence);
        IReadOnlyList<PreviewSequence> controlSequences = activeSequence is null ? [] : preview.ControlSiblings(activeSequence);
        uint[] controlFrames = activeSequence?.FrameIds ?? [];
        int[] controlOrientations = controlSequences.Select(sequence => sequence.Orientation).Distinct().Order().ToArray();
        return builder.Object(
            ("id", builder.String(entry.Id)), ("label", builder.String(entry.Label)), ("kind", builder.String(entry.Kind.ToString())),
            ("provenance", builder.Object(("path", builder.String(entry.Closure.RelativePath)), ("digest", builder.String(entry.Closure.ContentDigest.Value)), ("byteLength", builder.Number(entry.Closure.ByteLength)),
                ("dependsOnPaths", builder.Array(entry.Closure.DependsOnPaths.Select(value => builder.String(value)).ToArray())),
                ("sources", builder.Array(entry.Closure.PublicationSources.Select(source => builder.Object(("path", builder.String(source.SourcePath)), ("digest", builder.String(source.ContentHash.Value)), ("byteLength", builder.Number(source.ByteLen)))).ToArray())))),
            ("atlas", builder.Object(("width", builder.Number(entry.Atlas.Width)), ("height", builder.Number(entry.Atlas.Height)))),
            ("frames", builder.Array(frames)), ("states", builder.Array(states)), ("actions", builder.Array(actions)), ("availableSequences", builder.Array(sequences)),
            // These arrays are control affordances, unlike the complete inspection
            // facts above: every value must be legal for the active sequence.
            ("availableOrientations", builder.Array(controlOrientations.Select(value => builder.Number(value)).ToArray())),
            ("availableSequenceFrames", builder.Array(controlFrames.Select((frameId, sequenceIndex) => builder.Object(
                ("sequenceIndex", builder.Number(sequenceIndex)), ("frameId", builder.Number(frameId)))).ToArray())),
            ("authored", BuildOverlay(builder, entry.AuthoredValues.DisplayName, entry.AuthoredValues.Pivot, entry.AuthoredValues.DisplaySize, entry.AuthoredValues.FramesPerSecond, entry.AuthoredValues.Loop, entry.AuthoredValues.Sequence)),
            ("saved", savedOverlays.TryGetValue(entry.Id, out SpriteAuthoredOverlay? overlay) ? BuildOverlay(builder, overlay.DisplayName, overlay.Pivot, overlay.DisplaySize, overlay.FramesPerSecond, overlay.Loop, overlay.Sequence) : builder.Null()),
            ("pending", pendingEdit is { } pending ? BuildOverlay(builder, pending.DisplayName, pending.Pivot, pending.DisplaySize, pending.FramesPerSecond, pending.Loop, pending.Sequence) : builder.Null()),
            ("playback", playback is null ? builder.Null() : builder.Object(("state", builder.String(playback.Value.State.ToString())), ("frameIndex", builder.Number(playback.Value.FrameIndex)), ("frameId", builder.Number(playback.Value.FrameId)), ("cycle", builder.Number(playback.Value.Cycle)))));
    }

    private static uint BuildOverlay(UiValueBuilder builder, string? displayName, NormalizedVector2? pivot, NormalizedVector2? displaySize, float? fps, bool? loop, IReadOnlyList<int>? sequence) => builder.Object(
        ("displayName", displayName is null ? builder.Null() : builder.String(displayName)),
        ("pivotX", pivot is null ? builder.Null() : builder.Number(pivot.Value.X)), ("pivotY", pivot is null ? builder.Null() : builder.Number(pivot.Value.Y)),
        ("displaySizeX", displaySize is null ? builder.Null() : builder.Number(displaySize.Value.X)), ("displaySizeY", displaySize is null ? builder.Null() : builder.Number(displaySize.Value.Y)),
        ("fps", fps is null ? builder.Null() : builder.Number(fps.Value)), ("loop", loop is null ? builder.Null() : builder.Number(loop.Value ? 1 : 0)),
        ("frameSequence", builder.Array((sequence ?? []).Select(value => builder.Number(value)).ToArray())));

    private static ContentSha256 ToEngineDigest(ContentDigest digest)
    {
        byte[] bytes = Convert.FromHexString(digest.Value);
        return new(BinaryPrimitives.ReadUInt64BigEndian(bytes), BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8)), BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(16)), BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(24)));
    }

    private Dictionary<string, SpriteAuthoredOverlay> LoadSavedOverlays()
    {
        string path = SpriteAuthoredOverlayStore.ResolveRelativePath(configuration.AuthoringRoot, configuration.OverlayPath);
        FileInfo info = new(path);
        if (!info.Exists) return new(StringComparer.Ordinal);
        if (info.Length < 1 || info.Length > MaximumOverlayBytes) throw new FormatException("The authored overlay file is empty or exceeds its byte quota.");
        SpriteAuthoredOverlayDocument document = SpriteAuthoredOverlayStore.Read(File.ReadAllBytes(info.FullName));
        SpriteAuthoredOverlayStore.Validate(document, publication.Catalog, publication.AuthoringBasisDigest);
        return document.Overlays.ToDictionary(value => value.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// Captures the complete immutable publication from Engine product content
    /// before construction.  No configuration path is permitted to redirect
    /// publication parsing to a mutable external filesystem location.
    /// </summary>
    private static AdmittedWorkbenchContent ReadAdmittedContent(ProductContent content)
    {
        Dictionary<string, byte[]> filesByPath = new(StringComparer.Ordinal);
        byte[]? configurationBytes = null;
        foreach (ProductContentFile file in content.Files.Span)
        {
            string path;
            try
            {
                path = StrictUtf8.GetString(file.Path.Span);
            }
            catch (DecoderFallbackException error)
            {
                throw new FormatException("Engine content contains an invalid sprite workbench path.", error);
            }

            if (!filesByPath.TryAdd(path, file.Bytes.ToArray()))
            {
                throw new FormatException($"Engine content contains duplicate sprite workbench path '{path}'.");
            }

            if (StringComparer.Ordinal.Equals(path, SpriteWorkbenchConfiguration.ContentPath))
            {
                configurationBytes = filesByPath[path];
            }
        }

        if (configurationBytes is null)
        {
            throw new FormatException($"Engine content must contain '{SpriteWorkbenchConfiguration.ContentPath}'.");
        }

        SpriteWorkbenchConfiguration value = SpriteWorkbenchConfiguration.Read(configurationBytes);
        SpritePublicationFile[] publicationFiles = filesByPath
            .Where(pair => !StringComparer.Ordinal.Equals(pair.Key, SpriteWorkbenchConfiguration.ContentPath))
            .Select(pair => new SpritePublicationFile(pair.Key, pair.Value))
            .ToArray();
        return new(value, SpritePublicationReader.Read(publicationFiles));
    }

    private Preview RequireCurrent(SpriteWorkbenchIntent intent)
    {
        if (selectedId is null || intent.Id is null || !StringComparer.Ordinal.Equals(selectedId, intent.Id) || !previews.TryGetValue(selectedId, out Preview? preview))
            throw new InvalidOperationException("The semantic intent does not match the current sprite selection.");
        return preview;
    }

    private SpritePlayback RequirePlayback()
    {
        if (selectedId is null || selectedSequence is null || !previews.TryGetValue(selectedId, out Preview? preview)) throw new InvalidOperationException("No sprite playback is selected.");
        return RequireSelectedSequence(preview).Playback ?? throw new InvalidOperationException("The selected Engine playback is unavailable.");
    }

    private PreviewSequence RequireSelectedSequence(Preview preview)
    {
        if (selectedSequence is null) throw new InvalidOperationException("No sprite playback is selected.");
        return preview.Sequences.Single(sequence => sequence.Name == selectedSequence);
    }

    private SpritePlayback? CurrentPlayback() => selectedId is not null && selectedSequence is not null && previews.TryGetValue(selectedId, out Preview? preview)
        ? preview.Sequences.Single(sequence => sequence.Name == selectedSequence).Playback : null;
    private void ThrowIfShutdown() { if (shutdown) throw new ObjectDisposedException(nameof(SpriteWorkbenchProduct)); }
    private static float RequiredFinite(float? value) => value is { } scalar && float.IsFinite(scalar) ? scalar : throw new FormatException("A paired vector edit requires finite values.");
    private static Vector2 ToVector(NormalizedVector2? value, Vector2 fallback = default) => value is { } vector ? new(vector.X, vector.Y) : fallback;
    private void TryDisposePreviews(ICollection<Exception> failures)
    {
        foreach (Preview preview in previews.Values.Reverse()) preview.Dispose(failures);
        previews.Clear();
    }

    private static void TryDispose(IDisposable value, ICollection<Exception> failures)
    {
        try { value.Dispose(); }
        catch (Exception error) { failures.Add(error); }
    }

    private sealed class Preview
    {
        internal Preview(SpriteInspectionEntry entry, SpriteAtlas atlas, Appearance appearance, List<PreviewSequence> sequences, HashSet<uint> frameIds)
        {
            Entry = entry;
            Atlas = atlas;
            Appearance = appearance;
            Sequences = sequences;
            FrameIds = frameIds;
        }
        internal SpriteInspectionEntry Entry { get; }
        internal SpriteAtlas Atlas { get; }
        internal Appearance Appearance { get; }
        internal List<PreviewSequence> Sequences { get; }
        internal HashSet<uint> FrameIds { get; }
        internal int OrientationCount => Math.Max(1, Sequences.Max(sequence => sequence.Orientation) + 1);
        internal PreviewSequence? FindSequence(string? name) => name is null ? null : Sequences.SingleOrDefault(sequence => sequence.Name == name);
        internal PreviewSequence ResolveSequence(string? requested, int orientation)
        {
            if (requested is null)
            {
                return Sequences.FirstOrDefault(sequence => sequence.Orientation == orientation)
                    ?? throw new FormatException("The selected sprite sequence does not exist for the requested orientation.");
            }

            // The UI keeps the current named state while selecting another
            // orientation.  State names carry their orientation as a suffix;
            // select the sibling rather than silently retaining the old one.
            int suffix = requested.LastIndexOf('@');
            string candidate = suffix >= 0 ? $"{requested[..suffix]}@{orientation}" : requested;
            return Sequences.FirstOrDefault(sequence => sequence.Name == candidate && sequence.Orientation == orientation)
                ?? throw new FormatException("The selected sprite sequence does not exist for the requested orientation.");
        }

        internal IReadOnlyList<PreviewSequence> ControlSiblings(PreviewSequence active)
        {
            string? family = StateFamily(active.Name);
            if (family is null) return [active];
            return Sequences.Where(sequence => StringComparer.Ordinal.Equals(StateFamily(sequence.Name), family))
                .OrderBy(sequence => sequence.Orientation).ToArray();
        }

        private static string? StateFamily(string sequenceName)
        {
            const string prefix = "state:";
            if (!sequenceName.StartsWith(prefix, StringComparison.Ordinal)) return null;
            int suffix = sequenceName.LastIndexOf('@');
            if (suffix <= prefix.Length || suffix == sequenceName.Length - 1
                || !int.TryParse(sequenceName[(suffix + 1)..], out _)) return null;
            return sequenceName[..suffix];
        }
        internal void Dispose(ICollection<Exception> failures)
        {
            foreach (PreviewSequence sequence in Sequences.AsEnumerable().Reverse()) if (sequence.Playback is { } playback) TryDispose(playback, failures);
            TryDispose(Appearance, failures);
            TryDispose(Atlas, failures);
        }
    }

    private sealed class PreviewSequence(string name, uint[] frameIds, double framesPerSecond, bool loops, int orientation)
    {
        internal string Name { get; } = name;
        internal uint[] FrameIds { get; } = frameIds;
        internal double FramesPerSecond { get; } = framesPerSecond;
        internal bool Loops { get; } = loops;
        internal int Orientation { get; } = orientation;
        internal SpritePlayback? Playback { get; set; }
    }

    private sealed record AdmittedWorkbenchContent(SpriteWorkbenchConfiguration Configuration, SpritePublicationSnapshot Publication);
}

public sealed record SpriteWorkbenchSnapshot(IReadOnlyList<SpriteInspectionEntry> Entries, string? SelectedId, string? SelectedSequence,
    int SelectedOrientation, SpritePlaybackReadout? Playback, SpriteAuthoredOverlay? PendingEdit, bool IsPlaying);
