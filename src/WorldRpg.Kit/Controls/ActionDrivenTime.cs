using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>
/// Selects Engine-owned world time from a product's activity policy. Input and presentation
/// continue in zero-step updates; this owner never advances a private clock or manufactures steps.
/// </summary>
public sealed class ActionDrivenTime(IGameplayTimeService time)
{
    private double? _selectedRate;

    public bool Enabled { get; private set; }
    public bool Held => Enabled && _selectedRate == 0d;

    public void Select(bool enabled) => Enabled = enabled;

    /// <summary>
    /// The product decides whether movement, a live action, or presentation needs time, and whether
    /// that time runs slowed, as a heavy blow slows it for an instant. A world the product holds
    /// (<paramref name="worldHeld"/>: a screen over it, a pause, a defeat) admits no step whether or not
    /// action-driven play is chosen, so nothing counted in steps ages behind it.
    /// </summary>
    public void Update(bool active, double slowedTo = 1d, bool worldHeld = false)
    {
        double rate = worldHeld ? 0d : !Enabled || active ? slowedTo : 0d;
        if (_selectedRate == rate) return;
        time.SetRate(rate);
        _selectedRate = rate;
    }
}
