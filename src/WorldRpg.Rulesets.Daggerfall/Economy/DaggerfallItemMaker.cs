using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using KitItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallItemMakerDraft(string Item, string Name, string[] Settings);
internal sealed record DaggerfallItemMakerItem(string Key, string Name, int Capacity, ulong Quantity);
internal sealed record DaggerfallItemMakerSetting(string Key, string Name, int Cost, string[] Forced);
internal sealed record DaggerfallItemMakerQuote(string Key, DaggerfallItemMakerDraft Draft, int Capacity, int Power, int Gold,
    DaggerfallItemMakerSetting[] Payloads, bool Eligible, string? Reason);
internal sealed record DaggerfallItemMakerResult(bool Accepted, string Outcome, ulong? Item = null, ulong Paid = 0);
internal sealed record DaggerfallItemMakerView(string Revision, string Provider, bool Eligible,
    DaggerfallItemMakerItem[] Items, DaggerfallItemMakerSetting[] Settings, DaggerfallItemMakerDraft Draft, DaggerfallItemMakerQuote? Quote);

/// <summary>
/// The Mages Guild's paid enchanting service: provider and draft policy over the existing enchantment
/// construction and item owners. A guild service, so Economy's, as skill training is.
/// </summary>
internal sealed class DaggerfallItemMaker(DaggerfallDefinitions definitions, DaggerfallNpcRegistry npcs,
    DaggerfallConcreteGuildServiceRuntime guildServices, DaggerfallServiceTransactions services,
    MechanicsInventoryCoordinator inventory, DaggerfallItemInstances instances,
    DaggerfallItemEnchanting enchanting, DaggerfallSoulGems souls, DaggerfallUniqueItemAllocator identities,
    Func<DaggerfallCalendar> calendar)
{
    private DaggerfallItemMakerDraft? _draft;
    private long _revision;
    internal DaggerfallItemMakerDraft Draft => _draft ?? new("", "", []);
    internal string Revision => _revision.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal void SetDraft(DaggerfallItemMakerDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Item is null || draft.Name is null || draft.Name.Length > 64 || draft.Settings is null || draft.Settings.Length > 10
            || draft.Settings.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Select an item, a name, and up to ten enchantments.");
        _draft = draft with { Settings = [.. draft.Settings] };
        _revision = checked(_revision + 1);
    }

    internal bool CanUse(DaggerfallServiceProvider provider) => Guild(provider) is { } guild
        && guildServices.Evaluate(guild.FactionId, DaggerfallConcreteGuildService.MakeMagicItems,
            checked((int)calendar().DayNumber), new(provider)).CanUse;

    private DaggerfallConcreteGuildDefinition? Guild(DaggerfallServiceProvider provider)
    {
        DaggerfallNpc npc;
        try { npc = npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return null; }
        return DaggerfallConcreteGuildCatalog.All.FirstOrDefault(guild => guild.TryGetService(DaggerfallConcreteGuildService.MakeMagicItems, out var service)
            && service.ProviderFactionId == npc.Appearance.FactionId);
    }

    internal DaggerfallItemMakerItem[] Items()
    {
        var carried = inventory.Read(); List<DaggerfallItemMakerItem> items = [];
        foreach (var item in carried.UniqueItems)
        {
            ulong id = inventory.GetDurableItemId(item.Entity).Value;
            Add("unique:" + id, instances.RequireUnique(id), 1);
        }
        foreach (var item in carried.Stacks) Add("stack:" + item.Id.Value, instances.RequireStack(DaggerfallItemOwner.Player, item.Id), item.Quantity);
        return [.. items.OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Key, StringComparer.Ordinal)];
        void Add(string key, DaggerfallItemInstanceMetadata metadata, ulong quantity)
        {
            var definition = definitions.RequireItem(new(metadata.ItemId));
            if (DaggerfallEnchantmentConstruction.IsEligible(definition, metadata))
                items.Add(new(key, definition.Template!.Name, DaggerfallMagicCostPolicy.ItemEnchantmentPower(definition, metadata), quantity));
        }
    }

    internal DaggerfallItemMakerSetting[] Settings()
    {
        var selected = Selection(Draft.Item);
        bool weapon = selected is { } choice && definitions.RequireItem(new(choice.Metadata.ItemId)).Weapon is not null;
        return [.. definitions.Magic.EnchantmentSettings.Values
            .Where(setting => (weapon || setting.Type is not (2 or 4 or 20)) && (setting.Type != 15 || souls.HasSoul(setting.Param)))
            .OrderByDescending(setting => setting.Cost > 0).ThenBy(setting => setting.DisplayName, StringComparer.Ordinal)
            .Select(Setting)];
    }

    private DaggerfallItemMakerSetting Setting(DaggerfallEnchantmentSetting value) => new(value.Key, value.DisplayName, value.Cost,
        [.. (value.ForcedSettings ?? []).Select(key => definitions.Magic.EnchantmentSettings[key].DisplayName)]);

    internal DaggerfallItemMakerQuote? Quote(DaggerfallServiceProvider provider)
    {
        if (_draft is null) return null;
        DaggerfallItemMakerQuote Refused(string reason) => new(Revision, Draft, 0, 0, 0, [], false, reason);
        if (!CanUse(provider)) return Refused("ProviderUnavailable");
        if (Selection(_draft.Item) is not { } item) return Refused("ItemUnavailable");
        try
        {
            var quote = DaggerfallEnchantmentConstruction.Quote(definitions, item.Metadata, _draft.Name, _draft.Settings);
            var settings = quote.Enchantment.Settings.Select(value => definitions.Magic.EnchantmentSettings[value.Key]).ToArray();
            if (settings.Any(value => value.Type == 15 && !souls.HasSoul(value.Param))) return Refused("SoulUnavailable");
            return new(Revision, Draft, quote.Capacity, quote.Power, quote.Gold, [.. settings.Select(Setting)], true, null);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { return Refused(DaggerfallRejectionText.For(exception)); }
    }

    internal DaggerfallItemMakerResult Buy(DaggerfallServiceProvider provider, string revision, ulong price, bool confirm)
    {
        if (!confirm) return new(false, "ConfirmationRequired");
        if (revision != Revision) return new(false, "DraftChanged");
        var preview = Quote(provider);
        if (preview is not { Eligible: true }) return new(false, preview?.Reason ?? "InvalidSettings");
        if ((ulong)preview.Gold != price) return new(false, "PriceChanged");
        var selected = Selection(Draft.Item)!;
        var made = DaggerfallEnchantmentConstruction.Quote(definitions, selected.Metadata, Draft.Name, Draft.Settings).Enchantment;
        var guild = Guild(provider)!;
        guild.TryGetService(DaggerfallConcreteGuildService.MakeMagicItems, out var policy);
        var request = new DaggerfallServiceRequest($"item-maker-{provider.NpcId}-{Revision}", provider);
        var eligibility = new DaggerfallServiceEligibility(policy.RequiresMembership, policy.MinimumRank ?? 0, guild.FactionId);
        var payment = services.Quote(request, eligibility, new(price));
        if (payment.Quote is null) return new(false, payment.Outcome.Denial.ToString());
        DaggerfallServiceOutcome paid;
        ulong itemId;
        if (selected.Unique is KitItem unique)
        {
            paid = DaggerfallServiceOutcome.Refused(DaggerfallServiceDenial.GrantUnavailable);
            var result = enchanting.TryEnchantMade(unique, made, changes =>
            {
                paid = services.Commit(payment.Quote, changes);
                return paid.Accepted;
            });
            if (result is null) return new(false, paid.Denial.ToString());
            itemId = selected.Id!.Value;
        }
        else
        {
            // One gem becomes an individual. Its remaining stack keeps exactly its original meaning.
            var definition = definitions.RequireItem(new(DaggerfallTemplateItemDefinitions.MadeGemDefinition(selected.Metadata.ItemId)));
            var metadata = selected.Metadata with { ItemId = definition.Id.Value };
            var identity = identities.AllocateReference();
            var weight = DaggerfallEnchantmentConstruction.EnchantedWeight(definitions, metadata, made);
            var grant = new DaggerfallServiceGrant(new(new(definition.Id.Value), UniqueItem: identity,
                CapacityCosts: DaggerfallEncumbrancePolicy.CapacityOverride(weight)), metadata);
            var quoted = services.Quote(request, eligibility, new(price), [grant]);
            if (quoted.Quote is null) { identities.Remove(identity); return new(false, quoted.Outcome.Denial.ToString()); }
            paid = services.Commit(quoted.Quote, edit => edit.Consume(inventory.Component.Owner, selected.Stack!, 1));
            if (!paid.Accepted) { identities.Remove(identity); return new(false, paid.Denial.ToString()); }
            if (!inventory.Read().Stacks.Any(value => value.Id == selected.Stack)) instances.RemoveStack(DaggerfallItemOwner.Player, selected.Stack!);
            var entity = inventory.Entities.Resolve(identity);
            // Definition, soul, weight, and settings were admitted before the paid candidate;
            // the canonical mutation now performs its source callbacks and equipment notification.
            enchanting.CompleteGrantedEnchantment(new(entity.Value, new(definition.Id.Value)), made);
            itemId = identity.Value;
        }
        _draft = null; _revision = checked(_revision + 1);
        return new(true, "Enchanted", itemId, paid.PaidGold);
    }

    private sealed record SelectedItem(DaggerfallItemInstanceMetadata Metadata, KitItem? Unique, InventoryStackId? Stack, ulong? Id);
    private SelectedItem? Selection(string key)
    {
        if (key.StartsWith("unique:", StringComparison.Ordinal) && ulong.TryParse(key[7..], out ulong id))
        {
            var item = inventory.Read().UniqueItems.FirstOrDefault(value => inventory.GetDurableItemId(value.Entity).Value == id);
            if (item.Entity.Value != 0) return new(instances.RequireUnique(id), new(item.Entity.Value, new(item.Definition.Value)), null, id);
        }
        if (key.StartsWith("stack:", StringComparison.Ordinal))
        {
            var stack = inventory.Read().Stacks.FirstOrDefault(value => value.Id.Value == key[6..]);
            if (stack.Quantity > 0) return new(instances.RequireStack(DaggerfallItemOwner.Player, stack.Id), null, stack.Id, null);
        }
        return null;
    }
}
