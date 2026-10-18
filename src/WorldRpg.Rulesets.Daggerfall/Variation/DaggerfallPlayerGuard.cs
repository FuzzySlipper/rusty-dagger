using Rusty.Engine;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player guard seam (<see cref="DaggerfallRulesSeams.PlayerGuard"/>): whether the player holds a
/// guard up. The owner reads its own input intent, so a ruleset with no guard reads nothing.
/// </summary>
internal interface IDaggerfallPlayerGuard
{
    /// <summary>Whether the player's guard is raised now.</summary>
    bool Raised { get; }

    /// <summary>
    /// Whether the guard needs admitted steps now, so action-driven time runs for it: to raise a held guard,
    /// to spend stamina while it is up, and to lower one that was released.
    /// </summary>
    bool WantsTime { get; }

    /// <summary>Reads one update's input for the owner's guard intent.</summary>
    void Observe(ReadOnlySpan<ProductInputEvent> inputs);

    /// <summary>
    /// Advances the guard one admitted step. <paramref name="canGuard"/> is whether the player may hold a
    /// guard at all now (a drawn weapon, free to act); <paramref name="swinging"/> is a swing in progress.
    /// </summary>
    void Step(double seconds, bool canGuard, bool swinging, ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts);

    /// <summary>Drops held input on a focus, mode or site change, which a missed release must not keep raised.</summary>
    void Neutralize(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts);
}

/// <summary>Classic Daggerfall has no guard: the player never blocks, and no input is read for one.</summary>
internal sealed class DaggerfallNoPlayerGuard : IDaggerfallPlayerGuard
{
    internal static DaggerfallNoPlayerGuard Instance { get; } = new();
    private DaggerfallNoPlayerGuard() { }
    public bool Raised => false;
    public bool WantsTime => false;
    public void Observe(ReadOnlySpan<ProductInputEvent> inputs) { }
    public void Step(double seconds, bool canGuard, bool swinging, ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) { }
    public void Neutralize(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) { }
}
