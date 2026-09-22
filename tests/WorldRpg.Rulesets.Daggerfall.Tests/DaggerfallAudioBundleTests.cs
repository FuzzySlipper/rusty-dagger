using System.Reflection;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Checks the audio bodies are an Engine bundle while catalog metadata remains eager.</summary>
public sealed class DaggerfallAudioBundleTests
{
    [Fact]
    public void Opens_the_exact_named_audio_resource_without_admitting_its_body_to_the_snapshot()
    {
        const string mediaId = "swing";
        const string contentPath = "worldrpg/imports/privateers-hold/media/audio/clips/audio-melee-dagger-swing.wav";
        const string bundlePath = "audio-melee-dagger-swing.wav";
        BundleContentFake contentService = BundleContentFake.Create(bundlePath);
        ProductContent content = new(
            new ProductContentFile[] { new(Encoding.UTF8.GetBytes("worldrpg/media/audio/classic-sound-catalog.json"), "catalog"u8.ToArray()) },
            contentService.Service);
        DaggerfallAudioBundle audio = new(content, [new NormalizedAudioClip(mediaId, contentPath, default)]);
        AudioFake engineAudio = AudioFake.Create();

        // Metadata discovery does not make the WAV one of ProductContent's eager files.
        Assert.Contains(content.ListBundles(), bundle => bundle.Id == DaggerfallAudioBundle.BundleId);
        Assert.False(content.TryReadFile(contentPath, out _));
        Assert.Empty(contentService.OpenBundleRequests);

        using AudioClip clip = audio.OpenClip(engineAudio.Service, mediaId);

        Assert.Equal([DaggerfallAudioBundle.BundleId], contentService.OpenBundleRequests);
        Assert.Equal([bundlePath], contentService.OpenReferenceRequests);
        Assert.Equal(1, engineAudio.OpenedFromContent);
        Assert.Equal(0, contentService.ReadBodyRequests);
    }

    [Fact]
    public void Rejects_an_unknown_media_identity_before_opening_any_bundle()
    {
        BundleContentFake contentService = BundleContentFake.Create("audio-melee-dagger-swing.wav");
        ProductContent content = new(Array.Empty<ProductContentFile>(), contentService.Service);
        DaggerfallAudioBundle audio = new(content, [new NormalizedAudioClip("audio.melee.dagger.swing", "worldrpg/imports/privateers-hold/media/audio/clips/audio-melee-dagger-swing.wav", default)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => audio.OpenClip(AudioFake.Create().Service, "audio.no-such-clip"));

        Assert.Contains("audio.no-such-clip", error.Message, StringComparison.Ordinal);
        Assert.Empty(contentService.OpenBundleRequests);
    }

    [Fact]
    public void Composed_appearance_defers_audio_until_its_first_cue_then_reuses_and_releases_the_clip()
    {
        const string mediaId = "swing";
        const string contentPath = "worldrpg/imports/privateers-hold/media/audio/clips/audio-melee-dagger-swing.wav";
        const string bundlePath = "audio-melee-dagger-swing.wav";
        BundleContentFake contentService = BundleContentFake.Create(bundlePath);
        ProductContent content = new(Array.Empty<ProductContentFile>(), contentService.Service);
        DaggerfallAudioBundle audioBundle = new(content,
        [
            new NormalizedAudioClip(mediaId, contentPath, default),
            new NormalizedAudioClip("hit1", "worldrpg/imports/privateers-hold/media/audio/clips/audio-melee-hit-1.wav", default),
        ]);
        AudioFake audio = AudioFake.Create();
        IGraphicsService graphics = GraphicsFake.Create();
        PrivateersHoldInputs inputs = new(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/hold.json", default, 1),
            new ContentArtifact("mesh/hold.json", default),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene),
            new PlayerInitialLook(0F, 0F),
            [],
            new Dictionary<long, NormalizedActorSprite>(),
            [
                new NormalizedAudioClip(mediaId, contentPath, default),
                new NormalizedAudioClip("hit1", "worldrpg/imports/privateers-hold/media/audio/clips/audio-melee-hit-1.wav", default),
            ]);

        PrivateersHoldAppearance appearance = new(contentService.Service, graphics, inputs, audio.Service, audioBundle: audioBundle);
        try
        {
            Assert.Empty(contentService.OpenBundleRequests);
            Assert.Equal(0, audio.OpenedFromContent);

            appearance.React(new PlayerAttackStartedFact(7, 11));
            appearance.React(new PlayerAttackStartedFact(7, 11));
            appearance.React(new PlayerAttackStartedFact(7, 12));

            Assert.Equal([DaggerfallAudioBundle.BundleId], contentService.OpenBundleRequests);
            Assert.Equal([bundlePath], contentService.OpenReferenceRequests);
            Assert.Equal(1, audio.OpenedFromContent);
            Assert.Equal(2, audio.Emitted);
        }
        finally
        {
            Assert.Throws<AggregateException>(appearance.Dispose);
        }

        Assert.Equal(1, audio.ReleasedClips);
    }

    [Fact]
    public void Published_catalog_admissions_close_over_the_generated_audio_bundle_paths()
    {
        string root = RepositoryRoot();
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/media/audio/classic-sound-catalog.json")));
        using JsonDocument inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/media/classic-media-inventory.json")));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/classic/manifest.json")));

        Dictionary<string, string> publicPaths = inventory.RootElement.GetProperty("artifacts").EnumerateArray()
            .Where(entry => entry.TryGetProperty("mediaId", out JsonElement id) && id.ValueKind == JsonValueKind.String)
            .ToDictionary(entry => entry.GetProperty("mediaId").GetString()!, entry => entry.GetProperty("path").GetString()!, StringComparer.Ordinal);
        Dictionary<string, string> importedPaths = manifest.RootElement.GetProperty("media").GetProperty("resources").EnumerateArray()
            .Where(resource => resource.GetProperty("kind").GetString() == "audio")
            .ToDictionary(resource => resource.GetProperty("id").GetString()!, resource => resource.GetProperty("relativePath").GetString()!, StringComparer.Ordinal);
        string[] admitted = [.. catalog.RootElement.GetProperty("clips").EnumerateArray()
            .Where(clip => clip.GetProperty("disposition").GetString() == "admitted")
            .Select(clip => clip.GetProperty("mediaId").GetString()!)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(admitted, publicPaths.Keys.Where(id => id.StartsWith("audio.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(admitted, importedPaths.Keys.Order(StringComparer.Ordinal));
        Assert.All(admitted, id =>
        {
            Assert.StartsWith("worldrpg/media/audio/clips/", publicPaths[id], StringComparison.Ordinal);
            Assert.StartsWith("media/audio/clips/", importedPaths[id], StringComparison.Ordinal);
        });
        Assert.True(File.Exists(Path.Combine(root, "content/worldrpg/media/audio/classic-sound-catalog.json")));
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AGENTS.md"))) directory = Path.GetDirectoryName(directory);
        return directory ?? throw new InvalidOperationException("The repository root was not found above the test output directory.");
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
            nameof(IContentService.ListBundles) => (ReadOnlyMemory<ContentBundleInfo>)new[] { new ContentBundleInfo(DaggerfallAudioBundle.BundleId, 1, 1) },
            nameof(IContentService.OpenBundle) => Open((ContentBundleOpenRequest)arguments![0]!),
            nameof(IContentService.ReadBundleFiles) => (ReadOnlyMemory<ContentReferenceInfo>)new[] { new ContentReferenceInfo(bundlePath, default, 1) },
            nameof(IContentService.OpenBundleReference) => OpenReference((ContentBundleReferenceRequest)arguments![0]!),
            nameof(IContentService.ReadBytes) => ReadBytes(),
            _ => throw new NotSupportedException(method?.Name),
        };

        private ContentBundle Open(ContentBundleOpenRequest request)
        {
            OpenBundleRequests.Add(request.Id);
            if (request.Id != DaggerfallAudioBundle.BundleId) throw new FileNotFoundException("Unexpected bundle.", request.Id);
            return new ContentBundle(new ContentBundleHandle(1), static () => { });
        }

        private ContentReference OpenReference(ContentBundleReferenceRequest request)
        {
            OpenReferenceRequests.Add(request.Path);
            if (request.Path != bundlePath) throw new FileNotFoundException("Unexpected bundled audio resource.", request.Path);
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
            nameof(IGraphicsService.CreateStaticMeshFromContent) => new Appearance(new AppearanceHandle(1), static () => { }),
            nameof(IGraphicsService.UpdateStaticMeshMaterials) or nameof(IGraphicsService.PublishSnapshot) => null,
            _ => throw new NotSupportedException(method?.Name),
        };
    }
}
