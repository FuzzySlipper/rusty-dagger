using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class HeldSocialEnchantmentSessionTests
{
    [Theory]
    [InlineData(14, 0, 10)]
    [InlineData(25, 5, -10)]
    public void Setting_enchantment_rebuilds_social_and_npc_reactions_from_saved_equipment(int type, int param, int amount)
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
        RulesetSavePayload save;
        ulong durableId;
        int factionId = definitions.Factions.Factions.Values.First(faction => faction.SocialGroup == 0).Id;
        DaggerfallNpc npc = new(9001, DaggerfallNpcKind.Static, "held-social-test",
            new DaggerfallNpcSite(0, "Daggerfall", "held-social-test"),
            new DaggerfallNpcAppearance("Breton", "Female", 0, 0, 0, FactionId: factionId), "talker", ["talk"],
            DaggerfallNpcPresence.Hidden, null, null, null);
        int baseline;
        string key = $"enchantment.{type}.{param}";
        using (DaggerfallSession session = DaggerfallSession.StartNew(Engine().Context, composition))
        {
            session.State.Character.BeginChoices();
            session.State.Character.ReplacePending(session.State.Character.ReadCreation().Current with
            {
                CareerId = "class16", CustomCareer = null, Background = null,
            });
            session.State.Character.CommitChoices();
            DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId("template-120-daedric"));
            var itemIdentity = session.UniqueItemAllocator.AllocateReference();
            durableId = itemIdentity.Value;
            var item = session.State.Equipment.Materialize(itemIdentity, new InventoryItemId(definition.Id.Value));
            session.State.ItemInstances.RegisterDefaultUnique(durableId, definition, DaggerfallItemOwner.Player);
            baseline = session.State.Social.ReactionForFaction(factionId).Value;
            Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, session.ItemCondition.Enchant(item, key).Outcome);
            EquipmentMoveResult moved = session.EquipmentMoves.MoveToSlot(item, new EquipmentSlotId("left-hand"));
            Assert.True(moved.Outcome == EquipmentMoveOutcome.Applied, moved.Detail);
            Assert.Equal(baseline + amount, session.State.Social.ReactionForFaction(factionId).Value);
            Assert.Equal(baseline + amount, session.State.Social.ReactionForNpc(npc).Value);
            Assert.Equal(0, session.State.Social.PersonalReputation(0));
            save = session.CaptureSave();
        }
        using DaggerfallSession restored = DaggerfallSession.Restore(Engine().Context, composition, save);
        Assert.Equal(baseline + amount, restored.State.Social.ReactionForNpc(npc).Value);
        Assert.Equal(key, Assert.Single(restored.State.ItemInstances.RequireUnique(durableId).MadeEnchantment!.Settings, value => value.Parent is null).Key);
        var worn = restored.State.Equipment.Read().Assignments.Single(assignment =>
            restored.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durableId).Item;
        Assert.Equal(EquipmentMoveOutcome.Applied, restored.EquipmentMoves.MoveToGrid(worn, 49).Outcome);
        Assert.Equal(baseline, restored.State.Social.ReactionForNpc(npc).Value);
        using DaggerfallSession unequipped = DaggerfallSession.Restore(Engine().Context, composition, restored.CaptureSave());
        Assert.Equal(baseline, unequipped.State.Social.ReactionForFaction(factionId).Value);
    }
}
