using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Checks the score's tracks are read by name from the music container installed beside the product.</summary>
public sealed class DaggerfallMusicBundleTests
{
    [Fact]
    public void Tracks_are_named_by_the_classic_track_in_any_admitted_format_and_case()
    {
        ProductContentBundle container = Container("song_dungeon.ogg", "SONG_04.MP3", "song_gshop.flac", "song_tavern.mid", "notes/song_23.ogg", "worldrpg/data-container.json");

        DaggerfallMusicBundle music = DaggerfallMusicBundle.Read(container);

        Assert.Equal(["song_04", "song_dungeon", "song_gshop"], music.Tracks);
        Assert.True(music.CanPlay("song_04"));
        // A format the Engine does not play, a file in a folder and the container's own description are not tracks.
        Assert.False(music.CanPlay("song_tavern"));
        Assert.False(music.CanPlay("song_23"));
    }

    [Fact]
    public void The_tracks_no_recording_answers_are_named_once()
    {
        DaggerfallMusicBundle music = DaggerfallMusicBundle.Read(Container("song_dungeon.ogg"));

        Assert.Equal(DaggerfallMusicBundle.PlaylistTracks.Count - 1, music.MissingTracks.Count);
        Assert.DoesNotContain("song_dungeon", music.MissingTracks);
        Assert.Contains("song_tavern", music.MissingTracks);
        Assert.Equal(music.MissingTracks.Order(StringComparer.Ordinal), music.MissingTracks);
        // Every playlist the director plays, in either set, names its tracks here.
        foreach (DaggerfallMusicContext context in Enum.GetValues<DaggerfallMusicContext>())
            Assert.All(DaggerfallMusicDirector.PlaylistFor(context, alternatePlaylists: true), track => Assert.Contains(track, DaggerfallMusicBundle.PlaylistTracks));
    }

    [Fact]
    public void Two_recordings_of_one_track_are_refused_by_name()
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => DaggerfallMusicBundle.Read(Container("song_dungeon.ogg", "song_dungeon.mp3")));

        Assert.Contains("'song_dungeon.mp3'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'song_dungeon.ogg'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_track_opens_its_recording_from_the_container_and_an_unrecorded_one_opens_nothing()
    {
        BundleContentFake service = new();
        service.Container(DaggerfallMusicBundle.ContainerName);
        service.Add(DaggerfallMusicBundle.ContainerName, "song_04.mp3", [1, 2, 3]);
        DaggerfallMusicBundle music = DaggerfallMusicBundle.Read(new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallMusicBundle.ContainerName));
        AudioFake audio = AudioFake.Create();

        using AudioClip? clip = music.OpenClip(audio.Service, "song_04");

        Assert.NotNull(clip);
        Assert.Equal([(DaggerfallMusicBundle.ContainerName, "song_04.mp3")], service.OpenedReferences);
        Assert.Null(music.OpenClip(audio.Service, "song_tavern"));
        Assert.Equal(1, audio.Opened);
    }

    [Fact]
    public void A_composition_without_a_music_container_composes_no_score()
    {
        ProductContent content = TestSessions.FullContent(TestData.RepositoryRoot, "worldrpg/imports/privateers-hold");
        ResolvedGameComposition composition = TestSessions.Resolve(content, new GameBundleId("daggerfall.classic")).RequireComposition();

        Assert.DoesNotContain(composition.Installed, installed => installed.Name == DaggerfallMusicBundle.ContainerName);
        Assert.Null(DaggerfallMusicBundle.For(composition));
    }

    private static ProductContentBundle Container(params string[] paths)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallMusicBundle.ContainerName);
        foreach (string path in paths) service.Add(DaggerfallMusicBundle.ContainerName, path, [7]);
        return new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallMusicBundle.ContainerName);
    }

    private class AudioFake : DispatchProxy
    {
        internal IAudioService Service { get; private set; } = null!;
        internal int Opened { get; private set; }

        internal static AudioFake Create()
        {
            IAudioService service = DispatchProxy.Create<IAudioService, AudioFake>();
            AudioFake fake = (AudioFake)(object)service;
            fake.Service = service;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IAudioService.OpenClipFromContent) => new AudioClip(new AudioClipHandle((ulong)++Opened), static () => { }),
            _ => throw new NotSupportedException(method?.Name),
        };
    }
}
