using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The Create Item choices a Create Item spell on the player queues: one item from the donor's list each, conjured
/// for the spell's minutes. The queue is saved; declining one spends the spell and makes nothing.
/// </summary>
internal sealed class DaggerfallCreateItem(DaggerfallDefinitions definitions, IRandomService random, MechanicsInventoryCoordinator inventory,
    DaggerfallItemInstances instances, DaggerfallEncumbrancePolicy encumbrance, DaggerfallUniqueItemAllocator uniqueItems,
    DaggerfallCharacterState character, Func<DaggerfallCalendar> calendar, PresentationState presentation, DaggerfallCreateItemRequest? restored)
{
    internal DaggerfallCreateItemRequest? Pending { get; private set; } = restored;

    internal DaggerfallCreateItemView? View => Pending is { } request ? new(request.Instance, Options()) : null;

    internal void Clear() => Pending = null;

    internal void Request(DaggerfallCreateItemRequest request)
    {
        static DaggerfallCreateItemRequest Append(DaggerfallCreateItemRequest prior, DaggerfallCreateItemRequest added) =>
            prior with { Next = prior.Next is { } next ? Append(next, added) : added };
        Pending = Pending is { } prior ? Append(prior, request) : request;
    }

    private DaggerfallCreateItemOption[] Options()
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
        bool female = character.Identity.Gender == DaggerfallCharacterGender.Female;
        choices.Add(new("robes", "Robes", female ? "WomensClothing" : "MensClothing", female ? 200 : 163, null));
        return choices.ToArray();
    }

    internal void Choose(string revision, string key)
    {
        if (Pending is not { } request || request.Instance != revision)
        { presentation.SetOutcome("Create Item choice is no longer current."); return; }
        var option = Options().SingleOrDefault(choice => choice.Id == key);
        if (option is null) { presentation.SetOutcome("Choose one of the offered items."); return; }
        bool appearance = option.Category != "Weapons";
        var factory = new DaggerfallItemFactory(definitions, random);
        var created = factory.Create(new(option.Category, request.Instance, DaggerfallItemOwner.Player,
            TemplateIndex: option.Template, Material: option.Material,
            Race: appearance ? character.Identity.RaceId : null,
            Gender: appearance ? character.Identity.Gender.ToString().ToLowerInvariant() : null));
        if (!encumbrance.CanCarry(definitions.RequireItem(new DaggerfallItemId(created.Item.Value)), created.Quantity))
        {
            presentation.SetOutcome("Cannot create that item: you cannot carry any more.");
            return;
        }
        created = created with { Metadata = created.Metadata with { Conjuration = new(request.Instance, checked(calendar().MinuteNumber + request.Duration)) } };
        var identity = created.Stackable ? (WorldRpg.Kit.World.DurableIdentityReference?)null : uniqueItems.AllocateReference();
        try
        {
            factory.Materialize(created, inventory, instances,
                created.Stackable ? InventoryStackId.Parse($"daggerfall.conjured.{request.Instance}") : null, identity);
        }
        catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
        {
            if (identity is { } rejected) uniqueItems.Remove(rejected);
            presentation.SetOutcome($"You cannot carry {option.Label}.");
            return;
        }
        Pending = request.Next;
        presentation.SetOutcome($"Created {option.Label} for {request.Duration} minutes.");
    }

    /// <summary>Declines the current Create Item choice: the spell's magicka is spent and no item is made, as when the donor's picker closes.</summary>
    internal void Cancel(string revision)
    {
        if (Pending is not { } request || request.Instance != revision)
        { presentation.SetOutcome("Create Item choice is no longer current."); return; }
        Pending = request.Next;
        presentation.SetOutcome("Create Item cancelled.");
    }
}
