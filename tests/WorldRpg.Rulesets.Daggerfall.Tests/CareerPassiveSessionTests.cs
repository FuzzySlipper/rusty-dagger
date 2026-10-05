using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CareerPassiveSessionTests
{
    [Theory]
    [InlineData("general", 1)]
    [InlineData("darkness", 1)]
    [InlineData("light", 0)]
    [InlineData("immersed", 0)]
    public void Regeneration_uses_shared_round_cadence_and_dungeon_environment_across_save(string condition, int amount)
    {
        using var session = Restore(null);
        SetCareer(session, [new("regenerate-health", condition)]);
        var health = Track(session, "health"); health.SetCurrent(10);
        session.AdvanceElapsedTime(60);
        Assert.Equal(10 + amount, health.Current);
        using var restored = Restore(session.CaptureSave());
        restored.AdvanceElapsedTime(3 * 60);
        Assert.Equal(health.Current, Track(restored, "health").Current);
        restored.AdvanceElapsedTime(60);
        Assert.Equal(10 + 2 * amount, Track(restored, "health").Current);
    }

    [Theory]
    [InlineData("general", true)]
    [InlineData("darkness", true)]
    [InlineData("light", false)]
    public void Career_absorption_reaches_actual_incoming_cast_and_refund_after_restore(string condition, bool absorbs)
    {
        using var original = Restore(null); SetCareer(original, [new("spell-absorption", condition)]);
        foreach (var session in new[] { original, Restore(original.CaptureSave()) })
        {
            var magicka = Track(session, "magicka"); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(0);
            var caster = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("magicka"));
            caster.Maximum.BaseValue = 10000; caster.SetCurrent(10000);
            Assert.Equal(DaggerfallCastOutcome.Ready, session.Casting.Ready(2000, "spell.009").Outcome);
            var spell = session.Casting.Release(2000, true).Bundle!;
            session.Casting.Deliver(spell, [1]);
            Assert.Equal(absorbs, Assert.Single(spell.Results).Outcome == DaggerfallCastOutcome.Absorbed);
            Assert.Equal(absorbs, magicka.Current > 0);
            if (absorbs) Assert.Empty(Assert.Single(spell.Absorptions).SourceItems);
            if (!ReferenceEquals(session, original)) session.Dispose();
        }
    }

    [Theory]
    [InlineData("light-powered-magery", "reduced", 67)]
    [InlineData("light-powered-magery", "unable", 0)]
    [InlineData("darkness-powered-magery", "reduced", 100)]
    [InlineData("darkness-powered-magery", "unable", 100)]
    public void Magery_changes_canonical_maximum_and_removes_only_its_source(string trait, string variant, int maximum)
    {
        using var session = Restore(null);
        SetCareer(session, [], [new(trait, variant)]);
        Track(session, "magicka").Maximum.BaseValue = 100;
        session.AdvanceElapsedTime(60);
        Assert.Equal(maximum, Track(session, "magicka").Maximum.Value);
        using var restored = Restore(session.CaptureSave());
        Assert.Equal(maximum, Track(restored, "magicka").Maximum.Value);
        SetCareer(restored, []);
        restored.AdvanceElapsedTime(60);
        Assert.DoesNotContain(Track(restored, "magicka").Maximum.Sources,
            source => source.Identity is IntrinsicSourceIdentity { Instance.Value: "daggerfall.passive-magery" });
    }

    [Fact]
    public void Hearing_uses_live_career_and_worn_talent_and_rebuilds_after_save()
    {
        using var session = Restore(null);
        Assert.Equal(16, session.EnemyAudibleRange);
        SetCareer(session, [new("acute-hearing")]);
        Assert.Equal(20, session.EnemyAudibleRange);
        var definition = TestPayload.Definitions.RequireItem(new DaggerfallItemId("template-121-daedric"));
        var identity = session.UniqueItemAllocator.AllocateReference();
        var item = session.State.Equipment.Materialize(identity, new WorldRpg.Kit.Inventory.InventoryItemId(definition.Id.Value));
        session.State.ItemInstances.RegisterDefaultUnique(identity.Value, definition, DaggerfallItemOwner.Player);
        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, session.ItemCondition.Enchant(item, "enchantment.13.0").Outcome);
        session.EquipmentMoves.MoveToSlot(item, new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand"));
        Assert.Equal(24, session.EnemyAudibleRange);
        using var restored = Restore(session.CaptureSave());
        Assert.Equal(24, restored.EnemyAudibleRange);
        foreach (var assignment in restored.State.Equipment.Read().Assignments.ToArray()) restored.State.Equipment.Unequip(assignment.Item);
        restored.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(20, restored.EnemyAudibleRange);
        SetCareer(restored, []); Assert.Equal(16, restored.EnemyAudibleRange);
    }

    [Fact]
    public void Immersed_regeneration_reads_accepted_swimming_and_stops_after_surfacing()
    {
        using var session = Restore(null); SetCareer(session, [new("regenerate-health", "immersed")]);
        var health = Track(session, "health"); health.SetCurrent(10);
        var receipt = default(CharacterStepReceipt) with
        { Movement = new(CharacterMovementMode.Swimming, 1F, true, false, false, false) };
        session.State.Swimming.Complete(receipt, false, 100, 1, 1, 0, _ => { });
        session.AdvanceElapsedTime(60); Assert.Equal(11, health.Current);
        receipt = receipt with { Movement = new(CharacterMovementMode.Walking, 0F, false, false, false, false) };
        session.State.Swimming.Complete(receipt, false, 100, 1, 1, 1, _ => { });
        session.AdvanceElapsedTime(240); Assert.Equal(11, health.Current);
    }

    [Theory]
    [InlineData("general", true)]
    [InlineData("darkness", true)]
    [InlineData("light", false)]
    public void Rest_uses_conditional_rapid_healing_and_preserves_magicka_with_no_regeneration(string condition, bool rapid)
    {
        using var original = Restore(null); SetCareer(original, [new("rapid-healing", condition)], [new("inability-to-regen")]);
        foreach (var session in new[] { original, Restore(original.CaptureSave()) })
        {
            session.State.PlayerControl.MoveTo(new Vector3(1000, 1, 1000));
            var health = Track(session, "health"); health.SetCurrent(1);
            var magicka = Track(session, "magicka"); magicka.SetCurrent(1);
            var stats = session.State.Actors.Player.Stats;
            int expected = DaggerfallFormulaPolicy.HealthRecoveryRate((int)stats.GetStat(StatId.Parse("endurance")).Value,
                (int)stats.GetStat(StatId.Parse("medical")).Value, (int)health.Maximum.Value, rapid);
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
            Assert.Equal(3600, session.RestView.ElapsedSeconds);
            Assert.Equal(Math.Min(health.Maximum.Value, 1 + expected), health.Current);
            Assert.Equal(1, magicka.Current);
            if (!ReferenceEquals(session, original)) session.Dispose();
        }
    }

    [Fact]
    public void Last_selected_single_field_variants_win_without_combining_conditions()
    {
        using var session = Restore(null);
        SetCareer(session, [new("regenerate-health", "general"), new("regenerate-health", "light"),
            new("spell-absorption", "general"), new("spell-absorption", "light")], [new("damage", "holy-places")]);
        var health = Track(session, "health"); health.SetCurrent(10);
        session.AdvanceElapsedTime(60);
        Assert.Equal(10, health.Current); // Dungeon in daytime is dark for regeneration and absorption.
        Assert.Equal(0, session.State.Effects.MagicDefenseFor(1).AbsorptionChance);
        using var restored = Restore(session.CaptureSave());
        Assert.Equal(0, restored.State.Effects.MagicDefenseFor(1).AbsorptionChance);
        restored.AdvanceElapsedTime(240);
        Assert.Equal(10, Track(restored, "health").Current);
    }

    [Theory]
    [InlineData(14, 0, true)]
    [InlineData(18, 0, false)]
    public void Holy_damage_uses_admitted_building_and_survives_save(int buildingType, int faction, bool holy)
    {
        var source = ReadInputs(TestData.RepositoryRoot);
        var selected = TestPayload.Definitions.Locations.Records.Where(site => site.Exterior is not null)
            .SelectMany(site => site.Exterior!.Buildings.Values.Select(building => (Site: site.Id, Building: building)))
            .First(value => value.Building.Source.BuildingType == buildingType
                && (buildingType == 14 || value.Building.Source.FactionId == faction));
        var building = selected.Building;
        var profile = new DaggerfallSiteProfile(new ProjectFacts(new WorldPoint(1, 1, 1), new Dictionary<long, AuthoredActor>()),
            source.SpatialArtifact, source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials,
            new Dictionary<long, NormalizedActorSprite>(), source.MobileSprites, source.Audio, source.ClassicPresentation, selected.Site,
            profileKind: DaggerfallWorldProfileKind.Interior, logicalProfileId: "career-temple",
            interiorBuilding: new(building.Id.BlockX, building.Id.BlockY, building.Source.Id, building.Source.BuildingType, building.Source.FactionId));
        using var original = Restore(null, profile); SetCareer(original, [], [new("damage", "holy-places")]);
        foreach (var session in new[] { original, Restore(original.CaptureSave(), profile) })
        {
            var health = Track(session, "health"); health.SetCurrent(health.Maximum.Value);
            double before = health.Current; session.AdvanceElapsedTime(60);
            Assert.Equal(before - (holy ? 12 : 0), health.Current);
            if (!ReferenceEquals(session, original)) session.Dispose();
        }
    }

    [Theory]
    [InlineData(12, "light", true)]
    [InlineData(0, "light", false)]
    [InlineData(12, "darkness", false)]
    [InlineData(0, "darkness", true)]
    public void Outdoor_day_and_night_reach_real_passive_health_magicka_and_absorption_consumers(int hour, string condition, bool active)
    {
        var source = ReadInputs(TestData.RepositoryRoot);
        var profile = new DaggerfallSiteProfile(new ProjectFacts(new WorldPoint(1, 1, 1), new Dictionary<long, AuthoredActor>()),
            source.SpatialArtifact, source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials,
            new Dictionary<long, NormalizedActorSprite>(), source.MobileSprites, source.Audio, source.ClassicPresentation, source.Site,
            profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "career-outdoors",
            billboardSprites: source.BillboardSprites, terrainTextures: source.TerrainTextures);
        using var seed = Restore(null, profile);
        SetCareer(seed, [new("regenerate-health", condition), new("spell-absorption", condition)],
            [new("damage", "sunlight"), new("darkness-powered-magery", "reduced")]);
        var saved = DaggerfallSavePayload.Read(seed.CaptureSave());
        saved = saved with { Calendar = saved.Calendar with { Hour = hour, Minute = 0, Second = 0 } };
        using var session = Restore(DaggerfallSavePayload.Encode(saved), profile);
        var health = Track(session, "health"); health.Maximum.BaseValue = 100; health.SetCurrent(50);
        double before = health.Current;
        var magicka = Track(session, "magicka"); magicka.Maximum.BaseValue = 100;
        session.AdvanceElapsedTime(60);
        Assert.Equal(before + (active ? 1 : 0) - (hour == 12 ? 12 : 0), health.Current);
        Assert.Equal(hour == 12 ? 67 : 100, magicka.Maximum.Value);
        Assert.Equal(active ? 100 : 0, session.State.Effects.MagicDefenseFor(1).AbsorptionChance);
    }

    internal static void SetCareer(DaggerfallSession session, DaggerfallCustomCareerTrait[] advantages,
        DaggerfallCustomCareerTrait[]? disadvantages = null)
    {
        var character = session.State.Character;
        var choices = DaggerfallCustomCareerChoices.Default(TestPayload.Definitions, character.Career) with
        { Name = "Test career", HitPointsPerLevel = 12, Advantages = advantages, Disadvantages = disadvantages ?? [] };
        character.BeginChoices();
        character.ReplacePending(new("Passive tester", "breton", DaggerfallCharacterGender.Male, 0,
            DaggerfallCharacterReflexes.Average, "custom", choices));
        character.CommitChoices();
    }
    private static Track Track(DaggerfallSession session, string id) => session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(id));
    private static DaggerfallSession Restore(RulesetSavePayload? save, DaggerfallSiteProfile? profile = null)
    {
        var inputs = profile ?? ReadInputs(TestData.RepositoryRoot);
        List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, inputs);
        foreach (var texture in inputs.TerrainTextures.Values) content.Add(texture.TexturePath, texture.TextureSha256);
        var context = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service,
            new AppearanceFake(releases), random: RandomMaximum.Create()).Context;
        DaggerfallSessionComposition composition = new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults)
        { Sky = profile?.ProfileKey.Kind == DaggerfallWorldProfileKind.Exterior ? DaggerfallSkyMedia.Read(FullContent(TestData.RepositoryRoot)) : null };
        return save is null ? DaggerfallSession.StartNew(context, composition) : DaggerfallSession.Restore(context, composition, save);
    }
}
