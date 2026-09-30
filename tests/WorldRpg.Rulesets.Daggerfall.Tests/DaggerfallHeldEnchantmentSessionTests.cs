using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class NormalizedRuntimeSeamTests
{
    [Fact]
    public void Holy_place_uses_the_admitted_interior_building_not_the_location_kind()
    {
        static DaggerfallInteriorBuilding Building(int type, int faction) =>
            new(0, 0, new DaggerfallRmbBuildingId("TEST.RMB", 0), type, faction);

        Assert.True(DaggerfallSession.IsHolyPlace(DaggerfallWorldProfileKind.Interior, Building(14, 0)));
        Assert.True(DaggerfallSession.IsHolyPlace(DaggerfallWorldProfileKind.Interior, Building(18, 849)));
        Assert.False(DaggerfallSession.IsHolyPlace(DaggerfallWorldProfileKind.Interior, Building(18, 0)));
        Assert.False(DaggerfallSession.IsHolyPlace(DaggerfallWorldProfileKind.Exterior, Building(14, 0)));
        Assert.False(DaggerfallSession.IsHolyPlace(DaggerfallWorldProfileKind.Dungeon, Building(14, 0)));
        Assert.False(DaggerfallSession.IsHolyPlace(DaggerfallWorldProfileKind.Interior, null));
    }

    [Fact]
    public void Worn_deterioration_uses_the_session_time_and_survives_save_restore()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile inputs = SameContentAt(source, source.ProfileKey.Site,
            DaggerfallWorldProfileKind.Exterior, "held-enchantment-rest");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service,
            new AppearanceFake(releases), random: RandomMaximum.Create());

        RulesetSavePayload saved;
        ulong durableId;
        int afterFirstHour;
        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            DurableIdentityReference identity = session.UniqueItemAllocator.AllocateReference();
            durableId = identity.Value;
            DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId("template-115-daedric"));
            session.State.ItemInstances.RegisterDefaultUnique(durableId, definition, DaggerfallItemOwner.Player);
            int authoredCondition = DaggerfallItemFactory.StartingCondition(
                definitions.ItemTemplateCatalog.Templates[115], "daedric");
            DaggerfallItemInstanceMetadata metadata = session.State.ItemInstances.RequireUnique(durableId);
            session.State.ItemInstances.ReplaceUnique(durableId, metadata with
            {
                CurrentCondition = authoredCondition,
                MaximumCondition = authoredCondition,
            });
            UniqueInventoryItem item = session.State.Equipment.Materialize(identity,
                new InventoryItemId("template-115-daedric"));
            int initial = session.State.ItemInstances.RequireUnique(durableId).CurrentCondition;
            Assert.True(initial > 30, $"authored condition was {initial}");
            string setting = DaggerfallEnchantmentSettings.All.Single(value => value.Type == 16 && value.Param == 0).Key;
            Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, session.ItemCondition.Enchant(item, setting).Outcome);
            EquipmentMoveResult equipped = session.EquipmentMoves.MoveToSlot(item, new EquipmentSlotId("right-hand"));
            Assert.True(equipped.Outcome == EquipmentMoveOutcome.Applied, equipped.Detail);

            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
            Assert.Equal(3600, session.RestView.ElapsedSeconds);
            afterFirstHour = session.State.ItemInstances.RequireUnique(durableId).CurrentCondition;
            Assert.Equal(initial - 15, afterFirstHour);
            Assert.Contains(session.State.Equipment.Read().Assignments,
                assignment => session.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durableId);
            saved = session.CaptureSave();
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service,
            new AppearanceFake(releases), random: RandomMaximum.Create());
        ResolvedCompositionIdentity composition = GameCompositionResolver.Resolve(FullContent(root),
            new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, composition, definitions, inputs,
            DaggerfallTuning.Defaults, saved, RandomMaximum.Create());
        DaggerfallItemInstanceMetadata retained = restored.State.ItemInstances.RequireUnique(durableId);
        Assert.Equal(afterFirstHour, retained.CurrentCondition);
        Assert.Equal(DaggerfallEnchantmentSettings.All.Single(value => value.Type == 16 && value.Param == 0).Key,
            retained.Enchantment);
        Assert.Contains(restored.State.Equipment.Read().Assignments,
            assignment => restored.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durableId);

        restored.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
        Assert.Equal(3600, restored.RestView.ElapsedSeconds);
        Assert.Equal(afterFirstHour - 15, restored.State.ItemInstances.RequireUnique(durableId).CurrentCondition);
    }
}
