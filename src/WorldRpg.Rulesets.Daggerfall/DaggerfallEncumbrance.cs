using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One live carried-load read in the donor's smallest weight unit: one gold piece (2.5g).</summary>
internal readonly record struct DaggerfallEncumbrance(long CurrentClassicUnits, long MaximumClassicUnits)
{
    internal const int ClassicUnitsPerKilogram = 400;
    internal bool CanCarry => CurrentClassicUnits <= MaximumClassicUnits;
    internal bool CanMove => CanCarry;
    internal long RemainingClassicUnits => Math.Max(0, MaximumClassicUnits - CurrentClassicUnits);
    internal double CurrentKilograms => CurrentClassicUnits / (double)ClassicUnitsPerKilogram;
    internal double MaximumKilograms => MaximumClassicUnits / (double)ClassicUnitsPerKilogram;
}

/// <summary>
/// Daggerfall's player-load policy over Engine inventory facts. Item templates express ordinary
/// weights in quarter kilograms, but classic gold weighs one 1/400kg unit; calculations therefore
/// use gold-piece units throughout and never lose coin weight to integer rounding.
/// </summary>
internal sealed class DaggerfallEncumbrancePolicy
{
    private const string GoldPiece = "gold-piece";
    private const int TemplateWeightToClassicUnits = 100;
    private readonly MechanicsInventoryCoordinator _playerInventory;
    private readonly StatsComponent _playerStats;
    private readonly Func<double> _carryMultiplier;

    /// <summary>
    /// The carried maximum is the strength formula times whatever the worn items allow, so a
    /// weight-allowance enchantment extends this owner's answer instead of adding a second one.
    /// </summary>
    internal DaggerfallEncumbrancePolicy(MechanicsInventoryCoordinator playerInventory, StatsComponent playerStats,
        Func<double>? carryMultiplier = null)
    {
        _playerInventory = playerInventory ?? throw new ArgumentNullException(nameof(playerInventory));
        _playerStats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
        _carryMultiplier = carryMultiplier ?? (() => 1d);
    }

    internal DaggerfallEncumbrance Read() => new(CurrentClassicUnits(_playerInventory.Read()), MaximumClassicUnits());

    /// <summary>Only the player's carried inventory contributes; corpse, actor, and wagon containers remain remote owners.</summary>
    internal long WeightForOwner(DaggerfallItemOwner owner, InventoryView inventory) => owner == DaggerfallItemOwner.Player
        ? CurrentClassicUnits(inventory ?? throw new ArgumentNullException(nameof(inventory)))
        : 0;

    internal bool CanCarry(DaggerfallItemDefinition definition, ulong quantity)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (quantity == 0) return true;
        DaggerfallEncumbrance current = Read();
        try
        {
            long next = checked((long)current.CurrentClassicUnits + WeightClassicUnits(definition, quantity));
            return next <= current.MaximumClassicUnits;
        }
        catch (OverflowException)
        {
            // A UI amount may be UInt64-sized while load is deliberately represented by a
            // signed Engine-safe quantity. It is an inadmissible carry request, never a crash.
            return false;
        }
    }

    internal long WeightClassicUnits(DaggerfallItemDefinition definition, ulong quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (quantity == 0) return 0;
        long unit = checked((long)ClassicWeightCost(definition));
        return checked(unit * checked((long)quantity));
    }

    private long CurrentClassicUnits(InventoryView inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ulong used = inventory.Capacity.Single(value => value.Metric == DaggerActorFactory.ClassicWeightMetric).Used;
        return checked((long)used);
    }

    private long MaximumClassicUnits()
    {
        int strength = _playerStats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).ValueInt;
        double multiplier = _carryMultiplier();
        if (!double.IsFinite(multiplier) || multiplier < 1d) throw new InvalidOperationException("Carry multiplier must be finite and at least one.");
        long baseUnits = checked((long)DaggerfallFormulaPolicy.MaxEncumbrance(strength) * DaggerfallEncumbrance.ClassicUnitsPerKilogram);
        // The donor scales the maximum with a float multiplier; the product keeps the classic integer
        // unit and truncates, which can only ever take back a fraction of one 2.5g unit.
        return checked((long)(baseUnits * multiplier));
    }

    internal static bool IsGold(DaggerfallItemDefinition definition) => definition.Id.Value == GoldPiece || definition.Template?.Index == 276;
    internal static ulong ClassicWeightCost(DaggerfallItemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // ItemTemplate.hasNoEncumbrance: transportation, maps, and arrows stay in inventory
        // without contributing to PlayerEntity.CarriedWeight. The compact authored arrow has no
        // template link, so it names the same exception explicitly.
        if (IsGold(definition)) return 1UL;
        if (definition.Id.Value == "arrow" || definition.Template?.Index == 131
            || definition.Template?.Groups.Contains("Transportation", StringComparer.Ordinal) == true
            || definition.Template?.Groups.Contains("Maps", StringComparer.Ordinal) == true) return 0;
        return checked((ulong)definition.Weight * TemplateWeightToClassicUnits);
    }
}
