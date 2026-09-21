using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>The published catalog of the numeric sound archive and the artifacts its references name.</summary>
public sealed class SoundCatalogTests
{
    [Fact]
    public void Catalogues_every_numeric_clip_in_the_archives_own_order()
    {
        SoundArchive archive = RepositoryArchive();
        DaggerfallSoundCatalog catalog = DaggerfallSoundCatalogBuilder.Build(archive, Admissions());
        catalog.Validate();

        // Measured: the archive carries four hundred and fifty-nine numeric records, and the catalog is
        // the archive's own directory order because that ordinal is the identity a consumer keeps.
        Assert.Equal(459, archive.Count);
        Assert.Equal(459, catalog.Clips.Count);
        Assert.Equal(Enumerable.Range(0, 459), catalog.Clips.Select(clip => clip.Ordinal));
        Assert.Equal(catalog.Clips.Select(clip => clip.Ordinal), catalog.Clips.Select(clip => clip.Ordinal).Order());

        // Every clip carries a numeric identity and a stated reason, and the six the product publishes
        // are admitted while the rest say they have no consumer rather than being absent.
        // The numeric identity is not unique in this archive: four hundred and fifty-nine records carry
        // four hundred and fifty-eight distinct numbers, so exactly one number appears twice. That is
        // why the catalog's stable identity is the ordinal and the numeric id is carried beside it -
        // a consumer keying on the number would have one collision it could not resolve.
        Assert.Equal(458, catalog.Clips.Select(clip => clip.NumericId).Distinct().Count());
        Assert.Single(catalog.Clips.GroupBy(clip => clip.NumericId), group => group.Count() == 2);
        Assert.All(catalog.Clips, clip => Assert.False(string.IsNullOrWhiteSpace(clip.Reason)));
        Assert.Equal(6, catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.Admitted));
        Assert.Equal(452, catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.ReadableNoConsumer));

        // One record of the four hundred and fifty-nine carries no sample bytes at all, and it is the
        // case the task asks to be explicit rather than omitted: the catalog states it, names why, and
        // still counts it, so the archive cannot lose a clip without the count changing.
        DaggerfallSoundClip unsupported = Assert.Single(catalog.Clips, clip => clip.Disposition == DaggerfallSoundClipDisposition.Unsupported);
        Assert.Equal(0, unsupported.ByteLength);
        Assert.Contains("no sample bytes", unsupported.Reason, StringComparison.Ordinal);
        Assert.Equal(459, catalog.Clips.Count);

        // The media identity is the machine-readable half of an admission, and it is present exactly
        // when a published artifact carries the clip: a consumer asks the content store for a name, so
        // an admission that named one only inside a sentence would not be a reference it could follow.
        Assert.Equal("audio.melee.dagger.swing", catalog.Clips[106].MediaId);
        Assert.Equal("audio.melee.hit.5", catalog.Clips[112].MediaId);
        Assert.All(catalog.Clips.Where(clip => clip.Disposition != DaggerfallSoundClipDisposition.Admitted), clip => Assert.Null(clip.MediaId));
        Assert.All(catalog.Clips.Where(clip => clip.Disposition == DaggerfallSoundClipDisposition.Admitted),
            clip => Assert.Contains(clip.MediaId!, clip.Reason, StringComparison.Ordinal));

        // The donor's own names are the usage candidates, transcribed from its clip enum: three
        // hundred and seventy-three of the four hundred and fifty-nine clips are named there, and the
        // six this product publishes are named differently on purpose - the donor's 'SwingHighPitch'
        // is published as 'audio.melee.dagger.swing', so both the original name and the product's
        // identity are carried rather than one replacing the other.
        // The donor's enum has three hundred and seventy-four named entries and one of them is 'None',
        // which is not a clip at all, so the table carries three hundred and seventy-three.
        Assert.Equal(373, DaggerfallSoundNames.Count);
        Assert.Equal("SwingHighPitch", catalog.Clips[106].UsageCandidate);
        Assert.Equal("Hit1", catalog.Clips[108].UsageCandidate);
        Assert.Equal("Hit5", catalog.Clips[112].UsageCandidate);

        // The record with no sample bytes is the one the donor itself marks as invalid, which is what
        // makes its disposition a fact about the archive rather than a shortcoming of this reader.
        Assert.Equal("Invalid", unsupported.UsageCandidate);
        Assert.Equal(5, unsupported.Ordinal);
        Assert.Contains("'Invalid'", unsupported.Reason, StringComparison.Ordinal);

        // A representative clip decodes to samples, which is what "readable" has to mean rather than a
        // record that merely exists in the directory.
        Arena2PcmClip swing = archive.GetClip(106);
        Assert.False(swing.PcmUnsigned8.IsEmpty);
        Assert.NotEmpty(archive.CreateWave(106));

        // The catalog is deterministic: the same archive catalogues the same way twice.
        DaggerfallSoundCatalog again = DaggerfallSoundCatalogBuilder.Build(archive, Admissions());
        Assert.Equal(
            catalog.Clips.Select(clip => (clip.Ordinal, clip.NumericId, clip.ByteLength, clip.Disposition, clip.UsageCandidate, clip.MediaId)),
            again.Clips.Select(clip => (clip.Ordinal, clip.NumericId, clip.ByteLength, clip.Disposition, clip.UsageCandidate, clip.MediaId)));
    }

    /// <summary>
    /// The reference closure the task asks for: every admitted catalog entry names the artifact the
    /// publication emitted for it, and the delivered content is what this build produces.
    /// </summary>
    [Fact]
    public void Every_admitted_clip_is_carried_by_the_artifact_the_publication_emitted()
    {
        SoundArchive archive = RepositoryArchive();
        Arena2ClassicMediaPublication publication = PublishFromCorpus();
        DaggerfallSoundCatalog catalog = DaggerfallSoundCatalogBuilder.Build(archive, publication.SoundAdmissions);

        // The admitted set is the publication's own audio manifests, so these two records of the same
        // fact cannot drift: an entry the catalog calls admitted is one the publication emitted.
        DaggerfallSoundClip[] admitted = [.. catalog.Clips.Where(clip => clip.Disposition == DaggerfallSoundClipDisposition.Admitted)];
        Assert.Equal(6, admitted.Length);
        Assert.Equal([106, 108, 109, 110, 111, 112], admitted.Select(clip => clip.Ordinal));
        Assert.Equal(
            publication.Audio.Select(audio => (audio.SourceRecordOrdinal, audio.MediaId)).OrderBy(entry => entry.SourceRecordOrdinal),
            admitted.Select(clip => (clip.Ordinal, clip.MediaId!)).OrderBy(entry => entry.Item1));

        // Which clip is which cue is the product's decision rather than the producer's convenience, so
        // the binding is also pinned against this test's own literals: a consistent republish that
        // swapped two identities would move the table and the artifacts together and stay green here
        // and everywhere else.
        Assert.Equal(Admissions().Select(admission => (admission.Ordinal, admission.MediaId)), admitted.Select(clip => (clip.Ordinal, clip.MediaId!)));

        foreach (DaggerfallSoundClip clip in admitted)
        {
            // The reference a consumer follows, followed to the end: clip ordinal -> media id -> one
            // emitted artifact -> the samples the archive holds for that ordinal.
            NormalizedMediaDescriptor resource = Assert.Single(publication.MediaManifest.Resources, value => value.Id == clip.MediaId);
            ImportPublicationArtifact artifact = Assert.Single(publication.Artifacts, value => value.RelativePath == resource.RelativePath);
            Assert.Equal(resource.ContentDigest, artifact.ContentHash);
            Assert.Equal(resource.ByteLength, artifact.Bytes.Length);

            // The catalog states the sample bytes the archive holds for the clip, and the artifact is
            // that many bytes inside a 44-byte WAV container, so the two are checked against each
            // other's content rather than against a count that happens to agree.
            Assert.Equal(clip.ByteLength + 44, artifact.Bytes.Length);
            AssertWave(artifact.Bytes.Span);
            Assert.True(artifact.Bytes.Span[44..].SequenceEqual(archive.GetClip(clip.Ordinal).PcmUnsigned8.Span));

            // The delivered tree carries that same artifact under its content-relative name, which is
            // the name a consumer holds rather than a path relative to this publication.
            Assert.Equal(artifact.Bytes.ToArray(), File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content", "worldrpg", resource.RelativePath)));
        }

        // The published catalog is current and readable. Reading the committed artifact is checked
        // structurally before the bytes, so a drift names the clip that differs rather than only a
        // byte position; the byte comparison still pins formatting and the trailing newline.
        byte[] committed = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content", "worldrpg", DaggerfallSoundCatalogJson.RelativePath));
        DaggerfallSoundCatalog reread = DaggerfallSoundCatalogJson.Read(committed);
        Assert.Equal(catalog.Clips, reread.Clips);
        Assert.Equal(catalog.Sources, reread.Sources);
        Assert.Equal(DaggerfallSoundCatalogJson.Write(catalog), committed);
    }

    /// <summary>
    /// A persisted catalog that could not be read back as the same record is refused rather than
    /// accepted: the reader is the boundary a consumer crosses, and it may be handed a file this
    /// build did not write.
    /// </summary>
    [Fact]
    public void Refuses_a_persisted_catalog_that_could_not_be_read_back()
    {
        DaggerfallSoundCatalog catalog = DaggerfallSoundCatalogBuilder.Build(RepositoryArchive(), Admissions());
        byte[] written = DaggerfallSoundCatalogJson.Write(catalog);
        Assert.Equal(catalog.Clips, DaggerfallSoundCatalogJson.Read(written).Clips);

        // A version whose meaning is not this one is refused rather than read as if it were.
        Assert.Throws<InvalidOperationException>(() => DaggerfallSoundCatalogJson.Read(Unvalidated(catalog with { SchemaVersion = 2 })));

        // Ordinals that are not the archive's own order would silently repoint every stored reference.
        Assert.Throws<InvalidOperationException>(() => DaggerfallSoundCatalogJson.Read(Unvalidated(catalog with { Clips = [.. catalog.Clips.Skip(1), catalog.Clips[0]] })));

        // Two clips naming one artifact: the builder refuses to produce this closure, and the record
        // refuses to carry it, which is what a consumer of the persisted file relies on.
        DaggerfallSoundClip[] aliased = [.. catalog.Clips];
        aliased[108] = aliased[108] with { MediaId = "audio.melee.dagger.swing" };
        Assert.Throws<InvalidOperationException>(() => DaggerfallSoundCatalogJson.Read(Unvalidated(catalog with { Clips = aliased })));

        // An admitted clip that states no samples, and bytes that are not a catalog at all.
        DaggerfallSoundClip[] empty = [.. catalog.Clips];
        empty[106] = empty[106] with { ByteLength = 0 };
        Assert.Throws<InvalidOperationException>(() => DaggerfallSoundCatalogJson.Read(Unvalidated(catalog with { Clips = empty })));
        Assert.Throws<InvalidOperationException>(() => DaggerfallSoundCatalogJson.Read(written.AsSpan(0, written.Length / 2)));
    }

    /// <summary>
    /// A closure that could not be published is refused where it is stated rather than published as a
    /// reference to nothing: an ordinal the archive does not carry, one clip admitted twice, one media
    /// identity claimed by two clips, and the archive's sample-less record.
    /// </summary>
    [Fact]
    public void Refuses_an_admitted_closure_that_could_not_be_published()
    {
        SoundArchive archive = RepositoryArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallSoundCatalogBuilder.Build(archive, [new(archive.Count, "audio.melee.dagger.swing")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallSoundCatalogBuilder.Build(archive, [new(-1, "audio.melee.dagger.swing")]));
        Assert.Throws<ArgumentNullException>(() => DaggerfallSoundCatalogBuilder.Build(archive, [null!]));
        Assert.Throws<ArgumentException>(() => DaggerfallSoundCatalogBuilder.Build(archive, [new(106, " ")]));
        Assert.Throws<ArgumentException>(() => DaggerfallSoundCatalogBuilder.Build(archive, [new(106, "audio.melee.dagger.swing"), new(106, "audio.melee.hit.1")]));
        Assert.Throws<ArgumentException>(() => DaggerfallSoundCatalogBuilder.Build(archive, [new(106, "audio.melee.hit.1"), new(108, "audio.melee.hit.1")]));

        // Ordinal five is the record the archive carries no samples for, so an admission of it would
        // claim an artifact that no producer can emit; the catalog states it unsupported instead.
        InvalidOperationException unsupported = Assert.Throws<InvalidOperationException>(
            () => DaggerfallSoundCatalogBuilder.Build(archive, [new(5, "audio.melee.hit.1")]));
        Assert.Contains("no sample bytes", unsupported.Message, StringComparison.Ordinal);
    }

    /// <summary>The six clips the product publishes today, stated here so the identity test does not read them off the producer.</summary>
    private static IReadOnlyList<DaggerfallSoundAdmission> Admissions() =>
    [
        new(106, "audio.melee.dagger.swing"),
        new(108, "audio.melee.hit.1"),
        new(109, "audio.melee.hit.2"),
        new(110, "audio.melee.hit.3"),
        new(111, "audio.melee.hit.4"),
        new(112, "audio.melee.hit.5"),
    ];

    private static SoundArchive RepositoryArchive() => SoundArchive.Parse(File.ReadAllBytes(Corpus("DAGGER.SND")), Arena2ClassicMediaPublication.DaggerSoundSourcePath);

    private static Arena2ClassicMediaPublication PublishFromCorpus() => Arena2ClassicMediaPublication.Create(new(
        Read("WEAPON01.CIF"), Read("WEAPON02.CIF"), Read("WEAPON04.CIF"), Read("WEAPON05.CIF"), Read("WEAPON06.CIF"),
        Read("WEAPON07.CIF"), Read("WEAPON08.CIF"), Read("WEAPON09.CIF"), Read("WEAPON10.CIF"),
        Read("ART_PAL.COL"), Read("TEXTURE.380"), Read("PAL.PAL"), Read("DAGGER.SND"),
        Read("MAIN00I0.IMG"), Read("MAIN03I0.IMG"), Read("MAIN04I0.IMG"), Read("MAIN05I0.IMG"),
        Read("INVE00I0.IMG"), Read("INFO00I0.IMG"), Read("DIE_00I0.IMG"),
        Read("CHGN00I0.IMG"), Read("PICK02I0.IMG"), Read("PICK03I0.IMG"), Read("PRIS00I0.IMG"), Read("TITL00I0.IMG"),
        Read("BOOK00I0.IMG"), Read("REST00I0.IMG"), Read("SHOP00I0.IMG"), Read("GILD00I0.IMG"), Read("BANK00I0.IMG"),
        Read("REST01I0.IMG"), Read("REST02I0.IMG"), Read("INVE08I0.IMG"), Read("INVE10I0.IMG"), Read("INVE11I0.IMG"),
        Read("INVE12I0.IMG"), Read("INVE14I0.IMG"), Read("GILD01I0.IMG"),
        Read("TEXTURE.207"), Read("TEXTURE.216"), Read("TEXTURE.234"), Read("TEXTURE.245"), Read("FONT0003.FNT"), Read("WEAPON00.CIF"), Read("WEAPON03.CIF"), Read("WEAPON11.CIF"), Read("FONT0000.FNT"), Read("FONT0001.FNT"), Read("FONT0002.FNT"), Read("FONT0004.FNT")));

    private static byte[] Read(string name) => File.ReadAllBytes(Corpus(name));

    /// <summary>Writes a catalog the way a foreign producer might: without this repository's validation.</summary>
    private static byte[] Unvalidated(DaggerfallSoundCatalog catalog) =>
        JsonSerializer.SerializeToUtf8Bytes(catalog, PublishedJson.Section);

    private static void AssertWave(ReadOnlySpan<byte> wave)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave[..4]));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave.Slice(8, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(wave.Slice(20, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(wave.Slice(22, 2)));
        Assert.Equal(SoundArchive.SampleRate, BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(24, 4)));
        Assert.Equal(8, BinaryPrimitives.ReadUInt16LittleEndian(wave.Slice(34, 2)));
        Assert.Equal("data", Encoding.ASCII.GetString(wave.Slice(36, 4)));
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AGENTS.md")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
