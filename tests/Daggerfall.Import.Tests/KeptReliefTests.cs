using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Relief;
using Daggerfall.Import.Tool;
using Daggerfall.Import.Tool.Commands;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The tracked relief bake is taken only when its record names exactly the bake the import would make; anything else is
/// baked again, and the reason names what changed. The bake itself is <see cref="ReliefBakeTests"/>'.
/// </summary>
public sealed class KeptReliefTests
{
    private static readonly byte[] Relief = [1, 2, 3], Rivers = [4, 5];

    private const string Refresh = "refresh the tracked bake with this code: run the import on den-agents (or `relief --arena2 local/inputs/arena2 "
        + "--pack local/build/import/staging/worldrpg/payloads/daggerfall.imported.json --out local/build/import/staging "
        + "--keep src/Daggerfall.Import.Tool/inputs/relief --update`) and commit what it rewrote with the change to the bake";

    [Fact]
    public void The_tracked_bake_is_the_one_the_bake_code_in_the_tree_makes()
    {
        string tracked = Path.Combine(TestData.RepositoryRoot, ImportLayout.KeptRelief);
        JsonObject json = JsonNode.Parse(File.ReadAllBytes(Path.Combine(tracked, KeptRelief.RecordFile)))!.AsObject();
        ReliefRecord record = ReliefRecord.FromJson(json);

        Assert.True(record.Code == ImporterBuild.ReliefBakeRevision,
            $"the tracked bake was baked by bake code {record.Code[..12]}, and the tree's is {ImporterBuild.ReliefBakeRevision[..12]}: {Refresh}.");
        Assert.True(record.Settings == ReliefRecord.SettingsOf(ReliefBake.DefaultStages, ReliefBake.DefaultSourceSquareKilometres),
            $"the tracked bake was baked with '{record.Settings}', not the tree's stages and river threshold: {Refresh}.");
        Assert.True(ReliefRecord.Digest(File.ReadAllBytes(Path.Combine(tracked, "relief.bin"))) == record.Relief
            && ReliefRecord.Digest(File.ReadAllBytes(Path.Combine(tracked, "rivers.bin"))) == record.Rivers,
            $"the tracked relief.bin and rivers.bin are not the files their record names: {Refresh}.");
        // Only what the bake is baked from: nothing that names a machine, a path or a runtime.
        Assert.Equal(["code", "heights", "places", "rain", "relief", "rivers", "settings"], json.Select(field => field.Key).Order(StringComparer.Ordinal));
    }

    [ImportToolDataFact]
    public void The_tracked_bake_is_taken_for_the_supplied_arena2_and_the_imported_climate_and_locations()
    {
        string imported = ImportedContent.Staged("worldrpg/payloads/daggerfall.imported.json");
        ReliefInput input = ReliefInputs.Read(WoodsReader.Read(File.ReadAllBytes(TestData.Corpus("WOODS.WLD")), "WOODS.WLD"),
            PayloadFiles.ReadSection<DaggerfallClimateGrid>(imported, "climate"), PayloadFiles.ReadSection<DaggerfallLocations>(imported, "locations"));

        Assert.True(KeptRelief.Take(Path.Combine(TestData.RepositoryRoot, ImportLayout.KeptRelief), Record(input), out string why) is not null,
            $"an import here would bake, since {why}");
    }

    [Fact]
    public void A_bake_kept_with_its_record_is_taken_when_nothing_it_is_baked_from_changed()
    {
        using Folder folder = new();
        ReliefRecord record = Record(Input());
        KeptRelief.Keep(folder.Path, record, Relief, Rivers);

        (byte[] Relief, byte[] Rivers)? kept = KeptRelief.Take(folder.Path, Record(Input()), out string why);

        Assert.NotNull(kept);
        Assert.Equal(Relief, kept.Value.Relief);
        Assert.Equal(Rivers, kept.Value.Rivers);
        Assert.Equal("", why);
    }

    [Fact]
    public void A_changed_input_is_baked_again_and_named()
    {
        using Folder folder = new();
        ReliefInput input = Input();
        KeptRelief.Keep(folder.Path, Record(input), Relief, Rivers);

        double[] higher = (double[])input.Classic.Clone();
        higher[7] += .125;
        double[] wetter = (double[])input.Rain.Clone();
        wetter[3] = 1.7;
        Assert.Contains("the heights from WOODS.WLD changed", Refusal(folder, Record(input with { Classic = higher })));
        Assert.Contains("the rain from the climate section changed", Refusal(folder, Record(input with { Rain = wetter })));
        Assert.Contains("the places from the locations section changed", Refusal(folder, Record(input with { Places = [new(3, 2, 4)] })));
        Assert.Contains("the bake's stages and river threshold changed",
            Refusal(folder, ReliefRecord.Of(input, [new(ReliefBake.NodesPerPixel, 1, 1)], ReliefBake.DefaultSourceSquareKilometres)));
        Assert.Contains("the bake's code (src/Daggerfall.Import/Relief) changed", Refusal(folder, Record(input) with { Code = new string('0', 64) }));
    }

    [Fact]
    public void A_missing_unreadable_or_contradicted_record_is_baked_again_with_why()
    {
        using Folder folder = new();
        ReliefRecord record = Record(Input());
        Assert.Contains("holds no bake", Refusal(folder, record));

        KeptRelief.Keep(folder.Path, record, Relief, Rivers);
        string recordFile = Path.Combine(folder.Path, KeptRelief.RecordFile);
        string text = File.ReadAllText(recordFile);
        File.WriteAllText(recordFile, text[..(text.Length / 2)]);
        Assert.Contains("cannot be read", Refusal(folder, record));
        File.WriteAllText(recordFile, text.Replace("\"places\"", "\"spots\"", StringComparison.Ordinal));
        Assert.Contains("cannot be read: it has no 'places'", Refusal(folder, record));

        File.WriteAllText(recordFile, text);
        File.WriteAllBytes(Path.Combine(folder.Path, "rivers.bin"), [4, 6]);
        Assert.Contains("are not the ones its record names", Refusal(folder, record));
        File.Delete(Path.Combine(folder.Path, "rivers.bin"));
        Assert.Contains("are not the ones its record names", Refusal(folder, record));
    }

    [Fact]
    public void Only_the_import_keeps_the_bake_so_a_relief_run_by_hand_still_bakes()
    {
        ImportCommand.Step relief = Assert.Single(ImportCommand.Steps(new("local/inputs/arena2", null)), step => step.Label == "relief");
        Assert.Equal(ImportLayout.KeptRelief, relief.Arguments[relief.Arguments.ToList().IndexOf("--keep") + 1]);
        CommandOption keep = Assert.Single(ReliefCommand.Command.Options, option => option.Name == "--keep");
        Assert.Null(keep.Default);
        Assert.False(keep.IsRequired);
    }

    private static string Refusal(Folder folder, ReliefRecord wanted)
    {
        Assert.Null(KeptRelief.Take(folder.Path, wanted, out string why));
        return why;
    }

    private static ReliefRecord Record(ReliefInput input) => ReliefRecord.Of(input, ReliefBake.DefaultStages, ReliefBake.DefaultSourceSquareKilometres);

    private static ReliefInput Input()
    {
        double[] classic = new double[4 * 3], rain = new double[4 * 3];
        for (int i = 0; i < classic.Length; i++) classic[i] = ReliefBake.OceanMetres + (i * 10);
        Array.Fill(rain, 1);
        return new(4, 3, classic, rain, [new(2, 1, 4)]);
    }

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kept-relief-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
