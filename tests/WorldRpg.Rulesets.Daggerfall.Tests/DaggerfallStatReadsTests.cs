using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Permanent and live stat reads: resistance and immunity stats, overlapping effect mods that
/// remove independently, derived maxima that follow live attributes, and save round trips.
/// </summary>
public sealed class DaggerfallStatReadsTests
{
    [Fact]
    public void Resistances_read_permanent_bases_with_mods_over_them()
    {
        StatsComponent mechanics = PlayerStats();

        Stat fire = mechanics.GetStat(StatId.Parse(DaggerfallMechanicsIds.ResistanceFire.Value));
        Assert.Equal(0, fire.ValueInt);
        StatModifierHandle first = DaggerfallStatModifiers.ApplyMod(mechanics, DaggerfallMechanicsIds.ResistanceFire, 25);
        StatModifierHandle second = DaggerfallStatModifiers.ApplyMod(mechanics, DaggerfallMechanicsIds.ResistanceFire, 10);
        Assert.Equal(35, fire.ValueInt);

        // Removing one mod removes exactly its contribution.
        Assert.True(DaggerfallStatModifiers.RemoveMod(mechanics, DaggerfallMechanicsIds.ResistanceFire, first));
        Assert.Equal(10, fire.ValueInt);
        Assert.False(DaggerfallStatModifiers.RemoveMod(mechanics, DaggerfallMechanicsIds.ResistanceFire, first));
        Assert.True(DaggerfallStatModifiers.RemoveMod(mechanics, DaggerfallMechanicsIds.ResistanceFire, second));
        Assert.Equal(0, fire.ValueInt);

        // Permanent moves the base the mods read over.
        DaggerfallStatModifiers.AdjustPermanent(mechanics, DaggerfallMechanicsIds.ResistanceFire, 40);
        Assert.Equal(40, fire.ValueInt);
        DaggerfallStatModifiers.ApplyMod(mechanics, DaggerfallMechanicsIds.ResistanceFire, -60);
        Assert.Equal(-20, fire.ValueInt);

        // Immunities are flags: only 0 or 1 is honest.
        Stat paralysis = mechanics.GetStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityParalysis.Value));
        Assert.Equal(0, paralysis.ValueInt);
        DaggerfallStatModifiers.AdjustPermanent(mechanics, DaggerfallMechanicsIds.ImmunityParalysis, 1);
        Assert.Equal(1, paralysis.ValueInt);
    }

    [Fact]
    public void Derived_maxima_follow_live_attributes_with_explicit_track_policy()
    {
        StatsComponent mechanics = PlayerStats();
        Stat endurance = mechanics.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value));
        Stat healthMaximum = mechanics.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value));
        Track health = mechanics.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        Track stamina = mechanics.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        int created = healthMaximum.ValueInt;

        // Wounding first proves the two policies: stamina keeps current while its live ceiling
        // moves, while career/level health is permanent and must not move with endurance.
        health.Current = health.Current - 10;
        stamina.Current = stamina.Current - 10;
        double woundedHealth = health.Current;
        double woundedStamina = stamina.Current;
        DaggerfallStatModifiers.AdjustPermanent(mechanics, DaggerfallMechanicsIds.Endurance, 10);
        Assert.Equal(50, endurance.ValueInt);
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(mechanics, Career());
        Assert.Equal(created, healthMaximum.ValueInt);
        Assert.Equal(woundedStamina, stamina.Current);
        Assert.Equal(woundedHealth, health.Current);

        // Save and restore keep the changed base, the maxima and the wounds.
        DaggerfallStatsSave saved = DaggerfallStatsSaveBoundary.Capture(mechanics, new EntityId(DaggerfallActorIdentity.PlayerEntityId));
        DaggerfallRestoredStats restored = DaggerfallStatsSaveBoundary.Restore(saved, new EntityId(DaggerfallActorIdentity.PlayerEntityId));
        Assert.Equal(50, restored.Component.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt);
        Assert.Equal(healthMaximum.ValueInt, restored.Component.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value)).ValueInt);
        Assert.Equal(woundedHealth, restored.Component.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    private static StatsComponent PlayerStats()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        return new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, Career()));
    }

    private static DaggerfallCareerDefinition Career() => LoadDefinitions().Catalogs.RequireCareer("class00");

    private static DaggerfallDefinitions LoadDefinitions()
    {
        string root = RepositoryRoot();
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
