using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Audio;
using Daggerfall.Import.Publication;
using Daggerfall.Import.Tool;
using Daggerfall.Import.Tool.Commands;
using Rusty.Engine;
using Rusty.Engine.Testing;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The import into the data containers: the music naming scheme, the input check that runs before anything is
/// written, the step list against the commands' own grammar, the container's description, and packing through
/// the Engine.
/// </summary>
public sealed class DataContainerImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "data-container-import-" + Guid.NewGuid().ToString("N"));

    public DataContainerImportTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_music_folder_is_matched_to_tracks_by_name_in_any_case_and_admitted_format()
    {
        ClassicMusicFolder folder = ClassicMusicTracks.Match(["song_dungeon.ogg", "SONG_04.MP3", "song_gshop.flac", "song_dungeon5).ogg", "song_tavern.mid", "notes.txt"]);

        Assert.Equal(
            [("song_04", "SONG_04.MP3", "song_04.mp3"), ("song_dungeon", "song_dungeon.ogg", "song_dungeon.ogg"), ("song_gshop", "song_gshop.flac", "song_gshop.flac")],
            folder.Recordings.Select(recording => (recording.Track, recording.SourceFile, recording.PackedName)));
        Assert.Equal(["notes.txt", "song_dungeon5).ogg", "song_tavern.mid"], folder.Unknown);
        Assert.Equal(ClassicMusicTracks.All.Count - 3, folder.Missing.Count);
        Assert.Contains("song_tavern", folder.Missing);
        Assert.DoesNotContain("song_dungeon", folder.Missing);
    }

    [Fact]
    public void Two_recordings_of_one_track_are_refused_by_name()
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => ClassicMusicTracks.Match(["song_dungeon.ogg", "song_dungeon.mp3"]));

        Assert.Contains("'song_dungeon.mp3'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'song_dungeon.ogg'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_track_list_is_every_track_the_catalogue_plays_once()
    {
        Assert.Equal(ClassicMusicCatalogue.All.Select(cue => cue.Track).Distinct().Order(StringComparer.Ordinal), ClassicMusicTracks.All);
        Assert.All(ClassicMusicTracks.All, track => Assert.StartsWith("song_", track, StringComparison.Ordinal));
    }

    [Fact]
    public void The_music_tracks_command_stages_scheme_names_and_refuses_a_folder_with_none()
    {
        string music = Directory.CreateDirectory(Path.Combine(root, "Sound")).FullName;
        File.WriteAllBytes(Path.Combine(music, "Song_Dungeon.OGG"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(music, "readme.txt"), [4]);
        string output = Path.Combine(root, "tracks");

        Assert.Equal(0, Program.Commands["music-tracks"].Invoke(["music-tracks", "--music", music, "--out", output]));

        Assert.Equal(["song_dungeon.ogg"], Directory.EnumerateFiles(output).Select(Path.GetFileName));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(output, "song_dungeon.ogg")));

        File.Delete(Path.Combine(music, "Song_Dungeon.OGG"));
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            Program.Commands["music-tracks"].Invoke(["music-tracks", "--music", music, "--out", output]));
        Assert.Contains("leave out --music", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_missing_input_is_named_before_anything_is_written()
    {
        ImportCommand.Inputs inputs = new("local/arena2", Path.Combine(root, "donor"), "local/Sound", "authoring", "variants.json");

        string[] missing = [.. ImportCommand.MissingInputs(inputs, root)];

        Assert.Contains(missing, entry => entry.StartsWith("block variant descriptor 'variants.json'", StringComparison.Ordinal));
        Assert.Contains(missing, entry => entry.StartsWith("Arena2 directory 'local/arena2'", StringComparison.Ordinal));
        Assert.Contains(missing, entry => entry.StartsWith($"Daggerfall Unity checkout '{Path.Combine(root, "donor")}'", StringComparison.Ordinal));
        Assert.Contains(missing, entry => entry.StartsWith("music folder 'local/Sound'", StringComparison.Ordinal));
        Assert.Contains(missing, entry => entry.StartsWith("sprite authoring root 'authoring'", StringComparison.Ordinal));
        Assert.Contains("tracked file 'data/content-source-manifest.csv'", missing);
        Assert.Contains("tracked file 'content/worldrpg/payloads/daggerfall.base.json'", missing);

        // An Arena2 folder that exists is checked file by file, so each absent archive is named.
        Directory.CreateDirectory(Path.Combine(root, "local/arena2"));
        missing = [.. ImportCommand.MissingInputs(inputs, root)];
        Assert.Contains("Arena2 file 'local/arena2/MAPS.BSA'", missing);
        Assert.Contains("Arena2 sky file 'local/arena2/SKY31.DAT'", missing);
        Assert.Contains("Arena2 books directory 'local/arena2/books'", missing);
        Assert.DoesNotContain(missing, entry => entry.StartsWith("Arena2 directory", StringComparison.Ordinal));
    }

    [Fact]
    public void An_import_with_missing_inputs_is_refused_without_writing()
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data/content-source-manifest.csv"), "id\n");
        string before = Directory.GetCurrentDirectory();

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => Program.Commands["import"].Invoke(
            ["import", "--repository", root, "--arena2", Path.Combine(root, "absent"), "--donor", Path.Combine(root, "absent-donor")]));

        Assert.Contains("missing inputs:", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Arena2 directory 'absent'", refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "local")));
        Assert.Equal(before, Directory.GetCurrentDirectory());
    }

    [Fact]
    public void Every_step_is_a_tool_command_the_commands_own_grammar_accepts()
    {
        ImportCommand.Inputs inputs = new("local/arena2", "/donor", "local/Sound", null, "variants.json");

        IReadOnlyList<ImportCommand.Step> steps = ImportCommand.Steps(inputs, root);

        Assert.Equal(steps.Count, steps.Select(step => step.Label).Distinct(StringComparer.Ordinal).Count());
        foreach (ImportCommand.Step step in steps)
        {
            Assert.True(Program.Commands.TryGetValue(step.Arguments[0], out ToolCommand? command), $"step '{step.Label}' runs no command");
            CommandArguments.Parse(step.Arguments, command!);
        }

        // Each step writes into the staging tree or the records, never into the repository's content.
        Assert.All(steps.SelectMany(step => step.Arguments).Where(argument => argument.StartsWith("content/", StringComparison.Ordinal)),
            argument => Assert.Equal("content/worldrpg/payloads/daggerfall.base.json", argument));
        Assert.Equal(["music-media", "music-tracks", "classic-media"], steps.Take(3).Select(step => step.Label));
        Assert.Equal("source-coverage", steps[^1].Label);
        Assert.Contains("--music-manifest", steps.Single(step => step.Label == "charing-exterior").Arguments);
        Assert.Contains("variants.json", steps.Single(step => step.Label == "world-blocks").Arguments);
    }

    [Fact]
    public void Without_music_no_cue_is_named_and_no_tracks_are_staged()
    {
        IReadOnlyList<ImportCommand.Step> steps = ImportCommand.Steps(new("local/arena2", "/donor", null, null, null), root);

        Assert.DoesNotContain(steps, step => step.Label is "music-media" or "music-tracks");
        Assert.DoesNotContain(steps, step => step.Arguments.Contains("--music-manifest"));
    }

    [Fact]
    public void A_caller_path_inside_the_repository_is_recorded_relative_and_one_outside_absolute()
    {
        string repository = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;

        Assert.Equal("local/arena2", ImportCommand.Portable("local/arena2", repository, repository));
        Assert.Equal("local/Sound", ImportCommand.Portable("../local/Sound", Path.Combine(repository, "scripts"), repository));
        Assert.Equal(Path.Combine(root, "games", "ARENA2"), ImportCommand.Portable(Path.Combine(root, "games", "ARENA2"), repository, repository));
    }

    [Fact]
    public void The_derived_pack_descriptors_are_the_tracked_ones_they_replace()
    {
        string packs = Path.Combine(TestData.RepositoryRoot, "content/worldrpg/content-packs");
        foreach (JsonObject pack in DataContainers.DerivedPacks())
        {
            string id = (string)pack["id"]!;
            JsonNode tracked = JsonNode.Parse(File.ReadAllText(Path.Combine(packs, id + ".pack.json")))!;
            Assert.True(JsonNode.DeepEquals(tracked, pack), $"the generated descriptor of '{id}' differs from the tracked one");
        }

        // Every tracked descriptor whose payload the import writes is one of them.
        string[] derived = [.. DataContainers.DerivedPacks().Select(pack => (string)pack["id"]!)];
        foreach (string file in Directory.EnumerateFiles(packs, "*.pack.json"))
        {
            string payload = (string)JsonNode.Parse(File.ReadAllText(file))!["payload"]!;
            bool generated = payload is "worldrpg/payloads/daggerfall.imported.json" or "worldrpg/payloads/daggerfall.blocks.json"
                || payload.StartsWith("worldrpg/payloads/daggerfall.quests.", StringComparison.Ordinal);
            Assert.Equal(generated, derived.Contains(Path.GetFileName(file)[..^".pack.json".Length]));
        }
    }

    [Fact]
    public void The_description_names_the_importer_revision_and_refuses_a_staging_tree_without_its_payloads()
    {
        string staging = Path.Combine(root, "staging");
        IReadOnlyList<JsonObject> packs = DataContainers.DerivedPacks();
        foreach (JsonObject pack in packs.Skip(1)) Write(staging, (string)pack["payload"]!, "{}"u8);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => DataContainers.WriteDescriptor(staging));
        Assert.Contains((string)packs[0]["payload"]!, refusal.Message, StringComparison.Ordinal);

        Write(staging, (string)packs[0]["payload"]!, "{}"u8);
        DataContainers.WriteDescriptor(staging);
        JsonNode descriptor = JsonNode.Parse(File.ReadAllText(Path.Combine(staging, ImportLayout.DescriptorPath)))!;
        Assert.Equal(DataContainers.DescriptorKind, (string)descriptor["kind"]!);
        Assert.Equal(ImporterBuild.Revision, (string)descriptor["importerRevision"]!);
        Assert.Matches("^[0-9a-f]{64}$", ImporterBuild.Revision);
        Assert.Equal(packs.Count, descriptor["contentPacks"]!.AsArray().Count);
    }

    [Fact]
    public void A_packed_container_opens_through_the_engine_with_every_staged_file_by_path_and_digest()
    {
        string staging = Path.Combine(root, "staging");
        byte[] mesh = [.. Enumerable.Range(0, 4096).Select(index => (byte)(index % 7))];
        Write(staging, "worldrpg/imports/shared/meshes/a.rstatmsh", mesh);
        Write(staging, "worldrpg/payloads/daggerfall.imported.json", "{\"locations\":{}}"u8);
        string container = Path.Combine(root, "local", "daggerfall-data.rpak");
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());

        DataContainers.PackOne(host, staging, container, compress: true);

        host.Call(engine =>
        {
            using ProductContentBundle opened = ProductContentBundle.OpenContainer(engine.Content, container);
            Assert.Equal(["worldrpg/imports/shared/meshes/a.rstatmsh", "worldrpg/payloads/daggerfall.imported.json"],
                opened.Entries.ToArray().Select(entry => entry.Path).Order(StringComparer.Ordinal));
            Assert.Equal(mesh, opened.ReadBytes("worldrpg/imports/shared/meshes/a.rstatmsh").ToArray());
            using ContentReference reference = engine.Content.ResolveReference(new ContentResolveRequest("worldrpg/imports/shared/meshes/a.rstatmsh", Digest(mesh)));
        });
    }

    [ImportedDataContainerFact]
    public void The_imported_data_container_carries_every_artifact_its_publications_name()
    {
        string container = Path.Combine(TestData.RepositoryRoot, ImportLayout.DataContainer);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using ProductContentBundle opened = ProductContentBundle.OpenContainer(engine.Content, container);
            Dictionary<string, ContentReferenceInfo> entries = opened.Entries.ToArray().ToDictionary(entry => entry.Path, StringComparer.Ordinal);
            string[] manifests = [.. entries.Keys.Where(path => path.StartsWith("worldrpg/imports/", StringComparison.Ordinal)
                && path.EndsWith("/" + ImportPublicationManifestSerializer.ManifestRelativePath, StringComparison.Ordinal))];
            Assert.Contains($"worldrpg/imports/shared/{ImportPublicationManifestSerializer.ManifestRelativePath}", manifests);
            Assert.Contains($"worldrpg/imports/world-blocks/{ImportPublicationManifestSerializer.ManifestRelativePath}", manifests);
            Assert.Contains($"worldrpg/imports/charing/exterior/{ImportPublicationManifestSerializer.ManifestRelativePath}", manifests);
            foreach (string manifest in manifests)
            {
                string publication = manifest[..^ImportPublicationManifestSerializer.ManifestRelativePath.Length];
                foreach (ImportPublicationManifestArtifact artifact in ImportPublicationManifestSerializer.Deserialize(opened.ReadBytes(manifest).ToArray()).Artifacts)
                {
                    Assert.True(entries.TryGetValue(publication + artifact.RelativePath, out ContentReferenceInfo entry), $"'{publication}{artifact.RelativePath}' is not in the container");
                    Assert.Equal((ulong)artifact.ByteLen, entry.ByteLength);
                    Assert.Equal(FromHash(Convert.FromHexString(artifact.ContentHash.Value)), entry.Sha256);
                }
            }

            JsonNode descriptor = JsonNode.Parse(opened.ReadText(ImportLayout.DescriptorPath))!;
            Assert.All(descriptor["contentPacks"]!.AsArray(), pack => Assert.Contains((string)pack!["payload"]!, entries.Keys));
        });
    }

    private static void Write(string directory, string path, ReadOnlySpan<byte> bytes)
    {
        string file = Path.Combine(directory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes.ToArray());
    }

    private static ContentSha256 Digest(byte[] bytes) => FromHash(SHA256.HashData(bytes));

    private static ContentSha256 FromHash(byte[] hash) => new(
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8)),
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(8, 8)),
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(16, 8)),
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(24, 8)));
}

/// <summary>A fact over the data container a real import packed; it is skipped, naming the command, without one.</summary>
internal sealed class ImportedDataContainerFactAttribute : FactAttribute
{
    public ImportedDataContainerFactAttribute() => Skip = File.Exists(Path.Combine(TestData.RepositoryRoot, ImportLayout.DataContainer))
        ? null
        : $"no data container at {ImportLayout.DataContainer}; run `daggerfall-import-tool import` with local/arena2 supplied.";
}
