using System.Text;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Developer overrides of the player's possessions and progress (<see cref="IDeveloperPlayerSession"/>). Items are
/// made by the item factory and materialized into the inventory as new-game and Create Item make them, held through
/// the equipment owner's ordinary rules, gold received through the currency owner, skills set on the permanent stat
/// progression reads, a level earned by raising the level skills until progression says one is due (the ordinary
/// level-up then opens), and vitals filled as a new game fills them. An item is named as the game labels it
/// ("Steel Dagger"), or by its template name or index, with an optional material first ("ebony 113"); a weapon or
/// armour named without a material takes one rolled for the player's level, as stock does.
/// </summary>
internal sealed partial class DaggerfallSession : IDeveloperPlayerSession
{
    // A developer grant may give this many of one item at once.
    private const int MostGiven = 1000;
    // How many single-point skill rises a developer level-up tries before giving up.
    private const int MostLevelRises = 2000;
    private ulong _developerGrants;

    public DebugCommandResult Give(string item, int count)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (count is < 1 or > MostGiven)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"A count is 1 to {MostGiven}.");
        if (ResolveItemTemplate(item, out DaggerfallItemTemplateDefinition? template, out string? material) is { } unresolved) return Refused(unresolved);
        int given = 0;
        string label = template!.Name;
        DaggerfallCreatedItem[] made;
        try { made = [.. CreateDeveloperItems(template, material, count)]; }
        catch (ArgumentException refused) { return Refused($"{template.Name} cannot be made: {refused.Message}"); }
        foreach (DaggerfallCreatedItem created in made)
        {
            if (Grant(created, out _) is { } failed)
                return given == 0 ? Refused(failed) : DebugCommandResult.Success($"Gave {label} ×{given}; then: {failed}");
            given += checked((int)created.Quantity);
            label = ItemLabel(created);
        }
        return DebugCommandResult.Success($"Gave {label} ×{given}.");
    }

    public DebugCommandResult Hold(string item)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (ResolveItemTemplate(item, out DaggerfallItemTemplateDefinition? template, out string? material) is { } unresolved) return Refused(unresolved);
        DaggerfallCreatedItem created;
        try { created = CreateDeveloperItems(template!, material, 1).Single(); }
        catch (ArgumentException refused) { return Refused($"{template!.Name} cannot be made: {refused.Message}"); }
        if (created.Stackable) return Refused($"{template!.Name} stacks; player.hold wields one item, so give it with player.give.");
        if (Grant(created, out DurableIdentityReference? identity) is { } failed) return Refused(failed);
        EquipmentMoveResult held = EquipmentMoves.MoveToSlot(new UniqueInventoryItem(State.Actors.Entities.Resolve(identity!.Value).Value, created.Item),
            new EquipmentSlotId("right-hand"));
        if (held.Outcome != EquipmentMoveOutcome.Applied) return Refused($"Gave {ItemLabel(created)}, but it was not wielded: {held.Detail}");
        // The next admitted update draws it: the appearance follows the equipment there, inside the update that
        // publishes its snapshot, never from a command between updates.
        State.WeaponDrawn = true;
        return DebugCommandResult.Success($"Holding {ItemLabel(created)}, drawn.");
    }

    public DebugCommandResult Money(long amount)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (amount is < 1 or > 100_000_000)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "An amount is 1 to 100000000 gold.");
        return State.Currency.ReceiveGold((ulong)amount)
            ? DebugCommandResult.Success($"Gave {amount} gold; carrying {State.Currency.Read().Gold}.")
            : Refused("The gold could not be added.");
    }

    public DebugCommandResult Skill(string skill, int value)
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (!State.SkillUses.Skills.Contains(skill))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments,
                $"Unknown skill '{skill}'. Skills: {string.Join(", ", State.SkillUses.Skills)}.");
        int set = State.SkillUses.SetPermanentSkill(skill, value);
        return DebugCommandResult.Success($"{skill} is {set}" + (State.SkillUses.PendingLevelUp ? "; a level is due (player.levelup opens it)." : "."));
    }

    public DebugCommandResult LevelUp()
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        if (State.LevelUps.Read() is not null) return Refused("A level-up is already open on the character sheet.");
        IReadOnlyList<string> skills = State.SkillUses.LevelSkills;
        for (int rise = 0; !State.SkillUses.PendingLevelUp; rise++)
            if (rise >= MostLevelRises || !State.SkillUses.TryTrainPermanentSkill(skills[rise % skills.Count], DaggerfallSkillUseReactions.MaximumSkillValue, out _)
                && skills.All(skill => State.SkillUses.PermanentSkillValue(skill) >= DaggerfallSkillUseReactions.MaximumSkillValue))
                return Refused("The level skills are all at their cap; no further level can be earned.");
        return State.LevelUps.BeginIfEligible()
            ? DebugCommandResult.Success($"Level {State.SkillUses.CalculatedPlayerLevel} earned: allocate it on the character sheet.")
            : Refused("Progression says a level is due, but the level-up did not begin.");
    }

    public DebugCommandResult Heal()
    {
        if (DeveloperMoveRefusal() is { } refusal) return Refused(refusal);
        foreach (var (_, track) in State.Actors.Player.Stats.Tracks) track.SetCurrent(track.Maximum.Value, clamp: true);
        return DebugCommandResult.Success("Vitals restored.");
    }

    public DebugCommandResult Items(string filter)
    {
        StringBuilder text = new();
        foreach (DaggerfallItemTemplateDefinition template in DeveloperTemplates()
            .Where(template => template.Name.Contains(filter ?? "", StringComparison.OrdinalIgnoreCase)))
            text.AppendLine($"{template.Index} {template.Name} ({template.Groups[0]})");
        text.AppendLine($"Weapon materials: {string.Join(", ", DaggerfallItemMaterialPolicy.WeaponMaterials)}; armour also leather, chain.");
        text.Append($"Skills: {string.Join(", ", State.SkillUses.Skills)}.");
        return DebugCommandResult.Success(text.ToString());
    }

    // The templates a developer grant can make: the ordinary item templates with a category the factory creates.
    private IEnumerable<DaggerfallItemTemplateDefinition> DeveloperTemplates() =>
        _definitions.ItemTemplateCatalog.Templates.Values.Where(template => template.Groups.Count > 0).OrderBy(template => template.Index);

    // Resolves "Steel Dagger", "dagger", "ebony dagger" or "113": an optional material, then a template's name or index.
    private string? ResolveItemTemplate(string requested, out DaggerfallItemTemplateDefinition? template, out string? material)
    {
        template = null;
        material = null;
        string text = (requested ?? "").Trim();
        if (text.Length == 0) return "Name an item; player.items lists them.";
        string[] materials = [.. DaggerfallItemMaterialPolicy.WeaponMaterials, "leather", "chain"];
        material = materials.FirstOrDefault(name => text.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
        string name = material is null ? text : text[(material.Length + 1)..].Trim();
        DaggerfallItemTemplateDefinition[] all = [.. DeveloperTemplates()];
        DaggerfallItemTemplateDefinition[] found = int.TryParse(name, out int index)
            ? [.. all.Where(candidate => candidate.Index == index)]
            : all.Where(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray() is { Length: > 0 } exact
                ? exact : [.. all.Where(candidate => candidate.Name.Contains(name, StringComparison.OrdinalIgnoreCase))];
        if (found.Length == 0) return $"No item is named '{requested}'; player.items lists them.";
        if (found.Length > 1)
            return $"'{requested}' names {found.Length} items: {string.Join(", ", found.Take(8).Select(candidate => $"{candidate.Index} {candidate.Name}"))}{(found.Length > 8 ? ", ..." : "")}.";
        template = found[0];
        string group = template.Groups[0];
        if (material is not null && !(group == "Weapons" ? DaggerfallItemMaterialPolicy.IsWeaponMaterial(material)
            : group == "Armor" && DaggerfallItemMaterialPolicy.IsArmorMaterial(material)))
            return $"{template.Name} ({group}) takes no material '{material}'.";
        return null;
    }

    // The items a grant makes: one stack of the count for a stackable item, else one item each.
    private IEnumerable<DaggerfallCreatedItem> CreateDeveloperItems(DaggerfallItemTemplateDefinition template, string? material, int count)
    {
        DaggerfallItemFactory factory = new(_definitions, _random);
        string group = template.Groups[0];
        bool appearance = group is "Armor" or "MensClothing" or "WomensClothing";
        DaggerfallItemCreateRequest Request(ulong? quantity) => new(group, $"developer.{++_developerGrants}", DaggerfallItemOwner.Player,
            Quantity: quantity, TemplateIndex: template.Index, Material: material, Level: Math.Max(1, State.Progression.Level),
            Race: appearance ? State.Character.Identity.RaceId : null,
            Gender: appearance ? State.Character.Identity.Gender.ToString().ToLowerInvariant() : null);
        if (template.Stackable)
        {
            yield return factory.Create(Request((ulong)count));
            yield break;
        }
        for (int made = 0; made < count; made++) yield return factory.Create(Request(null));
    }

    // Puts a made item in the player's possession as a pickup would: gold as currency, the rest into the inventory if
    // it can be carried. Answers why not, or nothing.
    private string? Grant(DaggerfallCreatedItem created, out DurableIdentityReference? identity)
    {
        identity = null;
        DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(created.Item.Value));
        if (DaggerfallEncumbrancePolicy.IsGold(definition))
            return State.Currency.ReceiveGold(created.Quantity) ? null : "The gold could not be added.";
        if (!State.Encumbrance.CanCarry(definition, created.Quantity)) return $"{ItemLabel(created)} is more than the player can carry.";
        identity = created.Stackable ? null : _uniqueItems.AllocateReference();
        try
        {
            new DaggerfallItemFactory(_definitions, _random).Materialize(created, State.Inventory, State.ItemInstances,
                created.Stackable ? InventoryStackId.Parse($"daggerfall.developer.{_developerGrants}") : null, identity);
            return null;
        }
        catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
        {
            if (identity is { } rejected) _uniqueItems.Remove(rejected);
            identity = null;
            return $"There is no room for {ItemLabel(created)}.";
        }
    }

    private string ItemLabel(DaggerfallCreatedItem created) =>
        DaggerfallItemNames.Label(_definitions, _definitions.RequireItem(new DaggerfallItemId(created.Item.Value)), created.Metadata);
}
