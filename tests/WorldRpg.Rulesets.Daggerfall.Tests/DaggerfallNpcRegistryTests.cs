using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
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

    [Fact]
    public void Source_population_key_reuses_one_identity_across_reentry_and_restore()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance look = new("Breton", "Female", 211, 12, 7, 0);

        long first = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", look, "civilian", ["talk"]);
        long reentry = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", look, "civilian", ["talk"]);

        Assert.Equal(first, reentry);
        npcs.SetPresence(first, DaggerfallNpcPresence.Hidden);
        DaggerfallNpc saved = npcs.Require(first);

        DaggerfallNpcRegistry restored = Registry();
        restored.Restore([saved]);

        Assert.Equal(first, restored.RegisterPopulationCivilian(site, saved.StableKey, look, "civilian", ["talk"]));
        Assert.Equal(DaggerfallNpcPresence.Hidden, restored.Require(first).Presence);
    }

    [Fact]
    public void Source_population_facts_refresh_without_replacing_identity_or_pose()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance original = new("Breton", "Female", 211, 12, 7, 0);
        DaggerfallNpcAppearance source = new("Nord", "Male", 212, 13, 8, 41);

        long id = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", original, "civilian", ["talk"]);
        npcs.Relocate(id, 1, 2, 3);
        npcs.SetPresence(id, DaggerfallNpcPresence.Hidden);
        npcs.RefreshPopulationFacts(id, source, "guard", ["talk", "arrest"]);

        DaggerfallNpc refreshed = npcs.Require(id);
        Assert.Equal(id, refreshed.DurableId);
        Assert.Equal("population/17-2/0/4", refreshed.StableKey);
        Assert.Equal(source, refreshed.Appearance);
        Assert.Equal("guard", refreshed.Role);
        Assert.Equal(["talk", "arrest"], refreshed.Services);
        Assert.Equal(DaggerfallNpcPresence.Hidden, refreshed.Presence);
        Assert.Equal((1F, 2F, 3F), (refreshed.X, refreshed.Y, refreshed.Z));
    }

    [Fact]
    public void Hidden_population_is_not_a_live_gameplay_actor()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance look = new("Breton", "Male", 211, 12, 7, 0);
        long id = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", look, "civilian", ["talk"]);

        Assert.True(npcs.IsGameplayActive(id));
        npcs.SetPresence(id, DaggerfallNpcPresence.Hidden);
        Assert.False(npcs.IsGameplayActive(id));
        Assert.True(npcs.IsGameplayActive(DaggerfallActorIdentity.PlayerEntityId));
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
