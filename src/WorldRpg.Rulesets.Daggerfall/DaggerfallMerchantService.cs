using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

using EngineUniqueInventoryItem = Rusty.Engine.Mechanics.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One row projected from an Engine-backed merchant or player container.</summary>
/// <remarks>
/// <see cref="RepairCost"/> and <see cref="IdentifyCost"/> are the provider's current quote for a
/// player row this provider would repair or identify, and null where the action would be refused.
/// </remarks>
internal sealed record DaggerfallMerchantItemView(
    string Key,
    string Definition,
    string Label,
    ulong Quantity,
    ulong UnitPrice,
    int CurrentCondition,
    int MaximumCondition,
    bool Identified,
    bool Stolen,
    bool CanBuy,
    bool CanSell,
    ulong? RepairCost = null,
    ulong? IdentifyCost = null);

internal sealed record DaggerfallRepairView(
    string RequestId,
    ulong DurableItemId,
    string Definition,
    long DueMinute,
    bool Ready,
    string Label = "",
    string Status = "");

/// <summary>Provider-facing merchant projection. Contents and quantities are read from Kit owners.</summary>
internal sealed record DaggerfallMerchantView(
    string Revision,
    string Provider,
    int Quality,
    ulong PlayerGold,
    bool CanBuy,
    bool CanSell,
    bool CanRepair,
    bool CanIdentify,
    IReadOnlyList<DaggerfallMerchantItemView> Stock,
    IReadOnlyList<DaggerfallMerchantItemView> PlayerItems,
    IReadOnlyList<DaggerfallRepairView> Repairs,
    string Result = "", bool CanShoplift = true);

internal sealed record DaggerfallMerchantResult(bool Accepted, string Outcome, ulong PaidGold = 0);

/// <summary>Provider context resolved by the live dialogue/site owner.</summary>
internal sealed record DaggerfallMerchantProviderContext(
    DaggerfallServiceProvider Provider,
    int Quality,
    int BuildingType,
    int BlockX,
    int BlockY,
    int BuildingIndex)
{
    internal string Key => $"{Provider.NpcId}:{Provider.Site.Region}:{Provider.Site.Location}:{Provider.Site.Building}:{BlockX}:{BlockY}:{BuildingIndex}";
}

internal sealed record DaggerfallMerchantRepairSave(
    string RequestId,
    ulong DurableItemId,
    string Definition,
    long DueMinute,
    ulong PaidGold);

/// <summary>Persisted merchant-owned and repair-custody containers for one provider.</summary>
internal sealed record DaggerfallMerchantSave(
    string Key,
    long ProviderNpcId,
    int ProviderRegion,
    string ProviderLocation,
    string ProviderBuilding,
    string Service,
    int BuildingType,
    int BlockX,
    int BlockY,
    int BuildingIndex,
    int Quality,
    long MerchantContainerId,
    long CustodyContainerId,
    long StockedDay,
    DaggerfallInventorySave Inventory,
    DaggerfallInventorySave Custody,
    string[] GeneratedStacks,
    ulong[] GeneratedUniqueItems,
    DaggerfallMerchantRepairSave[] Repairs)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        if (ProviderNpcId <= 0 || ProviderRegion is < 0 or > 61 || string.IsNullOrWhiteSpace(ProviderLocation)
            || BuildingType is < 0 or > 255 || BlockX is < 0 or > 255 || BlockY is < 0 or > 255 || BuildingIndex < 0
            || Quality is < DaggerfallRegionalEconomyPolicy.MinimumShopQuality or > DaggerfallRegionalEconomyPolicy.MaximumShopQuality
            || MerchantContainerId <= 0 || CustodyContainerId <= 0 || MerchantContainerId == CustodyContainerId || StockedDay < 0)
            throw new ArgumentException("Saved merchant provider identity or stock state is invalid.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Service);
        ArgumentNullException.ThrowIfNull(Inventory);
        ArgumentNullException.ThrowIfNull(Custody);
        Inventory.Validate();
        Custody.Validate();
        ArgumentNullException.ThrowIfNull(GeneratedStacks);
        ArgumentNullException.ThrowIfNull(GeneratedUniqueItems);
        ArgumentNullException.ThrowIfNull(Repairs);
        if (GeneratedStacks.Any(string.IsNullOrWhiteSpace) || GeneratedStacks.Distinct(StringComparer.Ordinal).Count() != GeneratedStacks.Length
            || GeneratedUniqueItems.Any(value => value == 0) || GeneratedUniqueItems.Distinct().Count() != GeneratedUniqueItems.Length)
            throw new ArgumentException("Saved merchant generated identities must be explicit and unique.");
        if (GeneratedStacks.Any(id => !Inventory.Stacks.Any(stack => stack.StackId == id))
            || GeneratedUniqueItems.Any(id => !Inventory.UniqueItems.Any(item => item.EntityId == id)))
            throw new ArgumentException("Saved merchant generated identities must still be present in merchant stock.");
        HashSet<string> requests = new(StringComparer.Ordinal);
        foreach (DaggerfallMerchantRepairSave repair in Repairs)
        {
            ArgumentNullException.ThrowIfNull(repair);
            ArgumentException.ThrowIfNullOrWhiteSpace(repair.RequestId);
            ArgumentException.ThrowIfNullOrWhiteSpace(repair.Definition);
            if (repair.DurableItemId == 0 || repair.DueMinute < 0 || !requests.Add(repair.RequestId)
                || !Custody.UniqueItems.Any(item => item.EntityId == repair.DurableItemId && item.ItemId == repair.Definition))
                throw new ArgumentException("Saved repair orders must have distinct positive identities and due times.");
        }
    }
}

/// <summary>
/// Daggerfall's merchant provider owner. Inventory remains in the Engine-backed Kit containers;
/// this service owns only stock policy, quote callers, provider custody, and item meaning changes.
/// </summary>
internal sealed class DaggerfallMerchantService
{
    private const string MerchantContainerTypeName = "daggerfall.merchant-container";
    private const string RepairContainerTypeName = "daggerfall.repair-custody";
    private const int OpenHour = 6;
    private const int CloseHour = 18;
    private const long RandomSeed = 0;
    private const string RandomScope = "daggerfall.merchant-stock.v1";

    // The source's DaggerfallLootDataTables itemGroups* pairs: donor group id, chance modifier.
    private static readonly IReadOnlyDictionary<int, (string Category, int Chance)[]> StockPools =
        new Dictionary<int, (string, int)[]>
        {
            [0] = [("Gems", 0x1E), ("PlantIngredients1", 0x32), ("PlantIngredients2", 0x32),
                ("CreatureIngredients1", 0x1E), ("CreatureIngredients2", 0x14), ("CreatureIngredients3", 0x14),
                ("MiscellaneousIngredients1", 0x3C), ("MetalIngredients", 0x28), ("MiscellaneousIngredients2", 0x1E)],
            [2] = [("Armor", 0x50), ("Weapons", 0x14)],
            [5] = [("Books", 0x28), ("Maps", 0x05)],
            [6] = [("MensClothing", 0x32), ("WomensClothing", 0x32)],
            [8] = [("Gems", 0x28), ("Jewellery", 0x32)],
            [9] = [("Weapons", 0x14), ("MensClothing", 0x0A), ("Books", 0x0A), ("UselessItems2", 0x32),
                ("Transportation", 0x00), ("WomensClothing", 0x0A), ("MagicItems", 0x00)],
            [12] = [("Armor", 0x0A), ("Weapons", 0x0A), ("MagicItems", 0x0A), ("Books", 0x0A),
                ("UselessItems2", 0x14), ("Paintings", 0x05), ("Gems", 0x0A), ("Jewellery", 0x0A),
                ("UselessItems1", 0x0A)],
            [13] = [("Armor", 0x1E), ("Weapons", 0x46)],
            [7] = [("Furniture", 0x14)],
        };

    private readonly DaggerfallDefinitions _definitions;
    private readonly IRandomService _random;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallSocialState _social;
    private readonly StatsComponent _playerStats;
    private readonly DaggerfallCharacterState _character;
    private readonly ProgressionState _progression;
    private readonly MechanicsInventoryCoordinator _inventory;
    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly DaggerfallItemInstances _instances;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly DurableIdentityAllocator _identities;
    private readonly DaggerfallCurrencyService _currency;
    private readonly DaggerfallServiceTransactions _services;
    private readonly DaggerfallConcreteGuildServiceRuntime _concreteGuildServices;
    private readonly DaggerfallTradeQuoteService _tradeQuotes;
    private readonly DaggerfallRegionalPriceState _regionalPrices;
    private readonly DaggerfallItemConditionService _conditions;
    private readonly DaggerfallSkillUseReactions _skillUses;
    private readonly DaggerfallCrimeState _crime;
    private readonly Func<DaggerfallCrimeWitnessEvidence> _crimeWitnesses;
    private readonly Func<DaggerfallCalendar> _calendar;
    private readonly Func<DaggerfallNpcSite?> _currentSite;
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);
    private long _nextRequest;

    private sealed class Binding
    {
        internal required DaggerfallMerchantProviderContext Context;
        internal required long MerchantContainerId;
        internal required long CustodyContainerId;
        internal required EntityId MerchantOwner;
        internal required EntityId CustodyOwner;
        internal long StockedDay;
        internal string? PotionVisit;
        internal long PotionStockRoll;
        internal HashSet<string> GeneratedStacks { get; } = new(StringComparer.Ordinal);
        internal HashSet<ulong> GeneratedUniqueItems { get; } = [];
        internal Dictionary<string, DaggerfallMerchantRepairSave> Repairs { get; } = new(StringComparer.Ordinal);
    }

    internal DaggerfallMerchantService(
        DaggerfallDefinitions definitions,
        IRandomService random,
        DaggerfallNpcRegistry npcs,
        DaggerfallSocialState social,
        StatsComponent playerStats,
        DaggerfallCharacterState character,
        ProgressionState progression,
        MechanicsInventoryCoordinator inventory,
        MechanicsInventoryContainerCoordinator containers,
        DaggerfallItemInstances instances,
        DaggerfallUniqueItemAllocator uniqueItems,
        DurableIdentityAllocator identities,
        DaggerfallCurrencyService currency,
        DaggerfallServiceTransactions services,
        DaggerfallConcreteGuildServiceRuntime concreteGuildServices,
        DaggerfallTradeQuoteService tradeQuotes,
        DaggerfallRegionalPriceState regionalPrices,
        DaggerfallItemConditionService conditions,
        DaggerfallSkillUseReactions skillUses,
        DaggerfallCrimeState crime,
        Func<DaggerfallCalendar> calendar,
        Func<DaggerfallNpcSite?> currentSite,
        IEnumerable<DaggerfallMerchantSave>? restored = null,
        Func<DaggerfallCrimeWitnessEvidence>? crimeWitnesses = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _playerStats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _uniqueItems = uniqueItems ?? throw new ArgumentNullException(nameof(uniqueItems));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _concreteGuildServices = concreteGuildServices ?? throw new ArgumentNullException(nameof(concreteGuildServices));
        _tradeQuotes = tradeQuotes ?? throw new ArgumentNullException(nameof(tradeQuotes));
        _regionalPrices = regionalPrices ?? throw new ArgumentNullException(nameof(regionalPrices));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _skillUses = skillUses ?? throw new ArgumentNullException(nameof(skillUses));
        _crime = crime ?? throw new ArgumentNullException(nameof(crime));
        _crimeWitnesses = crimeWitnesses ?? (() => DaggerfallCrimeWitnessEvidence.NotQueried);
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _currentSite = currentSite ?? throw new ArgumentNullException(nameof(currentSite));
        foreach (DaggerfallMerchantSave save in restored ?? []) MaterializeSaved(save);
    }

    internal IReadOnlyList<DaggerfallMerchantSave> Capture() => _bindings.Values.OrderBy(value => value.Context.Key, StringComparer.Ordinal)
        .Select(value => Capture(value)).ToArray();

    internal DaggerfallMerchantView Read(DaggerfallMerchantProviderContext context, string result = "", string? dialogueRevision = null)
    {
        Binding binding = Ensure(context);
        if (context.Provider.Service == "buy-potions" && dialogueRevision is not null && binding.PotionVisit != dialogueRevision)
        {
            binding.PotionVisit = dialogueRevision;
            binding.PotionStockRoll = checked(++_nextRequest);
            Restock(binding);
        }
        return Project(binding, result, dialogueRevision);
    }

    internal DaggerfallMerchantResult Buy(DaggerfallMerchantProviderContext context, string revision, string itemKey, ulong quantity)
    {
        var potionGuild = context.Provider.Service == "buy-potions" ? ConcreteGuildProvider(context, DaggerfallConcreteGuildService.BuyPotions) : null;
        if (context.Provider.Service == "buy-potions" && potionGuild is not { CanUse: true }) return Refused("ProviderUnavailable");
        Binding binding = Ensure(context);
        if (!RevisionMatches(binding, revision)) return Refused("Stale");
        DaggerfallItemOwner merchantOwner = DaggerfallItemOwner.Merchant(binding.MerchantContainerId);
        if (!TrySelection(merchantOwner, itemKey, quantity, out InventoryContainerSelection selection, out DaggerfallTradeLine line))
            return Refused("ItemUnavailable");
        DaggerfallTradeQuote? trade = Quote(binding, DaggerfallTradeSide.BuyFromMerchant, line);
        if (trade is null) return Refused("QuoteUnavailable");
        DaggerfallServiceRequest request = new(RequestId(binding, "buy", itemKey, revision), binding.Context.Provider);
        DaggerfallServiceEligibility eligibility = OpenEligibility;
        if (potionGuild is not null)
        {
            potionGuild.Definition.TryGetService(DaggerfallConcreteGuildService.BuyPotions, out var service);
            eligibility = new(service.RequiresMembership, service.MinimumRank ?? 0, potionGuild.Definition.FactionId);
        }
        DaggerfallServiceQuoteResult quote = _services.Quote(request, eligibility, new(checked((ulong)Math.Max(0, trade.Total))));
        if (quote.Quote is null) return Refused(quote.Outcome.Denial.ToString());
        InventoryContainerSelection prepared = PrepareDestination(selection, merchantOwner, DaggerfallItemOwner.Player,
            _inventory.Read().Stacks, binding);
        DaggerfallServiceOutcome payment = _services.CommitTransfer(quote.Quote, binding.MerchantOwner, _inventory.Component.Owner,
            prepared, DaggerfallItemOwner.Merchant(binding.MerchantContainerId), DaggerfallItemOwner.Player, playerPays: true);
        if (!payment.Accepted) return Refused(payment.Denial.ToString());
        RetireGeneratedSelection(binding, selection);
        RecordTradeSkill();
        return new(true, "Purchased", payment.PaidGold);
    }

    internal DaggerfallMerchantResult Sell(DaggerfallMerchantProviderContext context, string revision, string itemKey, ulong quantity)
    {
        Binding binding = Ensure(context);
        if (!RevisionMatches(binding, revision)) return Refused("Stale");
        if (!TrySelection(DaggerfallItemOwner.Player, itemKey, quantity, out InventoryContainerSelection selection, out DaggerfallTradeLine line))
            return Refused("ItemUnavailable");
        if (!CanSellToBuilding(binding.Context.BuildingType, line.Metadata)) return Refused("ItemNotAccepted");
        DaggerfallTradeQuote? trade = Quote(binding, DaggerfallTradeSide.SellToMerchant, line);
        if (trade is null) return Refused("QuoteUnavailable");
        DaggerfallServiceItemReference item = line.Metadata.Owner == DaggerfallItemOwner.Player && selection.UniqueEntityId is ulong uniqueEntity
            ? new(_containers.GetDurableItemId(new EntityId(uniqueEntity)).Value, line.Definition.Id.Value, line.Metadata.CurrentCondition)
            : new(0, line.Definition.Id.Value, line.Metadata.CurrentCondition, selection.Stack!.Value, quantity);
        DaggerfallServiceRequest request = new(RequestId(binding, "sell", itemKey, revision), binding.Context.Provider, item);
        DaggerfallServiceQuoteResult quote = _services.Quote(request, OpenEligibility, new(checked((ulong)Math.Max(0, trade.Total))));
        if (quote.Quote is null) return Refused(quote.Outcome.Denial.ToString());
        InventoryContainerSelection prepared = PrepareSaleDestination(selection, binding);
        DaggerfallServiceOutcome payment = _services.CommitTransfer(quote.Quote, _inventory.Component.Owner, binding.MerchantOwner,
            prepared, DaggerfallItemOwner.Player, DaggerfallItemOwner.Merchant(binding.MerchantContainerId), playerPays: false);
        if (!payment.Accepted) return Refused(payment.Denial.ToString());
        RecordTradeSkill();
        return new(true, "Sold", payment.PaidGold);
    }

    internal DaggerfallMerchantResult RequestRepair(DaggerfallMerchantProviderContext context, string revision, string itemKey)
    {
        Binding binding = Ensure(context);
        if (!RevisionMatches(binding, revision)) return Refused("Stale");
        if (!TrySelection(DaggerfallItemOwner.Player, itemKey, 1, out InventoryContainerSelection selection, out DaggerfallTradeLine line)
            || selection.UniqueEntityId is not ulong itemEntity || line.Metadata.MaximumCondition == 0)
            return Refused("ItemUnavailable");
        ulong itemId = _containers.GetDurableItemId(new EntityId(itemEntity)).Value;
        if (line.Metadata.CurrentCondition == line.Metadata.MaximumCondition) return Refused("AlreadyRepaired");
        DaggerfallConcreteGuildServiceRuntimeDecision? guild = ConcreteGuildProvider(context, DaggerfallConcreteGuildService.Repair);
        // Generic Armorer, GeneralStore, and WeaponSmith providers use this same transaction
        // contract without Fighters membership. Guild repair remains the richer path below, with
        // its canonical membership gate and rank-based price reduction.
        if (guild is null && !IsGenericRepairProvider(context)) return Refused("ProviderUnavailable");
        if (guild is { CanUse: false }) return Refused(GuildRefusal(guild));
        int cost = RepairCost(binding, line.Definition, line.Metadata, guild);
        long due = checked(CurrentMinute() + Math.Max(1, (DaggerfallRegionalEconomyPolicy.CalculateItemRepairTime(
            line.Metadata.CurrentCondition, line.Metadata.MaximumCondition) + DaggerfallCalendar.SecondsPerMinute - 1) / DaggerfallCalendar.SecondsPerMinute));
        string requestId = RequestId(binding, "repair", itemKey, revision);
        DaggerfallServiceRequest request = new(requestId, binding.Context.Provider,
            new(itemId, line.Definition.Id.Value, line.Metadata.CurrentCondition));
        DaggerfallServiceQueuedWork work = new(requestId, binding.Context.Provider.Service, binding.Context.Provider, due);
        DaggerfallServiceQuoteResult quote = _services.Quote(request, OpenEligibility, new(checked((ulong)Math.Max(0, cost))), queuedWork: work);
        if (quote.Quote is null) return Refused(quote.Outcome.Denial.ToString());
        InventoryContainerSelection transfer = selection;
        DaggerfallServiceOutcome accepted = _services.CommitTransfer(quote.Quote, _inventory.Component.Owner, binding.CustodyOwner,
            transfer, DaggerfallItemOwner.Player, DaggerfallItemOwner.RepairCustody(binding.CustodyContainerId), playerPays: true);
        if (!accepted.Accepted) return Refused(accepted.Denial.ToString());
        binding.Repairs[requestId] = new(requestId, itemId, line.Definition.Id.Value, due, accepted.PaidGold);
        RecordTradeSkill();
        return new(true, "RepairAccepted", accepted.PaidGold);
    }

    internal DaggerfallMerchantResult CollectRepair(DaggerfallMerchantProviderContext context, string revision, string requestId)
    {
        Binding binding = Ensure(context);
        if (!RevisionMatches(binding, revision)) return Refused("Stale");
        if (string.IsNullOrWhiteSpace(requestId) || !binding.Repairs.TryGetValue(requestId, out DaggerfallMerchantRepairSave? order))
            return Refused("RepairUnavailable");
        DaggerfallServiceQueuedWork? pending = _services.Pending.SingleOrDefault(value => value.Id == requestId);
        if (pending is null) return Refused("RepairUnavailable");
        if (CurrentMinute() < pending.CompletesAtMinute) return Refused("RepairNotReady");
        if (_services.ProviderAvailable(pending.Provider) != DaggerfallServiceDenial.None) return Refused("ProviderUnavailable");
        DaggerfallItemInstanceMetadata metadata = _instances.RequireUnique(order.DurableItemId);
        if (metadata.Owner != DaggerfallItemOwner.RepairCustody(binding.CustodyContainerId)) return Refused("RepairUnavailable");
        _ = _conditions.Repair(order.DurableItemId, metadata.Owner);
        EntityId itemEntity;
        try { itemEntity = _containers.Entities.Resolve(new(DurableIdentityKind.Item, order.DurableItemId)); }
        catch (KeyNotFoundException) { return Refused("RepairUnavailable"); }
        InventoryContainerSelection selection = new(new(order.Definition), 1, UniqueEntityId: itemEntity.Value);
        InventoryContainerTransferReceipt transfer = _containers.Transfer(binding.CustodyOwner, _inventory.Component.Owner, selection);
        SyncTransfer(transfer, DaggerfallItemOwner.RepairCustody(binding.CustodyContainerId), DaggerfallItemOwner.Player);
        DaggerfallServiceOutcome completed = _services.CompletePending(requestId);
        if (!completed.Accepted) return Refused(completed.Denial.ToString());
        binding.Repairs.Remove(requestId);
        return new(true, "RepairCollected");
    }

    internal DaggerfallMerchantResult Identify(DaggerfallMerchantProviderContext context, string revision, string itemKey)
    {
        Binding binding = Ensure(context);
        if (!RevisionMatches(binding, revision)) return Refused("Stale");
        if (!TrySelection(DaggerfallItemOwner.Player, itemKey, 1, out InventoryContainerSelection selection, out DaggerfallTradeLine line))
            return Refused("ItemUnavailable");
        if (selection.UniqueEntityId is not ulong itemEntity || !line.Metadata.HasEnchantment || line.Metadata.Identified)
            return Refused("AlreadyIdentified");
        ulong itemId = _containers.GetDurableItemId(new EntityId(itemEntity)).Value;
        DaggerfallConcreteGuildServiceRuntimeDecision? guild = ConcreteGuildProvider(context, DaggerfallConcreteGuildService.Identify);
        if (guild is null) return Refused("ProviderUnavailable");
        if (guild is { CanUse: false }) return Refused(GuildRefusal(guild));
        int cost = IdentifyCost(binding, line.Definition, line.Metadata);
        string requestId = RequestId(binding, "identify", itemKey, revision);
        DaggerfallServiceRequest request = new(requestId, binding.Context.Provider,
            new(itemId, line.Definition.Id.Value, line.Metadata.CurrentCondition));
        DaggerfallServiceQuoteResult quote = _services.Quote(request, OpenEligibility, new(checked((ulong)Math.Max(0, cost))));
        if (quote.Quote is null) return Refused(quote.Outcome.Denial.ToString());
        DaggerfallServiceOutcome paid = _services.Commit(quote.Quote);
        if (!paid.Accepted) return Refused(paid.Denial.ToString());
        _ = _conditions.Identify(new WorldRpg.Kit.Inventory.UniqueInventoryItem(itemEntity, new InventoryItemId(line.Definition.Id.Value)));
        RecordTradeSkill();
        return new(true, "Identified", paid.PaidGold);
    }

    internal DaggerfallMerchantResult Shoplift(DaggerfallMerchantProviderContext context, string revision, string itemKey, ulong quantity)
    {
        if (context.Provider.Service == "buy-potions") return Refused("ServiceUnavailable");
        Binding binding = Ensure(context);
        if (!RevisionMatches(binding, revision)) return Refused("Stale");
        DaggerfallItemOwner merchantOwner = DaggerfallItemOwner.Merchant(binding.MerchantContainerId);
        if (!TrySelection(merchantOwner, itemKey, quantity, out InventoryContainerSelection selection, out DaggerfallTradeLine line))
            return Refused("ItemUnavailable");
        // DaggerfallTradeWindow only attempts a theft when the current buy-mode cost is
        // positive.  Recompute that quote for the selected quantity before consuming the
        // pickpocket attempt or moving any property; a zero quote is not a stealable item.
        DaggerfallTradeQuote? trade = Quote(binding, DaggerfallTradeSide.BuyFromMerchant, line);
        if (trade is null || trade.Total <= 0) return Refused("QuoteUnavailable");
        string operation = RequestId(binding, "shoplift", itemKey, revision);
        if (_crime.HasAttempt(operation)) return Refused("AlreadyAttempted");
        int pickpocket = Math.Clamp(_playerStats.GetStat(StatId.Parse("pickpocket")).ValueInt, 0, 100);
        int chance = DaggerfallCrimePolicy.CalculateShopliftingChance(pickpocket, binding.Context.Quality,
            DaggerfallCrimePolicy.TheftWeight(line.Definition) * quantity, 1);
        int roll = checked((int)_random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope,
            $"{binding.Context.Key}:{revision}:{itemKey}", 0, 99)).Value);
        long minute = CurrentMinute();
        _ = _skillUses.Record(new("pickpocket", DaggerfallSkillUseReason.ShopliftingAttempt, DaggerfallSkillUseOutcome.Attempted));
        bool caught = roll < chance;
        DaggerfallCrimeWitnessEvidence witnesses = _crimeWitnesses();
        if (caught)
        {
            _crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Shoplifting, DaggerfallActorIdentity.PlayerEntityId,
                binding.MerchantContainerId, binding.Context.Provider.Site.Region, minute,
                DaggerfallCrimeAttemptOutcome.Failed, witnesses));
            _crime.RecordIncident(new(operation, DaggerfallCrimeKind.Theft, DaggerfallCrimeStage.Attempted,
                DaggerfallActorIdentity.PlayerEntityId, binding.MerchantContainerId, binding.Context.Provider.Site.Region,
                minute, DaggerfallCrimeTargetKind.Other, witnesses, DaggerfallCrimeGuildCredit.None, Reported: true));
            return Refused("Caught");
        }
        InventoryContainerSelection transfer = PrepareDestination(selection, merchantOwner, DaggerfallItemOwner.Player,
            _inventory.Read().Stacks, binding, allowCompatible: false, destinationPrefix: "daggerfall.player.stolen");
        InventoryContainerTransferReceipt receipt;
        try { receipt = _containers.Transfer(binding.MerchantOwner, _inventory.Component.Owner, transfer); }
        catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
        {
            _crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Shoplifting, DaggerfallActorIdentity.PlayerEntityId,
                binding.MerchantContainerId, binding.Context.Provider.Site.Region, minute,
                DaggerfallCrimeAttemptOutcome.Failed, witnesses));
            return Refused("Capacity");
        }
        SyncTransfer(receipt, DaggerfallItemOwner.Merchant(binding.MerchantContainerId), DaggerfallItemOwner.Player);
        RetireGeneratedSelection(binding, selection);
        MarkStolen(transfer, quantity);
        _crime.RecordAttempt(new(operation, DaggerfallCrimeAction.Shoplifting, DaggerfallActorIdentity.PlayerEntityId,
            binding.MerchantContainerId, binding.Context.Provider.Site.Region, minute,
            DaggerfallCrimeAttemptOutcome.PropertyTransferred, witnesses));
        _ = _crime.RecordIncident(new(operation, DaggerfallCrimeKind.Theft, DaggerfallCrimeStage.Completed,
            DaggerfallActorIdentity.PlayerEntityId, binding.MerchantContainerId, binding.Context.Provider.Site.Region, minute,
            DaggerfallCrimeTargetKind.Other, witnesses, DaggerfallCrimeGuildCredit.Thieving));
        return new(true, "Stolen");
    }

    private static DaggerfallMerchantResult Refused(string outcome) => new(false, outcome);

    private const int WitchesFestival = 43;

    private DaggerfallConcreteGuildServiceRuntimeDecision? ConcreteGuildProvider(
        DaggerfallMerchantProviderContext context, DaggerfallConcreteGuildService service)
    {
        DaggerfallNpc npc = _npcs.Require(context.Provider.NpcId);
        DaggerfallConcreteGuildDefinition? guild = DaggerfallConcreteGuildCatalog.All
            .Where(candidate => candidate.TryGetService(service, out DaggerfallConcreteGuildServiceDefinition? definition)
                && definition.ProviderFactionId == npc.Appearance.FactionId)
            .SingleOrDefault();
        return guild is null
            ? null
            : _concreteGuildServices.Evaluate(guild.FactionId, service,
                checked((int)_calendar().DayNumber),
                new DaggerfallConcreteGuildServiceInput(context.Provider, context.Provider.Site.Region));
    }

    private bool IsGenericRepairProvider(DaggerfallMerchantProviderContext context)
    {
        if (!StringComparer.Ordinal.Equals(context.Provider.Service, "repair")
            || !DaggerfallNpcServiceFacts.IsGenericRepairShop(context.BuildingType))
            return false;
        return _npcs.Require(context.Provider.NpcId).Services.Contains("repair", StringComparer.Ordinal);
    }

    /// <summary>The price this provider asks to repair an item, with a Fighters Guild member's rank reduction.</summary>
    private int RepairCost(Binding binding, DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata,
        DaggerfallConcreteGuildServiceRuntimeDecision? guild)
    {
        int cost = DaggerfallRegionalEconomyPolicy.CalculateItemRepairCost(definition.Value, binding.Context.Quality,
            metadata.CurrentCondition, metadata.MaximumCondition, binding.Context.Provider.Site.Region,
            _regionalPrices.AdjustmentForRegion(binding.Context.Provider.Site.Region));
        if (guild is { Definition.FactionId: DaggerfallConcreteGuildCatalog.FightersFactionId, CanUse: true })
            cost = DaggerfallConcreteGuildPolicy.FightersRepairCost(cost, guild.Membership.Rank);
        return cost;
    }

    /// <summary>
    /// The price this provider asks to identify an item. The donor makes identification free during
    /// Witches Festival. Mages Guild does not override ReducedIdentifyCost, so its canonical service
    /// admission changes eligibility and provider identity while leaving the ordinary cost unchanged
    /// outside that holiday.
    /// </summary>
    private int IdentifyCost(Binding binding, DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata) =>
        _calendar().GetHolidayId(binding.Context.Provider.Site.Region) == WitchesFestival
            ? 0 : DaggerfallRegionalEconomyPolicy.CalculateItemIdentifyCost(IdentifySourceValue(definition, metadata));

    private static string GuildRefusal(DaggerfallConcreteGuildServiceRuntimeDecision decision) =>
        !decision.Policy.Eligible ? decision.Policy.Denial.ToString()
        : decision.ProviderDenial != DaggerfallServiceDenial.None ? decision.ProviderDenial.ToString()
        : "ServiceUnavailable";

    private DaggerfallMerchantView Project(Binding binding, string result, string? dialogueRevision)
    {
        InventoryView stock = _containers.Read(binding.MerchantOwner);
        InventoryView player = _inventory.Read();
        DaggerfallNpc npc = _npcs.Require(binding.Context.Provider.NpcId);
        DaggerfallMerchantItemView[] stockRows = Rows(stock, DaggerfallItemOwner.Merchant(binding.MerchantContainerId), binding, buying: true);
        bool repairService = npc.Services.Contains("repair", StringComparer.Ordinal);
        bool identifyService = npc.Services.Contains("identify", StringComparer.Ordinal);
        DaggerfallConcreteGuildServiceRuntimeDecision? repairGuild = repairService ? ConcreteGuildProvider(binding.Context, DaggerfallConcreteGuildService.Repair) : null;
        DaggerfallMerchantItemView[] playerRows = Rows(player, DaggerfallItemOwner.Player, binding, buying: false,
            // A cost is quoted where the matching action would be admitted: a guild repairer the player
            // may use or a generic repair shop, and an identifier whose guild service admits the player.
            quoteRepair: repairService && (repairGuild is { CanUse: true } || repairGuild is null && IsGenericRepairProvider(binding.Context)),
            repairGuild: repairGuild,
            quoteIdentify: identifyService && ConcreteGuildProvider(binding.Context, DaggerfallConcreteGuildService.Identify) is { CanUse: true });
        long now = CurrentMinute();
        DaggerfallRepairView[] repairs = binding.Repairs.Values.OrderBy(value => value.DueMinute).ThenBy(value => value.RequestId, StringComparer.Ordinal)
            .Select(value => new DaggerfallRepairView(value.RequestId, value.DurableItemId, value.Definition, value.DueMinute,
                now >= value.DueMinute, RepairLabel(value), now >= value.DueMinute ? "Ready to collect"
                    : "Ready in " + DaggerfallCalendar.DescribeDuration(checked((value.DueMinute - now) * DaggerfallCalendar.SecondsPerMinute)))).ToArray();
        string merchantRevision = MakeRevision(binding);
        string revision = string.IsNullOrWhiteSpace(dialogueRevision) ? merchantRevision : $"{dialogueRevision}|{merchantRevision}";
        bool canBuy = npc.Services.Any(value => value is "shop" or "merchant" or "buy-items")
            || binding.Context.Provider.Service == "buy-potions" && ConcreteGuildProvider(binding.Context, DaggerfallConcreteGuildService.BuyPotions) is { CanUse: true };
        bool canSell = npc.Services.Any(value => value is "shop" or "merchant" or "sell-items");
        bool canRepair = repairService;
        bool canIdentify = identifyService;
        return new(revision, npc.DisplayName ?? npc.Role, binding.Context.Quality, _currency.Read().Gold,
            canBuy, canSell, canRepair, canIdentify, stockRows, playerRows, repairs, result, binding.Context.Provider.Service != "buy-potions");
    }

    /// <summary>The name a repair order shows: the item in custody, as the merchant rows name items.</summary>
    private string RepairLabel(DaggerfallMerchantRepairSave order)
    {
        DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(order.Definition));
        try { return ItemLabel(definition, _instances.RequireUnique(order.DurableItemId)); }
        catch (InvalidOperationException) { return definition.Template?.Name ?? definition.Id.Value; }
    }

    private DaggerfallMerchantItemView[] Rows(InventoryView inventory, DaggerfallItemOwner owner, Binding binding, bool buying,
        bool quoteRepair = false, DaggerfallConcreteGuildServiceRuntimeDecision? repairGuild = null, bool quoteIdentify = false)
    {
        // Costs are quoted only for the player's rows, and only for services this provider offers.
        ulong? Repair(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata) =>
            quoteRepair && metadata.MaximumCondition > 0 && metadata.CurrentCondition < metadata.MaximumCondition
                ? checked((ulong)Math.Max(0, RepairCost(binding, definition, metadata, repairGuild))) : null;
        ulong? Identify(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata) =>
            quoteIdentify && metadata.HasEnchantment && !metadata.Identified
                ? checked((ulong)Math.Max(0, IdentifyCost(binding, definition, metadata))) : null;
        List<DaggerfallMerchantItemView> rows = [];
        foreach (InventoryStack stack in inventory.Stacks.OrderBy(value => value.Id.Value, StringComparer.Ordinal))
        {
            DaggerfallItemInstanceMetadata metadata = _instances.RequireStack(owner, stack.Id);
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(stack.Definition.Value));
            ulong price = Price(binding, buying ? DaggerfallTradeSide.BuyFromMerchant : DaggerfallTradeSide.SellToMerchant,
                // The projection field is UnitPrice. The caller supplies its selected quantity to
                // Quote during the action, so a stack row must price one item here rather than
                // displaying the total for the complete stock beside a one-item control.
                new(definition, metadata, 1));
            rows.Add(new("stack:" + stack.Id.Value, definition.Id.Value, ItemLabel(definition, metadata), stack.Quantity, price,
                metadata.CurrentCondition, metadata.MaximumCondition, metadata.Identified, metadata.Stolen,
                buying, !buying && CanSellToBuilding(binding.Context.BuildingType, metadata)));
        }
        foreach (EngineUniqueInventoryItem item in inventory.UniqueItems.OrderBy(value => value.Entity.Value))
        {
            ulong id = _containers.GetDurableItemId(item.Entity).Value;
            DaggerfallItemInstanceMetadata metadata = _instances.RequireUnique(id);
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(item.Definition.Value));
            ulong price = Price(binding, buying ? DaggerfallTradeSide.BuyFromMerchant : DaggerfallTradeSide.SellToMerchant,
                new(definition, metadata, 1));
            rows.Add(new("unique:" + id, definition.Id.Value, ItemLabel(definition, metadata), 1, price,
                metadata.CurrentCondition, metadata.MaximumCondition, metadata.Identified, metadata.Stolen,
                buying, !buying && CanSellToBuilding(binding.Context.BuildingType, metadata),
                Repair(definition, metadata), Identify(definition, metadata)));
        }
        return [.. rows];
    }

    private string ItemLabel(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata) =>
        metadata.PotionRecipeKey is int recipe ? $"{(definition.Template?.Index == 278 ? "Recipe" : "Potion of")} {_definitions.Magic.PotionRecipes[recipe].Name}"
            : definition.Template?.Name ?? definition.Id.Value;

    private DaggerfallTradeQuote? Quote(Binding binding, DaggerfallTradeSide side, DaggerfallTradeLine line)
    {
        DaggerfallNpc npc = _npcs.Require(binding.Context.Provider.NpcId);
        DaggerfallFactionReaction reaction = _social.ReactionForNpc(npc);
        DaggerfallTradeQuoteResult quote = _tradeQuotes.Quote(side, [line], binding.Context.Quality,
            Math.Clamp(_playerStats.GetStat(StatId.Parse("mercantile")).ValueInt, 0, 100),
            Math.Clamp(_playerStats.GetStat(StatId.Parse("personality")).ValueInt, 0, 100), reaction.Value,
            binding.Context.Provider.Site.Region);
        return quote.Quote;
    }

    private ulong Price(Binding binding, DaggerfallTradeSide side, DaggerfallTradeLine line) =>
        Quote(binding, side, line) is { Total: >= 0 } quote ? checked((ulong)quote.Total) : 0;

    private bool CanSellToBuilding(int buildingType, DaggerfallItemInstanceMetadata metadata)
    {
        // Pawn shops are the admitted fence for stolen goods; weapon smiths retain their
        // ordinary armor/weapon buying policy and do not launder stolen provenance.
        if (metadata.Stolen && buildingType != 12) return false;
        DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(metadata.ItemId));
        string[] accepted = buildingType switch
        {
            0 => ["Gems", "CreatureIngredients1", "CreatureIngredients2", "CreatureIngredients3", "PlantIngredients1", "PlantIngredients2", "MiscellaneousIngredients1", "MiscellaneousIngredients2", "MetalIngredients"],
            2 => ["Armor", "Weapons"],
            5 => ["Books"],
            6 => ["MensClothing", "WomensClothing"],
            7 => ["Furniture"],
            8 => ["Gems", "Jewellery"],
            9 => ["Books", "MensClothing", "WomensClothing", "Transportation", "Jewellery", "Weapons", "UselessItems2"],
            12 => ["Armor", "Books", "MensClothing", "WomensClothing", "Gems", "Jewellery", "ReligiousItems", "Weapons", "UselessItems2", "Paintings"],
            13 => ["Armor", "Weapons"],
            _ => [],
        };
        return definition.Template?.Groups.Any(group => accepted.Contains(group, StringComparer.Ordinal)) == true;
    }

    private bool TrySelection(DaggerfallItemOwner owner, string itemKey, ulong quantity,
        out InventoryContainerSelection selection, out DaggerfallTradeLine line)
    {
        selection = null!;
        line = null!;
        if (string.IsNullOrWhiteSpace(itemKey) || quantity == 0) return false;
        EntityId entity = owner == DaggerfallItemOwner.Player ? _inventory.Component.Owner : OwnerEntity(owner);
        InventoryView view;
        try { view = _containers.Read(entity); }
        catch (InvalidOperationException) { return false; }
        if (itemKey.StartsWith("stack:", StringComparison.Ordinal))
        {
            InventoryStackId stack = InventoryStackId.Parse(itemKey[6..]);
            InventoryStack? found = view.Stacks.SingleOrDefault(value => value.Id == stack);
            if (found is not { } selected || selected.Quantity < quantity) return false;
            DaggerfallItemInstanceMetadata metadata;
            try { metadata = _instances.RequireStack(owner, stack); }
            catch (InvalidOperationException) { return false; }
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(selected.Definition.Value));
            selection = new(new InventoryItemId(selected.Definition.Value), quantity, Stack: stack);
            line = new(definition, metadata, quantity);
            return true;
        }
        if (!itemKey.StartsWith("unique:", StringComparison.Ordinal) || !ulong.TryParse(itemKey[7..], out ulong id)) return false;
        EntityId uniqueEntity;
        try { uniqueEntity = _containers.Entities.Resolve(new(DurableIdentityKind.Item, id)); }
        catch (KeyNotFoundException) { return false; }
        EngineUniqueInventoryItem? item = view.UniqueItems.SingleOrDefault(value => value.Entity == uniqueEntity);
        if (item is not { } selectedUnique || quantity != 1) return false;
        DaggerfallItemInstanceMetadata uniqueMetadata;
        try { uniqueMetadata = _instances.RequireUnique(id); }
        catch (InvalidOperationException) { return false; }
        if (uniqueMetadata.Owner != owner) return false;
        DaggerfallItemDefinition uniqueDefinition = _definitions.RequireItem(new DaggerfallItemId(selectedUnique.Definition.Value));
        selection = new(new InventoryItemId(selectedUnique.Definition.Value), 1, UniqueEntityId: uniqueEntity.Value);
        line = new(uniqueDefinition, uniqueMetadata, 1);
        return true;
    }

    private InventoryContainerSelection PrepareSaleDestination(InventoryContainerSelection selection, Binding binding)
    {
        if (selection.UniqueEntityId is not null) return selection;
        InventoryStackId source = selection.Stack!;
        InventoryStackId destination = NewDestinationStack(
            DaggerfallItemOwner.Merchant(binding.MerchantContainerId),
            _containers.Read(binding.MerchantOwner).Stacks,
            $"daggerfall.merchant.{binding.MerchantContainerId}.acquired");
        _instances.EnsureTransferCompatible(DaggerfallItemOwner.Player, DaggerfallItemOwner.Merchant(binding.MerchantContainerId), source, destination);
        return selection with { DestinationStack = destination };
    }

    private InventoryContainerSelection PrepareDestination(InventoryContainerSelection selection, DaggerfallItemOwner sourceOwner,
        DaggerfallItemOwner destinationOwner, IReadOnlyList<InventoryStack> destinationStacks, Binding binding,
        bool allowCompatible = true, string destinationPrefix = "daggerfall.player.purchase")
    {
        if (selection.UniqueEntityId is not null) return selection;
        DaggerfallItemInstanceMetadata sourceMetadata = _instances.RequireStack(sourceOwner, selection.Stack!);
        InventoryStackId? compatible = allowCompatible
            ? destinationStacks.OrderBy(value => value.Id.Value, StringComparer.Ordinal)
                .Where(value => _instances.ContainsStack(destinationOwner, value.Id)
                    && sourceMetadata.IsStackCompatibleWith(_instances.RequireStack(destinationOwner, value.Id)))
                .Select(value => value.Id).FirstOrDefault()
            : null;
        InventoryStackId destination = compatible ?? NewDestinationStack(destinationOwner, destinationStacks,
            $"{destinationPrefix}.{binding.MerchantContainerId}");
        _instances.EnsureTransferCompatible(sourceOwner, destinationOwner, selection.Stack!, destination);
        return selection with { DestinationStack = destination };
    }

    private InventoryStackId NewDestinationStack(DaggerfallItemOwner owner, IReadOnlyList<InventoryStack> destinationStacks, string prefix)
    {
        HashSet<string> existing = destinationStacks.Select(value => value.Id.Value).ToHashSet(StringComparer.Ordinal);
        InventoryStackId candidate;
        do
        {
            candidate = InventoryStackId.Parse($"{prefix}.{checked(++_nextRequest)}");
        }
        while (existing.Contains(candidate.Value) || _instances.ContainsStack(owner, candidate));
        return candidate;
    }

    private void SyncTransfer(InventoryContainerTransferReceipt transfer, DaggerfallItemOwner sourceOwner, DaggerfallItemOwner destinationOwner) =>
        _instances.ApplyTransfer(transfer, _containers.Read(transfer.SourceBefore.Owner), _containers.Entities, sourceOwner, destinationOwner);

    private void MarkStolen(InventoryContainerSelection selection, ulong quantity)
    {
        if (selection.UniqueEntityId is ulong entity)
        {
            ulong id = _containers.GetDurableItemId(new EntityId(entity)).Value;
            _instances.ReplaceUnique(id, _instances.RequireUnique(id) with { Stolen = true });
            return;
        }
        DaggerfallItemOwner player = DaggerfallItemOwner.Player;
        _instances.ReplaceStack(player, selection.DestinationStack!, _instances.RequireStack(player, selection.DestinationStack!) with { Stolen = true });
    }

    private void RetireGeneratedSelection(Binding binding, InventoryContainerSelection selection)
    {
        if (selection.UniqueEntityId is ulong entity)
        {
            ulong unique = _containers.GetDurableItemId(new EntityId(entity)).Value;
            binding.GeneratedUniqueItems.Remove(unique);
            return;
        }
        if (selection.Stack is not InventoryStackId stack) return;
        if (!_containers.Read(binding.MerchantOwner).Stacks.Any(value => value.Id == stack))
            binding.GeneratedStacks.Remove(stack.Value);
    }

    private void RecordTradeSkill() => _ = _skillUses.Record(new("mercantile", DaggerfallSkillUseReason.MercantileTrade, DaggerfallSkillUseOutcome.Accepted));

    private DaggerfallMerchantProviderContext ContextFor(Binding binding) => binding.Context;

    private Binding Ensure(DaggerfallMerchantProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Provider.Validate();
        if (context.Quality is < DaggerfallRegionalEconomyPolicy.MinimumShopQuality or > DaggerfallRegionalEconomyPolicy.MaximumShopQuality)
            throw new ArgumentOutOfRangeException(nameof(context), "Merchant quality is outside the admitted range.");
        if (_bindings.TryGetValue(context.Key, out Binding? existing))
        {
            existing.Context = context;
            if (_calendar().DayNumber > existing.StockedDay) Restock(existing);
            return existing;
        }
        Binding binding = CreateEmpty(context, null);
        _bindings.Add(context.Key, binding);
        GenerateStock(binding);
        binding.StockedDay = _calendar().DayNumber;
        return binding;
    }

    private Binding CreateEmpty(DaggerfallMerchantProviderContext context, DaggerfallMerchantSave? saved)
    {
        long merchantId = saved?.MerchantContainerId ?? checked((long)_identities.Allocate(DurableIdentityKind.Container).Value);
        long custodyId = saved?.CustodyContainerId ?? checked((long)_identities.Allocate(DurableIdentityKind.Container).Value);
        EntityId merchant = _containers.Entities.Create(new(DurableIdentityKind.Container, checked((ulong)merchantId)), new(MerchantContainerTypeName));
        EntityId custody = _containers.Entities.Create(new(DurableIdentityKind.Container, checked((ulong)custodyId)), new(RepairContainerTypeName));
        try
        {
            _containers.RegisterOwner(merchant);
            _containers.RegisterOwner(custody);
            return new()
            {
                Context = context,
                MerchantContainerId = merchantId,
                CustodyContainerId = custodyId,
                MerchantOwner = merchant,
                CustodyOwner = custody,
                StockedDay = saved?.StockedDay ?? 0,
            };
        }
        catch
        {
            if (_containers.Entities.Store.IsAlive(merchant)) _containers.Entities.Destroy(new(DurableIdentityKind.Container, checked((ulong)merchantId)));
            if (_containers.Entities.Store.IsAlive(custody)) _containers.Entities.Destroy(new(DurableIdentityKind.Container, checked((ulong)custodyId)));
            if (saved is null)
            {
                _identities.Remove(new(DurableIdentityKind.Container, checked((ulong)merchantId)));
                _identities.Remove(new(DurableIdentityKind.Container, checked((ulong)custodyId)));
            }
            throw;
        }
    }

    private void GenerateStock(Binding binding)
    {
        List<InventoryContainerSeed> seeds = [];
        List<(InventoryContainerSeed Seed, DaggerfallItemInstanceMetadata Metadata, string? Stack, ulong? Unique)> generated = [];
        string stockKey = $"{binding.Context.Key}:{_calendar().DayNumber}";
        if (binding.Context.Provider.Service == "buy-potions") stockKey += $":{binding.PotionStockRoll}";
        if (binding.Context.Provider.Service == "buy-potions")
        {
            for (int index = 0; index <= binding.Context.Quality; index++)
            {
                string key = $"{stockKey}:potion:{index}";
                int recipe = _definitions.Magic.ChoosePotionRecipe((low, high) => checked((int)_random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope, key + ":recipe", low, high)).Value));
                ulong quantity = checked((ulong)_random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope, key + ":quantity", 1, 4)).Value);
                var item = CreateItem(binding, "UselessItems1", 83, key, recipe) with { Quantity = quantity };
                AddGenerated(binding, item, seeds, generated, $"potion:{index}");
            }
        }
        else if (StockPools.TryGetValue(binding.Context.BuildingType, out (string Category, int Chance)[]? pools))
        {
            foreach ((string category, int chance) in pools)
            {
                if (category is "Furniture" or "UselessItems1")
                    continue;
                if (category == "MagicItems")
                {
                    // The donor's MagicItems enum has one synthetic slot. Its stock roll admits
                    // one random regular magic item; it does not roll every MAGIC.DEF template.
                    if (Roll($"{stockKey}:MagicItems:0", chance * 5))
                    {
                        DaggerfallCreatedItem item = CreateItem(binding, "Magic", null,
                            $"{stockKey}:MagicItems:0");
                        AddGenerated(binding, item, seeds, generated, "magic:0");
                    }
                    continue;
                }
                if (category == "Books")
                {
                    int qualityMod = (binding.Context.Quality + 3) / 5;
                    if (qualityMod >= 4) qualityMod--;
                    qualityMod++;
                    for (int index = 0; index <= qualityMod; index++)
                    {
                        DaggerfallCreatedItem book = CreateItem(binding, category, null, $"{stockKey}:{category}:{index}");
                        AddGenerated(binding, book, seeds, generated, $"{category}:{index}");
                    }
                    continue;
                }
                string actualCategory = category is "MensClothing" or "WomensClothing"
                    ? (_character.Identity.Gender == DaggerfallCharacterGender.Male ? "MensClothing" : "WomensClothing") : category;
                DaggerfallItemTemplateDefinition[] templates = _definitions.ItemTemplateCatalog.Templates.Values
                    .Where(template => template.Groups.Contains(actualCategory, StringComparer.Ordinal) && template.Rarity <= binding.Context.Quality)
                    .OrderBy(template => template.Index).ToArray();
                foreach (DaggerfallItemTemplateDefinition template in templates)
                {
                    int stockChance = checked(chance * 5 * (21 - template.Rarity) / 100);
                    if (!Roll($"{stockKey}:{category}:{template.Index}", stockChance)) continue;
                    DaggerfallCreatedItem item = CreateItem(binding, actualCategory, template.Index,
                        $"{stockKey}:{category}:{template.Index}");
                    AddGenerated(binding, item, seeds, generated, $"{category}:{template.Index}");
                }
            }
        }
        if (binding.Context.Provider.Service != "buy-potions" && binding.Context.BuildingType == 9)
        {
            AddGenerated(binding, CreateItem(binding, "Transportation", 94, stockKey + ":horse"), seeds, generated, "horse");
            AddGenerated(binding, CreateItem(binding, "Transportation", 93, stockKey + ":cart"), seeds, generated, "cart");
        }
        if (binding.Context.Provider.Service != "buy-potions" && binding.Context.BuildingType == 0 && Roll(stockKey + ":potion-recipe", 25))
        {
            int recipe = _definitions.Magic.ChoosePotionRecipe((low, high) =>
                checked((int)_random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope, stockKey + ":potion-recipe:key", low, high)).Value));
            DaggerfallCreatedItem recipeItem = CreateItem(binding, "MiscItems", 4, stockKey + ":potion-recipe:item", recipe);
            AddGenerated(binding, recipeItem, seeds, generated, "potion-recipe");
        }
        if (seeds.Count == 0) return;
        _containers.Seed(binding.MerchantOwner, seeds);
        foreach ((InventoryContainerSeed seed, DaggerfallItemInstanceMetadata metadata, string? stack, ulong? unique) in generated)
        {
            if (stack is not null)
            {
                InventoryStackId id = InventoryStackId.Parse(stack);
                _instances.RegisterStack(DaggerfallItemOwner.Merchant(binding.MerchantContainerId), id, metadata);
                binding.GeneratedStacks.Add(stack);
            }
            else
            {
                _instances.RegisterUnique(unique!.Value, metadata);
                binding.GeneratedUniqueItems.Add(unique.Value);
            }
        }
    }

    private DaggerfallCreatedItem CreateItem(Binding binding, string category, int? template, string key, int? recipe = null,
        string? magicKey = null) =>
        new DaggerfallItemFactory(_definitions, _random).Create(new(category, key,
            DaggerfallItemOwner.Merchant(binding.MerchantContainerId), Quantity: null, TemplateIndex: template,
            Level: Math.Max(1, _progression.Level),
            Race: RequiresAppearance(category) ? _character.Identity.RaceId : null,
            Gender: RequiresAppearance(category) ? _character.Identity.Gender.ToString().ToLowerInvariant() : null,
            PotionRecipeKey: recipe, MagicItemKey: magicKey));

    private static bool RequiresAppearance(string category) =>
        category is "Armor" or "MensClothing" or "WomensClothing" or "Magic";

    private void AddGenerated(Binding binding, DaggerfallCreatedItem item,
        List<InventoryContainerSeed> seeds,
        List<(InventoryContainerSeed Seed, DaggerfallItemInstanceMetadata Metadata, string? Stack, ulong? Unique)> generated,
        string key)
    {
        if (item.Stackable)
        {
            InventoryStackId stack = InventoryStackId.Parse($"daggerfall.merchant.{binding.MerchantContainerId}.stock.{key.Replace(':', '.')}");
            InventoryContainerSeed seed = new(item.Item, item.Quantity, Stack: stack);
            seeds.Add(seed); generated.Add((seed, item.Metadata with { Owner = DaggerfallItemOwner.Merchant(binding.MerchantContainerId) }, stack.Value, null));
        }
        else
        {
            DurableIdentityReference identity = _uniqueItems.AllocateReference();
            InventoryContainerSeed seed = new(item.Item, UniqueItem: identity);
            seeds.Add(seed); generated.Add((seed, item.Metadata with { Owner = DaggerfallItemOwner.Merchant(binding.MerchantContainerId) }, null, identity.Value));
        }
    }

    private void Restock(Binding binding)
    {
        RetireGenerated(binding);
        GenerateStock(binding);
        binding.StockedDay = _calendar().DayNumber;
    }

    private void RetireGenerated(Binding binding)
    {
        InventoryView contents = _containers.Read(binding.MerchantOwner);
        List<InventoryStackId> retiredStacks = [];
        HashSet<ulong> retired = [];
        using (InventoryEdit edit = _containers.Entities.Store.Get<InventoryComponent>(binding.MerchantOwner).Store.Prepare())
        {
            foreach (InventoryStack stack in contents.Stacks.Where(value => binding.GeneratedStacks.Contains(value.Id.Value)))
            {
                edit.Consume(binding.MerchantOwner, stack.Id, stack.Quantity);
                retiredStacks.Add(stack.Id);
            }
            foreach (EngineUniqueInventoryItem item in contents.UniqueItems)
            {
                ulong id = _containers.GetDurableItemId(item.Entity).Value;
                if (!binding.GeneratedUniqueItems.Contains(id)) continue;
                edit.DestroyUnique(item.Entity);
                retired.Add(id);
            }
            if (contents.Stacks.Any(value => binding.GeneratedStacks.Contains(value.Id.Value)) || retired.Count > 0) edit.Publish();
        }
        foreach (InventoryStackId stack in retiredStacks)
            _instances.RemoveStack(DaggerfallItemOwner.Merchant(binding.MerchantContainerId), stack);
        foreach (ulong id in retired)
        {
            _instances.RemoveUnique(id);
            _containers.Entities.Destroy(new(DurableIdentityKind.Item, id));
            _uniqueItems.Remove(new(DurableIdentityKind.Item, id));
        }
        binding.GeneratedStacks.Clear();
        binding.GeneratedUniqueItems.Clear();
    }

    private void MaterializeSaved(DaggerfallMerchantSave save)
    {
        save.Validate();
        if (_bindings.ContainsKey(save.Key)) throw new InvalidOperationException($"Merchant '{save.Key}' appears more than once.");
        DurableIdentityReference merchantIdentity = new(DurableIdentityKind.Container, checked((ulong)save.MerchantContainerId));
        DurableIdentityReference custodyIdentity = new(DurableIdentityKind.Container, checked((ulong)save.CustodyContainerId));
        if (_identities.Classify(merchantIdentity) != DurableIdentityClassification.Live || _identities.Classify(custodyIdentity) != DurableIdentityClassification.Live)
            throw new ArgumentException($"Saved merchant '{save.Key}' names a container identity that is not live.", nameof(save));
        DaggerfallNpc providerNpc = _npcs.Require(save.ProviderNpcId);
        DaggerfallMerchantProviderContext context = new(new(save.ProviderNpcId,
            new(save.ProviderRegion, save.ProviderLocation, save.ProviderBuilding, providerNpc.Site.ProfileId), save.Service), save.Quality,
            save.BuildingType, save.BlockX, save.BlockY, save.BuildingIndex);
        Binding binding = CreateEmpty(context, save);
        try
        {
            InventoryContainerSeed[] seeds = save.Inventory.Stacks.Select(value => new InventoryContainerSeed(new(value.ItemId), value.Quantity,
                    Stack: InventoryStackId.Parse(value.StackId)))
                .Concat(save.Inventory.UniqueItems.Select(value => new InventoryContainerSeed(new(value.ItemId), UniqueItem: new(DurableIdentityKind.Item, value.EntityId), CapacityCosts: DaggerfallEncumbrancePolicy.CapacityOverride(value.Metadata.WeightClassicUnits))))
                .ToArray();
            if (seeds.Length != 0) _containers.Seed(binding.MerchantOwner, seeds);
            InventoryContainerSeed[] custody = save.Custody.Stacks.Select(value => new InventoryContainerSeed(new(value.ItemId), value.Quantity,
                    Stack: InventoryStackId.Parse(value.StackId)))
                .Concat(save.Custody.UniqueItems.Select(value => new InventoryContainerSeed(new(value.ItemId), UniqueItem: new(DurableIdentityKind.Item, value.EntityId), CapacityCosts: DaggerfallEncumbrancePolicy.CapacityOverride(value.Metadata.WeightClassicUnits))))
                .ToArray();
            if (custody.Length != 0) _containers.Seed(binding.CustodyOwner, custody);
            RegisterSavedMetadata(save.Inventory, DaggerfallItemOwner.Merchant(binding.MerchantContainerId));
            RegisterSavedMetadata(save.Custody, DaggerfallItemOwner.RepairCustody(binding.CustodyContainerId));
            binding.StockedDay = save.StockedDay;
            binding.GeneratedStacks.UnionWith(save.GeneratedStacks);
            binding.GeneratedUniqueItems.UnionWith(save.GeneratedUniqueItems);
            foreach (DaggerfallMerchantRepairSave order in save.Repairs) binding.Repairs.Add(order.RequestId, order);
            _bindings.Add(save.Key, binding);
        }
        catch
        {
            _containers.Entities.Destroy(merchantIdentity);
            _containers.Entities.Destroy(custodyIdentity);
            throw;
        }
    }

    private void RegisterSavedMetadata(DaggerfallInventorySave save, DaggerfallItemOwner owner)
    {
        foreach (DaggerfallStackSave stack in save.Stacks)
        {
            DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata);
            if (metadata.Owner != owner) throw new ArgumentException($"Saved merchant stack '{stack.StackId}' has the wrong owner.", nameof(save));
            _instances.RegisterStack(owner, InventoryStackId.Parse(stack.StackId), metadata);
        }
        foreach (DaggerfallUniqueSave item in save.UniqueItems)
        {
            DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Restore(item.ItemId, item.Metadata);
            if (metadata.Owner != owner) throw new ArgumentException($"Saved merchant item '{item.EntityId}' has the wrong owner.", nameof(save));
            _instances.RegisterUnique(item.EntityId, metadata);
        }
    }

    private DaggerfallMerchantSave Capture(Binding binding)
    {
        DaggerfallItemOwner merchantOwner = DaggerfallItemOwner.Merchant(binding.MerchantContainerId);
        DaggerfallItemOwner custodyOwner = DaggerfallItemOwner.RepairCustody(binding.CustodyContainerId);
        string service = PersistedProviderService(binding);
        return new(binding.Context.Key, binding.Context.Provider.NpcId, binding.Context.Provider.Site.Region,
            binding.Context.Provider.Site.Location, binding.Context.Provider.Site.Building, service,
            binding.Context.BuildingType, binding.Context.BlockX, binding.Context.BlockY, binding.Context.BuildingIndex,
            binding.Context.Quality, binding.MerchantContainerId, binding.CustodyContainerId, binding.StockedDay,
            CaptureContents(binding.MerchantOwner, merchantOwner), CaptureContents(binding.CustodyOwner, custodyOwner),
            [.. binding.GeneratedStacks.Order(StringComparer.Ordinal)], [.. binding.GeneratedUniqueItems.Order()],
            [.. binding.Repairs.Values.OrderBy(value => value.RequestId, StringComparer.Ordinal)]);
    }

    private string PersistedProviderService(Binding binding)
    {
        if (binding.Repairs.Count == 0) return binding.Context.Provider.Service;

        DaggerfallServiceQueuedWork[] pending = _services.Pending
            .Where(value => binding.Repairs.ContainsKey(value.Id))
            .ToArray();
        if (pending.Length != binding.Repairs.Count
            || pending.Any(value => value.Provider.NpcId != binding.Context.Provider.NpcId
                || value.Provider.Site != binding.Context.Provider.Site))
            throw new InvalidOperationException($"Merchant '{binding.Context.Key}' has repair custody without matching provider work.");

        string? service = pending.Select(value => value.Provider.Service)
            .Distinct(StringComparer.Ordinal).SingleOrDefault();
        if (!StringComparer.Ordinal.Equals(service, "repair"))
            throw new InvalidOperationException($"Merchant '{binding.Context.Key}' has repair work with an invalid provider service.");
        return service;
    }

    private DaggerfallInventorySave CaptureContents(EntityId owner, DaggerfallItemOwner itemOwner)
    {
        (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = DaggerfallInventorySaveBoundary.CaptureContents(
            _containers.Read(owner), itemOwner, _instances, _containers.Entities);
        return new(stacks, uniques, []);
    }

    private bool RevisionMatches(Binding binding, string revision)
    {
        string expected = MakeRevision(binding);
        int separator = revision.IndexOf('|');
        return string.Equals(separator < 0 ? revision : revision[(separator + 1)..], expected, StringComparison.Ordinal);
    }

    private string MakeRevision(Binding binding) =>
        $"{binding.Context.Key}:{_containers.Read(binding.MerchantOwner).InventoryRevision}:{_containers.Read(binding.CustodyOwner).InventoryRevision}:{CurrentMinute()}";

    private string RequestId(Binding binding, string operation, string itemKey, string revision) =>
        $"merchant:{binding.Context.Key}:{operation}:{itemKey}:{revision}";

    private long CurrentMinute()
    {
        DaggerfallCalendar value = _calendar();
        return checked(value.DayNumber * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour
            + value.Hour * DaggerfallCalendar.MinutesPerHour + value.Minute);
    }

    private static DaggerfallServiceEligibility OpenEligibility { get; } = new(false, OpensAtHour: OpenHour, ClosesAtHour: CloseHour);

    private bool Roll(string key, int chance)
    {
        int roll = checked((int)_random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope, key, 0, 99)).Value);
        return roll < Math.Clamp(chance, 0, 100);
    }

    private EntityId OwnerEntity(DaggerfallItemOwner owner) => owner.Scope switch
    {
        "merchant" => _bindings.Values.Single(binding => binding.MerchantContainerId == owner.Id).MerchantOwner,
        "repair-custody" => _bindings.Values.Single(binding => binding.CustodyContainerId == owner.Id).CustodyOwner,
        _ => throw new ArgumentException($"No merchant entity maps item owner '{owner.Scope}'.", nameof(owner)),
    };

    private int IdentifySourceValue(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata)
    {
        if (metadata.Enchantment is not { } enchantment) return definition.Value;
        string suffix = "-magic-" + enchantment.Replace('.', '-');
        if (!definition.Id.Value.EndsWith(suffix, StringComparison.Ordinal)) return definition.Value;
        string baseId = definition.Id.Value[..^suffix.Length];
        return _definitions.TryResolveItem(new DaggerfallItemId(baseId), out DaggerfallItemDefinition baseDefinition)
            ? baseDefinition.Value : definition.Value;
    }
}
