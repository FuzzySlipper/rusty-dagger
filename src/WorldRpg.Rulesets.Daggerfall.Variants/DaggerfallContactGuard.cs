using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// The contact variant's active block. Holding the <c>block</c> intent raises the player's guard while a
/// weapon is drawn and the player is free to act; the guard costs stamina each second it is up, and an
/// emptied stamina pool drops it. A swing in progress keeps the guard down, so attacking ends a block and a
/// guard cannot be raised mid-swing. Enemies do not block: their deflection stays passive (Dodging and
/// Agility), since classic actor art has no guard pose to show. The guard is transient and never saved.
/// </summary>
internal sealed class DaggerfallContactGuard(DaggerfallSeamContext context, DaggerfallContactTuning tuning) : IDaggerfallPlayerGuard
{
    /// <summary>The product input intent the player holds to block.</summary>
    internal static ReadOnlySpan<byte> Intent => "block"u8;
    private bool _held;

    public bool Raised { get; private set; }

    public void Observe(ReadOnlySpan<ProductInputEvent> inputs)
    {
        foreach (ProductInputEvent input in inputs)
        {
            if (input.Kind == InputEventKind.Clear) _held = false;
            else if (input.Kind is InputEventKind.MappedDigital or InputEventKind.DirectDigital && input.Intent.Span.SequenceEqual(Intent))
                _held = input.Edge != InputEdge.Released && input.X > 0f;
        }
    }

    public void Step(double seconds, bool canGuard, bool swinging, ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts)
    {
        Track? stamina = context.Session.StatsOf(DaggerfallActorIdentity.PlayerEntityId)?.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        bool raise = _held && canGuard && !swinging && stamina is { Current: > 0d };
        if (raise && Raised && stamina is { } pool)
        {
            _ = pool.Spend(Math.Min(pool.Current, tuning.GuardStaminaPerSecond * seconds));
            raise = pool.Current > 0d;
        }
        Set(raise, generation, simulationStep, facts);
    }

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
