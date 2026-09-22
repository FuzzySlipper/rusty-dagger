using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCinematicPresentationTests
{
    [Fact]
    public void Normal_bundle_reference_is_released_after_play_and_completion_releases_playback()
    {
        Harness state = new();
        using DaggerfallCinematicPresentation presentation = state.Presentation;
        presentation.Play("ANIM0011.VID");
        Assert.Equal("anim0011.webm", Assert.Single(state.Paths));
        Assert.Equal(1, state.ReferencesReleased);
        Assert.Equal(1, state.BundlesReleased);
        Assert.Equal("ANIM0011.VID", presentation.ActiveSource);
        state.Facts.Add(new(true, VideoRealizationFactKind.Completed, 1, new(1), VideoFailureCode.None));
        presentation.Poll();
        Assert.Null(presentation.ActiveSource);
        Assert.Equal(VideoRealizationFactKind.Completed, presentation.LastResult!.Kind);
        Assert.Equal(VideoRealizationFactKind.Completed, presentation.TakeResult()!.Kind);
        Assert.Null(presentation.TakeResult());
        Assert.Equal(1, state.Stops);
    }

    [Fact]
    public void Replacement_ignores_old_completion_and_skip_and_disposal_clean_up_current_playback()
    {
        Harness state = new();
        DaggerfallCinematicPresentation presentation = state.Presentation;
        presentation.Play("ANIM0011.VID");
        presentation.Play("ANIM0011.VID");
        state.Facts.Add(new(true, VideoRealizationFactKind.Completed, 1, new(1), VideoFailureCode.None));
        presentation.Poll();
        Assert.NotNull(presentation.ActiveSource);
        presentation.Skip();
        Assert.Equal(1, state.Skips);
        Assert.Null(presentation.ActiveSource);
        Assert.Equal(VideoRealizationFactKind.Skipped, presentation.LastResult!.Kind);
        presentation.Play("ANIM0011.VID");
        presentation.Dispose();
        Assert.Equal(2, state.Stops);
    }

    [Fact]
    public void Missing_publication_is_explicit_and_native_failure_still_releases_the_content_lease()
    {
        Harness state = new();
        Assert.Contains("no accepted packaged artifact", Assert.Throws<InvalidOperationException>(() => state.Presentation.Play("AZURA.FLC")).Message);
        Assert.Empty(state.Paths);
        state.FailPlay = true;
        Assert.Throws<InvalidOperationException>(() => state.Presentation.Play("ANIM0011.VID"));
        Assert.Equal(1, state.ReferencesReleased);
        Assert.Equal(1, state.BundlesReleased);
        Assert.Null(state.Presentation.ActiveSource);
    }

    [Fact]
    public void Opening_plays_the_donor_order_and_each_terminal_result_advances_once()
    {
        Harness state = new();
        DaggerfallOpeningCinematics opening = new(state.Presentation, videosEnabled: true);
        Assert.Equal(EntryScreenStartupResult.Waiting, opening.Start());
        Assert.Equal(["anim0000.webm"], state.Paths);
        state.Facts.Add(new(true, VideoRealizationFactKind.Completed, 1, new(1), VideoFailureCode.None));
        state.Presentation.Poll();
        opening.Poll();
        Assert.Equal(["anim0000.webm", "anim0011.webm"], state.Paths);
        opening.Skip();
        opening.Poll();
        Assert.Equal(["anim0000.webm", "anim0011.webm", "dag2.webm"], state.Paths);
        state.Facts.Add(new(true, VideoRealizationFactKind.Completed, 2, new(3), VideoFailureCode.None));
        state.Presentation.Poll();
        opening.Poll();
        Assert.True(opening.TakeReady());
        Assert.False(opening.TakeReady());
        Assert.False(opening.IsActive);
        Assert.Equal(EntryScreenStartupResult.ReadyForPlay, opening.Start());
        opening.Poll();
        Assert.False(opening.TakeReady());
        Assert.Equal(3, state.Paths.Count);
    }

    [Fact]
    public void Opening_requires_admitted_media_unless_videos_are_explicitly_disabled()
    {
        DaggerfallOpeningCinematics missing = new(null, videosEnabled: true);
        Assert.Equal(EntryScreenStartupResult.Failed, missing.Start());
        Assert.Contains("unavailable", missing.Failure!.Failure, StringComparison.OrdinalIgnoreCase);
        DaggerfallOpeningCinematics disabled = new(null, videosEnabled: false);
        Assert.Equal(EntryScreenStartupResult.ReadyForPlay, disabled.Start());
        Assert.True(disabled.TakeReady());
        Assert.False(disabled.TakeReady());
    }

    private sealed class Harness
    {
        internal readonly List<string> Paths = [];
        internal readonly List<VideoRealizationFactAtReceipt> Facts = [];
        internal int ReferencesReleased, BundlesReleased, Stops, Skips;
        internal bool FailPlay;
        private ulong nextHandle;
        internal DaggerfallCinematicPresentation Presentation { get; }

        internal Harness()
        {
            IVideoService video = Proxy.Create<IVideoService>((method, args) => method switch
            {
                nameof(IVideoService.PlayFromContent) => FailPlay ? throw new InvalidOperationException("Rejected video") : new VideoPlaybackHandle(++nextHandle),
                nameof(IVideoService.ReadRealization) => new VideoRealizationReadout((uint)Facts.Count, 0),
                nameof(IVideoService.ReadRealizationFactAt) => Facts[(int)((VideoRealizationFactAtRequest)args![0]!).Index],
                nameof(IVideoService.Stop) => Stop(),
                nameof(IVideoService.Skip) => Skip(),
                _ => throw new NotSupportedException(method),
            });
            IContentService content = Proxy.Create<IContentService>((method, args) => method switch
            {
                nameof(IContentService.ListBundles) => (ReadOnlyMemory<ContentBundleInfo>)new[] { new ContentBundleInfo(DaggerfallCinematicPresentation.BundleId, 1, 1) },
                nameof(IContentService.OpenBundle) => OpenBundle((ContentBundleOpenRequest)args![0]!),
                nameof(IContentService.ReadBundleFiles) => (ReadOnlyMemory<ContentReferenceInfo>)new[]
                {
                    new ContentReferenceInfo("anim0000.webm", default, 1),
                    new ContentReferenceInfo("anim0011.webm", default, 1),
                    new ContentReferenceInfo("dag2.webm", default, 1),
                },
                nameof(IContentService.OpenBundleReference) => OpenReference((ContentBundleReferenceRequest)args![0]!),
                _ => throw new NotSupportedException(method),
            });
            IEngineContext engine = Proxy.Create<IEngineContext>((method, _) => method == "get_Video" ? video : throw new NotSupportedException(method));
            DaggerfallCinematicDefinition Published(string source) => new(source, DaggerfallCinematicKind.Vid, 1, "source-digest", DaggerfallCinematicBinding.Bound, "new-game opening", null, "")
            { Artifact = new($"worldrpg/media/cinematics/{Path.GetFileNameWithoutExtension(source).ToLowerInvariant()}.webm", "video/webm", 1, "artifact-digest", 320, 200, 35, 5.562, true) };
            DaggerfallCinematicDefinition published = Published("ANIM0011.VID");
            DaggerfallCinematicSet catalog = new(new Dictionary<string, DaggerfallCinematicDefinition>
            {
                ["ANIM0000.VID"] = Published("ANIM0000.VID"),
                [published.FileName] = published,
                ["DAG2.VID"] = Published("DAG2.VID"),
                ["AZURA.FLC"] = new("AZURA.FLC", DaggerfallCinematicKind.Flc, 1, "source-digest", DaggerfallCinematicBinding.Bound, "Daedric summons", 16, "T0C00Y00"),
            });
            Presentation = new(engine, new ProductContent(Array.Empty<ProductContentFile>(), content), catalog);
        }

        private ContentBundle OpenBundle(ContentBundleOpenRequest request)
        {
            Assert.Equal(DaggerfallCinematicPresentation.BundleId, request.Id);
            return new(new ContentBundleHandle(1), () => BundlesReleased++);
        }
        private ContentReference OpenReference(ContentBundleReferenceRequest request)
        {
            Paths.Add(request.Path);
            return new(new ContentReferenceHandle(1), () => ReferencesReleased++);
        }
        private object? Stop() { Stops++; return null; }
        private object? Skip() { Skips++; return null; }
    }

    public class Proxy : DispatchProxy
    {
        private Func<string, object?[]?, object?> call = null!;
        internal static T Create<T>(Func<string, object?[]?, object?> callback) where T : class
        {
            T result = Create<T, Proxy>();
            ((Proxy)(object)result).call = callback;
            return result;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => call(targetMethod!.Name, args);
    }
}
