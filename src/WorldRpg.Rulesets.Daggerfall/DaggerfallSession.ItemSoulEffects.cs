using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit.Controls;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallCreateItemRequest? _pendingCreateItem;
    internal DaggerfallSoulGems SoulGems => new(State.Inventory, State.ItemInstances, _definitions.Magic, _uniqueItems);
    internal DaggerfallCreateItemView? CreateItemView => _pendingCreateItem is { } request ? new(request.Instance, CreateItemOptions()) : null;

    private DaggerfallInventoryUseResult UseAzurasStar(WorldRpg.Kit.Inventory.UniqueInventoryItem item)
    {
        ulong id = State.Inventory.GetDurableItemId(new(item.EntityId)).Value;
        int? released = SoulGems.ReleaseStar(id);
        return released is { } mobile ? new(true, $"Released {_definitions.Actors.Values.First(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId == mobile).Id.Value} from Azura's Star.")
            : new(false, "Azura's Star has no soul to release.");
    }

    private void ReleaseBoundSoul(ulong itemId)
    {
        if (!State.ItemInstances.ContainsUnique(itemId)) return;
        var item = State.ItemInstances.RequireUnique(itemId);
        if (!item.BoundSoulReleasePending || item.BoundSoulReleased) return;
        if (!_definitions.Magic.TryEnchantments(item, out var payloads)) return;
        var soul = payloads.SingleOrDefault(value => value.Type == 15);
        if (soul is null) return;
        var definition = _definitions.Actors.Values.FirstOrDefault(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId == soul.Param);
        if (definition is null) throw new InvalidOperationException($"Bound soul {soul.Param} has no admitted actor definition.");
        WorldPoint? origin = item.Owner == DaggerfallItemOwner.Player ? State.PlayerControl.Position
            : item.Owner.Scope == "actor" && State.Actors.TryGet(item.Owner.Id, out var owner) ? owner.Position : null;
        if (origin is not { } position || !_sites.Projection.Inputs.MobileSprites.ContainsKey(soul.Param)
            || !TrySpawnPose($"soul:{itemId}", "daggerfall.soul-bound.v1", position, 4f, 20f, out var pose)) return;
        var spawned = _roster.Spawn(definition.Id.Value, pose, playerAllied: false);
        _enemyBehavior.MakeHostile(spawned);
        State.ItemInstances.ReplaceUnique(itemId, item with { BoundSoulReleasePending = false, BoundSoulReleased = true });
        Presentation.SetOutcome($"The bound {definition.Id.Value} escaped from the broken item.");
    }

    private void ReleasePendingBoundSouls()
    {
        foreach (ulong itemId in State.ItemInstances.UniqueItems.Where(value => value.Value.BoundSoulReleasePending).Select(value => value.Key).ToArray())
            ReleaseBoundSoul(itemId);
    }

    private void CaptureHeldSoul(ActorDiedFact death)
    {
        if (death.ActorId == DaggerfallActorIdentity.PlayerEntityId
            || death.KillerId != DaggerfallActorIdentity.PlayerEntityId
            || !State.HeldEnchantments.AzurasStarEquipped
            || DaggerfallItemSoulEffects.WasSoulCaptured(State.Effects, death.ActorId)) return;
        int? mobile = DefinitionsByActor.TryGetValue(death.ActorId, out var actor) && actor.Kind == DaggerfallActorKinds.Monster ? actor.MobileId : null;
        var result = SoulGems.CaptureStar(mobile);
        _facts.Append(new AzurasStarCaptureFact(death.ActorId, result.ItemId, result.Outcome));
    }

    private void RequestCreateItem(DaggerfallCreateItemRequest request)
    {
        static DaggerfallCreateItemRequest Append(DaggerfallCreateItemRequest prior, DaggerfallCreateItemRequest added) =>
            prior with { Next = prior.Next is { } next ? Append(next, added) : added };
        _pendingCreateItem = _pendingCreateItem is { } prior ? Append(prior, request) : request;
    }

    private DaggerfallCreateItemOption[] CreateItemOptions()
    {
        List<DaggerfallCreateItemOption> choices = [];
        string[] names = ["Cuirass", "Gauntlets", "Greaves", "Left Pauldron", "Right Pauldron", "Helm", "Boots"];
        foreach (string material in new[] { "leather", "chain", "steel" })
            for (int slot = 0; slot < names.Length; slot++)
                choices.Add(new($"{material}-{102 + slot}", $"{char.ToUpperInvariant(material[0])}{material[1..]} {names[slot]}", "Armor", 102 + slot, material));
        choices.Add(new("steel-109", "Steel Buckler", "Armor", 109, "steel"));
        foreach (var (template, name) in new[] { (113, "Steel Dagger"), (120, "Steel Longsword"), (115, "Steel Staff"),
            (129, "Short Bow"), (131, "Arrows"), (127, "Steel Battle Axe") })
            choices.Add(new($"weapon-{template}", name, "Weapons", template, template == 131 ? null : "steel"));
        bool female = State.Character.Identity.Gender == DaggerfallCharacterGender.Female;
        choices.Add(new("robes", "Robes", female ? "WomensClothing" : "MensClothing", female ? 200 : 163, null));
        return choices.ToArray();
    }

    internal void ChooseCreateItem(string revision, string key)
    {
        if (_pendingCreateItem is not { } request || request.Instance != revision)
        { Presentation.SetOutcome("Create Item choice is no longer current."); return; }
        var option = CreateItemOptions().SingleOrDefault(choice => choice.Id == key);
        if (option is null) { Presentation.SetOutcome("Choose one of the offered items."); return; }
        bool appearance = option.Category != "Weapons";
        var factory = new DaggerfallItemFactory(_definitions, _random);
        var created = factory.Create(new(option.Category, request.Instance, DaggerfallItemOwner.Player,
            TemplateIndex: option.Template, Material: option.Material,
            Race: appearance ? State.Character.Identity.RaceId : null,
            Gender: appearance ? State.Character.Identity.Gender.ToString().ToLowerInvariant() : null));
        if (!State.Encumbrance.CanCarry(_definitions.RequireItem(new DaggerfallItemId(created.Item.Value)), created.Quantity))
        {
            Presentation.SetOutcome("Cannot create that item: you cannot carry any more.");
            return;
        }
        created = created with { Metadata = created.Metadata with { Conjuration = new(request.Instance, checked(MinuteIndex(_time.Calendar) + request.Duration)) } };
        var identity = created.Stackable ? (WorldRpg.Kit.World.DurableIdentityReference?)null : _uniqueItems.AllocateReference();
        try
        {
            factory.Materialize(created, State.Inventory, State.ItemInstances,
                created.Stackable ? InventoryStackId.Parse($"daggerfall.conjured.{request.Instance}") : null, identity);
        }
        catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
        {
            if (identity is { } rejected) _uniqueItems.Remove(rejected);
            Presentation.SetOutcome($"Cannot create that item: {failure.Message}");
            return;
        }
        _pendingCreateItem = request.Next;
        Presentation.SetOutcome($"Created {option.Label} for {request.Duration} minutes.");
    }
}
