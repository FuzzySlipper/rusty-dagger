using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using UniqueInventoryItem = Rusty.Engine.Mechanics.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The identify choice an Identify spell on the player opens: which unidentified magic item to try, or all of them,
/// each attempt paid in magicka and drawn at the spell's chance. Its pending request is saved, and lapses when the
/// item that cast it is no longer the player's to use.
/// </summary>
/// <param name="label">How the inventory names a carried item.</param>
internal sealed class DaggerfallIdentify(MechanicsInventoryCoordinator inventory, DaggerfallItemInstances instances,
    DaggerfallItemConditionService itemCondition, DaggerfallMagicCatalogSet magic, StatsComponent playerStats, Actor player,
    DaggerfallVitalityConsequences vitality, IRandomService random, FactBuffer<IProductFact> facts, PresentationState presentation,
    Func<UniqueInventoryItem, string> label, DaggerfallIdentifyRequest? restored)
{
    private DaggerfallIdentifyRequest? _pending = restored;

    /// <summary>The pending request while the item that cast it can still be used.</summary>
    internal DaggerfallIdentifyRequest? Pending
    {
        get
        {
            if (_pending?.SourceItem is ulong source && !SourceAvailable(source))
                _pending = null;
            return _pending;
        }
    }

    /// <summary>Whether a request is held, without checking its source.</summary>
    internal bool Open => _pending is not null;

    internal void Clear() => _pending = null;

    /// <summary>A destroyed item that cast the pending request ends it.</summary>
    internal void ItemRemoved(ulong item)
    {
        if (_pending?.SourceItem == item) _pending = null;
    }

    private bool SourceAvailable(ulong source) => instances.ContainsUnique(source)
        && instances.RequireUnique(source) is { Owner: var owner } metadata
        && owner == DaggerfallItemOwner.Player
        && (metadata.MaximumCondition == 0 || metadata.CurrentCondition > 0)
        && inventory.Read().UniqueItems.Any(item => inventory.GetDurableItemId(item.Entity).Value == source);

    /// <summary>The identify choice: each unidentified magic item, then one choice for all of them (key <c>all</c>) when there are any.</summary>
    internal DaggerfallIdentifyView? View => Pending is { } request
        ? new(request.Instance, request.Cost, Options()) : null;

    private DaggerfallIdentifyOption[] Options()
    {
        DaggerfallIdentifyOption[] items = [.. Candidates().Select(item => new DaggerfallIdentifyOption(
            inventory.GetDurableItemId(item.Entity).Value.ToString(CultureInfo.InvariantCulture), label(item)))];
        return items.Length == 0 ? items : [.. items, new("all", "All unidentified items")];
    }

    private UniqueInventoryItem[] Candidates() => inventory.Read().UniqueItems
        .Where(item => instances.RequireUnique(inventory.GetDurableItemId(item.Entity).Value)
            is { Enchantment: not null, Identified: false }).ToArray();

    /// <summary>The spell lands on the player: its magicka is refunded and the choice opens at the spell's chance and cost.</summary>
    internal void Request(DaggerfallActiveEffect effect, DaggerfallCastEffectState state)
    {
        if (effect.Context.Target.Value != DaggerfallActorIdentity.PlayerEntityId
            || (effect.Context.Item is { } source && !SourceAvailable(source.Value)))
        { effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch; return; }
        int cost = Math.Max(5, DaggerfallMagicCostPolicy.CalculateEffectCosts(magic, state.Settings,
            new[] { "destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism" }
                .ToDictionary(key => key, key => DaggerfallCasting.Read(playerStats, key))).SpellPoints);
        vitality.RestoreSpellTrack(player, TrackId.Parse("magicka"), cost);
        _pending = new(effect.Context.Instance.Value, DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Settings, state.CasterLevel), cost, effect.Context.Item?.Value);
    }

    internal void Choose(string revision, string? key)
    {
        if (Pending is not { } request || request.Instance != revision)
        { presentation.SetOutcome("Identify choice is no longer current."); return; }
        if (key is null) { _pending = null; presentation.SetOutcome("Identify cancelled."); return; }
        var candidates = Candidates();
        var selected = key == "all" ? candidates : candidates.Where(item => inventory.GetDurableItemId(item.Entity).Value.ToString(CultureInfo.InvariantCulture) == key).ToArray();
        if (selected.Length == 0) { presentation.SetOutcome("That unidentified item is no longer available."); return; }
        var magicka = playerStats.GetTrack(TrackId.Parse("magicka"));
        if (magicka.Current < request.Cost) { presentation.SetOutcome("Not enough magicka to identify."); return; }
        magicka.SetCurrent(magicka.Current - request.Cost, clamp: true);
        _pending = request with { Attempts = checked(request.Attempts + 1) };
        int successes = 0;
        foreach (var item in selected)
        {
            ulong id = inventory.GetDurableItemId(item.Entity).Value;
            bool success = random.DrawKeyed(new(0, "daggerfall.identify.v1", $"{request.Instance}:attempt:{request.Attempts}:item:{id}", 1, 100)).Value <= request.Chance;
            if (success) { itemCondition.Identify(new WorldRpg.Kit.Inventory.UniqueInventoryItem(item.Entity.Value, new WorldRpg.Kit.Inventory.InventoryItemId(item.Definition.Value))); successes++; }
            facts.Append(new MagicItemIdentifiedFact(id, success));
        }
        presentation.SetOutcome($"Identified {successes} of {selected.Length} items.");
    }
}
