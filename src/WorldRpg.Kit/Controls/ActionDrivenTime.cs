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

    /// <summary>The product decides whether movement, a live action, or presentation needs time.</summary>
    public void Update(bool active)
    {
        double rate = !Enabled || active ? 1d : 0d;
        if (_selectedRate == rate) return;
        time.SetRate(rate);
        _selectedRate = rate;
    }
}
