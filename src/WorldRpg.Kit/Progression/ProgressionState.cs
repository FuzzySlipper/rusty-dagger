namespace WorldRpg.Kit.Progression;

public sealed class ProgressionState
{
    public int Experience { get; private set; }
    public void Award(int amount) => Experience += Math.Max(0, amount);
}
