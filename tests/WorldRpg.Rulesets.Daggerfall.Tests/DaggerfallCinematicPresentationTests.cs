using System.Reflection;
using Rusty.Engine;
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
                nameof(IContentService.ReadBundleFiles) => (ReadOnlyMemory<ContentReferenceInfo>)new[] { new ContentReferenceInfo("anim0011.webm", default, 1) },
                nameof(IContentService.OpenBundleReference) => OpenReference((ContentBundleReferenceRequest)args![0]!),
                _ => throw new NotSupportedException(method),
            });
            IEngineContext engine = Proxy.Create<IEngineContext>((method, _) => method == "get_Video" ? video : throw new NotSupportedException(method));
            DaggerfallCinematicDefinition published = new("ANIM0011.VID", DaggerfallCinematicKind.Vid, 1, "source-digest", DaggerfallCinematicBinding.Bound, "new-game opening", null, "")
            { Artifact = new("worldrpg/media/cinematics/anim0011.webm", "video/webm", 1, "artifact-digest", 320, 200, 35, 5.562, true) };
            DaggerfallCinematicSet catalog = new(new Dictionary<string, DaggerfallCinematicDefinition>
            {
                [published.FileName] = published,
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
