using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11140: Item exchange and equipment management with hired retainers through Kit's
/// inventory and equipment owners, dialogue person service projection, UI actions, travel custody,
/// and full save/reload persistence.
/// </summary>
public sealed class RetainerItemExchangeTests
{
    private ulong _nextUpdate = 1;

    [Fact]
    public void Hired_retainer_supports_give_equip_unequip_take_through_ui_actions_and_refuses_equipped_and_stale()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Give the player plenty of gold and spawn a hireable civilian nearby.
        session.State.Currency.ReceiveGold(500UL);
        long companionId = SpawnHireableCivilian(session, "retainer-exchange/alden", "Alden");

        // Before hiring, talking to Alden offers Talk only (no Retainer view / Gear service).
        OpenDialogueWith(session, companionId);
        Assert.Null(session.ActivationPanel.View.Dialogue?.Retainer);
        Assert.DoesNotContain(session.ActivationPanel.View.Dialogue!.Services, s => s.Id == DaggerfallPersonServices.Retainer);

        // Hire Alden through the canonical dialogue topic.
        string talkRev = session.ActivationPanel.View.Dialogue.Revision;
        SendUiAction(session, new { action = "dialogue-topic", revision = talkRev, topic = "hire-companion" });
        Assert.Contains(companionId, session.OverlandParties.StagedPartyMemberIds);

        // Now the person screen exposes the Gear ("retainer") service and live Retainer projection.
        DaggerfallDialogueView? dialogue = session.ActivationPanel.View.Dialogue;
        Assert.NotNull(dialogue);
        Assert.Contains(dialogue.Services, s => s.Id == DaggerfallPersonServices.Retainer && s.Label == "Gear");
        DaggerfallRetainerView? retainer = dialogue.Retainer;
        Assert.NotNull(retainer);
        Assert.Equal(companionId, retainer.ActorId);

        // Materialize an unequipped unique weapon and a fungible stack in the player's pack.
        DaggerfallItemDefinition weaponDef = TestPayload.Definitions.Items.Values.First(i => !i.IsFungible && i.Weapon is not null);
        DaggerfallItemDefinition stackDef = TestPayload.Definitions.Items.Values.First(i => i.IsFungible && i.Id.Value != "gold");
        (ulong weaponDurableId, string grantedWeaponKey) = GrantPlayerUnique(session, weaponDef, 910_001UL);
        string stackIdValue = "retainer-test-stack";
        GrantPlayerStack(session, stackDef, stackIdValue, 10UL);

        // Also confirm an equipped player item is refused for giving.
        var equippedPlayerRow = session.ActivationPanel.View.Dialogue!.Retainer!.PlayerItems
            .FirstOrDefault(r => r.EquippedSlots.Length > 0);
        if (equippedPlayerRow is not null)
        {
            Assert.NotNull(equippedPlayerRow.TransferRefusal);
            string revBefore = session.ActivationPanel.View.Dialogue!.Retainer!.Revision;
            SendUiAction(session, new { action = "retainer-give", revision = revBefore, item = equippedPlayerRow.Key });
            Assert.Contains("Unequip", session.ActivationPanel.View.Dialogue!.Retainer!.Result, StringComparison.OrdinalIgnoreCase);
        }

        // Stale revision is cleanly refused without mutating inventories.
        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        var playerWeaponRow = retainer.PlayerItems.Single(r => r.Key == grantedWeaponKey);
        SendUiAction(session, new { action = "retainer-give", revision = "99999:99999:99999", item = playerWeaponRow.Key });
        Assert.Contains("Inventory changed", session.ActivationPanel.View.Dialogue!.Retainer!.Result, StringComparison.Ordinal);

        // 1. Give the unique weapon to the retainer through retainer-give UI action.
        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        SendUiAction(session, new { action = "retainer-give", revision = retainer.Revision, item = playerWeaponRow.Key });

        InventoryView retainerInv = session.State.ActorInventories.InventoryFor(companionId)!.Read();
        Assert.Contains(retainerInv.UniqueItems, u => u.Definition.Value == weaponDef.Id.Value);
        Assert.Equal(DaggerfallItemOwner.Actor(companionId), session.State.ItemInstances.RequireUnique(weaponDurableId).Owner);

        // 2. Give 4 of the 10 stackable items to the retainer.
        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        string stackKey = $"stack:{stackIdValue}";
        SendUiAction(session, new { action = "retainer-give", revision = retainer.Revision, item = stackKey, amount = 4 });
        Assert.Equal(6UL, session.State.Inventory.Read().Stacks.Single(s => s.Id.Value == stackIdValue).Quantity);
        Assert.Equal(4UL, session.State.ActorInventories.InventoryFor(companionId)!.Read().Stacks
            .Single(s => s.Definition.Value == stackDef.Id.Value).Quantity);

        // 3. Equip the weapon on the retainer through retainer-equip UI action.
        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        DaggerfallRetainerItemView retainerWeapon = retainer.RetainerItems.Single(r => r.Definition == weaponDef.Id.Value);
        Assert.True(retainerWeapon.CanEquip);
        Assert.False(retainerWeapon.CanUnequip);
        SendUiAction(session, new { action = "retainer-equip", revision = retainer.Revision, item = retainerWeapon.Key });

        EquipmentRead retainerEquip = session.State.ActorInventories.EquipmentFor(companionId).Read();
        Assert.Contains(retainerEquip.Assignments, a => a.Item.Definition.Value == weaponDef.Id.Value);

        // Equipped item on retainer cannot be taken until unequipped.
        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        retainerWeapon = retainer.RetainerItems.Single(r => r.Definition == weaponDef.Id.Value);
        Assert.True(retainerWeapon.CanUnequip);
        Assert.NotNull(retainerWeapon.TransferRefusal);
        SendUiAction(session, new { action = "retainer-take", revision = retainer.Revision, item = retainerWeapon.Key });
        Assert.Contains("Unequip", session.ActivationPanel.View.Dialogue!.Retainer!.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.State.ActorInventories.InventoryFor(companionId)!.Read().UniqueItems,
            u => u.Definition.Value == weaponDef.Id.Value);

        // 4. Unequip the weapon on the retainer through retainer-unequip, then take it back to player pack.
        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        SendUiAction(session, new { action = "retainer-unequip", revision = retainer.Revision, item = retainerWeapon.Key });
        Assert.DoesNotContain(session.State.ActorInventories.EquipmentFor(companionId).Read().Assignments,
            a => a.Item.Definition.Value == weaponDef.Id.Value);

        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        retainerWeapon = retainer.RetainerItems.Single(r => r.Definition == weaponDef.Id.Value);
        Assert.Null(retainerWeapon.TransferRefusal);
        SendUiAction(session, new { action = "retainer-take", revision = retainer.Revision, item = retainerWeapon.Key });
        Assert.DoesNotContain(session.State.ActorInventories.InventoryFor(companionId)!.Read().UniqueItems,
            u => u.Definition.Value == weaponDef.Id.Value);
        Assert.Contains(session.State.Inventory.Read().UniqueItems, u => u.Definition.Value == weaponDef.Id.Value);
        Assert.Equal(DaggerfallItemOwner.Player, session.State.ItemInstances.RequireUnique(weaponDurableId).Owner);

        // 5. Take 2 of the 4 stackable items back to player pack (merges into a compatible player stack).
        ulong playerStackTotalBefore = (ulong)session.State.Inventory.Read().Stacks
            .Where(s => s.Definition.Value == stackDef.Id.Value).Sum(s => (long)s.Quantity);
        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        DaggerfallRetainerItemView retainerStack = retainer.RetainerItems.Single(r => r.Definition == stackDef.Id.Value);
        SendUiAction(session, new { action = "retainer-take", revision = retainer.Revision, item = retainerStack.Key, amount = 2 });
        string takeOutcome = session.ActivationPanel.View.Dialogue!.Retainer!.Result;
        Assert.StartsWith("Took 2", takeOutcome, StringComparison.Ordinal);
        ulong playerStackTotalAfter = (ulong)session.State.Inventory.Read().Stacks
            .Where(s => s.Definition.Value == stackDef.Id.Value).Sum(s => (long)s.Quantity);
        Assert.Equal(playerStackTotalBefore + 2UL, playerStackTotalAfter);
        Assert.Equal(2UL, session.State.ActorInventories.InventoryFor(companionId)!.Read().Stacks
            .Single(s => s.Definition.Value == stackDef.Id.Value).Quantity);
    }

    [Fact]
    public void Retainer_inventory_and_equipped_gear_persist_across_party_boarding_restaging_and_save_reload()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        session.State.Currency.ReceiveGold(500UL);
        long companionId = SpawnHireableCivilian(session, "retainer-persist/bren", "Bren");
        OpenDialogueWith(session, companionId);
        SendUiAction(session, new { action = "dialogue-topic", revision = session.ActivationPanel.View.Dialogue!.Revision, topic = "hire-companion" });

        DaggerfallItemDefinition weaponDef = TestPayload.Definitions.Items.Values.First(i => !i.IsFungible && i.Weapon is not null);
        DaggerfallItemDefinition stackDef = TestPayload.Definitions.Items.Values.First(i => i.IsFungible && i.Id.Value != "gold");
        (ulong weaponDurableId, string weaponKey) = GrantPlayerUnique(session, weaponDef, 910_002UL);
        GrantPlayerStack(session, stackDef, "retainer-persist-stack", 5UL);

        // Give both items to Bren and equip the weapon on Bren.
        DaggerfallRetainerView retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        Assert.Contains(retainer.PlayerItems, r => r.Key == weaponKey);
        SendUiAction(session, new { action = "retainer-give", revision = retainer.Revision, item = weaponKey });

        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        SendUiAction(session, new { action = "retainer-give", revision = retainer.Revision, item = "stack:retainer-persist-stack", amount = 3 });

        retainer = session.ActivationPanel.View.Dialogue!.Retainer!;
        string brenWeaponKey = retainer.RetainerItems.Single(r => r.Definition == weaponDef.Id.Value).Key;
        SendUiAction(session, new { action = "retainer-equip", revision = retainer.Revision, item = brenWeaponKey });

        // Close dialogue, save/reload while staged, verify Bren keeps both items and equipped weapon!
        SendUiAction(session, new { action = "dialogue-close", revision = session.ActivationPanel.View.Dialogue!.Revision });
        using (DaggerfallSession restoredStaged = fixture.Restore(session.CaptureSave()))
        {
            InventoryView restoredInv = restoredStaged.State.ActorInventories.InventoryFor(companionId)!.Read();
            Assert.Contains(restoredInv.UniqueItems, u => u.Definition.Value == weaponDef.Id.Value);
            Assert.Equal(3UL, restoredInv.Stacks.Single(s => s.Definition.Value == stackDef.Id.Value).Quantity);
            Assert.Contains(restoredStaged.State.ActorInventories.EquipmentFor(companionId).Read().Assignments,
                a => a.Item.Definition.Value == weaponDef.Id.Value);
            Assert.Equal(DaggerfallItemOwner.Actor(companionId), restoredStaged.State.ItemInstances.RequireUnique(weaponDurableId).Owner);

            // Board into WithParty custody, save & reload while boarded, then re-stage and verify gear survived travel custody!
            IReadOnlyList<long> boarded = restoredStaged.OverlandParties.BoardStagedPartyMembers();
            Assert.Contains(companionId, boarded);

            using DaggerfallSession restoredBoarded = fixture.Restore(restoredStaged.CaptureSave());
            WorldPoint spot = restoredBoarded.State.PlayerControl.Position ?? new WorldPoint(0f, 0f, 0f);
            ActorState? restaged = restoredBoarded.OverlandParties.StagePartyMember(
                companionId, fixture.ExteriorProfile, new WorldPoint(spot.X + 2f, spot.Y, spot.Z + 2f), 0f);
            Assert.NotNull(restaged);

            InventoryView afterTravelInv = restoredBoarded.State.ActorInventories.InventoryFor(companionId)!.Read();
            Assert.Contains(afterTravelInv.UniqueItems, u => u.Definition.Value == weaponDef.Id.Value);
            Assert.Equal(3UL, afterTravelInv.Stacks.Single(s => s.Definition.Value == stackDef.Id.Value).Quantity);
            Assert.Contains(restoredBoarded.State.ActorInventories.EquipmentFor(companionId).Read().Assignments,
                a => a.Item.Definition.Value == weaponDef.Id.Value);
        }
    }

    private static long SpawnHireableCivilian(DaggerfallSession session, string stableKey, string displayName)
    {
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        DaggerfallNpcAppearance appearance = new("breton", "Male", 334, 8, Math.Abs(stableKey.GetHashCode()), 0);
        long id = session.State.Npcs.RegisterCivilian(npcSite, appearance, displayName, ["talk"]);
        session.State.Npcs.SetDisplayName(id, displayName);
        WorldPoint playerPos = session.State.PlayerControl.Position ?? new WorldPoint(0f, 0f, 0f);
        session.Civilians.Materialize(id, new ActorPose(new WorldPoint(playerPos.X + 2f, playerPos.Y, playerPos.Z + 2f), 0f));
        session.State.Npcs.Bind(id, session.Sites.ActiveProfile);
        return id;
    }

    private static void OpenDialogueWith(DaggerfallSession session, long actorId)
    {
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            t => t.Identity.Value == checked((ulong)actorId));
        DaggerfallActivationOutcome outcome = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target));
        Assert.True(outcome.Applied, outcome.Message);
    }

    private static (ulong DurableId, string RuntimeKey) GrantPlayerUnique(DaggerfallSession session, DaggerfallItemDefinition def, ulong durableId)
    {
        DurableIdentityReference identity = new(DurableIdentityKind.Item, durableId);
        session.State.Inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId(def.Id.Value), UniqueItem: identity)]);
        int maxCond = DaggerfallItemFactory.AuthoredMaximumCondition(TestPayload.Definitions, def);
        session.State.ItemInstances.RegisterUnique(identity.Value,
            DaggerfallItemInstanceMetadata.Default(def, DaggerfallItemOwner.Player) with
            {
                CurrentCondition = maxCond,
                MaximumCondition = maxCond,
            });
        ulong runtimeEntity = session.State.Inventory.Read().UniqueItems
            .Single(u => session.State.Inventory.GetDurableItemId(u.Entity).Value == durableId).Entity.Value;
        return (identity.Value, $"unique:{runtimeEntity}");
    }

    private static void GrantPlayerStack(DaggerfallSession session, DaggerfallItemDefinition def, string stackIdValue, ulong qty)
    {
        InventoryStackId stackId = InventoryStackId.Parse(stackIdValue);
        session.State.Inventory.Grant(new InventoryGrant(new InventoryItemId(def.Id.Value), stackId, qty));
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Player,
            new InventoryStack(stackId, ItemDefinitionId.Parse(def.Id.Value), qty), def);
    }

    private void SendUiAction(DaggerfallSession session, object payload)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        ProductInputEvent inputEvent = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = DaggerfallUiAction.Contract.ToArray(),
            PayloadData = json,
        };
        session.Update(new ProductUpdate(OuterUpdate(_nextUpdate++), [inputEvent]));
    }
}
