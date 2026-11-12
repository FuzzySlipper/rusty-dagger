using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the classic spell and magic-item catalogs into the imported payload. Both Arena2 files are
/// required inputs: a catalog built from one of them would look complete while resolving half of what the
/// game defines, and SPELL.RSC is refused by name because it is not a file this corpus carries.
/// </summary>
internal static class MagicCatalogCommand
{
    public static ToolCommand Command { get; } = new("magic-catalog",
        [Options.Arena2, Options.Pack, CommandOption.Required("--spell-costs", "SPELL_COSTS.json"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        // The spell-cost tables are not in any Arena2 file (ours were taken from the donor, which reversed them
        // from the executable), and the ruleset refuses a catalog whose spell effects carry no cost row.
        string costs = args["--spell-costs"];
        string? refused = Directory.EnumerateFiles(arena2, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "SPELL.RSC", StringComparison.OrdinalIgnoreCase));
        if (refused is not null)
        {
            throw new InvalidOperationException($"'{refused}' is a SPELL.RSC input; the classic spell catalog lives in SPELLS.STD and this corpus carries no SPELL.RSC to read.");
        }

        string spellPath = Path.Combine(arena2, "SPELLS.STD");
        string magicPath = Path.Combine(arena2, "MAGIC.DEF");
        if (!File.Exists(spellPath)) throw new FileNotFoundException($"SPELLS.STD is required to build the spell catalog and is not in '{arena2}'.", spellPath);
        if (!File.Exists(magicPath)) throw new FileNotFoundException($"MAGIC.DEF is required to build the magic-item catalog and is not in '{arena2}'.", magicPath);
        Arena2MagicCatalogPublication publication = Arena2MagicCatalogDocument.Build(
            File.ReadAllBytes(spellPath), File.ReadAllBytes(magicPath), Options.Arena2Label("SPELLS.STD"), Options.Arena2Label("MAGIC.DEF"),
            Arena2MagicEffectCostTable.From(TakenTable.Read<SpellCostTable>(File.ReadAllBytes(costs), costs)));
        Console.WriteLine($"magic catalog: {publication.Spells} spells, {publication.MagicItems} magic items, {publication.Enchantments} enchantments, {publication.UnresolvedLinks} unresolved spell links, {publication.Dispositions} dispositions");
        if (!args.Switch("--update")) return Options.ReportOnly("this catalog");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal) { ["magic"] = publication.Json });
        return 0;
    }
}
