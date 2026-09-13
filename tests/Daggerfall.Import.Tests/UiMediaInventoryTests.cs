using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic UI media inventory: every documented family file with its canvas and its
/// binding, the files a published consumer already binds, and the rest retained as
/// required-pending rather than dropped.
/// </summary>
public sealed class UiMediaInventoryTests
{
    [Fact]
    public void Enumerates_every_documented_family_file_with_its_canvases()
    {
        UiMediaInventory inventory = ReadInventory();

        // The documented counts are the check: 6;2;4;9;4;3;1;18;2;1;12.
        Assert.Equal(63, inventory.Files.Count);
        Assert.Equal(
            UiMediaInventory.DocumentedFamilies.Select(family => (family.Prefix, inventory.Family(family.Prefix).Count())),
            UiMediaInventory.DocumentedFamilies.Select(family => (family.Prefix, family.Count)));
        Assert.All(inventory.Files, file => Assert.False(string.IsNullOrWhiteSpace(file.Family)));
        // A file count is not a canvas count: the supplied CIF and the two GFX members carry
        // 27 canvases between them, so the families supply 87 canvases across 63 files.
        Assert.Equal(87, inventory.Files.Sum(file => file.CanvasCount));
        Assert.All(inventory.Files.Where(file => file.Decode != Arena2CanvasKind.Unread), file =>
        {
            Assert.True(file.CanvasCount > 0);
            Assert.All(file.Canvases, canvas => Assert.True(canvas.Width > 0 && canvas.Height > 0));
            Assert.Contains("Read as", file.Note, StringComparison.Ordinal);
        });
        // Every supplied file reads. TALK00I0.IMG declares compression 0x0800, and the classic
        // IMG reader never consults that field — it reads the record's shape and nothing else — so
        // the file is one 320x200 record rather than the unreadable file an earlier revision
        // reported it as.
        Assert.Empty(inventory.Unread);
        UiMediaRecord talk = inventory.Files.Single(file => file.Path == "TALK00I0.IMG");
        Assert.Equal(Arena2CanvasKind.ImgRecord, talk.Decode);
        Assert.Equal((320, 200), (talk.Canvases[0].Width, talk.Canvases[0].Height));
    }

    [Fact]
    public void Keeps_every_family_even_when_no_consumer_binds_it()
    {
        UiMediaInventory inventory = ReadInventory();

        // No family may be dropped for being unbound: the supply is the fact, the binding
        // is the pending work.
        Assert.Equal(UiMediaInventory.DocumentedFamilies.Length, inventory.Files.Select(file => file.Family).Distinct(StringComparer.Ordinal).Count());
        Assert.All(inventory.RequiredPending, file => Assert.False(string.IsNullOrWhiteSpace(file.Note)));
        Assert.All(inventory.RequiredPending, file => Assert.Equal(string.Empty, file.Consumer));
    }

    [Fact]
    public void Admits_exactly_the_files_the_published_ui_manifest_binds()
    {
        UiMediaInventory inventory = ReadInventory();

        // The admitted set is the published manifest's, so a published artifact can never
        // lack a source identity or a consumer.
        Assert.Equal(
            ["INFO00I0.IMG", "INVE00I0.IMG", "MAIN00I0.IMG", "MAIN03I0.IMG", "MAIN04I0.IMG", "MAIN05I0.IMG"],
            inventory.Admitted.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.All(inventory.Admitted, file => Assert.Equal("the published classic UI media manifest", file.Consumer));
        Assert.Equal(57, inventory.RequiredPending.Count());
    }

    [Fact]
    public void Names_the_documented_inventory_for_a_pending_binding()
    {
        UiMediaInventory inventory = ReadInventory();

        // A pending binding says which task's inventory names the family, so the concern is
        // carried rather than noted.
        UiMediaRecord guild = inventory.Family("GILD").First();
        Assert.Equal(MediaBinding.RequiredPending, guild.Binding);
        Assert.Contains("F104", guild.Note, StringComparison.Ordinal);
        Assert.Contains("GILD", guild.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Binds_a_file_whatever_case_the_consumer_names_it_in()
    {
        // The family is matched case-insensitively, so a consumer naming MAIN00I0.IMG binds
        // a path stored as main00i0.img rather than half-matching it.
        UiMediaInventory inventory = UiMediaInventory.Enumerate(
            [("main00i0.img", ValidImage())],
            new HashSet<string>(["MAIN00I0.IMG"], StringComparer.Ordinal),
            "the fixture consumer",
            "fixture");

        Assert.Equal(MediaBinding.Admitted, inventory.Files.Single().Binding);
    }

    [Fact]
    public void Refuses_a_file_in_none_of_the_documented_families()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => UiMediaInventory.Enumerate(
            [("NOTAFAMILY.IMG", new byte[16])],
            new HashSet<string>(StringComparer.Ordinal),
            "fixture consumer",
            "fixture"));

        Assert.Contains("none of the documented UI media families", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retains_a_supplied_file_the_decoder_does_not_read()
    {
        UiMediaInventory inventory = UiMediaInventory.Enumerate(
            [("MAIN00I0.IMG", new byte[8]), ("MAIN01I0.IMG", ValidImage())],
            new HashSet<string>(StringComparer.Ordinal),
            "fixture consumer",
            "fixture");

        Assert.Equal(Arena2CanvasKind.Unread, inventory.Files.Single(file => file.Path == "MAIN00I0.IMG").Decode);
        // The neighbour must still read: an assertion that only restates the expected
        // disposition cannot fail and would not notice one bad file affecting another.
        UiMediaRecord neighbour = inventory.Files.Single(file => file.Path == "MAIN01I0.IMG");
        Assert.Equal(Arena2CanvasKind.ImgRecord, neighbour.Decode);
        Assert.Equal((320, 46), (neighbour.Canvases[0].Width, neighbour.Canvases[0].Height));
        Assert.Single(inventory.Unread);
    }

    [Fact]
    public void The_documented_inventory_carries_the_families_it_claims()
    {
        // The family counts come from the manifest's CNT-020 row; this checks the corpus
        // against them in both directions rather than trusting either side.
        string line = File.ReadLines(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv"))
            .Single(value => value.StartsWith("CNT-020,family,", StringComparison.Ordinal));
        int[] documented = [.. line.Split(',')[5].Split(';', StringSplitOptions.TrimEntries).Select(int.Parse)];

        // The documented counts and the corpus agree for every family the corpus supplies
        // under that prefix, with one recorded difference: the inventory records one INFO
        // file where the corpus carries two, because INFO01I0.IMG is present and undocumented.
        UiMediaInventory inventory = ReadInventory();
        int[] measured = [.. UiMediaInventory.DocumentedFamilies.Select(entry => inventory.Family(entry.Prefix).Count())];
        Assert.Equal(UiMediaInventory.DocumentedFamilies.Length, documented.Length);
        Assert.Equal(
            documented.Select(count => count),
            measured.Select(count => count).Select((count, index) => UiMediaInventory.DocumentedFamilies[index].Prefix == "INFO" ? count - 1 : count));
        Assert.Equal(2, inventory.Family("INFO").Count());
    }

    private static byte[] ValidImage() => File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/MAIN00I0.IMG"));

    private static UiMediaInventory ReadInventory()
    {
        string root = RepositoryRoot();
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach ((string prefix, int _) in UiMediaInventory.DocumentedFamilies)
        {
            foreach (string path in Directory.GetFiles(Path.Combine(root, "local/arena2"), $"{prefix}*"))
            {
                sources.Add((Path.GetFileName(path), File.ReadAllBytes(path)));
            }
        }

        return UiMediaInventory.Enumerate(sources, BoundFiles(), "the published classic UI media manifest", "local/arena2");
    }

    /// <summary>The files the published classic media manifest binds.</summary>
    private static HashSet<string> BoundFiles() =>
    [
        "INFO00I0.IMG", "INVE00I0.IMG", "MAIN00I0.IMG", "MAIN03I0.IMG", "MAIN04I0.IMG", "MAIN05I0.IMG",
    ];

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
