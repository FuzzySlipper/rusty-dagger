using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Checks the site cue list is joined against the one published music manifest.</summary>
public sealed class DaggerfallMusicBundleTests
{
    [Fact]
    public void The_bundle_the_product_opens_is_the_one_the_host_declares_and_its_manifest_sits_above_it()
    {
        // The Engine stages a bundle only when the Host declares it, and the product opens it by the id
        // this owner holds. Two spellings of the same id are a product that loads and a first cue that
        // fails, so the declaration is pinned against the constants here rather than discovered by running.
        string project = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/WorldRpg.Host/WorldRpg.Host.csproj"));
        Assert.Contains(
            $"Include=\"{DaggerfallMusicBundle.BundleId}\" Root=\"{DaggerfallMusicBundle.LogicalRoot}\"",
            project,
            StringComparison.Ordinal);
        // The manifest is read eagerly, so it may not live inside the bundle root that stages lazily.
        Assert.Equal(DaggerfallMusicBundle.LogicalRoot + "/", DaggerfallMusicBundle.ContentRoot, StringComparer.Ordinal);
        Assert.Equal("worldrpg/media/music/manifest.json", DaggerfallMusicBundle.ManifestPath);
        Assert.DoesNotContain(DaggerfallMusicBundle.LogicalRoot, DaggerfallMusicBundle.ManifestPath, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }

    [Fact]
    public void A_site_that_names_no_cue_admits_nothing_and_reads_no_manifest()
    {
        ProductContent content = new(Array.Empty<ProductContentFile>());

        Assert.Null(DaggerfallMusicBundle.Admit(content, []));
    }

    [Fact]
    public void Admitted_cues_answer_by_donor_track_through_the_product_wide_music_bundle()
    {
        PublishedCue dungeon = MusicCue("music.dungeon", "song_dungeon", "dungeon", "song_dungeon.ogg", 5985696);
        PublishedCue sunny = MusicCue("music.sunny", "song_gday___d", "sunny", "song_gday___d.ogg", 3472545);
        MusicContentFake contentService = MusicContentFake.Create("song_dungeon.ogg", "song_gday___d.ogg");
        ProductContent content = Content(Manifest([dungeon, sunny]), contentService.Service);

        DaggerfallMusicBundle? admitted = DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon), SiteCue(sunny)]);

        DaggerfallMusicBundle music = Assert.IsType<DaggerfallMusicBundle>(admitted);
        Assert.Equal(["song_dungeon", "song_gday___d"], music.Tracks);
        Assert.True(music.CanPlay("song_dungeon"));
        Assert.False(music.CanPlay("song_02"));

        AudioFake audio = AudioFake.Create();
        using AudioClip clip = Assert.IsType<AudioClip>(music.OpenClip(audio.Service, "song_gday___d"));
        Assert.Equal(["daggerfall.music"], contentService.OpenBundleRequests);
        Assert.Equal(["song_gday___d.ogg"], contentService.OpenReferenceRequests);
        Assert.Equal(1, audio.OpenedFromContent);
        Assert.Null(music.OpenClip(audio.Service, "song_02"));
    }

    [Fact]
    public void A_site_cue_that_disagrees_with_the_published_manifest_is_refused()
    {
        PublishedCue dungeon = MusicCue("music.dungeon", "song_dungeon", "dungeon", "song_dungeon.ogg", 5985696);
        PublishedCue sunny = MusicCue("music.sunny", "song_gday___d", "sunny", "song_gday___d.ogg", 3472545);
        ProductContent content = Content(Manifest([dungeon, sunny]));

        InvalidOperationException length = Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon with { ByteLength = dungeon.ByteLength + 1 })]));
        Assert.Contains(dungeon.MediaId, length.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon with { Digest = Digest("another-encode") })]));
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon with { Track = "song_other" })]));
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon with { Context = "sunny" })]));
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon with { File = "song_other.ogg" })]));
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon with { MimeType = "audio/mpeg" })]));
        // Every other field agrees with the carried cue, so only the unnamed media identity can refuse this one.
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(content, [SiteCue(dungeon with { MediaId = "music.dungeon.alt", Digest = dungeon.Digest })]));
    }

    [Fact]
    public void A_site_that_names_cues_without_a_published_manifest_of_the_generated_shape_is_refused()
    {
        PublishedCue dungeon = MusicCue("music.dungeon", "song_dungeon", "dungeon", "song_dungeon.ogg", 5985696);
        NormalizedMusicCue siteCue = SiteCue(dungeon);

        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(new ProductContent(Array.Empty<ProductContentFile>()), [siteCue]));
        Assert.Contains("worldrpg/media/music/manifest.json", missing.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(Content(Manifest([dungeon], "someone-elses-tool")), [siteCue]));
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(Content(JsonSerializer.SerializeToUtf8Bytes(new { generator = DaggerfallMusicBundle.ManifestGenerator })), [siteCue]));
        Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(Content("not json"u8.ToArray()), [siteCue]));

        // The site names the same container the manifest does: the manifest's own cue is the thing
        // refused here, not the site's agreement with it.
        InvalidOperationException container = Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(
            Content(Manifest([dungeon with { MimeType = "audio/mpeg" }])),
            [SiteCue(dungeon with { MimeType = "audio/mpeg" })]));
        Assert.Contains("audio/mpeg", container.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_written_manifest_is_read_as_strictly_as_the_published_one()
    {
        PublishedCue dungeon = MusicCue("music.dungeon", "song_dungeon", "dungeon", "song_dungeon.ogg", 5985696);
        NormalizedMusicCue site = SiteCue(dungeon);

        // A member the record does not declare, and a member stated twice, are mistakes rather than
        // fields to ignore: this reader is the only one a hand-written content pack can reach.
        byte[] unknown = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Manifest([dungeon])).Replace("\"context\"", "\"contexts\"", StringComparison.Ordinal));
        Assert.Contains("contexts", Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(Content(unknown), [site])).Message, StringComparison.Ordinal);

        byte[] repeated = JsonSerializer.SerializeToUtf8Bytes(new
        {
            generator = "daggerfall-import-tool music-media",
            cues = new[] { new { mediaId = dungeon.MediaId, track = dungeon.Track, context = dungeon.Context, file = dungeon.File, mimeType = dungeon.MimeType, byteLength = dungeon.ByteLength, contentDigest = dungeon.Digest, extra = 1 } },
        });
        Assert.Contains("extra", Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(Content(repeated), [site])).Message, StringComparison.Ordinal);

        // The shape failures name the manifest rather than surfacing a raw cast error.
        byte[] wrongGeneratorType = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Manifest([dungeon])).Replace("\"generator\":\"daggerfall-import-tool music-media\"", "\"generator\":42", StringComparison.Ordinal));
        Assert.Contains("manifest", Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(Content(wrongGeneratorType), [site])).Message, StringComparison.Ordinal);

        byte[] traversing = Manifest([dungeon with { File = "../song_dungeon.ogg" }]);
        Assert.Contains("does not carry", Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Admit(Content(traversing), [SiteCue(dungeon with { File = "../song_dungeon.ogg" })])).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Serves the manifest the way the publication writes it: one eager file above the bundle root,
    /// which is why the path is spelled here rather than read from the bundle it does not belong to.
    /// </summary>
    private static ProductContent Content(byte[] manifest, IContentService? service = null) => new(
        new[] { new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/media/music/manifest.json"), manifest) },
        service);

    /// <summary>
    /// Builds the manifest the way the publication writes it: the generator name is spelled here because
    /// the importer carries its own copy of it, so a drift between the two would silence every site.
    /// </summary>
    private static byte[] Manifest(IReadOnlyList<PublishedCue> cues, string generator = "daggerfall-import-tool music-media") =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            generator,
            cues = cues.Select(cue => new
            {
                mediaId = cue.MediaId,
                track = cue.Track,
                context = cue.Context,
                file = cue.File,
                mimeType = cue.MimeType,
                byteLength = cue.ByteLength,
                contentDigest = cue.Digest,
            }).ToArray(),
        });

    private static PublishedCue MusicCue(string mediaId, string track, string context, string file, long byteLength) =>
        new(mediaId, track, context, file, DaggerfallMusicBundle.MimeType, byteLength, Digest(mediaId));

    private static NormalizedMusicCue SiteCue(PublishedCue published) => new(
        published.MediaId,
        published.Track,
        published.Context,
        published.File,
        published.MimeType,
        published.ByteLength,
        DaggerfallContentHash.Parse(published.Digest, $"site music cue '{published.MediaId}'"));

    private static string Digest(string seed) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    private sealed record PublishedCue(string MediaId, string Track, string Context, string File, string MimeType, long ByteLength, string Digest);

    private class MusicContentFake : DispatchProxy
    {
        private string[] clipPaths = null!;
        internal IContentService Service { get; private set; } = null!;
        internal List<string> OpenBundleRequests { get; } = [];
        internal List<string> OpenReferenceRequests { get; } = [];

        internal static MusicContentFake Create(params string[] clipPaths)
        {
            IContentService service = DispatchProxy.Create<IContentService, MusicContentFake>();
            MusicContentFake fake = (MusicContentFake)(object)service;
            fake.Service = service;
            fake.clipPaths = clipPaths;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IContentService.OpenBundle) => Open((ContentBundleOpenRequest)arguments![0]!),
            nameof(IContentService.ReadBundleFiles) => (ReadOnlyMemory<ContentReferenceInfo>)clipPaths
                .Select(path => new ContentReferenceInfo(path, default, 1))
                .ToArray(),
            nameof(IContentService.OpenBundleReference) => OpenReference((ContentBundleReferenceRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private ContentBundle Open(ContentBundleOpenRequest request)
        {
            OpenBundleRequests.Add(request.Id);
            if (request.Id != DaggerfallMusicBundle.BundleId) throw new FileNotFoundException("Unexpected bundle.", request.Id);
            return new ContentBundle(new ContentBundleHandle(1), static () => { });
        }

        private ContentReference OpenReference(ContentBundleReferenceRequest request)
        {
            OpenReferenceRequests.Add(request.Path);
            if (!clipPaths.Contains(request.Path, StringComparer.Ordinal)) throw new FileNotFoundException("Unexpected bundled music clip.", request.Path);
            return new ContentReference(new ContentReferenceHandle(1), static () => { });
        }
    }

    private class AudioFake : DispatchProxy
    {
        internal IAudioService Service { get; private set; } = null!;
        internal int OpenedFromContent { get; private set; }

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
            _ => throw new NotSupportedException(method?.Name),
        };

        private AudioClip Open()
        {
            OpenedFromContent++;
            return new AudioClip(new AudioClipHandle((ulong)OpenedFromContent), static () => { });
        }
    }
}
