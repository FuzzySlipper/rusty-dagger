using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class HeldStatEnchantmentSessionTests
{
    [Theory]
    [InlineData(10, 29)]
    [InlineData(3, 2)]
    [InlineData(7, 0)]
    [InlineData(7, 1)]
    [InlineData(13, 0)]
    [InlineData(13, 1)]
    [InlineData(13, 2)]
    public void Held_families_rebuild_once_from_saved_equipment_and_break_removes_the_same_source(int type, int param)
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            return EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        }
        var identity = GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        DaggerfallSessionComposition composition = new(definitions, inputs, DaggerfallTuning.Defaults, identity);
        RulesetSavePayload save;
        ulong durableId;
        Values baseline, equipped;
        using (DaggerfallSession session = DaggerfallSession.StartNew(Engine().Context, composition))
        {
            session.State.Character.BeginChoices();
            session.State.Character.ReplacePending(session.State.Character.ReadCreation().Current with
            { CareerId = "class16", CustomCareer = null, Background = null });
            session.State.Character.CommitChoices();
            baseline = Read(session);
            DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId("template-121-daedric"));
            var itemIdentity = session.UniqueItemAllocator.AllocateReference();
            durableId = itemIdentity.Value;
            var item = session.State.Equipment.Materialize(itemIdentity, new InventoryItemId(definition.Id.Value));
            session.State.ItemInstances.RegisterDefaultUnique(durableId, definition, DaggerfallItemOwner.Player);
            Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, session.ItemCondition.Enchant(item, $"enchantment.{type}.{param}").Outcome);
            var moved = session.EquipmentMoves.MoveToSlot(item, new EquipmentSlotId("right-hand"));
            Assert.True(moved.Outcome == EquipmentMoveOutcome.Applied, moved.Detail);
            equipped = Read(session);
            Assert.Equal(baseline.Skill + (type == 10 ? 15 : 0), equipped.Skill);
            Assert.Equal(baseline.Magicka + (type == 3 ? 75 : 0), equipped.Magicka);
            Assert.Equal(type == 7 ? (param == 0 ? 1.25d : 1.5d) : 1d, equipped.Carry);
            Assert.Equal(checked((long)(baseline.CarryUnits * equipped.Carry)), equipped.CarryUnits);
            Assert.Equal(type == 13 && param == 0, equipped.Talents.AcuteHearing);
            Assert.Equal(type == 13 && param == 1, equipped.Talents.Athleticism);
            Assert.Equal(type == 13 && param == 2, equipped.Talents.AdrenalineRush);
            save = session.CaptureSave();
            DaggerfallSavePayload raw = DaggerfallSavePayload.Read(save);
            DaggerfallSavePayload brokenEquipment = raw with { Inventory = raw.Inventory with
            { UniqueItems = [.. raw.Inventory.UniqueItems.Select(value => value.EntityId == durableId
                ? value with { Metadata = value.Metadata with { CurrentCondition = 0 } } : value)] } };
            Assert.Contains("broken unique item", Assert.Throws<ArgumentException>(() => brokenEquipment.ResolveRestore(definitions, inputs)).Message);
            if (type is 10 or 3)
            {
                DaggerfallSavePayload withoutEquipment = raw with { Inventory = raw.Inventory with { Equipment = [] } };
                Assert.Throws<ArgumentException>(() => withoutEquipment.ResolveRestore(definitions, inputs));
                DaggerfallSavePayload wrongTarget = raw with { Player = raw.Player with { Stats = raw.Player.Stats with
                { Sources = [.. raw.Player.Stats.Sources.Select(value => value with { SourceStatId = "medical" })] } } };
                Assert.Throws<ArgumentException>(() => wrongTarget.ResolveRestore(definitions, inputs));
            }
            if (type == 3)
            {
                // The conditional value is rebuilt from the resumed world, never retained
                // merely because the item still owns the saved source identity.
                using DaggerfallSession changedSeason = DaggerfallSession.Restore(Engine().Context, composition,
                    DaggerfallSavePayload.Encode(raw with { Calendar = raw.Calendar with { Month = 11 }, Weather = raw.Weather with {NextDay = new World.DaggerfallCalendar(raw.Calendar.Year,11,raw.Calendar.Day,raw.Calendar.Hour,raw.Calendar.Minute,raw.Calendar.Second).DayNumber + 1} }));
                Assert.Equal(baseline, Read(changedSeason));
            }
        }
        using DaggerfallSession restored = DaggerfallSession.Restore(Engine().Context, composition, save);
        Assert.Equal(equipped, Read(restored));
        restored.State.HeldEnchantments.Refresh();
        Assert.Equal(equipped, Read(restored));
        var worn = restored.State.Equipment.Read().Assignments.First(assignment =>
            restored.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durableId).Item;
        Assert.Equal(DaggerfallItemConditionOutcome.Broken,
            restored.ItemCondition.Damage(worn, restored.State.ItemInstances.RequireUnique(durableId).CurrentCondition).Outcome);
        Assert.Equal(baseline, Read(restored));
        using DaggerfallSession broken = DaggerfallSession.Restore(Engine().Context, composition, restored.CaptureSave());
        Assert.Equal(baseline, Read(broken));
        Assert.DoesNotContain(broken.State.Equipment.Read().Assignments, assignment =>
            broken.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durableId);
    }

    private static Values Read(DaggerfallSession session) => new(
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt,
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).ValueInt,
        session.State.Encumbrance.Read().MaximumClassicUnits,
        session.State.HeldEnchantments.CarryMultiplier,
        session.State.HeldEnchantments.Talents);

    private sealed record Values(int Skill, int Magicka, long CarryUnits, double Carry, DaggerfallHeldTalents Talents);
}
