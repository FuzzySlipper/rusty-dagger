using WorldRpg.Rulesets.Daggerfall.Content;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallCreateItemRequest? _pendingCreateItem;
    internal DaggerfallSoulGems SoulGems => new(State.Inventory, State.ItemInstances, _definitions.Magic, _uniqueItems);
    internal DaggerfallCreateItemView? CreateItemView => _pendingCreateItem is { } request ? new(request.Instance, CreateItemOptions()) : null;

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
