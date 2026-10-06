using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class MasqueOfClavicusSessionTests
{
    [Fact]
    public void Published_artifact_rebuilds_reactions_from_saved_equipment_and_cleans_up_immediately()
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
        var identity = GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        DaggerfallSessionComposition composition = new(definitions, inputs, DaggerfallTuning.Defaults, identity);
        EngineContextFake engine = Engine();
        RulesetSavePayload save;
        ulong durableId;
        int magnitude;
        int baseline;
        DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        try
        {
            DaggerfallCreatedItem created = new DaggerfallItemFactory(definitions, engine.Context.Random)
                .Create(new("Magic", "masque-session", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0000"));
            var itemIdentity = session.UniqueItemAllocator.AllocateReference();
            durableId = itemIdentity.Value;
            var item = session.State.Equipment.Materialize(itemIdentity, created.Item);
            session.State.ItemInstances.RegisterUnique(durableId, created.Metadata);
            baseline = session.State.Social.ReactionForFaction(15).Value;
            magnitude = session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).ValueInt / 5;
            Assert.Equal(EquipmentMoveOutcome.Applied, session.EquipmentMoves.MoveToSlot(item, new EquipmentSlotId("ring0")).Outcome);
            Assert.Equal(baseline + magnitude, session.State.Social.ReactionForFaction(15).Value);
            Assert.Equal(0, session.State.Social.PersonalReputation(definitions.Factions.Factions[15].SocialGroup));
            save = session.CaptureSave();
        }
        finally { session.Dispose(); }
        Assert.Equal(0, session.State.Social.ReactionModifier(0));

        using DaggerfallSession restored = DaggerfallSession.Restore(Engine().Context, composition, save);
        Assert.Equal(baseline + magnitude, restored.State.Social.ReactionForFaction(15).Value);
        Assert.Equal("magic-item.0000", restored.State.ItemInstances.RequireUnique(durableId).Enchantment);
        var worn = restored.State.Equipment.Read().Assignments.Single(assignment =>
            restored.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durableId).Item;
        Assert.Equal(EquipmentMoveOutcome.Applied, restored.EquipmentMoves.MoveToGrid(worn, 49).Outcome);
        Assert.Equal(baseline, restored.State.Social.ReactionForFaction(15).Value);
        RulesetSavePayload unequipped = restored.CaptureSave();
        using DaggerfallSession restoredUnequipped = DaggerfallSession.Restore(Engine().Context, composition, unequipped);
        Assert.Equal(baseline, restoredUnequipped.State.Social.ReactionForFaction(15).Value);
        Assert.Equal(0, restoredUnequipped.State.Social.ReactionModifier(0));
    }
}
