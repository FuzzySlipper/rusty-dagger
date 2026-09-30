using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Enumerates MONSTER.BSA and reports what every record is, which source mobile it belongs to, and which
/// records nothing in this build explains. A report command still fails when the archive cannot be read at
/// all; records it merely cannot explain are reported rather than treated as a failure.
/// </summary>
internal static class MonsterArchiveCommand
{
    public static ToolCommand Command { get; } = new("monster-archive", [CommandOption.Required("--monster", "MONSTER.BSA")], Run);

    private static int Run(CommandArguments args)
    {
        string path = args["--monster"];
        MonsterArchiveInventory inventory = MonsterArchiveInventory.Enumerate(File.ReadAllBytes(path), Path.GetFileName(path));
        int decoded = inventory.Records.Count(record => record.Disposition == MonsterArchiveRecordDisposition.Decoded);
        int malformed = inventory.Records.Count(record => record.Disposition == MonsterArchiveRecordDisposition.Malformed);
        int unrecognized = inventory.Records.Count(record => record.Disposition == MonsterArchiveRecordDisposition.Unrecognized);
        Console.WriteLine($"{inventory.Source}: {inventory.Records.Count} records, {inventory.AnimationScripts.Count()} animation scripts, {inventory.EnemyConfigurations.Count()} enemy configurations, {decoded} decoded, {malformed} malformed, {unrecognized} unrecognized");
        Console.WriteLine($"links: {inventory.Records.Count(record => record.IsLinked)} records resolve to a supported source mobile, {inventory.Unlinked.Count()} stay unlinked");
        foreach (MonsterArchiveRecord record in inventory.Records.Where(record => record.Disposition != MonsterArchiveRecordDisposition.Decoded).Take(8))
        {
            Console.WriteLine($"  {record.Name}: {record.Disposition} - {record.Note}");
        }

        return 0;
    }
}
