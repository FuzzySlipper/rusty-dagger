namespace WorldRpg.Politics;

/// <summary>
/// The board's one source of chance. SplitMix64 rather than <see cref="Random"/>, whose algorithm the runtime does
/// not promise to keep, so a seed plays the same game on every machine and runtime version. Its whole state is
/// one number, which a saved board carries.
/// </summary>
public sealed class BoardRandom(ulong state)
{
    public ulong State { get; private set; } = state;

    public ulong NextUInt64()
    {
        ulong z = State += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A number in [0, <paramref name="count"/>).</summary>
    public int Next(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return (int)(NextUInt64() % (ulong)count);
    }
}
