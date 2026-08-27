namespace RustyDagger.Game.Modules.Progression;

internal sealed class ProgressionState
{
    internal int Experience { get; private set; }
    internal void Award(int amount) => Experience += Math.Max(0, amount);
}
