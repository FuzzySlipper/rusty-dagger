using Rusty.Engine;
using Rusty.Engine.Mechanics;
using EquipmentRead = WorldRpg.Kit.Inventory.EquipmentRead;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The session's live reads the player's guard and physical mitigation are built over.</summary>
/// <param name="Definitions">The session's admitted definitions.</param>
/// <param name="Character">The player's live character, when one exists.</param>
/// <param name="Random">The session's Engine random service, for keyed draws.</param>
/// <param name="EquipmentOf">An actor's current equipment.</param>
/// <param name="StatsOf">An actor's live stats, or null for one that is gone.</param>
/// <param name="PlayerArmorValueModifier">
/// The shift the player's worn enchantments give its whole armor value, in the donor's armor-value units:
/// negative is stronger armor, positive weaker.
/// </param>
internal sealed record DaggerfallContactInputs(DaggerfallDefinitions Definitions, Func<DaggerfallCharacterState?> Character,
    IRandomService Random, Func<long, EquipmentRead> EquipmentOf, Func<long, StatsComponent?> StatsOf, Func<int> PlayerArmorValueModifier)
{
    /// <summary>The curse the player's race is under, if any: a werebeast is harmed only by silver or better.</summary>
    internal Func<DaggerfallRacialOverrideView?> PlayerCurse { get; init; } = () => null;

    /// <summary>The player's biography bonus to avoiding blows, which deflection adds.</summary>
    internal Func<int> PlayerAvoidHit { get; init; } = () => 0;
}
