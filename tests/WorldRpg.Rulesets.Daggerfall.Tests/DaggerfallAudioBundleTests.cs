using System.Reflection;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Checks the audio bodies are read from the data container only when a cue first plays.</summary>
public sealed class DaggerfallAudioBundleTests
{
    // The product-wide world audio every site's sidecar names, carried by the data container.
    private const string SiteBundle = DaggerfallData.DataContainerName;
    private const string SiteRoot = DaggerfallWorldMedia.AudioRoot + "/";

    [Fact]
    public void Opens_the_exact_named_audio_resource_without_admitting_its_body_to_the_snapshot()
    {
        const string mediaId = "swing";
        const string contentPath = SiteRoot + "audio-melee-dagger-swing.wav";
        const string bundlePath = "audio-melee-dagger-swing.wav";
        BundleContentFake contentService = BundleContentFake.Create(bundlePath);
        ProductContent content = new(
            new ProductContentFile[] { new(Encoding.UTF8.GetBytes("worldrpg/media/classic-media-inventory.json"), "inventory"u8.ToArray()) },
            contentService.Service);
        DaggerfallAudioBundle audio = new(TestSessions.Data(content), DaggerfallWorldMedia.AudioRoot, [new NormalizedAudioClip(mediaId, contentPath, default)]);
        AudioFake engineAudio = AudioFake.Create();

        // The WAV is not one of ProductContent's eager files, and nothing below its root is referenced before a cue.
        Assert.False(content.TryReadFile(contentPath, out _));
        Assert.Empty(contentService.OpenReferenceRequests);

        using AudioClip clip = audio.OpenClip(engineAudio.Service, mediaId);

        Assert.Equal([SiteBundle], contentService.OpenBundleRequests);
        Assert.Equal([contentPath], contentService.OpenReferenceRequests);
        Assert.Equal(1, engineAudio.OpenedFromContent);
        Assert.Equal(0, contentService.ReadBodyRequests);
    }

    [Fact]
    public void Rejects_an_unknown_media_identity_before_opening_any_bundle()
    {
        BundleContentFake contentService = BundleContentFake.Create("audio-melee-dagger-swing.wav");
        ProductContent content = new(Array.Empty<ProductContentFile>(), contentService.Service);
        DaggerfallAudioBundle audio = new(TestSessions.Data(content), DaggerfallWorldMedia.AudioRoot, [new NormalizedAudioClip("audio.melee.dagger.swing", SiteRoot + "audio-melee-dagger-swing.wav", default)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => audio.OpenClip(AudioFake.Create().Service, "audio.no-such-clip"));

        Assert.Contains("audio.no-such-clip", error.Message, StringComparison.Ordinal);
        Assert.Empty(contentService.OpenReferenceRequests);
    }

    [Fact]
    public void Composed_appearance_defers_audio_until_its_first_cue_then_reuses_and_releases_the_clip()
    {
        const string mediaId = "swing";
        const string contentPath = SiteRoot + "audio-melee-dagger-swing.wav";
        const string bundlePath = "audio-melee-dagger-swing.wav";
        BundleContentFake contentService = BundleContentFake.Create(bundlePath);
        ProductContent content = new(Array.Empty<ProductContentFile>(), contentService.Service);
        DaggerfallAudioBundle audioBundle = new(TestSessions.Data(content), DaggerfallWorldMedia.AudioRoot,
        [
            new NormalizedAudioClip(mediaId, contentPath, default),
            new NormalizedAudioClip("hit1", SiteRoot + "audio-melee-hit-1.wav", default),
        ]);
        AudioFake audio = AudioFake.Create();
        IGraphicsService graphics = GraphicsFake.Create();
        DaggerfallSiteProfile inputs = new(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/hold.json", default, 1),
            new ContentArtifact("mesh/hold.json", default)),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene),
            new PlayerInitialLook(0F, 0F),
            [],
            new Dictionary<long, NormalizedActorSprite>(),
            mobileSprites: null,
            [
                new NormalizedAudioClip(mediaId, contentPath, default),
                new NormalizedAudioClip("hit1", SiteRoot + "audio-melee-hit-1.wav", default),
            ]);

        DaggerfallSiteAppearance appearance = new(contentService.Service, graphics, inputs, audio.Service, audioBundle: audioBundle);
        try
        {
            Assert.Empty(contentService.OpenReferenceRequests);
            Assert.Equal(0, audio.OpenedFromContent);

            appearance.React(new PlayerAttackStartedFact(7, 11));
            appearance.React(new PlayerAttackStartedFact(7, 11));
            appearance.React(new PlayerAttackStartedFact(7, 12));

            Assert.Equal([SiteBundle], contentService.OpenBundleRequests);
            Assert.Equal([contentPath], contentService.OpenReferenceRequests);
            Assert.Equal(1, audio.OpenedFromContent);
            Assert.Equal(2, audio.Emitted);
        }
        finally
        {
            Assert.Throws<AggregateException>(appearance.Dispose);
        }

        Assert.Equal(1, audio.ReleasedClips);
    }

    [Theory]
    [InlineData(false, 100, 100, false, "sound.205")]
    [InlineData(false, 100, 0, false, "sound.206")]
    [InlineData(true, 100, 100, false, "sound.199")]
    [InlineData(true, 100, 0, false, "sound.200")]
    [InlineData(false, 0, 100, false, "sound.205")]
    [InlineData(false, 100, 100, true, "sound.205")]
    public void Vampire_voice_uses_the_admitted_swing_not_damage_and_honors_voice_gate_and_bow_exclusion(
        bool female, int voiceChance, int barkChance, bool bow, string expected)
    {
        const string path = "voice.wav";
        var content = BundleContentFake.Create(path);
        var clips = new[] { new NormalizedAudioClip(expected, SiteRoot + path, default),
            new NormalizedAudioClip("sound.3", SiteRoot + path, default),
            new NormalizedAudioClip("hit1", SiteRoot + path, default) };
        var bundle = new DaggerfallAudioBundle(TestSessions.Data(new ProductContent(Array.Empty<ProductContentFile>(), content.Service)), DaggerfallWorldMedia.AudioRoot, clips);
        var audio = AudioFake.Create();
        var inputs = new DaggerfallSiteProfile(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/hold.json", default, 1), new ContentArtifact("mesh/hold.json", default)),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [],
            new Dictionary<long, NormalizedActorSprite>(), mobileSprites: null, clips);
        var appearance = new DaggerfallSiteAppearance(content.Service, GraphicsFake.Create(), inputs, audio.Service,
            DaggerfallTuning.Defaults.PresentationAudio with { VampireAttackChancePercent = voiceChance, VampireBarkChancePercent = barkChance }, audioBundle: bundle);
        appearance.UsePlayerVampireGender(() => female);
        var started = new PlayerAttackStartedFact(7, 11) { Feedback = new(false, bow ? "sound.3" : "") };
        try
        {
            // No target or damaging outcome is needed; a composition without a viewmodel admits
            // its attack frame immediately. Re-delivery and later miss/contact cannot duplicate it.
            appearance.React(started);
            appearance.React(started);
            int expectedEmissions = bow || voiceChance > 0 ? 1 : 0;
            Assert.Equal(expectedEmissions, audio.Emitted);
            appearance.React(new AttackMissedFact(1, 2, 100, 10, false, 7, 11) { Feedback = new(false, "") });
            appearance.React(new AttackHitFact(1, 2, 1, 0, 0, false, 7, 11) { Feedback = new(false, "") });
            Assert.Equal(expectedEmissions, audio.Emitted);
        }
        finally
        {
            Assert.Throws<AggregateException>(appearance.Dispose);
        }
    }

    private class BundleContentFake : DispatchProxy
    {
        private string bundlePath = null!;
        internal IContentService Service { get; private set; } = null!;
        internal List<string> OpenBundleRequests { get; } = [];
        internal List<string> OpenReferenceRequests { get; } = [];
        internal int ReadBodyRequests { get; private set; }

        internal static BundleContentFake Create(string bundlePath)
        {
            IContentService service = DispatchProxy.Create<IContentService, BundleContentFake>();
            BundleContentFake fake = (BundleContentFake)(object)service;
            fake.Service = service;
            fake.bundlePath = bundlePath;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IContentService.OpenContainer) => Open(((ContentContainerOpenRequest)arguments![0]!).Path),
            nameof(IContentService.ReadBundleIdentity) => TestSessions.Digest(Encoding.UTF8.GetBytes(bundlePath)),
            nameof(IContentService.ReadBundleFiles) => (ReadOnlyMemory<ContentReferenceInfo>)new[] { new ContentReferenceInfo(SiteRoot + bundlePath, default, 1) },
            nameof(IContentService.OpenBundleReference) => OpenReference((ContentBundleReferenceRequest)arguments![0]!),
            nameof(IContentService.ReadBytes) => ReadBytes(),
            // The site's own mesh, which the appearance opens by path and digest; these tests read no body of it.
            nameof(IContentService.ResolveReference) => new ContentReference(new ContentReferenceHandle(2), static () => { }),
            _ => throw new NotSupportedException(method?.Name),
        };

        private ContentBundle Open(string container)
        {
            OpenBundleRequests.Add(container);
            if (container != SiteBundle)
                throw new EngineCallException("content", "open_container", 1, new[] { new EngineDiagnostic("PRODUCT_SOURCE_IO", $"No container '{container}'.", "test") });
            return new ContentBundle(new ContentBundleHandle(1), static () => { });
        }

        private ContentReference OpenReference(ContentBundleReferenceRequest request)
        {
            OpenReferenceRequests.Add(request.Path);
            if (request.Path != SiteRoot + bundlePath) throw new FileNotFoundException("Unexpected container audio resource.", request.Path);
            return new ContentReference(new ContentReferenceHandle(1), static () => { });
        }

        private ReadOnlyMemory<byte> ReadBytes()
        {
            ReadBodyRequests++;
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private class AudioFake : DispatchProxy
    {
        internal IAudioService Service { get; private set; } = null!;
        internal int OpenedFromContent { get; private set; }
        internal int Emitted { get; private set; }
        internal int ReleasedClips { get; private set; }

        internal static AudioFake Create()
        {
            IAudioService service = DispatchProxy.Create<IAudioService, AudioFake>();
            AudioFake fake = (AudioFake)(object)service;
            fake.Service = service;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IAudioService.OpenClipFromContent) => Open(),
            nameof(IAudioService.Emit) => Emit(),
            _ => throw new NotSupportedException(method?.Name),
        };

        private AudioClip Open()
        {
            OpenedFromContent++;
            return new AudioClip(new AudioClipHandle((ulong)OpenedFromContent), () => ReleasedClips++);
        }

        private AudioSignalHandle Emit() => new((ulong)++Emitted);
    }

    private class GraphicsFake : DispatchProxy
    {
        internal static IGraphicsService Create() => DispatchProxy.Create<IGraphicsService, GraphicsFake>();

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IGraphicsService.CreateStaticMeshFromContentReference) => new Appearance(new AppearanceHandle(1), static () => { }),
            nameof(IGraphicsService.UpdateStaticMeshMaterials) or nameof(IGraphicsService.PublishSnapshot) => null,
            _ => throw new NotSupportedException(method?.Name),
        };
    }
}
