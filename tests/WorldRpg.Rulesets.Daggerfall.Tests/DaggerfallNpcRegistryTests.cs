using Rusty.Engine;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// NPC identity: stable keys return one person, civilians regenerate distinct, presence and
/// relocation preserve references, and saves round-trip.
/// </summary>
public sealed class DaggerfallNpcRegistryTests
{
    [Fact]
    public void Stable_keys_return_one_person_while_civilians_stay_distinct()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", "Mages Guild");
        DaggerfallNpcAppearance look = new("Breton", "Female", 211, 12, 7, 0);

        long first = npcs.RegisterStable(DaggerfallNpcKind.Questor, "M0B00Y00", site, look, "questor", ["talk", "quest"]);
        long second = npcs.RegisterStable(DaggerfallNpcKind.Questor, "M0B00Y00", site, look, "questor", ["talk", "quest"]);
        Assert.Equal(first, second);

        // Identical visuals still mint distinct civilians.
        long civilianA = npcs.RegisterCivilian(site, look, "civilian", ["talk"]);
        long civilianB = npcs.RegisterCivilian(site, look, "civilian", ["talk"]);
        Assert.NotEqual(civilianA, civilianB);
        Assert.NotEqual(first, civilianA);

        // Civilians cannot hold a stable key.
        Assert.Throws<ArgumentOutOfRangeException>(() => npcs.RegisterStable(DaggerfallNpcKind.Civilian, "key", site, look, "civilian", ["talk"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => npcs.RegisterCivilian(new DaggerfallNpcSite(62, "Nowhere", string.Empty), look, "civilian", ["talk"]));

        // Hiding and relocating preserve the reference.
        npcs.SetPresence(first, DaggerfallNpcPresence.Hidden);
        npcs.Relocate(first, 100, 200, 300);
        DaggerfallNpc questor = npcs.Require(first);
        Assert.Equal(DaggerfallNpcPresence.Hidden, questor.Presence);
        Assert.Equal((100, 200, 300), (questor.X, questor.Y, questor.Z));
        Assert.Equal(site, questor.Site);
    }

    [Fact]
    public void Npcs_round_trip_through_save_records()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance look = new("Nord", "Male", 210, 4, 3, 5);
        long questor = npcs.RegisterStable(DaggerfallNpcKind.Questor, "Q", site, look, "questor", ["talk", "quest"]);
        npcs.SetPresence(questor, DaggerfallNpcPresence.Hidden);
        long civilian = npcs.RegisterCivilian(site, look, "civilian", ["talk"]);

        DaggerfallNpcSave saved = new([.. npcs.Capture().Select(npc => new DaggerfallNpcEntry(
            npc.DurableId, (int)npc.Kind, npc.StableKey, npc.Site.Region, npc.Site.Location, npc.Site.Building,
            npc.Appearance.Race, npc.Appearance.Gender, npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord,
            npc.Appearance.NameSeed, npc.Appearance.FactionId, npc.Role, [.. npc.Services],
            (int)npc.Presence, npc.X, npc.Y, npc.Z))]);
        saved.Validate();

        DaggerfallNpcRegistry restored = Registry();
        restored.Restore(saved.Entries.Select(entry => new DaggerfallNpc(
            entry.DurableId, (DaggerfallNpcKind)entry.Kind, entry.StableKey,
            new DaggerfallNpcSite(entry.Region, entry.Location, entry.Building),
            new DaggerfallNpcAppearance(entry.Race, entry.Gender, entry.BillboardArchive, entry.BillboardRecord, entry.NameSeed, entry.FactionId),
            entry.Role, entry.Services, (DaggerfallNpcPresence)entry.Presence, entry.X, entry.Y, entry.Z)));

        // The questor keeps its identity and presence; the civilian keeps its own identity too.
        Assert.Equal(questor, restored.RegisterStable(DaggerfallNpcKind.Questor, "Q", site, look, "questor", ["talk", "quest"]));
        Assert.Equal(DaggerfallNpcPresence.Hidden, restored.Require(questor).Presence);
        Assert.Equal(civilian, restored.Require(civilian).DurableId);
    }

    private static DaggerfallNpcRegistry Registry()
    {
        DaggerfallNpcRegistry npcs = new()
        {
            Identities = DurableIdentityAllocator.Restore(new DurableIdentityState(
            [
                new KindAllocatorState(DurableIdentityKind.Actor, 1_000_000, [], []),
            ])),
        };
        return npcs;
    }
}
