using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRaceExposureTests
{
    [Fact]
    public void Published_catalog_preserves_each_donor_races_flags()
    {
        var races = TestPayload.Definitions.Catalogs.Races;
        Assert.Equal(8, races.Count);
        Assert.Equal(2, races.Single(race => race.Id == "breton").ResistanceFlags);
        Assert.Equal(16, races.Single(race => race.Id == "nord").ResistanceFlags);
        Assert.Equal(1, races.Single(race => race.Id == "high-elf").ImmunityFlags);
        Assert.All(races.Where(race => race.Id != "high-elf"), race => Assert.Equal(0, race.ImmunityFlags));
        Assert.All(races, race => { Assert.Equal(0, race.LowToleranceFlags); Assert.Equal(0, race.CriticalWeaknessFlags); });
    }

    [Fact]
    public void Race_flags_use_the_same_precedence_as_the_donor_raw_tolerances()
    {
        var race = TestPayload.Definitions.Catalogs.RequireRace("breton") with
            { ResistanceFlags = 4, ImmunityFlags = 4, LowToleranceFlags = 4, CriticalWeaknessFlags = 4 };
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Resistant, race.Tolerance(4));
        Assert.Equal(DaggerfallMagicEffectFlags.Poison, race.MagicTolerances.Resistance);
        Assert.Equal(DaggerfallMagicEffectFlags.None, race.MagicTolerances.Immunity);
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Immune, (race with { ResistanceFlags = 0 }).Tolerance(4));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.LowTolerance, (race with { ResistanceFlags = 0, ImmunityFlags = 0 }).Tolerance(4));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.CriticalWeakness, (race with { ResistanceFlags = 0, ImmunityFlags = 0, LowToleranceFlags = 0 }).Tolerance(4));
    }

    [Fact]
    public void Actual_session_poison_exposure_reads_its_normalized_race_instead_of_false()
    {
        using var session = SessionWithPlayerRaceImmunity(4);
        var exposure = session.PlayerPoisonExposure(bypassResistance: true);
        Assert.True(exposure.RaceImmune);
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Immune, exposure.RaceTolerance);
        Assert.Equal(DaggerfallPoisonAdmission.Immune, session.InflictPoison(exposure with { RaceImmune = false }, 128));
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Accepted_weapon_delivery_reads_player_race_and_spends_coating_even_when_immune()
    {
        using var session = SessionWithPlayerRaceImmunity(4);
        var weapon = session.State.Inventory.Read().UniqueItems.First(item => TestPayload.Definitions.RequireItem(new DaggerfallItemId(item.Definition.Value)).Weapon is not null);
        ulong identity = session.State.Inventory.GetDurableItemId(weapon.Entity).Value;
        var metadata = session.State.ItemInstances.RequireUnique(identity);
        session.State.ItemInstances.ReplaceUnique(identity, metadata with { PoisonVariant = 128 });
        session.DeliverWeaponPoison(session.State.Actors.Player.DurableId, identity);
        Assert.Null(session.State.ItemInstances.RequireUnique(identity).PoisonVariant);
        Assert.Empty(session.State.Effects.Active);
        using var restored = SessionWithPlayerRaceImmunity(4, session.CaptureSave());
        Assert.Equal(session.State.Character.Identity.RaceId, restored.State.Character.Identity.RaceId);
        Assert.True(restored.PlayerPoisonExposure(false).RaceImmune);
    }

    [Fact]
    public void Actual_session_disease_exposure_reads_its_normalized_race_before_infection()
    {
        using var session = SessionWithPlayerRaceImmunity(64);
        session.State.Progression.AdvanceTo(session.State.Progression.Experience, 2);
        var exposure = new DaggerfallDiseaseExposure("race-disease", "race-test", null, session.State.Actors.Player.DurableId, [DaggerfallClassicDisease.BrainFever]);
        Assert.Equal(DaggerfallDiseaseAdmission.Immune, session.InflictDisease(exposure));
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Magic_admission_consumes_the_published_high_elf_race_and_refuses_paralysis_without_a_draw()
    {
        var target = new DaggerfallMagicTargetProfile(50,
            new(DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal,
                DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal),
            TestPayload.Definitions.Catalogs.RequireRace("high-elf"), 0, 0, 0, new(0, 0, 0, 0, 0), []);
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.SavingThrow(DaggerfallMagicResistanceElement.Magic, DaggerfallMagicEffectFlags.Paralysis,
            target, 0, () => throw new Xunit.Sdk.XunitException("Published racial immunity must not roll.")));
    }

    [Theory]
    [InlineData(0, 55)]
    [InlineData(1, 85)]
    [InlineData(3, 30)]
    [InlineData(4, 0)]
    public void Race_tolerance_uses_the_donor_modifier_without_becoming_a_biography_assumption(int tolerance, int expected) =>
        Assert.Equal(expected, DaggerfallMagicAdmissionPolicy.DiseaseOrPoisonSavingThrowChance(50, raceTolerance: (DaggerfallDiseaseCareerTolerance)tolerance));

    private static DaggerfallSession SessionWithPlayerRaceImmunity(int flag, RulesetSavePayload? save = null)
    {
        // The shipped races have no poison/disease immunity. A normalized catalogue variant tests
        // that the real runtime owner respects such data instead of keeping the previous false literal.
        JsonObject payload = TestPayload.Sections("catalogs");
        foreach (JsonNode? race in payload["catalogs"]!["races"]!.AsArray())
            race!["immunityFlags"] = flag;
        var definitions = DaggerfallBaseContent.Read(TestPayload.Splice(payload.AsObject()));
        var profile = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, profile);
        var engine = EngineContextFake.Create(content, SpatialFake.Create(profile.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        var composition = new DaggerfallSessionComposition(definitions, profile, DaggerfallTuning.Defaults);
        return save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
    }
}
