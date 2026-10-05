using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class LycanthropySessionTests
{
    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void Morph_cast_unequips_complete_hands_once_restores_form_and_cure_reopens_equipment(int variant, bool twoHanded)
    {
        using var s = FreshSession();
        foreach (var held in s.State.Equipment.Read().Assignments.Select(x => x.Item).Distinct().ToArray()) s.State.Equipment.Unequip(held);
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create());
        var right = Make(twoHanded ? 115 : 113, "lycan.right");
        Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToSlot(right, new("right-hand")).Outcome);
        if (!twoHanded)
            Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToSlot(Make(113, "lycan.left"), new("left-hand")).Outcome);
        int expectedRemoved = twoHanded ? 1 : 2;
        Assert.True(s.State.RacialOverrides.Select((DaggerfallRacialKind)variant, "curse", 0));
        List<DaggerfallEquipmentChange> changes = [];
        s.EquipmentMoves.Changed += changes.Add;
        Cast(s);
        Assert.True(s.State.RacialOverrides.Current!.State.BeastForm);
        Assert.Empty(s.State.Equipment.Read().Assignments.Where(x => x.Slot.Value is "right-hand" or "left-hand"));
        Assert.Equal(expectedRemoved, Assert.Single(changes).Removed.Count);
        Assert.Equal(EquipmentMoveOutcome.Rejected, s.EquipmentMoves.MoveToSlot(right, new("right-hand")).Outcome);
        var source = s.State.RacialOverrides.Current;
        using var restored = Restore(s.CaptureSave());
        Assert.Equal(source, restored.State.RacialOverrides.Current);
        Assert.Empty(restored.State.Equipment.Read().Assignments.Where(x => x.Slot.Value is "right-hand" or "left-hand"));
        Assert.True(restored.CureLycanthropy());
        Assert.Null(restored.State.RacialOverrides.Current);
        Assert.DoesNotContain("spell.085", restored.State.Character.KnownSpells);
        var item = restored.State.Inventory.Read().UniqueItems.First(x => x.Definition.Value == right.Definition.Value);
        Assert.Equal(EquipmentMoveOutcome.Applied, restored.EquipmentMoves.MoveToSlot(new(item.Entity.Value, new(item.Definition.Value)), new("right-hand")).Outcome);
        Assert.Single(changes);

        UniqueInventoryItem Make(int template, string key)
        {
            var identity = s.UniqueItemAllocator.AllocateReference();
            var created = factory.Create(new("Weapons", key, DaggerfallItemOwner.Player, TemplateIndex: template, Material: "iron"));
            factory.Materialize(created, s.State.Inventory, s.State.ItemInstances, unique: identity);
            return new(s.State.Actors.Entities.Resolve(identity).Value, created.Item);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void Bonuses_cooldown_hunger_and_cure_use_current_stats_and_elapsed_calendar(int variant)
    {
        using var s = FreshSession();
        var stats = s.State.Actors.Player.Stats;
        double strength = stats.GetStat(StatId.Parse("strength")).Value;
        double swimming = stats.GetStat(StatId.Parse("swimming")).Value;
        s.State.RacialOverrides.Select((DaggerfallRacialKind)variant, "curse", 0);
        Assert.Equal(strength + 40, stats.GetStat(StatId.Parse("strength")).Value);
        Assert.Equal(swimming + 30, stats.GetStat(StatId.Parse("swimming")).Value);
        Cast(s); Cast(s);
        Assert.False(s.State.RacialOverrides.Current!.State.BeastForm);
        Cast(s);
        Assert.False(s.State.RacialOverrides.Current!.State.BeastForm);
        s.AdvanceElapsedTime(1441 * 60);
        if (!s.State.RacialOverrides.Current!.State.BeastForm) Cast(s);
        Assert.True(s.State.RacialOverrides.Current!.State.BeastForm);
        long minute = Minute(s);
        s.State.RacialOverrides.Satiate(minute);
        double rawHealth = stats.GetTrack(TrackId.Parse("health")).Maximum.Value;
        s.AdvanceElapsedTime(31 * 86400);
        Assert.True(stats.GetTrack(TrackId.Parse("health")).Maximum.Value < rawHealth);
        s.State.RacialOverrides.Satiate(Minute(s));
        s.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(rawHealth, stats.GetTrack(TrackId.Parse("health")).Maximum.Value);
        Assert.True(s.CureLycanthropy());
        Assert.Equal(strength, stats.GetStat(StatId.Parse("strength")).Value);
        Assert.Equal(swimming, stats.GetStat(StatId.Parse("swimming")).Value);
    }

    [Fact]
    public void Morph_without_curse_has_no_success_or_retained_effect()
    {
        using var s = FreshSession(); s.State.Character.LearnSpell("spell.085");
        Assert.Equal(DaggerfallCastOutcome.UnknownSpell, s.ReadyPlayerSpell("spell.085").Outcome);
        Assert.Null(s.State.RacialOverrides.Current);
        Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Compiled_quest_cure_uses_the_session_owner_once_and_advances_shared_time()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(TestPayload.CombinedText)!;
        root["questSources"]!["quests"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
            {"name":"curefixture","displayName":"Cure","sourceFile":"curefixture.txt","disposition":"compiled","messages":[],"blocks":[{"kind":"headless","firstLine":1,"lines":["cure lycanthropy"],"global":null}],"diagnostics":[]}
            """));
        var definitions = DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        using var s = Restore(null, definitions);
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0); Cast(s);
        long before = Minute(s);
        s.State.Quests.Start(new("curefixture", "curefixture.txt", "curefixture", DaggerfallQuestLifecycle.Active, null, [], []));
        s.State.Quests.Advance(s.State.Variables, World.DaggerfallCalendar.Start);
        Assert.Null(s.State.RacialOverrides.Current);
        Assert.Equal(before + 1, Minute(s));
        s.State.Quests.Advance(s.State.Variables, World.DaggerfallCalendar.Start);
        Assert.Equal(before + 1, Minute(s));
        Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Hircines_ring_bypasses_hunger_cooldown_and_forced_full_moon()
    {
        using var s = FreshSession();
        var artifact = TestPayload.Definitions.Magic.MagicItems.Values.Single(x => x.Type != 0 && x.Enchantments.Any(e => e.Type == 26 && e.Param == 3));
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create());
        var identity = s.UniqueItemAllocator.AllocateReference();
        var created = factory.Create(new("Jewellery", "hircine", DaggerfallItemOwner.Player, TemplateIndex: 135));
        factory.Materialize(created, s.State.Inventory, s.State.ItemInstances, unique: identity);
        var metadata = s.State.ItemInstances.RequireUnique(identity.Value);
        s.State.ItemInstances.ReplaceUnique(identity.Value, metadata with { Enchantment = artifact.Key });
        var item = new UniqueInventoryItem(s.State.Actors.Entities.Resolve(identity).Value, created.Item);
        Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToSlot(item, new("ring0")).Outcome);
        Assert.True(s.State.HeldEnchantments.HircinesRingEquipped);
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "curse", Minute(s));
        Cast(s); Cast(s); Cast(s); Cast(s);
        Assert.False(s.State.RacialOverrides.Current!.State.BeastForm);
        double health = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Maximum.Value;
        s.AdvanceElapsedTime(40 * 86400);
        Assert.False(s.State.RacialOverrides.Current!.State.BeastForm);
        Assert.Equal(health, s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Maximum.Value);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void Presentation_selects_published_beast_media_and_cure_restores_birth_identity(int variant)
    {
        using var s = FreshSession();
        var original = Presentation.CharacterIdentityPresentation.From(TestPayload.Definitions, s.State.Character.Identity, null);
        s.State.RacialOverrides.Select((DaggerfallRacialKind)variant, "curse", 0); Cast(s);
        var transformed = Presentation.CharacterIdentityPresentation.From(TestPayload.Definitions, s.State.Character.Identity, s.State.RacialOverrides.Current);
        Assert.Contains(transformed.SelectedMedia!, x => x.Layer == "background");
        Assert.NotEqual(original.SelectedMedia, transformed.SelectedMedia);
        s.CureLycanthropy();
        var cured = Presentation.CharacterIdentityPresentation.From(TestPayload.Definitions, s.State.Character.Identity, null);
        Assert.Equal(original.SelectedMedia, cured.SelectedMedia);
    }

    [Fact]
    public void Protected_morph_readiness_cost_restores_and_cure_cancels_it()
    {
        using var s = FreshSession();
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0);
        var mana = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        mana.SetCurrent(mana.Maximum.Value);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("spell.085").Outcome);
        Assert.Equal(5, s.Casting.ReadyFor(1)!.Cost);
        using var restored = Restore(s.CaptureSave());
        Assert.Equal(5, restored.Casting.ReadyFor(1)!.Cost);
        var bundle = Assert.IsType<DaggerfallLiveSpell>(restored.Casting.Release(1, true).Bundle);
        Assert.Equal(DaggerfallCastOutcome.InvalidTarget, restored.Casting.Deliver(bundle, [2000]).Outcome);
        Assert.False(restored.State.RacialOverrides.Current!.State.BeastForm);
        Assert.Equal(DaggerfallCastOutcome.Ready, restored.ReadyPlayerSpell("spell.085").Outcome);
        Assert.True(restored.CureLycanthropy());
        Assert.Null(restored.Casting.ReadyFor(1));
    }

    [Fact]
    public void Cure_quest_without_curse_keeps_valid_active_save_and_reports_refusal()
    {
        using var s = FreshSession();
        Assert.False(s.CureLycanthropy(fromQuest: true));
        Assert.Contains("no active curse", s.Presentation.LastOutcome);
        using var restored = Restore(s.CaptureSave());
        Assert.Null(restored.State.RacialOverrides.Current);
    }

    [Fact]
    public void Elapsed_interval_crossing_a_full_moon_retains_first_forced_transition_after_the_moon_passes()
    {
        using var s = FreshSession();
        s.State.RacialOverrides.Select(DaggerfallRacialKind.Wereboar, "curse", Minute(s));
        int first = Enumerable.Range(1, 32).First(day => World.DaggerfallCalendar.Start.Advance(day * 86400L, out _).IsFullMoon);
        long start = Minute(s);
        s.AdvanceElapsedTime((first + 1) * 86400L);
        Assert.True(s.State.RacialOverrides.Current!.State.BeastForm);
        Assert.Equal(start + first * 1440L, s.State.RacialOverrides.Current.State.LastMorphMinute);
        using var restored = Restore(s.CaptureSave());
        Assert.Equal(s.State.RacialOverrides.Current, restored.State.RacialOverrides.Current);
    }

    private static long Minute(DaggerfallSession s)
    {
        var c = DaggerfallSavePayload.Read(s.CaptureSave()).Calendar;
        return new World.DaggerfallCalendar(c.Year, c.Month, c.Day, c.Hour, c.Minute, c.Second).ToAbsoluteSeconds() / 60;
    }

    private static void Cast(DaggerfallSession s)
    {
        var mana = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        mana.Maximum.BaseValue = 10000; mana.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("spell.085").Outcome);
        var bundle = Assert.IsType<DaggerfallLiveSpell>(s.Casting.Release(1, true).Bundle);
        s.Casting.Deliver(bundle, [DaggerfallActorIdentity.PlayerEntityId]);
    }

    private static DaggerfallSession Restore(RulesetSavePayload? save, DaggerfallDefinitions? definitions = null)
    {
        var inputs = ReadInputs(TestData.RepositoryRoot); List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, inputs);
        var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        var composition = new DaggerfallSessionComposition(definitions ?? TestPayload.Definitions, inputs, DaggerfallTuning.Defaults);
        return save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
    }
}
