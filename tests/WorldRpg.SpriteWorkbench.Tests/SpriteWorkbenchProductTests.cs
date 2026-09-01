using System.Reflection;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Rusty.Engine;
using Xunit;

namespace WorldRpg.SpriteWorkbench.Tests;

public sealed class SpriteWorkbenchProductTests
{
    [Fact]
    public void Creation_verifies_content_preloads_engine_sprite_resources_and_publishes_projection()
    {
        using Harness harness = Harness.Create();

        Assert.Equal(harness.Publication.Catalog.Entries.Count, harness.Content.OpenRequests.Count);
        Assert.Equal(harness.Publication.Catalog.Entries.Count, harness.Content.ReadInfoRequests.Count);
        Assert.Equal(harness.Publication.Catalog.Entries.Count, harness.Appearance.OpenResourceRequests.Count);
        Assert.Equal(harness.Publication.Catalog.Entries.Count, harness.Appearance.AtlasRequests.Count);
        Assert.Equal(harness.Publication.Catalog.Entries.Count, harness.Appearance.SpriteRequests.Count);
        Assert.Equal(harness.Appearance.PlaybackRequests.Count, harness.Appearance.CreatedPlaybacks.Count);
        Assert.Equal(1, harness.Ui.OpenStreamCalls);
        Assert.Single(harness.Appearance.Snapshots);
        Assert.Empty(harness.Appearance.Snapshots[0]);

        UiProjection projection = Assert.Single(harness.Ui.Projections);
        Assert.Equal(1UL, projection.Sequence);
        Assert.Equal("worldrpg.sprite-workbench.snapshot.v1", harness.Ui.StreamRequest.Contract);
        Assert.Equal("sprite.actor", ValueReader.StringField(projection.Value, "entries", 0, "id"));
        Assert.Null(ValueReader.NullableStringField(projection.Value, "selectedId"));
        Assert.Empty(harness.Appearance.SetFrameRequests);

        harness.Product.Start();
        Assert.Equal(2UL, harness.Ui.LastProjection.Sequence);
        Assert.Equal(2, harness.Appearance.Snapshots.Count);
        Assert.Empty(harness.Appearance.Snapshots[^1]);
    }

    [Fact]
    public void Construction_uses_the_admitted_publication_snapshot_after_the_external_tree_changes()
    {
        using Harness harness = Harness.Create(new HarnessOptions(MutateExternalPublicationAfterAdmission: true));

        Assert.False(File.Exists(Path.Combine(harness.PublicationRoot, ImportPublicationManifestSerializer.ManifestRelativePath)));
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        harness.Send("edit", id: "sprite.actor", displayName: "Saved after immutable source removal");
        harness.Send("save", id: "sprite.actor");

        Assert.Equal("sprite.actor", harness.Product.Snapshot().SelectedId);
        Assert.NotEmpty(harness.Appearance.OpenResourceRequests);
        Assert.True(File.Exists(harness.OverlayPath));
    }

    [Fact]
    public void Construction_rejects_duplicate_or_invalid_admitted_content_paths()
    {
        Assert.Throws<FormatException>(() => Harness.Create(new HarnessOptions(AddDuplicateAdmittedPath: true)));
        Assert.Throws<FormatException>(() => Harness.Create(new HarnessOptions(AddInvalidAdmittedPath: true)));
    }

    [Fact]
    public void Attach_republishes_without_recreating_resources_and_update_sequences_are_monotonic()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Product.Attach();
        harness.Product.Update(harness.Update());

        Assert.Equal(new ulong[] { 1, 2, 3, 4 }, harness.Ui.Projections.Select(value => value.Sequence));
        Assert.Equal(4, harness.Appearance.Snapshots.Count);
        Assert.All(harness.Appearance.Snapshots, Assert.Empty);
        Assert.Equal(harness.Publication.Catalog.Entries.Count, harness.Appearance.OpenResourceRequests.Count);
        Assert.Equal(harness.Publication.Catalog.Entries.Count, harness.Appearance.AtlasRequests.Count);
        Assert.Equal(harness.Appearance.PlaybackRequests.Count, harness.Appearance.CreatedPlaybacks.Count);
    }

    [Fact]
    public void Selected_appearance_is_republished_on_attach_and_update_without_recreating_resources()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");

        int createdAppearances = harness.Appearance.CreatedAppearances;
        int openedResources = harness.Appearance.OpenResourceRequests.Count;
        int createdAtlases = harness.Appearance.CreatedAtlases;
        int createdPlaybacks = harness.Appearance.CreatedPlaybacks.Count;
        AppearanceHandle selectedHandle = harness.Appearance.SpriteAppearances[0].Handle;

        harness.Product.Attach();
        harness.Product.Update(harness.Update());

        Assert.Equal(5, harness.Appearance.Snapshots.Count);
        Assert.Equal(5, harness.Ui.Projections.Count);
        Assert.All(harness.Appearance.Snapshots.Skip(2), snapshot =>
        {
            AppearanceFact fact = Assert.Single(snapshot);
            Assert.Equal(SpriteWorkbenchPreviewPlacement.DefaultEntityId, fact.ObjectId);
            Assert.Equal(selectedHandle, fact.Appearance.Handle);
            Assert.Equal(RenderLayer.Viewmodel, fact.Layer);
            Assert.True(fact.Visible);
        });
        Assert.Equal(createdAppearances, harness.Appearance.CreatedAppearances);
        Assert.Equal(openedResources, harness.Appearance.OpenResourceRequests.Count);
        Assert.Equal(createdAtlases, harness.Appearance.CreatedAtlases);
        Assert.Equal(createdPlaybacks, harness.Appearance.CreatedPlaybacks.Count);
    }

    [Fact]
    public void Playback_controls_step_twice_progress_and_sample_forwards_exact_elapsed()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        int initialFrame = harness.Appearance.ReadFrameIndex(harness.Product.Snapshot().Playback);

        harness.Send("play", id: "sprite.actor");
        Assert.Equal(SpritePlaybackControl.Start, harness.Appearance.ControlRequests[^1].Control);
        harness.Send("pause", id: "sprite.actor");
        Assert.Equal(SpritePlaybackControl.Pause, harness.Appearance.ControlRequests[^1].Control);
        harness.Send("restart", id: "sprite.actor");
        Assert.Equal(SpritePlaybackControl.Restart, harness.Appearance.ControlRequests[^1].Control);
        harness.Send("stop", id: "sprite.actor");
        Assert.Equal(SpritePlaybackControl.Stop, harness.Appearance.ControlRequests[^1].Control);

        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        int beforeSteps = harness.Appearance.AdvanceRequests.Count;
        harness.Send("step", id: "sprite.actor");
        harness.Send("step", id: "sprite.actor");

        Assert.Equal(beforeSteps + 2, harness.Appearance.AdvanceRequests.Count);
        Assert.Equal(new[] { SpritePlaybackControl.Resume, SpritePlaybackControl.Pause, SpritePlaybackControl.Resume, SpritePlaybackControl.Pause },
            harness.Appearance.ControlRequests.Skip(harness.Appearance.ControlRequests.Count - 4).Select(value => value.Control));
        int afterSteps = harness.Appearance.ReadFrameIndex(harness.Product.Snapshot().Playback);
        Assert.Equal(initialFrame + 2, afterSteps);

        harness.Send("sample", id: "sprite.actor", elapsedSeconds: 17.125);
        Assert.Equal(17.125, Assert.Single(harness.Appearance.SampleRequests).ElapsedSeconds);
    }

    [Fact]
    public void Completed_step_does_not_pause_the_engine_terminal_playback()
    {
        using Harness harness = Harness.Create(new HarnessOptions(CompleteOnAdvance: true));
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        int controlsBeforeStep = harness.Appearance.ControlRequests.Count;

        harness.Send("step", id: "sprite.actor");

        Assert.Equal(new[] { SpritePlaybackControl.Resume }, harness.Appearance.ControlRequests.Skip(controlsBeforeStep).Select(value => value.Control));
        Assert.Equal(SpritePlaybackState.Completed, harness.Product.Snapshot().Playback?.State);
        Assert.Equal(0D, ValueReader.NumberField(harness.Ui.LastProjection.Value, "playing"));
    }

    [Fact]
    public void Selection_is_transactional_and_switches_the_named_state_to_the_requested_orientation()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0", orientation: 0);
        harness.Send("play", id: "sprite.actor");
        int controlsBeforeInvalidSelection = harness.Appearance.ControlRequests.Count;

        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0", orientation: 99);

        Assert.Equal(controlsBeforeInvalidSelection, harness.Appearance.ControlRequests.Count);
        Assert.Equal("state:Move@0", harness.Product.Snapshot().SelectedSequence);
        Assert.Equal(0, harness.Product.Snapshot().SelectedOrientation);

        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0", orientation: 1);

        Assert.Equal("state:Move@1", harness.Product.Snapshot().SelectedSequence);
        Assert.Equal(1, harness.Product.Snapshot().SelectedOrientation);
        Assert.Equal(3U, harness.Product.Snapshot().Playback?.FrameId);
    }

    [Fact]
    public void Selecting_sequence_frame_pauses_then_uses_engine_cursor_selection_and_next_step_continues()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        int appearancesBeforeSelection = harness.Appearance.CreatedAppearances;
        AppearanceHandle selectedHandle = harness.Appearance.SpriteAppearances[0].Handle;
        harness.Send("play", id: "sprite.actor");
        int selectionsBefore = harness.Appearance.FrameSelectionRequests.Count;

        harness.Send("select-frame", id: "sprite.actor", sequenceFrameIndex: 1);

        Assert.Equal(selectionsBefore + 1, harness.Appearance.FrameSelectionRequests.Count);
        Assert.Equal(1U, harness.Appearance.FrameSelectionRequests[^1].FrameIndex);
        Assert.Equal(SpritePlaybackControl.Pause, harness.Appearance.ControlRequests[^1].Control);
        Assert.Equal(1U, ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "playback", "frameId"));
        AppearanceFact selected = Assert.Single(harness.Appearance.Snapshots[^1]);
        Assert.Equal(SpriteWorkbenchPreviewPlacement.DefaultEntityId, selected.ObjectId);
        Assert.Equal(selectedHandle, selected.Appearance.Handle);
        Assert.Equal(RenderLayer.Viewmodel, selected.Layer);
        Assert.True(selected.Visible);
        Assert.Equal(new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), selected.Transform);
        Assert.Equal(appearancesBeforeSelection, harness.Appearance.CreatedAppearances);

        harness.Send("step", id: "sprite.actor");
        Assert.Equal(2U, harness.Product.Snapshot().Playback?.FrameId);

        harness.SendRaw("{\"action\":\"select\",\"id\":\"missing\"}");
        Assert.Equal("sprite.actor", harness.Product.Snapshot().SelectedId);
        string diagnostic = ValueReader.StringField(harness.Ui.LastProjection.Value, "diagnostic");
        Assert.Contains("does not exist", diagnostic, StringComparison.Ordinal);

        harness.Send("pause", id: "sprite.actor");
        Assert.Null(ValueReader.NullableStringField(harness.Ui.LastProjection.Value, "diagnostic"));
    }

    [Fact]
    public void Selecting_frame_outside_the_active_orientation_sequence_is_bounded_without_engine_or_local_mutation()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0", orientation: 0);
        int selectionCalls = harness.Appearance.FrameSelectionRequests.Count;
        SpriteWorkbenchSnapshot before = harness.Product.Snapshot();

        harness.Send("select-frame", id: "sprite.actor", sequenceFrameIndex: 3);

        Assert.Equal(selectionCalls, harness.Appearance.FrameSelectionRequests.Count);
        Assert.Equal(before.SelectedId, harness.Product.Snapshot().SelectedId);
        Assert.Equal(before.SelectedSequence, harness.Product.Snapshot().SelectedSequence);
        Assert.Equal(before.Playback?.FrameId, harness.Product.Snapshot().Playback?.FrameId);
        Assert.Contains("active playback sequence", ValueReader.StringField(harness.Ui.LastProjection.Value, "diagnostic"), StringComparison.Ordinal);
    }

    [Fact]
    public void Sequence_frame_selection_keeps_repeated_frame_occurrences_distinct_and_advances_from_the_selected_entry()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.effect");

        Assert.Equal(0D, ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "availableSequenceFrames", 0, "frameId"));
        Assert.Equal(0D, ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "availableSequenceFrames", 1, "frameId"));
        Assert.Equal(1D, ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "availableSequenceFrames", 1, "sequenceIndex"));

        harness.Send("select-frame", id: "sprite.effect", sequenceFrameIndex: 1);

        Assert.Equal(1U, harness.Appearance.FrameSelectionRequests[^1].FrameIndex);
        Assert.Equal(1U, harness.Product.Snapshot().Playback?.FrameIndex);
        Assert.Equal(0U, harness.Product.Snapshot().Playback?.FrameId);

        harness.Send("step", id: "sprite.effect");

        Assert.Equal(2U, harness.Product.Snapshot().Playback?.FrameIndex);
        Assert.Equal(1U, harness.Product.Snapshot().Playback?.FrameId);
    }

    [Fact]
    public void Projection_control_options_are_exact_for_selected_state_and_action_sequences()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0", orientation: 0);

        Assert.Equal(new[] { 0D, 1D, 2D }, Enumerable.Range(0, 3).Select(index => ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "availableSequenceFrames", index, "sequenceIndex")));
        Assert.Equal(new[] { 0D, 1D, 2D }, Enumerable.Range(0, 3).Select(index => ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "availableSequenceFrames", index, "frameId")));
        double[] stateOrientations = ValueReader.NumberArrayField(harness.Ui.LastProjection.Value, "selected", "availableOrientations");
        Assert.Equal(Enumerable.Range(0, 8).Select(value => (double)value), stateOrientations);
        Assert.DoesNotContain(3D, Enumerable.Range(0, 3).Select(index => ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "availableSequenceFrames", index, "frameId")));
        foreach (int sequenceFrameIndex in new[] { 0, 1, 2 }) harness.Send("select-frame", id: "sprite.actor", sequenceFrameIndex: sequenceFrameIndex);
        foreach (int orientation in stateOrientations.Select(value => checked((int)value)))
            harness.Send("select", id: "sprite.actor", sequence: "state:Move@0", orientation: orientation);

        harness.Send("select", id: "sprite.weapon");

        string actionSequence = harness.Product.Snapshot().SelectedSequence!;
        Assert.StartsWith("action:", actionSequence, StringComparison.Ordinal);
        double[] actionFrames = Enumerable.Range(0, 1).Select(index => ValueReader.NumberField(harness.Ui.LastProjection.Value, "selected", "availableSequenceFrames", index, "frameId")).ToArray();
        Assert.Single(actionFrames);
        Assert.Equal(new[] { 0D }, ValueReader.NumberArrayField(harness.Ui.LastProjection.Value, "selected", "availableOrientations"));
        Assert.NotEqual(harness.Publication.Catalog.Require("sprite.weapon").Frames.Count, actionFrames.Length);
        harness.Send("select-frame", id: "sprite.weapon", sequenceFrameIndex: 0);
        harness.Send("select", id: "sprite.weapon", sequence: actionSequence, orientation: 0);
        Assert.Null(ValueReader.NullableStringField(harness.Ui.LastProjection.Value, "diagnostic"));
    }

    [Fact]
    public void Engine_frame_selection_failure_escapes_without_a_bounded_diagnostic_or_local_mutation()
    {
        using Harness harness = Harness.Create(new HarnessOptions(ThrowOnFrameSelection: true));
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        int projectionsBefore = harness.Ui.Projections.Count;
        SpriteWorkbenchSnapshot before = harness.Product.Snapshot();

        Assert.Throws<EngineCallException>(() => harness.Send("select-frame", id: "sprite.actor", sequenceFrameIndex: 1));

        Assert.Equal(projectionsBefore, harness.Ui.Projections.Count);
        Assert.Equal(before.SelectedId, harness.Product.Snapshot().SelectedId);
        Assert.Equal(before.SelectedSequence, harness.Product.Snapshot().SelectedSequence);
        Assert.Equal(before.Playback?.FrameId, harness.Product.Snapshot().Playback?.FrameId);
    }

    [Fact]
    public void Saved_overlay_is_cached_save_merges_typed_values_and_discard_restores_without_reloading()
    {
        using Harness harness = Harness.Create(new SpriteAuthoredOverlay("sprite.actor", DisplayName: "Saved name"));
        SpriteAuthoredOverlayDocument externallyChanged = new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, harness.Publication.AuthoringBasisDigest,
            [new("sprite.actor", DisplayName: "Externally changed")]);
        File.WriteAllBytes(harness.OverlayPath, [.. JsonSerializer.SerializeToUtf8Bytes(externallyChanged, JsonOptions), (byte)'\n']);
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        Assert.Equal("Saved name", ValueReader.StringField(harness.Ui.LastProjection.Value, "selected", "saved", "displayName"));

        harness.Send("edit", id: "sprite.actor", displayName: "Unsaved name", pivotX: .25F, pivotY: .75F);
        Assert.Equal("Unsaved name", ValueReader.StringField(harness.Ui.LastProjection.Value, "selected", "pending", "displayName"));
        harness.Send("discard", id: "sprite.actor");

        Assert.Null(ValueReader.NullableStringField(harness.Ui.LastProjection.Value, "selected", "pending"));
        Assert.Equal("Saved name", ValueReader.StringField(harness.Ui.LastProjection.Value, "selected", "saved", "displayName"));
        Assert.False(File.Exists(harness.OverlayPath + ".discarded"));

        harness.Send("edit", id: "sprite.actor", displayName: "Merged name", displaySizeX: 2F, displaySizeY: 3F);
        harness.Send("save", id: "sprite.actor");

        Assert.Equal("saved", ValueReader.StringField(harness.Ui.LastProjection.Value, "saveStatus"));
        Assert.Equal("Merged name", ValueReader.StringField(harness.Ui.LastProjection.Value, "selected", "saved", "displayName"));
        SpriteAuthoredOverlayDocument saved = SpriteAuthoredOverlayStore.Read(File.ReadAllBytes(harness.OverlayPath));
        SpriteAuthoredOverlay overlay = Assert.Single(saved.Overlays);
        Assert.Equal("Merged name", overlay.DisplayName);
        Assert.Equal(new NormalizedVector2(2F, 3F), overlay.DisplaySize);
    }

    [Fact]
    public void Failed_save_leaves_cached_saved_overlay_and_pending_edit_unchanged()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        harness.Send("edit", id: "sprite.actor", displayName: "Pending name");
        Directory.CreateDirectory(Path.GetDirectoryName(harness.OverlayPath)!);
        File.WriteAllText(harness.OverlayPath, "existing overlay");
        File.WriteAllText(harness.OverlayPath + ".bak", "recovery collision");

        Assert.Throws<IOException>(() => harness.Send("save", id: "sprite.actor"));

        Assert.True(ValueReader.IsNull(harness.Ui.LastProjection.Value, "selected", "saved"));
        Assert.Equal("Pending name", ValueReader.StringField(harness.Ui.LastProjection.Value, "selected", "pending", "displayName"));
    }

    [Fact]
    public void Invalid_edit_does_not_replace_the_existing_pending_candidate()
    {
        using Harness harness = Harness.Create();
        harness.Product.Start();
        harness.Send("select", id: "sprite.actor", sequence: "state:Move@0");
        harness.Send("edit", id: "sprite.actor", displayName: "Valid pending");

        harness.Send("edit", id: "sprite.actor", displaySizeX: 3F);

        Assert.Equal("Valid pending", ValueReader.StringField(harness.Ui.LastProjection.Value, "selected", "pending", "displayName"));
        Assert.Contains("paired vector", ValueReader.StringField(harness.Ui.LastProjection.Value, "diagnostic"), StringComparison.Ordinal);
    }

    [Fact]
    public void Construction_failure_disposes_all_completed_objects_and_shutdown_continues_after_disposal_error()
    {
        HarnessOptions options = new(FailPlaybackCreateAt: 2);
        Assert.Throws<InvalidOperationException>(() => Harness.Create(options));
        Assert.NotNull(options.Appearance);
        Assert.Equal(options.Appearance!.CreatedPlaybacks.Count, options.Appearance.DisposedPlaybacks);
        Assert.Equal(options.Appearance.CreatedAtlases, options.Appearance.DisposedAtlases);
        Assert.Equal(options.Appearance.CreatedAppearances, options.Appearance.DisposedAppearances);
        Assert.Equal(1, options.Appearance.DisposedUiStreams);

        using Harness harness = Harness.Create(new HarnessOptions(ThrowOnPlaybackDisposeHandle: 3, ThrowOnAtlasDisposeHandle: 1, ThrowOnAppearanceDisposeHandle: 2, ThrowOnUiDispose: true));
        Assert.Throws<AggregateException>(() => harness.Product.Shutdown());
        Assert.Equal(harness.Appearance.CreatedPlaybacks.Count, harness.Appearance.DisposedPlaybacks);
        Assert.Equal(harness.Appearance.CreatedAtlases, harness.Appearance.DisposedAtlases);
        Assert.Equal(harness.Appearance.CreatedAppearances, harness.Appearance.DisposedAppearances);
        Assert.Equal(1, harness.Appearance.DisposedUiStreams);
        Assert.Empty(harness.Appearance.Snapshots[^1]);
        int snapshotCount = harness.Appearance.Snapshots.Count;
        harness.Product.Shutdown();
        Assert.Equal(snapshotCount, harness.Appearance.Snapshots.Count);
    }

    [Fact]
    public void Create_time_projection_failure_still_exhausts_preview_and_ui_cleanup()
    {
        HarnessOptions options = new(FailPublishAt: 1);
        Assert.Throws<InvalidOperationException>(() => Harness.Create(options));
        Assert.NotNull(options.Appearance);
        Assert.Equal(options.Appearance!.CreatedPlaybacks.Count, options.Appearance.DisposedPlaybacks);
        Assert.Equal(options.Appearance.CreatedAtlases, options.Appearance.DisposedAtlases);
        Assert.Equal(options.Appearance.CreatedAppearances, options.Appearance.DisposedAppearances);
        Assert.Equal(1, options.Appearance.DisposedUiStreams);
        Assert.Empty(options.Appearance.Snapshots[^1]);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TestPublication fixture;
        private ulong inputSequence;

        private Harness(TestPublication fixture, string publicationRoot, string authoringRoot, ContentFake content, AppearanceFake appearance, UiFake ui, SpriteWorkbenchProduct product, int overlayReadCount)
        {
            this.fixture = fixture;
            PublicationRoot = publicationRoot;
            AuthoringRoot = authoringRoot;
            Content = content;
            Appearance = appearance;
            Ui = ui;
            Product = product;
            OverlayReadCount = overlayReadCount;
        }

        internal string PublicationRoot { get; }
        internal string AuthoringRoot { get; }
        internal string OverlayPath => Path.Combine(AuthoringRoot, "sprites", "sprite-overlays.json");
        internal SpritePublicationSnapshot Publication => fixture.Snapshot;
        internal ContentFake Content { get; }
        internal AppearanceFake Appearance { get; }
        internal UiFake Ui { get; }
        internal SpriteWorkbenchProduct Product { get; }
        internal int OverlayReadCount { get; }

        internal static Harness Create(SpriteAuthoredOverlay? initialOverlay = null) => Create(new HarnessOptions(), initialOverlay);

        internal static Harness Create(HarnessOptions options) => Create(options, null);

        private static Harness Create(HarnessOptions options, SpriteAuthoredOverlay? initialOverlay)
        {
            TestPublication fixture = TestPublication.Create();
            string publicationRoot = Path.Combine(Path.GetTempPath(), $"sprite-workbench-publication-{Guid.NewGuid():N}");
            string authoringRoot = Path.Combine(Path.GetTempPath(), $"sprite-workbench-authoring-{Guid.NewGuid():N}");
            ImportPublicationWriter.Write(fixture.Plan, publicationRoot);
            Directory.CreateDirectory(authoringRoot);
            int overlayReadCount = 0;
            if (initialOverlay is not null)
            {
                SpriteAuthoredOverlayDocument document = new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, fixture.Snapshot.AuthoringBasisDigest, [initialOverlay]);
                SpriteAuthoredOverlayStore.Write(publicationRoot, authoringRoot, "sprites/sprite-overlays.json", document, fixture.Snapshot.Catalog, fixture.Snapshot.AuthoringBasisDigest);
            }

            ContentFake content = ContentFake.Create(fixture.Content);
            AppearanceFake appearance = AppearanceFake.Create(options);
            options.Appearance = appearance;
            UiFake ui = UiFake.Create(options);
            IEngineContext engine = EngineContextFake.Create(content.Service, appearance.Service, ui.Service);
            string config = JsonSerializer.Serialize(new SpriteWorkbenchConfiguration(publicationRoot, authoringRoot, "sprites/sprite-overlays.json"), JsonOptions);
            List<ProductContentFile> admittedFiles =
            [
                new("sprite-workbench.json"u8.ToArray(), Encoding.UTF8.GetBytes(config)),
                .. fixture.Plan.Artifacts.Select(artifact => new ProductContentFile(Encoding.UTF8.GetBytes(artifact.RelativePath), artifact.Bytes.ToArray())),
            ];
            if (options.AddDuplicateAdmittedPath)
            {
                ImportPublicationArtifact duplicate = fixture.Plan.Artifacts[0];
                admittedFiles.Add(new(Encoding.UTF8.GetBytes(duplicate.RelativePath), duplicate.Bytes.ToArray()));
            }
            if (options.AddInvalidAdmittedPath)
            {
                admittedFiles.Add(new("../invalid.json"u8.ToArray(), "invalid"u8.ToArray()));
            }
            ProductContent productContent = new(admittedFiles.ToArray());
            if (options.MutateExternalPublicationAfterAdmission)
            {
                File.Delete(Path.Combine(publicationRoot, ImportPublicationManifestSerializer.ManifestRelativePath));
                File.WriteAllBytes(Path.Combine(publicationRoot, "media", "dungeon", "actor.png"), "changed-after-admission"u8.ToArray());
            }
            ProductInputConfiguration input = new(new InputBinding(1, 1, 1), new(ReadOnlyMemory<byte>.Empty), ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
            try
            {
                SpriteWorkbenchProduct product = new(new ProductCreateContext(engine, productContent, input));
                return new(fixture, publicationRoot, authoringRoot, content, appearance, ui, product, overlayReadCount);
            }
            catch
            {
                options.Appearance = appearance;
                options.OverlayReadCount = overlayReadCount;
                DeleteRoots(publicationRoot, authoringRoot);
                throw;
            }
        }

        internal ProductUpdateFacts UpdateFacts() => new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 0, 1, 60, 1, 0, 1D / 60D);

        internal ProductUpdate Update() => new(UpdateFacts(), ReadOnlySpan<ProductInputEvent>.Empty);

        internal void Send(string action, string? id = null, string? sequence = null, int? orientation = null, int? sequenceFrameIndex = null, double? elapsedSeconds = null,
            string? displayName = null, float? pivotX = null, float? pivotY = null, float? displaySizeX = null, float? displaySizeY = null)
        {
            List<string> fields = [$"\"action\":\"{action}\""];
            if (id is not null) fields.Add($"\"id\":\"{id}\"");
            if (sequence is not null) fields.Add($"\"sequence\":\"{sequence}\"");
            if (orientation is not null) fields.Add($"\"orientation\":{orientation.Value}");
            if (sequenceFrameIndex is not null) fields.Add($"\"sequenceFrameIndex\":{sequenceFrameIndex.Value}");
            if (elapsedSeconds is not null) fields.Add($"\"elapsedSeconds\":{elapsedSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (displayName is not null) fields.Add($"\"displayName\":\"{displayName}\"");
            if (pivotX is not null) fields.Add($"\"pivotX\":{pivotX.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (pivotY is not null) fields.Add($"\"pivotY\":{pivotY.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (displaySizeX is not null) fields.Add($"\"displaySizeX\":{displaySizeX.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (displaySizeY is not null) fields.Add($"\"displaySizeY\":{displaySizeY.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            SendRaw($"{{{string.Join(',', fields)}}}");
        }

        internal void SendRaw(string payload)
        {
            ProductInputEvent input = new(
                InputEventKind.DirectProductPayload, InputEdge.Pressed, InputDevice.Product, InputChannel.Intent, InputAxis.None,
                KeyboardControl.None, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None,
                InputValueKind.ProductPayload, InputPhase.DirectUi, InputProvenance.DirectUi, new InputBinding(1, 1, 1), new InputSequence(++inputSequence),
                new InputContext(ReadOnlyMemory<byte>.Empty), 0F, 0F, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty, "worldrpg.sprite-workbench.intent.v1"u8.ToArray(), Encoding.UTF8.GetBytes(payload));
            Product.Update(new ProductUpdate(UpdateFacts(), new[] { input }));
        }

        public void Dispose()
        {
            try { Product.Dispose(); }
            finally { DeleteRoots(PublicationRoot, AuthoringRoot); }
        }

        private static void DeleteRoots(string publicationRoot, string authoringRoot)
        {
            if (Directory.Exists(publicationRoot)) Directory.Delete(publicationRoot, recursive: true);
            if (Directory.Exists(authoringRoot)) Directory.Delete(authoringRoot, recursive: true);
        }
    }

    private sealed class HarnessOptions(int? FailPlaybackCreateAt = null, ulong? ThrowOnPlaybackDisposeHandle = null,
        ulong? ThrowOnAtlasDisposeHandle = null, ulong? ThrowOnAppearanceDisposeHandle = null, bool ThrowOnUiDispose = false, int? FailPublishAt = null,
        bool CompleteOnAdvance = false, bool ThrowOnFrameSelection = false, bool MutateExternalPublicationAfterAdmission = false,
        bool AddDuplicateAdmittedPath = false, bool AddInvalidAdmittedPath = false)
    {
        internal int? FailPlaybackCreateAt { get; } = FailPlaybackCreateAt;
        internal ulong? ThrowOnPlaybackDisposeHandle { get; } = ThrowOnPlaybackDisposeHandle;
        internal ulong? ThrowOnAtlasDisposeHandle { get; } = ThrowOnAtlasDisposeHandle;
        internal ulong? ThrowOnAppearanceDisposeHandle { get; } = ThrowOnAppearanceDisposeHandle;
        internal bool ThrowOnUiDispose { get; } = ThrowOnUiDispose;
        internal int? FailPublishAt { get; } = FailPublishAt;
        internal bool CompleteOnAdvance { get; } = CompleteOnAdvance;
        internal bool ThrowOnFrameSelection { get; } = ThrowOnFrameSelection;
        internal bool MutateExternalPublicationAfterAdmission { get; } = MutateExternalPublicationAfterAdmission;
        internal bool AddDuplicateAdmittedPath { get; } = AddDuplicateAdmittedPath;
        internal bool AddInvalidAdmittedPath { get; } = AddInvalidAdmittedPath;
        internal AppearanceFake? Appearance { get; set; }
        internal int OverlayReadCount { get; set; }
    }

    private class ContentFake : DispatchProxy
    {
        private IReadOnlyDictionary<string, (ContentDigest Digest, long Length)> content = null!;
        internal IContentService Service { get; private set; } = null!;
        internal List<string> OpenRequests { get; } = [];
        internal List<string> ReadInfoRequests { get; } = [];

        internal static ContentFake Create(IReadOnlyDictionary<string, (ContentDigest Digest, long Length)> content)
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentFake>();
            ContentFake fake = (ContentFake)(object)service;
            fake.Service = service;
            fake.content = content;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            return method?.Name switch
            {
                nameof(IContentService.OpenReference) => Open((ContentOpenRequest)arguments![0]!),
                nameof(IContentService.ReadReferenceInfo) => ReadInfo((ContentReference)arguments![0]!),
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private ContentReference Open(ContentOpenRequest request)
        {
            if (!content.TryGetValue(request.Path, out _)) throw new InvalidOperationException($"Unknown content path {request.Path}.");
            OpenRequests.Add(request.Path);
            ContentReference value = new(new ContentReferenceHandle((ulong)OpenRequests.Count), static () => { });
            return value;
        }

        private ReadOnlyMemory<ContentReferenceInfo> ReadInfo(ContentReference reference)
        {
            string path = OpenRequests[checked((int)reference.Handle.Value) - 1];
            ReadInfoRequests.Add(path);
            (ContentDigest digest, long length) = content[path];
            return new[] { new ContentReferenceInfo(path, ToEngineDigest(digest), checked((ulong)length)) };
        }
    }

    private sealed class AppearanceFake : IAppearanceService
    {
        private readonly HarnessOptions options;
        private ulong nextHandle = 1;
        private readonly Dictionary<ulong, PlaybackState> playbacks = [];
        private readonly Dictionary<ulong, uint> appearanceFrames = [];
        private readonly HashSet<Appearance> retainedAppearances = new(ReferenceEqualityComparer.Instance);
        internal IAppearanceService Service => this;
        internal List<RenderResourceRequest> OpenResourceRequests { get; } = [];
        internal List<SpriteAtlasCreateRequest> AtlasRequests { get; } = [];
        internal List<SpriteFromAtlasRequest> SpriteRequests { get; } = [];
        internal List<Appearance> SpriteAppearances { get; } = [];
        internal List<SpritePlaybackCreateRequest> PlaybackRequests { get; } = [];
        internal List<SpritePlayback> CreatedPlaybacks { get; } = [];
        internal List<SpritePlaybackControlRequest> ControlRequests { get; } = [];
        internal List<SpritePlaybackFrameSelectionRequest> FrameSelectionRequests { get; } = [];
        internal List<SpritePlaybackAdvanceRequest> AdvanceRequests { get; } = [];
        internal List<SpritePlaybackSampleRequest> SampleRequests { get; } = [];
        internal List<AppearanceFact[]> Snapshots { get; } = [];
        internal List<SpriteFrameUpdateRequest> SetFrameRequests { get; } = [];
        internal int CreatedAtlases { get; private set; }
        internal int DisposedAtlases { get; private set; }
        internal int CreatedAppearances { get; private set; }
        internal int DisposedAppearances { get; private set; }
        internal int DisposedPlaybacks { get; private set; }
        internal int DisposedUiStreams { get; set; }
        internal int PublishCalls { get; private set; }

        private AppearanceFake(HarnessOptions options)
        {
            this.options = options;
        }

        internal static AppearanceFake Create(HarnessOptions options) => new(options);

        public RenderResourceInfo OpenResource(RenderResourceRequest request)
        {
            OpenResourceRequests.Add(request);
            return new(new RenderResourceHandle((ulong)OpenResourceRequests.Count), RenderResourceKind.Texture, 1);
        }

        public Material CreateMaterial(MaterialRequest request) => new(new MaterialHandle(1), static () => { });
        public void UpdateMaterial(MaterialUpdateRequest request) { }
        public Material ReplaceMaterial(MaterialUpdateRequest request) => CreateMaterial(request.Replacement);
        public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance(request.Color);
        public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => CreateAppearance(request.Replacement.Color);
        public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance(request.Color);
        public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) => CreateAppearance(request.Color);
        public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => CreateAppearance(request.Color);
        public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => CreateAppearance(request.Color);
        public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) { }
        public Appearance CreateSprite(SpriteAppearanceRequest request) => CreateAppearance(request.Tint);
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => CreateAppearance(request.Replacement.Tint);

        public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request)
        {
            AtlasRequests.Add(request);
            CreatedAtlases++;
            ulong handle = nextHandle++;
            return new(new SpriteAtlasHandle(handle), () =>
            {
                DisposedAtlases++;
                if (options.ThrowOnAtlasDisposeHandle == handle) throw new InvalidOperationException("atlas dispose failure");
            });
        }

        public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request)
        {
            SpriteRequests.Add(request);
            Appearance value = CreateAppearance(new Color(1F, 1F, 1F, 1F), request.FrameId);
            SpriteAppearances.Add(value);
            return value;
        }

        public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => CreateAppearance(request.Replacement.Tint, request.Replacement.FrameId);

        public void SetSpriteFrame(SpriteFrameUpdateRequest request) => DoSetFrame(request);
        public SpriteReadout ReadSprite(Appearance appearance) => default;

        public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest request)
        {
            PlaybackRequests.Add(request);
            if (options.FailPlaybackCreateAt == PlaybackRequests.Count) throw new InvalidOperationException("playback create failure");
            ulong handle = nextHandle++;
            PlaybackState state = new(request.Frames.ToArray());
            playbacks.Add(handle, state);
            state.AppearanceHandle = request.Appearance.Handle.Value;
            SpritePlayback value = new(new SpritePlaybackHandle(handle), () =>
            {
                DisposedPlaybacks++;
                if (options.ThrowOnPlaybackDisposeHandle == handle) throw new InvalidOperationException("playback dispose failure");
            });
            CreatedPlaybacks.Add(value);
            return value;
        }

        public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest request)
        {
            ControlRequests.Add(request);
            PlaybackState state = playbacks[request.Playback.Handle.Value];
            state.State = request.Control switch
            {
                SpritePlaybackControl.Start or SpritePlaybackControl.Resume or SpritePlaybackControl.Restart => SpritePlaybackState.Playing,
                SpritePlaybackControl.Pause => SpritePlaybackState.Paused,
                SpritePlaybackControl.Stop => SpritePlaybackState.Stopped,
                _ => state.State,
            };
            if (request.Control == SpritePlaybackControl.Restart) state.FrameIndex = 0;
            return state.Readout();
        }

        public SpritePlaybackReadout SelectSpritePlaybackFrame(SpritePlaybackFrameSelectionRequest request)
        {
            FrameSelectionRequests.Add(request);
            if (options.ThrowOnFrameSelection) throw new EngineCallException("Appearance", "SelectSpritePlaybackFrame", 0);
            PlaybackState state = playbacks[request.Playback.Handle.Value];
            if (request.FrameIndex >= state.Frames.Length) throw new EngineCallException("Appearance", "SelectSpritePlaybackFrame", 0);
            state.FrameIndex = checked((int)request.FrameIndex);
            if (state.State == SpritePlaybackState.Completed) state.State = SpritePlaybackState.Paused;
            return state.Readout();
        }

        public SpritePlaybackAdvanceLeaseReceipt AdvanceSpritePlayback(SpritePlaybackAdvanceRequest request)
        {
            AdvanceRequests.Add(request);
            PlaybackState state = playbacks[request.Playback.Handle.Value];
            if (state.Frames.Length > 0) state.FrameIndex = (state.FrameIndex + 1) % state.Frames.Length;
            if (options.CompleteOnAdvance) state.State = SpritePlaybackState.Completed;
            return new(ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty, state.Readout(), true);
        }

        public SpritePlaybackSample SampleSpritePlayback(SpritePlaybackSampleRequest request)
        {
            SampleRequests.Add(request);
            PlaybackState state = playbacks[request.Playback.Handle.Value];
            return new(state.FrameId, (uint)state.FrameIndex, request.ElapsedSeconds, 0, false);
        }

        public SpritePlaybackReadout ReadSpritePlayback(SpritePlayback playback) => Read(playback);

        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values)
        {
            PublishCalls++;
            AppearanceFact[] snapshot = values.ToArray();
            Snapshots.Add(snapshot);
            retainedAppearances.Clear();
            foreach (AppearanceFact value in snapshot) retainedAppearances.Add(value.Appearance);
            if (options.FailPublishAt == PublishCalls) throw new InvalidOperationException("ui publish failure");
        }

        public Light CreateLight(LightRequest request) => new(new LightHandle(1), static () => { });
        public void UpdateLight(LightUpdateRequest request) { }
        public Light ReplaceLight(LightUpdateRequest request) => new(new LightHandle(1), static () => { });
        public LightReadout ReadLight(Light light) => default;
        public PresentationReadout ReadPresentation() => default;

        private void DoSetFrame(SpriteFrameUpdateRequest request)
        {
            SetFrameRequests.Add(request);
            foreach (KeyValuePair<ulong, PlaybackState> item in playbacks)
            {
                if (item.Value.AppearanceHandle == request.Appearance.Handle.Value)
                {
                    SpritePlaybackFrame[] frames = item.Value.Frames;
                    for (int index = 0; index < frames.Length; index++)
                    {
                        if (frames[index].FrameId == request.FrameId)
                        {
                            item.Value.FrameIndex = index;
                            break;
                        }
                    }
                }
            }
            appearanceFrames[request.Appearance.Handle.Value] = request.FrameId;
        }

        private Appearance CreateAppearance(Color tint, uint frameId = 0)
        {
            CreatedAppearances++;
            ulong handle = nextHandle++;
            Appearance value = new(new AppearanceHandle(handle), () =>
            {
                DisposedAppearances++;
                if (options.ThrowOnAppearanceDisposeHandle == handle) throw new InvalidOperationException("appearance dispose failure");
            });
            appearanceFrames[handle] = frameId;
            return value;
        }

        private SpritePlaybackReadout Read(SpritePlayback playback) => playbacks[playback.Handle.Value].Readout();

        internal int ReadFrameIndex(SpritePlaybackReadout? readout) => checked((int)(readout?.FrameIndex ?? 0));

        private sealed class PlaybackState(SpritePlaybackFrame[] Frames)
        {
            internal SpritePlaybackFrame[] Frames { get; } = Frames;
            internal SpritePlaybackState State { get; set; } = SpritePlaybackState.Stopped;
            internal int FrameIndex { get; set; }
            internal ulong AppearanceHandle { get; set; }
            internal uint FrameId => Frames.Length == 0 ? 0 : Frames[Math.Clamp(FrameIndex, 0, Frames.Length - 1)].FrameId;
            internal SpritePlaybackReadout Readout() => new(FrameId, (uint)FrameIndex, State, 0, 0, 0, State == SpritePlaybackState.Completed);
        }
    }

    private class UiFake : DispatchProxy
    {
        private HarnessOptions options = null!;
        internal IUiService Service { get; private set; } = null!;
        internal int OpenStreamCalls { get; private set; }
        internal UiStreamRequest StreamRequest { get; private set; }
        internal List<UiProjection> Projections { get; } = [];
        internal UiProjection LastProjection
        {
            get
            {
                Assert.NotEmpty(Projections);
                return Projections[^1];
            }
        }

        internal static UiFake Create(HarnessOptions options)
        {
            IUiService service = DispatchProxy.Create<IUiService, UiFake>();
            UiFake fake = (UiFake)(object)service;
            fake.Service = service;
            fake.options = options;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IUiService.OpenStream) => Open((UiStreamRequest)arguments![0]!),
            nameof(IUiService.PublishProjection) => DoPublish((UiProjection)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private UiStream Open(UiStreamRequest request)
        {
            OpenStreamCalls++;
            StreamRequest = request;
            return new(new UiStreamHandle(1), () =>
            {
                options.Appearance!.DisposedUiStreams++;
                if (options.ThrowOnUiDispose) throw new InvalidOperationException("ui dispose failure");
            });
        }

        private object? DoPublish(UiProjection projection)
        {
            if (options.FailPublishAt == Projections.Count + 1) throw new InvalidOperationException("ui publish failure");
            Projections.Add(projection);
            return null;
        }
    }

    private class EngineContextFake : DispatchProxy
    {
        private IContentService content = null!;
        private IAppearanceService appearance = null!;
        private IUiService ui = null!;

        internal static IEngineContext Create(IContentService content, IAppearanceService appearance, IUiService ui)
        {
            IEngineContext service = DispatchProxy.Create<IEngineContext, EngineContextFake>();
            EngineContextFake fake = (EngineContextFake)(object)service;
            fake.content = content;
            fake.appearance = appearance;
            fake.ui = ui;
            return service;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            "get_Content" => content,
            "get_Appearance" => appearance,
            "get_Ui" => ui,
            _ => throw new NotSupportedException(method?.Name),
        };
    }

    private static class ValueReader
    {
        internal static string StringField(UiValue value, params object[] path) => GetString(value, path, false)!;
        internal static string? NullableStringField(UiValue value, params object[] path) => GetString(value, path, true);
        internal static double NumberField(UiValue value, params object[] path) => GetNode(value, path).NumberValue;
        internal static double[] NumberArrayField(UiValue value, params object[] path)
        {
            StructuredValueNode array = GetNode(value, path);
            Assert.Equal(StructuredValueKind.Array, array.Kind);
            return value.Edges.Span.Slice(checked((int)array.FirstEdge), checked((int)array.ChildCount)).ToArray()
                .Select(index => value.Nodes.Span[checked((int)index)].NumberValue).ToArray();
        }
        internal static bool IsNull(UiValue value, params object[] path) => GetNode(value, path).Kind == StructuredValueKind.Null;

        private static string? GetString(UiValue value, object[] path, bool allowNull)
        {
            StructuredValueNode node = GetNode(value, path);
            if (node.Kind == StructuredValueKind.Null && allowNull) return null;
            Assert.Equal(StructuredValueKind.String, node.Kind);
            return Encoding.UTF8.GetString(value.Utf8.Span.Slice(checked((int)node.TextOffset), checked((int)node.TextLen)));
        }

        private static StructuredValueNode GetNode(UiValue value, object[] path)
        {
            uint index = value.Root;
            foreach (object segment in path)
            {
                StructuredValueNode parent = value.Nodes.Span[checked((int)index)];
                if (segment is string key)
                {
                    index = value.Edges.Span.Slice(checked((int)parent.FirstEdge), checked((int)parent.ChildCount)).ToArray()
                        .Select(edge => (Index: edge, Node: value.Nodes.Span[checked((int)edge)]))
                        .Where(item => Encoding.UTF8.GetString(value.Utf8.Span.Slice(checked((int)item.Node.KeyOffset), checked((int)item.Node.KeyLen))) == key)
                        .Select(item => item.Index).Single();
                }
                else
                {
                    index = value.Edges.Span[checked((int)parent.FirstEdge + Convert.ToInt32(segment, System.Globalization.CultureInfo.InvariantCulture))];
                }
            }
            return value.Nodes.Span[checked((int)index)];
        }
    }

    private sealed class TestPublication
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.Strict,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        private TestPublication(ImportPublicationPlan plan, SpritePublicationSnapshot snapshot, IReadOnlyDictionary<string, (ContentDigest Digest, long Length)> content)
        {
            Plan = plan;
            Snapshot = snapshot;
            Content = content;
        }

        internal ImportPublicationPlan Plan { get; }
        internal SpritePublicationSnapshot Snapshot { get; }
        internal IReadOnlyDictionary<string, (ContentDigest Digest, long Length)> Content { get; }

        internal static TestPublication Create()
        {
            byte[] actorBytes = "actor-png"u8.ToArray();
            byte[] weaponBytes = "weapon-png"u8.ToArray();
            byte[] effectBytes = "effect-png"u8.ToArray();
            byte[] fontBytes = "font-bin"u8.ToArray();
            NormalizedMediaDescriptor actor = Descriptor("sprite.actor", NormalizedMediaKind.EnemySprite, "media/dungeon/actor.png", actorBytes, 24) with
            {
                Pivot = new(.5F, 0F), DisplaySize = new(1F, 1F), FramesPerSecond = 6F, Loop = true,
            };
            NormalizedMediaDescriptor weapon = Descriptor("sprite.weapon", NormalizedMediaKind.WeaponSprite, "media/classic/weapon.png", weaponBytes, 7);
            NormalizedMediaDescriptor effect = Descriptor("sprite.effect", NormalizedMediaKind.EffectSprite, "media/classic/effect.png", effectBytes, 2) with
            {
                FramesPerSecond = 10F, Loop = false, Sequence = [0, 0, 1],
            };
            NormalizedMediaDescriptor font = Descriptor("font.classic", NormalizedMediaKind.Font, "media/classic/font.bin", fontBytes, 1);
            DungeonMediaFrameLayout[] layouts = Enumerable.Range(0, actor.Frames.Count)
                .Select(index => new DungeonMediaFrameLayout(index, 0, index / 3, index / 3, false, actor.Frames[index], new(1F, 1F))).ToArray();
            DungeonActorMediaManifest actorManifest = new(
                "actor/rat", 1, "Rat", DungeonActorSpriteState.Move, new([0, -1], []), "sprite/rat", new(.5F, 0F), new(1F, 1F), new(1F, 1F),
                [new(DungeonActorSpriteState.Move, new(6F, true), new(6F, true), 0, 3, layouts)], null, actor.Id);
            DungeonMediaManifestSidecar dungeon = new(1, new([actor]), [], [], [actorManifest]);
            ClassicMediaManifestSidecar classic = new(
                1,
                new([weapon, effect, font]),
                Enum.GetValues<ClassicDaggerWeaponAction>().Select((action, index) => new ClassicWeaponActionManifest(action, index, index, 1, ClassicWeaponScreenAlignment.Right, 0F, new(10F, true), 0, 0)).ToArray(),
                Enum.GetValues<ClassicEffect>().Select((effectValue, index) => new ClassicEffectManifest(effectValue, effect.Id, index, new(10F, false))).ToArray(),
                [], [], [], new(font.Id, 1, 1, Enumerable.Range(0, 240).Select(index => new ClassicFontGlyphMetric(index, index, 0, 1, checked((ushort)index))).ToArray()), []);
            byte[] dungeonBytes = Serialize(dungeon);
            byte[] classicBytes = Serialize(classic);
            ImportProvenance provenance = new(ImportProvenance.CurrentSchemaVersion, "daggerfall-import", 1, [new(LogicalSourceRecord.CurrentSchemaVersion, "arena2/test", ContentDigest.Compute("source"u8), 6, 1)]);
            ImportPublicationArtifact[] artifacts =
            [
                new(actor.RelativePath, actorBytes),
                new(weapon.RelativePath, weaponBytes),
                new(effect.RelativePath, effectBytes),
                new(font.RelativePath, fontBytes),
                new(Arena2MediaBundlePublication.DungeonMediaManifestRelativePath, dungeonBytes, [actor.RelativePath]),
                new(Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, classicBytes, [weapon.RelativePath, effect.RelativePath, font.RelativePath]),
            ];
            ImportPublicationPlan plan = ImportPublicationPlan.Create(provenance, artifacts);
            SpritePublicationSnapshot snapshot = SpritePublicationReader.FromPlan(plan);
            Dictionary<string, (ContentDigest Digest, long Length)> content = snapshot.Catalog.Entries
                .Select(entry => entry.Closure)
                .ToDictionary(closure => closure.RelativePath, closure => (closure.ContentDigest, closure.ByteLength), StringComparer.Ordinal);
            return new(plan, snapshot, content);
        }

        private static NormalizedMediaDescriptor Descriptor(string id, NormalizedMediaKind kind, string path, byte[] bytes, int frameCount) => new(
            id, kind, path, ContentDigest.Compute(bytes), bytes.Length, "image/png", 2, 1, frameCount * 2, 1,
            Enumerable.Range(0, frameCount).Select(index => new NormalizedAtlasFrame($"frame.{index}", index, index * 2, 0, 2, 1, 2, 1, false)).ToArray(),
            null, new(.5F, 0F), new(1F, 1F), 6F, true, null);

        private static byte[] Serialize<T>(T value) => [.. JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), (byte)'\n'];
    }

    private static ContentSha256 ToEngineDigest(ContentDigest digest)
    {
        byte[] bytes = Convert.FromHexString(digest.Value);
        return new(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes), System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8)),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(24)));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict,
    };
}
