using System.Globalization;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using static WorldRpg.Rulesets.Daggerfall.DaggerfallDeveloperReplies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Developer overrides of the player's possessions and progress (player.*). Items are made by the item factory and
/// materialized into the inventory as new-game and Create Item make them, held through the equipment owner's ordinary
/// rules, gold received through the currency owner, skills set on the permanent stat progression reads, a level earned
/// by raising the level skills until progression says one is due (the ordinary level-up then opens), and vitals filled
/// as a new game fills them. An item is named as the game labels it ("Steel Dagger"), or by its template name or index,
/// with an optional material first ("ebony 113"); a weapon or armour named without a material takes one rolled for the
/// player's level, as stock does.
/// </summary>
/// <param name="drawWeapon">Draws the player's weapon, as a wield from the console does whatever the player rides.</param>
/// <param name="transportAccess">The transport access where the player stands.</param>
/// <param name="applyTransportChange">What a change of mount does (the session's ApplyTransportChange).</param>
internal sealed class DaggerfallDeveloperGrants(DaggerfallDeveloperHold hold, DaggerfallDefinitions definitions, IRandomService random,
    ActorsState actors, PlayerControlState player, DaggerfallEquipmentMoves equipmentMoves, Action drawWeapon, DaggerfallCurrencyService currency,
    DaggerfallSkillUseReactions skillUses, DaggerfallLevelUpState levelUps, DaggerfallCharacterState character, DaggerfallTransportPolicy transport,
    MechanicsInventoryCoordinator inventory, DaggerfallEncumbrancePolicy encumbrance, DaggerfallItemInstances itemInstances,
    DaggerfallUniqueItemAllocator uniqueItems, DaggerfallRacialOverrides racialOverrides, DaggerfallLycanthropy lycanthropy, DaggerfallWorldTime time,
    Func<DaggerfallTransportAccessContext> transportAccess, Action<DaggerfallTransportActionResult> applyTransportChange)
{
    // A developer grant may give this many of one item at once.
    private const int MostGiven = 1000;
    // How many single-point skill rises a developer level-up tries before giving up.
    private const int MostLevelRises = 2000;
    // Grants made this run, which name each grant's request and stack.
    private ulong _grants;

    internal DebugCommandResult Give(string item, int count)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (count is < 1 or > MostGiven)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"A count is 1 to {MostGiven}.");
        if (ResolveItemTemplate(item, out DaggerfallItemTemplateDefinition? template, out string? material) is { } unresolved) return Refused(unresolved);
        int given = 0;
        string label = template!.Name;
        DaggerfallCreatedItem[] made;
        try { made = [.. CreateItems(template, material, count)]; }
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

    internal DebugCommandResult Hold(string item)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (ResolveItemTemplate(item, out DaggerfallItemTemplateDefinition? template, out string? material) is { } unresolved) return Refused(unresolved);
        DaggerfallCreatedItem created;
        try { created = CreateItems(template!, material, 1).Single(); }
        catch (ArgumentException refused) { return Refused($"{template!.Name} cannot be made: {refused.Message}"); }
        if (created.Stackable) return Refused($"{template!.Name} stacks; player.hold wields one item, so give it with player.give.");
        return Wield(created);
    }

    internal DebugCommandResult HoldMagic()
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        // The regular magic templates of the weapons' base group.
        if (definitions.Magic.MagicItems.Values.Where(magic => magic is { Type: 0, Group: 2 }).MinBy(magic => magic.Key) is not { } weapon)
            return Refused("No magic template makes a weapon.");
        return Wield(new DaggerfallItemFactory(definitions, random).Create(new("Magic", $"developer.{++_grants}", DaggerfallItemOwner.Player,
            Level: Math.Max(1, actors.Player.Progression.Level), MagicItemKey: weapon.Key)));
    }

    private DebugCommandResult Wield(DaggerfallCreatedItem created)
    {
        if (Grant(created, out DurableIdentityReference? identity) is { } failed) return Refused(failed);
        EquipmentMoveResult held = equipmentMoves.MoveToSlot(new UniqueInventoryItem(actors.Entities.Resolve(identity!.Value).Value, created.Item),
            new EquipmentSlotId("right-hand"));
        if (held.Outcome != EquipmentMoveOutcome.Applied) return Refused($"Gave {ItemLabel(created)}, but it was not wielded: {held.Detail}");
        // The next admitted update draws it: the appearance follows the equipment there, inside the update that
        // publishes its snapshot, never from a command between updates.
        drawWeapon();
        return DebugCommandResult.Success($"Holding {ItemLabel(created)}, drawn.");
    }

    internal DebugCommandResult Equip(string item)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (ResolveItemTemplate(item, out DaggerfallItemTemplateDefinition? template, out string? material) is { } unresolved) return Refused(unresolved);
        DaggerfallCreatedItem created;
        try { created = CreateItems(template!, material, 1).Single(); }
        catch (ArgumentException refused) { return Refused($"{template!.Name} cannot be made: {refused.Message}"); }
        if (created.Stackable) return Refused($"{template!.Name} stacks, so give it with player.give.");
        if (Grant(created, out DurableIdentityReference? identity) is { } failed) return Refused(failed);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(created.Item.Value));
        // A shield goes on the off arm; anything else takes the first slot that fits it.
        string[] slots = [.. definitions.EquipmentSlots.Keys.Select(slot => slot.Value).Where(slot => DaggerfallEquipmentPolicy.IsCompatible(definitions, definition, slot))
            .OrderBy(slot => definition.Shield is not null && slot == "left-hand" ? 0 : 1)];
        if (slots.Length == 0) return Refused($"Gave {ItemLabel(created)}, but no slot takes it.");
        EquipmentMoveResult worn = equipmentMoves.MoveToSlot(new UniqueInventoryItem(actors.Entities.Resolve(identity!.Value).Value, created.Item), new EquipmentSlotId(slots[0]));
        return worn.Outcome == EquipmentMoveOutcome.Applied
            ? DebugCommandResult.Success($"Wearing {ItemLabel(created)} ({slots[0]}).")
            : Refused($"Gave {ItemLabel(created)}, but it was not worn: {worn.Detail}");
    }

    internal DebugCommandResult Money(long amount)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (amount is < 1 or > 100_000_000)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "An amount is 1 to 100000000 gold.");
        return currency.ReceiveGold((ulong)amount)
            ? DebugCommandResult.Success($"Gave {amount} gold; carrying {currency.Read().Gold}.")
            : Refused("The gold could not be added.");
    }

    internal DebugCommandResult Skill(string skill, int value)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (!skillUses.Skills.Contains(skill))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments,
                $"Unknown skill '{skill}'. Skills: {string.Join(", ", skillUses.Skills)}.");
        int set = skillUses.SetPermanentSkill(skill, value);
        return DebugCommandResult.Success($"{skill} is {set}" + (skillUses.PendingLevelUp ? "; a level is due (player.levelup opens it)." : "."));
    }

    internal DebugCommandResult LevelUp()
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (levelUps.Read() is not null) return Refused("A level-up is already open on the character sheet.");
        IReadOnlyList<string> skills = skillUses.LevelSkills;
        for (int rise = 0; !skillUses.PendingLevelUp; rise++)
            if (rise >= MostLevelRises || !skillUses.TryTrainPermanentSkill(skills[rise % skills.Count], DaggerfallSkillUseReactions.MaximumSkillValue, out _)
                && skills.All(skill => skillUses.PermanentSkillValue(skill) >= DaggerfallSkillUseReactions.MaximumSkillValue))
                return Refused("The level skills are all at their cap; no further level can be earned.");
        return levelUps.BeginIfEligible()
            ? DebugCommandResult.Success($"Level {skillUses.CalculatedPlayerLevel} earned: allocate it on the character sheet.")
            : Refused("Progression says a level is due, but the level-up did not begin.");
    }

    /// <summary>Fills every vital as a new game fills them; no owner has a call for it, so it sets the player's tracks.</summary>
    internal DebugCommandResult Heal()
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        foreach (var (_, track) in actors.Player.Stats.Tracks) track.SetCurrent(track.Maximum.Value, clamp: true);
        return DebugCommandResult.Success("Vitals restored.");
    }

    internal DebugCommandResult Spell(string spell)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        var named = definitions.Magic.Spells.Where(pair => pair.Value.Name.Contains(spell ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(pair => pair.Value.Name, StringComparer.Ordinal).ToArray();
        var exact = named.Where(pair => pair.Value.Name.Equals(spell, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 1) named = exact;
        if (named.Length != 1)
            return Refused(named.Length == 0 ? $"No spell is named '{spell}'." : $"'{spell}' names several spells: {string.Join(", ", named.Select(pair => pair.Value.Name))}.");
        return DebugCommandResult.Success(character.LearnSpell(named[0].Key) ? $"Learned {named[0].Value.Name}." : $"{named[0].Value.Name} is already known.");
    }

    internal DebugCommandResult Ride(string mount)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (!Enum.TryParse(mount, ignoreCase: true, out DaggerfallTransportMode mode) || mode == DaggerfallTransportMode.Ship)
            return Refused($"'{mount}' is no mount (foot, horse, cart).");
        if (mode != DaggerfallTransportMode.Foot && transport.SelectMount(mode, inventory.Read(), transportAccess()).Rejection is DaggerfallTransportRejection.MissingHorse or DaggerfallTransportRejection.MissingCart
            && Give(mode == DaggerfallTransportMode.Horse ? "94" : "93", 1) is { Succeeded: false } refused) return refused;
        DaggerfallTransportActionResult result = transport.SelectMount(mode, inventory.Read(), transportAccess());
        applyTransportChange(result);
        return result.Applied ? DebugCommandResult.Success(result.Message) : Refused(result.Message);
    }

    internal DebugCommandResult RideState()
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        if (transport.RideTuning is null) return Refused("The player rides nothing.");
        RideMotion ride = transport.Ride;
        float yaw = player.YawRadians;
        return DebugCommandResult.Success(string.Create(CultureInfo.InvariantCulture,
            $"{transport.Mode} at {ride.Speed:0.0} m/s, heading {(ride.HeadingRadians ?? yaw) * 180 / MathF.PI:0}, the view {-ride.TurnFrom(yaw) * 180 / MathF.PI:0} degrees past it."));
    }

    internal DebugCommandResult Morph(string form)
    {
        if (hold.Refusal() is { } refusal) return Refused(refusal);
        DaggerfallRacialOverrideView? racial = racialOverrides.Current;
        if (string.IsNullOrEmpty(form))
        {
            if (racial is not { State.BeastForm: true }) return Refused("The player is in no beast form.");
            lycanthropy.Morph(forced: true);
            return DebugCommandResult.Success("Back in human form.");
        }
        if (!Enum.TryParse(form, ignoreCase: true, out DaggerfallRacialKind kind) || kind == DaggerfallRacialKind.Vampire)
            return Refused($"'{form}' is no beast form (werewolf, wereboar).");
        if (racial is not null && racial.State.Kind != kind) return Refused($"The player is already a {racial.Name.ToLowerInvariant()}.");
        if (racial is { State.BeastForm: true }) return DebugCommandResult.Success($"Already in {kind} form.");
        if (racial is null) racialOverrides.Select(kind, "lycanthropy:developer", time.Calendar.MinuteNumber);
        lycanthropy.Morph(forced: true);
        return DebugCommandResult.Success($"Changed into {kind} form.");
    }

    internal DebugCommandResult Items(string filter)
    {
        StringBuilder text = new();
        foreach (DaggerfallItemTemplateDefinition template in Templates()
            .Where(template => template.Name.Contains(filter ?? "", StringComparison.OrdinalIgnoreCase)))
            text.AppendLine($"{template.Index} {template.Name} ({template.Groups[0]})");
        text.AppendLine($"Weapon materials: {string.Join(", ", DaggerfallItemMaterials.WeaponMaterials)}; armour also leather, chain.");
        text.Append($"Skills: {string.Join(", ", skillUses.Skills)}.");
        return DebugCommandResult.Success(text.ToString());
    }

    // The templates a developer grant can make: the ordinary item templates with a category the factory creates.
    private IEnumerable<DaggerfallItemTemplateDefinition> Templates() =>
        definitions.ItemTemplateCatalog.Templates.Values.Where(template => template.Groups.Count > 0).OrderBy(template => template.Index);

    // Resolves "Steel Dagger", "dagger", "ebony dagger" or "113": an optional material, then a template's name or index.
    private string? ResolveItemTemplate(string requested, out DaggerfallItemTemplateDefinition? template, out string? material)
    {
        template = null;
        material = null;
        string text = (requested ?? "").Trim();
        if (text.Length == 0) return "Name an item; player.items lists them.";
        string[] materials = [.. DaggerfallItemMaterials.WeaponMaterials, "leather", "chain"];
        material = materials.FirstOrDefault(name => text.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
        string name = material is null ? text : text[(material.Length + 1)..].Trim();
        DaggerfallItemTemplateDefinition[] all = [.. Templates()];
        DaggerfallItemTemplateDefinition[] found = int.TryParse(name, out int index)
            ? [.. all.Where(candidate => candidate.Index == index)]
            : all.Where(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray() is { Length: > 0 } exact
                ? exact : [.. all.Where(candidate => candidate.Name.Contains(name, StringComparison.OrdinalIgnoreCase))];
        if (found.Length == 0) return $"No item is named '{requested}'; player.items lists them.";
        if (found.Length > 1)
            return $"'{requested}' names {found.Length} items: {string.Join(", ", found.Take(8).Select(candidate => $"{candidate.Index} {candidate.Name}"))}{(found.Length > 8 ? ", ..." : "")}.";
        template = found[0];
        string group = template.Groups[0];
        if (material is not null && !(group == "Weapons" ? DaggerfallItemMaterials.IsWeaponMaterial(material)
            : group == "Armor" && DaggerfallItemMaterials.IsArmorMaterial(material)))
            return $"{template.Name} ({group}) takes no material '{material}'.";
        return null;
    }

    // The items a grant makes: one stack of the count for a stackable item, else one item each.
    private IEnumerable<DaggerfallCreatedItem> CreateItems(DaggerfallItemTemplateDefinition template, string? material, int count)
    {
        DaggerfallItemFactory factory = new(definitions, random);
        string group = template.Groups[0];
        bool appearance = group is "Armor" or "MensClothing" or "WomensClothing";
        DaggerfallItemCreateRequest Request(ulong? quantity) => new(group, $"developer.{++_grants}", DaggerfallItemOwner.Player,
            Quantity: quantity, TemplateIndex: template.Index, Material: material, Level: Math.Max(1, actors.Player.Progression.Level),
            Race: appearance ? character.Identity.RaceId : null,
            Gender: appearance ? character.Identity.Gender.ToString().ToLowerInvariant() : null);
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
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(created.Item.Value));
        if (DaggerfallEncumbrancePolicy.IsGold(definition))
            return currency.ReceiveGold(created.Quantity) ? null : "The gold could not be added.";
        if (!encumbrance.CanCarry(definition, created.Quantity)) return $"{ItemLabel(created)} is more than the player can carry.";
        identity = created.Stackable ? null : uniqueItems.AllocateReference();
        try
        {
            new DaggerfallItemFactory(definitions, random).Materialize(created, inventory, itemInstances,
                created.Stackable ? InventoryStackId.Parse($"daggerfall.developer.{_grants}") : null, identity);
            return null;
        }
        catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
        {
            if (identity is { } rejected) uniqueItems.Remove(rejected);
            identity = null;
            return $"There is no room for {ItemLabel(created)}.";
        }
    }

    private string ItemLabel(DaggerfallCreatedItem created) =>
        DaggerfallItemNames.Label(definitions, definitions.RequireItem(new DaggerfallItemId(created.Item.Value)), created.Metadata);
}
