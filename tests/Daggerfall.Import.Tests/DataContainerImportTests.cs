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
    public void The_track_list_names_each_classic_track_once_in_ordinal_order()
    {
        Assert.Equal(ClassicMusicTracks.All.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), ClassicMusicTracks.All);
        Assert.All(ClassicMusicTracks.All, track => Assert.Matches("^song_[a-z0-9_]+$", track));
        Assert.Equal(97, ClassicMusicTracks.All.Count);
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
        ImportCommand.Inputs inputs = new("local/arena2", "local/Sound", "authoring", "variants.json");

        string[] missing = [.. ImportCommand.MissingInputs(inputs, root)];

        Assert.Contains(missing, entry => entry.StartsWith("block variant descriptor 'variants.json'", StringComparison.Ordinal));
        Assert.Contains(missing, entry => entry.StartsWith("Arena2 directory 'local/arena2'", StringComparison.Ordinal));
        Assert.Contains($"tracked file '{ImportCommand.Donor}/Assets/Scripts/Utility/EnemyBasics.cs'", missing);
        Assert.Contains($"tracked directory '{ImportCommand.Donor}/Assets/StreamingAssets/Quests'", missing);
        Assert.Contains(missing, entry => entry.StartsWith("music folder 'local/Sound'", StringComparison.Ordinal));
        Assert.Contains(missing, entry => entry.StartsWith("sprite authoring root 'authoring'", StringComparison.Ordinal));
        Assert.Contains("tracked file 'data/content-source-manifest.csv'", missing);
        Assert.Contains("tracked file 'content/worldrpg/payloads/daggerfall.base.json'", missing);

        // A folder that is not ARENA2 is named as such rather than as a list of every archive it lacks.
        Directory.CreateDirectory(Path.Combine(root, "local/arena2"));
        missing = [.. ImportCommand.MissingInputs(inputs, root)];
        Assert.Contains("Arena2 folder: 'local/arena2' is not Daggerfall's ARENA2 folder and none was found inside it", missing);
        Assert.DoesNotContain(missing, entry => entry.StartsWith("Arena2 file", StringComparison.Ordinal));

        // An ARENA2 folder that exists is checked file by file, so each absent archive is named.
        File.WriteAllBytes(Path.Combine(root, "local/arena2/BLOCKS.BSA"), []);
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
            ["import", "--repository", root, "--arena2", Path.Combine(root, "absent")]));

        Assert.Contains("missing inputs:", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Arena2 directory 'absent'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Name a complete copy of the game's ARENA2 folder, or the folder Daggerfall is installed in.", refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "local")));
        Assert.Equal(before, Directory.GetCurrentDirectory());
    }

    [Fact]
    public void An_installation_folder_resolves_to_the_ARENA2_folder_inside_it()
    {
        string install = Path.Combine(root, "GOG Games", "Daggerfall");
        string arena2 = Directory.CreateDirectory(Path.Combine(install, "DF", "DAGGER", "arena2")).FullName;
        Directory.CreateDirectory(Path.Combine(install, "DF", "DAGGER", "ARENA2-backup"));
        File.WriteAllBytes(Path.Combine(arena2, "MAPS.BSA"), []);

        Assert.Equal(arena2, ImportCommand.Arena2Folder(install));
        // The ARENA2 folder itself, and a folder that holds none, are left for the input check to judge.
        Assert.Null(ImportCommand.Arena2Folder(arena2));
        Assert.Null(ImportCommand.Arena2Folder(Directory.CreateDirectory(Path.Combine(root, "Documents")).FullName));
        Assert.Null(ImportCommand.Arena2Folder(Path.Combine(root, "absent")));
    }

    [Fact]
    public void A_tool_published_into_a_release_with_its_inputs_beside_it_installs_beside_the_product()
    {
        string release = Directory.CreateDirectory(Path.Combine(root, "RustyDagger")).FullName;
        string tool = Directory.CreateDirectory(Path.Combine(release, "tools", "Daggerfall.Import.Tool")).FullName;

        // Neither a tool without its inputs nor one in a directory with no product.rpak is in a release.
        Assert.Null(ImportLayout.FindRelease(tool));
        Directory.CreateDirectory(Path.Combine(tool, "data"));
        File.WriteAllText(Path.Combine(tool, ImportLayout.Inventory), "id\n");
        Assert.Null(ImportLayout.FindRelease(tool));

        File.WriteAllBytes(Path.Combine(release, "product.rpak"), []);
        Assert.Equal(release, ImportLayout.FindRelease(tool));
        // Tests run from a build, never a release, so the containers stay in local/.
        Assert.Null(ImportLayout.ReleaseDirectory);
        Assert.Equal(ImportLayout.DataContainer, ImportLayout.Installed(ImportLayout.DataContainer));
    }

    [Fact]
    public void The_game_names_where_the_containers_go_and_a_tool_run_from_its_build_output_finds_its_repository()
    {
        string install = Path.Combine(root, "installed");
        Assert.Equal(Path.Combine(install, "daggerfall-data.rpak"), ImportLayout.Installed(ImportLayout.DataContainer, install));
        Assert.Equal(Path.Combine(install, "music.rpak"), ImportLayout.Installed(ImportLayout.MusicContainer, install));

        // The host runs the tool from its build output in the installed-content directory, so the repository is the
        // one that build output lies in.
        string repository = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repository, "data"));
        File.WriteAllText(Path.Combine(repository, ImportLayout.Inventory), "id\n");
        string output = Directory.CreateDirectory(Path.Combine(repository, "src", "Daggerfall.Import.Tool", "bin", "Debug", "net10.0")).FullName;
        Assert.Equal(repository, ImportLayout.EnclosingRepository(output));
        Assert.Null(ImportLayout.EnclosingRepository(Directory.CreateDirectory(Path.Combine(root, "elsewhere")).FullName));
        Assert.Contains("--install", Program.Commands["import"].Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_FFmpeg_the_import_converts_no_cinematics_and_changes_nothing_else()
    {
        IReadOnlyList<ImportCommand.Step> with = ImportCommand.Steps(new("local/arena2", null, null, null), root);
        IReadOnlyList<ImportCommand.Step> without = ImportCommand.Steps(new("local/arena2", null, null, null, Cinematics: false), root);

        Assert.Equal(["cinematic-media-vid", "cinematic-media-flc"], with.Select(step => step.Label).Where(label => label.StartsWith("cinematic-media", StringComparison.Ordinal)));
        Assert.Equal(
            with.Where(step => !step.Label.StartsWith("cinematic-media", StringComparison.Ordinal)).Select(step => string.Join('\u001f', [step.Label, .. step.Arguments])),
            without.Select(step => string.Join('\u001f', [step.Label, .. step.Arguments])));
    }

    [Fact]
    public void Every_step_is_a_tool_command_the_commands_own_grammar_accepts()
    {
        ImportCommand.Inputs inputs = new("local/arena2", "local/Sound", null, "variants.json");

        IReadOnlyList<ImportCommand.Step> steps = ImportCommand.Steps(inputs, root);

        Assert.Equal(steps.Count, steps.Select(step => step.Label).Distinct(StringComparer.Ordinal).Count());
        foreach (ImportCommand.Step step in steps)
        {
            Assert.True(Program.Commands.TryGetValue(step.Arguments[0], out ToolCommand? command), $"step '{step.Label}' runs no command");
            CommandArguments.Parse(step.Arguments, command!);
        }

        // Each step writes into the staging tree or the records, never into the repository's content: what it names
        // there is the authored base payload and the authored UI art, both read.
        Assert.All(steps.SelectMany(step => step.Arguments).Where(argument => argument.StartsWith("content/", StringComparison.Ordinal)),
            argument => Assert.Contains(argument, new[] { "content/worldrpg/payloads/daggerfall.base.json", AuthoredUi.ProductDirectory }));
        Assert.Equal(["music-tracks", "classic-media"], steps.Take(2).Select(step => step.Label));
        Assert.Equal("source-coverage", steps[^1].Label);
        Assert.Contains("variants.json", steps.Single(step => step.Label == "world-blocks").Arguments);
    }

    [Fact]
    public void A_command_rerun_without_its_staged_options_reads_and_writes_what_the_import_does()
    {
        IReadOnlyList<ImportCommand.Step> steps = ImportCommand.Steps(new("local/arena2", "local/Sound", null, null), root);

        int staged = 0;
        foreach (ImportCommand.Step step in steps)
        {
            ToolCommand command = Program.Commands[step.Arguments[0]];
            for (int index = 1; index < step.Arguments.Count - 1; index++)
            {
                if (command.Options.FirstOrDefault(option => option.Name == step.Arguments[index]) is not { Default: { } path } option) continue;
                Assert.True(path == step.Arguments[index + 1], $"step '{step.Label}' passes {option.Name} '{step.Arguments[index + 1]}', but the command defaults it to '{path}'");
                staged++;
            }
        }

        Assert.True(staged > 50, $"only {staged} staged options were checked");
        Assert.Equal(ImportLayout.ImportedPayload, Program.Commands["locations"].Options.Single(option => option.Name == "--pack").Default);
    }

    [Fact]
    public void A_staged_default_outside_the_repository_root_is_refused_with_the_usage()
    {
        ToolCommand corpora = Program.Commands["classic-quest-corpora"];

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => corpora.Invoke(["classic-quest-corpora"]));

        Assert.Contains($"--pack defaults to '{ImportLayout.ImportedPayload}' in the repository root", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(corpora.Usage, refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"[--pack IMPORTED.json (default {ImportLayout.ImportedPayload})]", corpora.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_data_container_steps_are_the_same_whichever_music_was_supplied()
    {
        IReadOnlyList<ImportCommand.Step> without = ImportCommand.Steps(new("local/arena2", null, null, null), root);
        IReadOnlyList<ImportCommand.Step> with = ImportCommand.Steps(new("local/arena2", "local/Sound", null, null), root);

        // Only the music container's own step reads the music folder; no other step names it or what it staged.
        Assert.DoesNotContain(without, step => step.Label == "music-tracks");
        ImportCommand.Step tracks = Assert.Single(with, step => step.Label == "music-tracks");
        Assert.Equal(["music-tracks", "--music", "local/Sound", "--out", ImportLayout.MusicTracks], tracks.Arguments);
        Assert.Equal(
            without.Select(step => string.Join('\u001f', [step.Label, .. step.Arguments])),
            with.Where(step => step != tracks).Select(step => string.Join('\u001f', [step.Label, .. step.Arguments])));
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
    public void Every_pack_a_tracked_bundle_selects_is_either_tracked_or_derived_and_derived_payloads_are_payloads()
    {
        // The derived packs' descriptors travel only in the data container, so a tracked descriptor of the same
        // pack would be a second statement of it.
        string content = Path.Combine(TestData.RepositoryRoot, "content/worldrpg");
        HashSet<string> tracked = [.. Directory.EnumerateFiles(Path.Combine(content, "content-packs"), "*.pack.json")
            .Select(file => (string)JsonNode.Parse(File.ReadAllText(file))!["id"]!)];
        HashSet<string> derived = [.. DataContainers.DerivedPacks().Select(pack => (string)pack["id"]!)];
        Assert.Empty(tracked.Intersect(derived));

        string[] selected = [.. Directory.EnumerateFiles(Path.Combine(content, "bundles"), "*.bundle.json")
            .SelectMany(file => JsonNode.Parse(File.ReadAllText(file))!["contentPacks"]!.AsArray().Select(pack => (string)pack!["id"]!))
            .Distinct(StringComparer.Ordinal)];
        Assert.NotEmpty(selected);
        Assert.All(selected, id => Assert.True(tracked.Contains(id) || derived.Contains(id), $"bundle-selected pack '{id}' is neither tracked nor derived"));
        Assert.Contains("daggerfall.imported", selected);

        Assert.All(DataContainers.DerivedPacks(), pack => Assert.StartsWith("worldrpg/payloads/", (string)pack["payload"]!, StringComparison.Ordinal));
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
        foreach (JsonObject pack in DataContainers.DerivedPacks()) Write(staging, (string)pack["payload"]!, "{}"u8);
        DataContainers.WriteDescriptor(staging);
        string tracks = Path.Combine(root, "tracks");
        Write(tracks, "song_dungeon.ogg", [9, 9, 9]);
        DataContainers.WriteMusicDescriptor(tracks);
        string container = Path.Combine(root, "local", "daggerfall-data.rpak");
        string music = Path.Combine(root, "local", "music.rpak");
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());

        DataContainers.PackOne(host, staging, container, compress: true);
        DataContainers.PackOne(host, tracks, music, compress: false);

        host.Call(engine =>
        {
            using ProductContentBundle opened = ProductContentBundle.OpenContainer(engine.Content, container);
            Assert.Contains("worldrpg/imports/shared/meshes/a.rstatmsh", opened.Entries.ToArray().Select(entry => entry.Path));
            Assert.Equal(mesh, opened.ReadBytes("worldrpg/imports/shared/meshes/a.rstatmsh").ToArray());
            using ContentReference reference = engine.Content.ResolveReference(new ContentResolveRequest("worldrpg/imports/shared/meshes/a.rstatmsh", Digest(mesh)));
            JsonNode data = JsonNode.Parse(opened.ReadText(ImportLayout.DescriptorPath))!;
            Assert.Equal(ImporterBuild.Revision, (string)data["importerRevision"]!);
            Assert.Equal(DataContainers.DerivedPacks().Count, data["contentPacks"]!.AsArray().Count);

            using ProductContentBundle score = ProductContentBundle.OpenContainer(engine.Content, music);
            Assert.Equal(["song_dungeon.ogg", ImportLayout.DescriptorPath], score.Entries.ToArray().Select(entry => entry.Path).Order(StringComparer.Ordinal));
            JsonNode described = JsonNode.Parse(score.ReadText(ImportLayout.DescriptorPath))!;
            Assert.Equal(DataContainers.DescriptorKind, (string)described["kind"]!);
            Assert.Equal(ImporterBuild.Revision, (string)described["importerRevision"]!);
            Assert.Empty(described["contentPacks"]!.AsArray());
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

            string music = Path.Combine(TestData.RepositoryRoot, ImportLayout.MusicContainer);
            if (!File.Exists(music)) return;
            using ProductContentBundle score = ProductContentBundle.OpenContainer(engine.Content, music);
            Assert.Equal(DataContainers.DescriptorKind, (string)JsonNode.Parse(score.ReadText(ImportLayout.DescriptorPath))!["kind"]!);
            Assert.All(score.Entries.ToArray().Where(entry => entry.Path != ImportLayout.DescriptorPath),
                entry => Assert.Contains(Path.GetFileNameWithoutExtension(entry.Path), ClassicMusicTracks.All));
        });
    }

    [ImportedDataContainerFact]
    public void The_imported_data_container_lists_the_authored_ui_art_it_leaves_to_the_product_and_names_no_music()
    {
        IReadOnlyDictionary<string, ContentReferenceInfo> entries = TestDataContainers.Entries(TestDataContainers.Data);
        Assert.DoesNotContain(entries.Keys, path => path.StartsWith("worldrpg/media/ui/authored/", StringComparison.Ordinal));

        // The classic media inventory names every tracked original by the product's content path, with its own digest.
        string tracked = Path.Combine(TestData.RepositoryRoot, AuthoredUi.ProductDirectory);
        JsonNode[] authored = [.. JsonNode.Parse(TestDataContainers.Read(TestDataContainers.Data, "worldrpg/media/classic-media-inventory.json"))!["artifacts"]!.AsArray()
            .Select(entry => entry!).Where(entry => ((string)entry["path"]!).StartsWith("worldrpg/media/ui/authored/", StringComparison.Ordinal))];
        Assert.Equal(
            Directory.EnumerateFiles(tracked, "*.png").Select(file => "worldrpg/media/ui/authored/" + Path.GetFileName(file)).Order(StringComparer.Ordinal),
            authored.Select(entry => (string)entry["path"]!).Order(StringComparer.Ordinal));
        foreach (JsonNode entry in authored)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(tracked, Path.GetFileName((string)entry["path"]!)));
            Assert.Equal(bytes.LongLength, (long)entry["byteLength"]!);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), (string)entry["sha256"]!);
        }

        Assert.Contains(authored, entry => entry["display"] is not null);

        // No classic sidecar names music: the data container is the same whichever recordings were supplied.
        string[] sidecars = [.. entries.Keys.Where(path => path.StartsWith("worldrpg/imports/", StringComparison.Ordinal)
            && path.EndsWith("/" + Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, StringComparison.Ordinal))];
        Assert.Contains("worldrpg/imports/shared/" + Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, sidecars);
        Assert.All(sidecars, path => Assert.False(JsonNode.Parse(TestDataContainers.Read(TestDataContainers.Data, path))!.AsObject().ContainsKey("music"), path));
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
