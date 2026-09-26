using System.Text;
using System.Text.Json;
using Daggerfall.Import.Audio;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ClassicMusicPublicationTests
{
    [Fact]
    public void A_cue_the_supplied_folder_does_not_answer_is_warned_about_and_never_substituted()
    {
        ClassicMusicCue[] answered = [.. ClassicMusicCatalogue.All.Take(3)];
        ClassicMusicCue[] missing = [.. ClassicMusicCatalogue.All.Skip(3)];
        ClassicMusicSource[] sources = [.. answered.Select(cue => new ClassicMusicSource(cue, Payload(cue.MediaId)))];

        ClassicMusicPublicationResult publication = ClassicMusicPublication.Create(ClassicMusicCatalogue.All, sources);

        Assert.Equal(answered.Select(cue => cue.MediaId), publication.Manifest.Cues.Select(record => record.MediaId));
        foreach (ClassicMusicCue cue in answered)
        {
            ImportPublicationArtifact clip = publication.Artifacts.Single(artifact => artifact.RelativePath == $"media/music/clips/{cue.SourceFile}");
            Assert.Equal(cue.MediaId, clip.MediaId);
            Assert.Equal(Payload(cue.MediaId), clip.Bytes.ToArray());
        }

        Assert.Single(publication.Artifacts, artifact => artifact.RelativePath == "media/music/manifest.json");
        Assert.Equal(answered.Length + 1, publication.Artifacts.Count);
        foreach (ClassicMusicCue cue in missing)
        {
            // The quotes keep 'music.sunny.fm' from matching the warning for 'music.sunny.fm.second'.
            string warning = Assert.Single(publication.Warnings, warning => warning.Contains($"'{cue.MediaId}'", StringComparison.Ordinal));
            Assert.Contains("is not published", warning, StringComparison.Ordinal);
            Assert.Contains(cue.SourceFile, warning, StringComparison.Ordinal);
            Assert.DoesNotContain(publication.Artifacts, artifact => artifact.RelativePath == $"media/music/clips/{cue.SourceFile}");
            Assert.DoesNotContain(publication.Manifest.Cues, record => record.MediaId == cue.MediaId);
        }
    }

    [Fact]
    public void A_source_whose_length_differs_from_the_catalogues_record_is_published_with_both_lengths_reported()
    {
        ClassicMusicCue cue = ClassicMusicCatalogue.All[0];
        byte[] replacement = "a shorter encode of the same donor song"u8.ToArray();

        ClassicMusicPublicationResult publication = ClassicMusicPublication.Create([cue], [new ClassicMusicSource(cue, replacement)]);

        ClassicMusicRecord record = Assert.Single(publication.Manifest.Cues);
        Assert.Equal(replacement.Length, record.ByteLength);
        Assert.Equal(replacement, publication.Artifacts.Single(artifact => artifact.RelativePath == $"media/music/clips/{cue.SourceFile}").Bytes.ToArray());
        string warning = Assert.Single(publication.Warnings);
        Assert.Contains(cue.MediaId, warning, StringComparison.Ordinal);
        Assert.Contains($"carries {replacement.Length} bytes where the catalogue records {cue.SourceBytes}", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_published_cue_names_the_bytes_and_the_donor_track_it_carries()
    {
        Dictionary<string, string> donorTracks = new(StringComparer.Ordinal)
        {
            ["music.sunny"] = "song_gday___d",
            ["music.sunny.third"] = "song_gsunny2",
        };
        ClassicMusicCue[] cues = [.. donorTracks.Keys.Select(mediaId => ClassicMusicCatalogue.All.Single(cue => cue.MediaId == mediaId))];

        ClassicMusicPublicationResult publication = ClassicMusicPublication.Create(cues, [.. cues.Select(cue => new ClassicMusicSource(cue, Payload(cue.MediaId)))]);

        foreach (ClassicMusicCue cue in cues)
        {
            byte[] payload = Payload(cue.MediaId);
            ClassicMusicRecord record = publication.Manifest.Cues.Single(value => value.MediaId == cue.MediaId);
            ImportPublicationArtifact clip = publication.Artifacts.Single(artifact => artifact.RelativePath == $"media/music/clips/{cue.SourceFile}");

            Assert.Equal(payload.Length, record.ByteLength);
            Assert.Equal(payload.Length, clip.Bytes.Length);
            Assert.Equal(ContentDigest.Compute(payload).Value, record.ContentDigest);
            Assert.Equal(ContentDigest.Compute(clip.Bytes.Span).Value, record.ContentDigest);
            Assert.Equal(donorTracks[cue.MediaId], record.Track);
            Assert.DoesNotContain(".ogg", record.Track, StringComparison.Ordinal);
            Assert.Equal(cue.SourceFile, record.File);
            Assert.Equal(cue.Context, record.Context);
            Assert.Equal("audio/ogg", record.MimeType);
        }
    }

    [Fact]
    public void The_published_manifest_round_trips_and_refuses_a_foreign_generator_or_a_cue_the_engine_cannot_open()
    {
        ClassicMusicCue[] cues = [.. ClassicMusicCatalogue.All.Take(2)];
        ClassicMusicPublicationResult publication = ClassicMusicPublication.Create(cues, [.. cues.Select(cue => new ClassicMusicSource(cue, Payload(cue.MediaId)))]);
        byte[] bytes = publication.Artifacts.Single(artifact => artifact.RelativePath == "media/music/manifest.json").Bytes.ToArray();

        ClassicMusicManifest reopened = ClassicMusicPublication.Read(bytes);

        // The generator is the published format's, not merely this assembly's: the runtime reader carries
        // its own copy of the name, so a drift here would publish a manifest no site would admit.
        Assert.Equal("daggerfall-import-tool music-media", reopened.Generator);
        Assert.Equal(publication.Manifest.Cues, reopened.Cues);

        ClassicMusicRecord cue = reopened.Cues[0];
        Assert.Throws<FormatException>(() => ClassicMusicPublication.Read(Write(publication.Manifest with { Generator = "someone-elses-tool" })));
        Assert.Throws<ArgumentException>(() => ClassicMusicPublication.Read(Write(publication.Manifest with { Cues = [cue with { MimeType = "audio/mpeg" }] })));
        Assert.Throws<ArgumentException>(() => ClassicMusicPublication.Read(Write(publication.Manifest with { Cues = [cue with { File = "clips/song.ogg" }] })));
        Assert.Throws<ArgumentException>(() => ClassicMusicPublication.Read(Write(publication.Manifest with { Cues = [cue with { File = "song.mp3" }] })));

        // The published dialect refuses a member the record does not declare, so a renamed section fails at
        // the reader rather than reading as a section that states nothing.
        Assert.Throws<JsonException>(() => ClassicMusicPublication.Read(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"generator\"", "\"writer\"", StringComparison.Ordinal))));
    }

    [Fact]
    public void Create_refuses_a_publication_that_exceeds_the_engines_clip_count_or_byte_budget()
    {
        ClassicMusicCue[] tooMany = [.. Enumerable.Range(0, ClassicMusicCatalogue.MaximumCueCount + 1)
            .Select(index => new ClassicMusicCue($"music.synthetic.{index}", $"synthetic-{index}.ogg", "dungeon", 1))];
        ClassicMusicSource[] sources = [.. tooMany.Select(cue => new ClassicMusicSource(cue, [7]))];

        InvalidOperationException count = Assert.Throws<InvalidOperationException>(() => ClassicMusicPublication.Create(tooMany, sources));

        Assert.Contains($"{tooMany.Length} cues, which is more than the Engine admits ({ClassicMusicCatalogue.MaximumCueCount})", count.Message, StringComparison.Ordinal);

        int admitted = ClassicMusicCatalogue.MaximumTotalBytes - ClassicMusicCatalogue.EffectHeadroomBytes;
        ClassicMusicCue oversized = new("music.synthetic.oversized", "synthetic-oversized.ogg", "dungeon", admitted + 1);
        InvalidOperationException bytes = Assert.Throws<InvalidOperationException>(() => ClassicMusicPublication.Create([oversized], [new ClassicMusicSource(oversized, new byte[admitted + 1])]));

        Assert.Contains($"{admitted + 1L} bytes, which leaves the imported effect clips less than {ClassicMusicCatalogue.EffectHeadroomBytes}", bytes.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_refuses_a_cue_with_no_published_bytes_or_a_malformed_digest()
    {
        ClassicMusicRecord cue = new(
            "music.dungeon",
            "song_dungeon",
            "dungeon",
            "song_dungeon.ogg",
            ClassicMusicRecord.OggMimeType,
            16,
            ContentDigest.Compute(Payload("music.dungeon")).Value);
        ClassicMusicManifest manifest = new(ClassicMusicPublication.Generator, [cue]);

        Assert.Equal(manifest.Cues, ClassicMusicPublication.Read(ClassicMusicPublication.Serialize(manifest)).Cues);
        ArgumentException noBytes = Assert.Throws<ArgumentException>(() => ClassicMusicPublication.Serialize(manifest with { Cues = [cue with { ByteLength = 0 }] }));
        Assert.Equal("ByteLength", noBytes.ParamName);
        Assert.Throws<ArgumentException>(() => ClassicMusicPublication.Serialize(manifest with { Cues = [cue with { ByteLength = -1 }] }));
        ArgumentException digest = Assert.Throws<ArgumentException>(() => ClassicMusicPublication.Serialize(manifest with { Cues = [cue with { ContentDigest = "not-a-sha256" }] }));
        Assert.Contains("SHA-256", digest.Message, StringComparison.Ordinal);
    }

    /// <summary>Writes the published dialect without the publisher's record validation, so a refusal is the reader's.</summary>
    private static byte[] Write(ClassicMusicManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, PublishedJson.Section);

    /// <summary>Fake donor bytes: the publication carries the source file rather than decoding it.</summary>
    private static byte[] Payload(string mediaId) => Encoding.UTF8.GetBytes($"synthetic ogg bytes for {mediaId}");
}
