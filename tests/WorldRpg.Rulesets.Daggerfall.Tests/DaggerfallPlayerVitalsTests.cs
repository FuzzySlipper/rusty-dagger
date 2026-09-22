using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallPlayerVitalsTests
{
    [Fact]
    public void Predefined_careers_supply_distinct_permanent_health_and_spell_point_multipliers()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition mage = definitions.Catalogs.RequireCareer("class00");
        DaggerfallCareerDefinition spellsword = definitions.Catalogs.RequireCareer("class01");

        DaggerfallVitalValues mageVitals = DaggerfallPlayerVitals.Initial(player.Stats, mage);
        DaggerfallVitalValues spellswordVitals = DaggerfallPlayerVitals.Initial(player.Stats, spellsword);

        Assert.Equal(31, mageVitals.HealthMaximum);
        Assert.Equal(37, spellswordVitals.HealthMaximum);
        Assert.Equal(5_760, mageVitals.StaminaMaximum);
        Assert.Equal(100, mageVitals.MagickaMaximum);
        Assert.Equal(75, spellswordVitals.MagickaMaximum);
        Assert.Equal(2_000, mage.SpellPointMultiplierMilli);
        Assert.Equal(1_500, spellsword.SpellPointMultiplierMilli);
    }

    [Fact]
    public void Temporary_live_attribute_changes_refresh_fatigue_and_magicka_without_resetting_permanent_health()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        Track health = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        health.TrySpend(5);

        StatModifierHandle endurance = DaggerfallStatModifiers.ApplyMod(stats, DaggerfallMechanicsIds.Endurance, -10);
        StatModifierHandle intelligence = DaggerfallStatModifiers.ApplyMod(stats, DaggerfallMechanicsIds.Intelligence, -10);
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(stats, career);

        Assert.Equal(31, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value)).ValueInt);
        Assert.Equal(26, health.ValueInt);
        Assert.Equal(5_120, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).ValueInt);
        Assert.Equal(80, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).ValueInt);

        Assert.True(DaggerfallStatModifiers.RemoveMod(stats, DaggerfallMechanicsIds.Endurance, endurance));
        Assert.True(DaggerfallStatModifiers.RemoveMod(stats, DaggerfallMechanicsIds.Intelligence, intelligence));
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(stats, career);

        Assert.Equal(31, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value)).ValueInt);
        Assert.Equal(26, health.ValueInt);
        Assert.Equal(5_760, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).ValueInt);
        Assert.Equal(100, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).ValueInt);
    }

    [Fact]
    public void Permanent_level_health_source_survives_temporary_modifiers_and_current_state_restore_without_rerolling()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class01");
        EntityId entity = new(DaggerfallActorIdentity.PlayerEntityId);
        StatsComponent original = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        Stat maximum = original.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value));
        maximum.SetSources(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value),
            [DaggerfallLevelUpHealthSource.Create(entity, 2, 7)]);
        Track health = original.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        health.TrySpend(10);

        StatModifierHandle temporaryEndurance = DaggerfallStatModifiers.ApplyMod(original, DaggerfallMechanicsIds.Endurance, -15);
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(original, career);
        Assert.Equal(44, maximum.ValueInt);
        Assert.Equal(34, health.ValueInt);

        DaggerfallStatsSave saved = DaggerfallStatsSaveBoundary.Capture(original, entity);
        DaggerfallRestoredStats restored = DaggerfallStatsSaveBoundary.Restore(saved, entity);
        DaggerfallPlayerVitals.Refresh(restored.Component, career);

        Stat restoredMaximum = restored.Component.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value));
        Assert.Equal(44, restoredMaximum.ValueInt);
        Assert.Equal(34, restored.Component.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).ValueInt);
        Assert.Single(restoredMaximum.Sources);
        Assert.Equal("daggerfall.player.level-up.2.health", ((IntrinsicSourceIdentity)restoredMaximum.Sources[0].Identity).Instance.Value);

        StatModifierHandle restoredTemporary = Assert.Single(restored.ModifierHandles[DaggerfallMechanicsIds.Endurance.Value]);
        Assert.True(DaggerfallStatModifiers.RemoveMod(restored.Component, DaggerfallMechanicsIds.Endurance, restoredTemporary));
        DaggerfallPlayerVitals.Refresh(restored.Component, career);
        Assert.Equal(44, restoredMaximum.ValueInt);
        Assert.Equal(34, restored.Component.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).ValueInt);
    }

    private static DaggerfallDefinitions Definitions() =>
        DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        }

        throw new InvalidOperationException("repository root not found");
    }
}
