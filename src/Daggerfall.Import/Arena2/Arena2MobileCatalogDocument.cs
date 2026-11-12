using System.Text.Json;
using Daggerfall.Import.Publication;
using System.Text.Json.Nodes;

namespace Daggerfall.Import.Arena2;

/// <summary>One mobile as the donor's static table defines it, with the parameters that table carries.</summary>
public sealed record Arena2MobileTableEntry(
    int Id,
    string Name,
    string Behaviour,
    string Affinity,
    int MaleTexture,
    int FemaleTexture,
    int CorpseArchive,
    int CorpseRecord,
    bool HasIdle,
    bool HasRangedAttack1,
    bool HasRangedAttack2,
    string MoveSound,
    string BarkSound,
    string AttackSound,
    string? LootTableKey,
    string MinMetalToHit,
    int MinDamage,
    int MaxDamage,
    int MinHealth,
    int MaxHealth,
    int Level,
    int ArmorValue,
    bool ParrySounds,
    int MapChance,
    int Weight,
    string Team,
    bool SeesThroughInvisibility,
    bool CastsMagic,
    bool CanOpenDoors)
{
    public int SoulPoints { get; init; }
    public bool NoShadow { get; init; }
    public bool PrefersRanged { get; init; }
    public Arena2MobileGlow? GlowColor { get; init; }
}

public sealed record Arena2MobileGlow(float Red, float Green, float Blue);

/// <summary>
/// Our mobile table, taken once from the donor's static <c>EnemyBasics.Enemies</c> table: every entry in file order,
/// with the values the donor states and nothing invented for a field it leaves out.
/// </summary>
public sealed record MobileTable(TakenFrom Source, IReadOnlyList<Arena2MobileTableEntry> Mobiles);

/// <summary>The published mobile catalog and how many mobiles it carried.</summary>
public sealed record Arena2MobileCatalogPublication(string Json, int Mobiles);

/// <summary>
/// Publishes the donor's static mobile table as one normalized record per mobile: the parameters a later
/// actor-construction task consumes, and the identity its donor name gives it.
/// </summary>
/// <remarks>
/// The donor's table is the parameter authority and the authored pack is the actor authority. Every mobile is
/// published, whether or not an authored actor carries it, because the coverage record is the point of
/// publishing the table at all; the ruleset joins each identity to the authored actors when it loads.
/// </remarks>
public static class Arena2MobileCatalogDocument
{
    /// <summary>
    /// Builds the document's JSON from the donor table. The mobile table is the parameter authority; a
    /// supplied MONSTER.BSA inventory additionally supplies each mobile's career attack-modifier byte, which
    /// lives in its <c>ENEMY###.CFG</c> record and not in the table, and the archive it was read from is cited
    /// beside the table.
    /// </summary>
    public static Arena2MobileCatalogPublication Build(
        MobileTable mobileTable, MonsterArchiveInventory? enemyConfigurations = null, PublishedSource? enemyConfigurationArchive = null)
    {
        IReadOnlyList<Arena2MobileTableEntry> table = mobileTable.Mobiles;
        JsonArray mobiles = [];
        foreach (Arena2MobileTableEntry entry in table)
        {
            string identity = MobileLedgerBuilder.Identity(entry.Name);
            var career = enemyConfigurations?.EnemyConfigurations.SingleOrDefault(record => record.MobileId == entry.Id)?.Configuration;
            mobiles.Add(new JsonObject
            {
                ["donorId"] = entry.Id,
                ["donorName"] = entry.Name,
                ["identity"] = identity,
                ["seesThroughInvisibility"] = entry.SeesThroughInvisibility,
                ["castsMagic"] = entry.CastsMagic,
                ["canOpenDoors"] = entry.CanOpenDoors,
                ["soulPoints"] = entry.SoulPoints,
                ["noShadow"] = entry.NoShadow,
                ["prefersRanged"] = entry.PrefersRanged,
                ["glowColor"] = entry.GlowColor is { } glow ? new JsonObject
                { ["red"] = glow.Red, ["green"] = glow.Green, ["blue"] = glow.Blue } : null,
                ["behaviour"] = entry.Behaviour,
                ["affinity"] = entry.Affinity,
                ["maleTexture"] = entry.MaleTexture,
                ["femaleTexture"] = entry.FemaleTexture,
                ["corpse"] = new JsonObject { ["archive"] = entry.CorpseArchive, ["record"] = entry.CorpseRecord },
                ["hasIdle"] = entry.HasIdle,
                ["hasRangedAttack1"] = entry.HasRangedAttack1,
                ["hasRangedAttack2"] = entry.HasRangedAttack2,
                ["sounds"] = new JsonObject
                {
                    ["move"] = entry.MoveSound,
                    ["bark"] = entry.BarkSound,
                    ["attack"] = entry.AttackSound,
                },
                ["lootTableKey"] = entry.LootTableKey,
                ["minMetalToHit"] = entry.MinMetalToHit,
                ["damage"] = new JsonObject { ["minimum"] = entry.MinDamage, ["maximum"] = entry.MaxDamage },
                ["health"] = new JsonObject { ["minimum"] = entry.MinHealth, ["maximum"] = entry.MaxHealth },
                ["level"] = entry.Level,
                ["armorValue"] = entry.ArmorValue,
                ["parrySounds"] = entry.ParrySounds,
                ["mapChance"] = entry.MapChance,
                ["weight"] = entry.Weight,
                ["team"] = entry.Team,
                ["attackModifierFlags"] = AttackModifierFlags(enemyConfigurations, entry.Id),
                ["resistanceFlags"] = (int)(career?.ResistanceFlags ?? 0),
                ["immunityFlags"] = (int)(career?.ImmunityFlags ?? 0),
                ["lowToleranceFlags"] = (int)(career?.LowToleranceFlags ?? 0),
                ["criticalWeaknessFlags"] = (int)(career?.CriticalWeaknessFlags ?? 0),
            });
        }

        JsonObject document = new()
        {
            ["sources"] = new JsonArray([.. new[] { mobileTable.Source.Cited(), enemyConfigurationArchive }
                .OfType<PublishedSource>().Select(source => JsonSerializer.SerializeToNode(source, PublishedJson.Section))]),
            ["mobiles"] = mobiles,
        };

        return new Arena2MobileCatalogPublication(
            document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            table.Count);
    }

    /// <summary>
    /// The career attack-modifier byte the classic <c>ENEMY###.CFG</c> record carries for one mobile:
    /// the bonus and phobia bits its career applies against each enemy group. A mobile the supplied
    /// archive carries no decoded configuration for — every human mobile among them — carries none.
    /// </summary>
    private static int AttackModifierFlags(MonsterArchiveInventory? enemyConfigurations, int mobileId) =>
        enemyConfigurations is null ? 0 : enemyConfigurations.EnemyConfigurations
            .Where(record => record.MobileId == mobileId && record.Configuration is not null)
            .Select(record => (int)record.Configuration!.AttackModifierFlags)
            .DefaultIfEmpty(0)
            .Max();
}
