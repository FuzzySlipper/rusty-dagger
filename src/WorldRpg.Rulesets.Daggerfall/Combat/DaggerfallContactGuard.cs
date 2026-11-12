using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// The player's active block. Holding the <c>block</c> intent raises the player's guard while a
/// weapon is drawn and the player is free to act; the guard costs stamina each second it is up, and an
/// emptied stamina pool drops it. A swing in progress keeps the guard down, so attacking ends a block and a
/// guard cannot be raised mid-swing. Enemies do not block: their deflection stays passive (Dodging and
/// Agility), since actor sprites have no guard pose to show. The guard is transient and never saved.
/// </summary>
internal sealed class DaggerfallContactGuard(DaggerfallContactInputs session, DaggerfallContactTuning tuning)
{
    /// <summary>The product input intent the player holds to block.</summary>
    internal static ReadOnlySpan<byte> Intent => "block"u8;
    private bool _held;
    // Whether the last step could have raised a guard: a held block that cannot rise (a sheathed weapon, an
    // empty pool) does not keep action-driven time running. A guard that has not stepped yet is assumed able.
    private bool _couldRaise = true;

    /// <summary>Whether the player's guard is raised now.</summary>
    public bool Raised { get; private set; }

    /// <summary>
    /// Whether the guard needs admitted steps now, so action-driven time runs for it: to raise a held guard,
    /// to spend stamina while it is up, and to lower one that was released.
    /// </summary>
    public bool WantsTime => Raised || _held && _couldRaise;

    /// <summary>Reads one update's input for the block intent and its buttons on the pad.</summary>
    public void Observe(ReadOnlySpan<ProductInputEvent> inputs, IReadOnlyList<ControllerActionBinding> pad)
    {
        foreach (ProductInputEvent input in inputs)
        {
            if (input.Kind == InputEventKind.Clear) _held = false;
            else if (DaggerfallInput.HeldChange(input, Intent, DaggerfallInput.Block, pad) is bool held) _held = held;
        }
    }

    /// <summary>
    /// Advances the guard one admitted step. <paramref name="canGuard"/> is whether the player may hold a
    /// guard at all now (a drawn weapon, free to act); <paramref name="swinging"/> is a swing in progress.
    /// </summary>
    public void Step(double seconds, bool canGuard, bool swinging, ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts)
    {
        Track? stamina = session.StatsOf(DaggerfallActorIdentity.PlayerEntityId)?.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        _couldRaise = canGuard && stamina is { Current: > 0d };
        bool raise = _held && _couldRaise && !swinging;
        if (raise && Raised && stamina is { } pool)
        {
            _ = pool.Spend(Math.Min(pool.Current, tuning.GuardStaminaPerSecond * seconds));
            raise = pool.Current > 0d;
        }
        Set(raise, generation, simulationStep, facts);
    }

    /// <summary>Drops held input on a focus, mode or site change, which a missed release must not keep raised.</summary>
    public void Neutralize(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts)
    {
        _held = false;
        Set(false, generation, simulationStep, facts);
    }

    private void Set(bool raised, ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts)
    {
        if (raised == Raised) return;
        Raised = raised;
        facts.Append(new PlayerGuardChangedFact(raised, generation, simulationStep));
    }
}
