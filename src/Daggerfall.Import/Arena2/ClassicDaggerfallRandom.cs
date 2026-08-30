namespace Daggerfall.Import.Arena2;

/// <summary>
/// Stateful reproduction of the classic Daggerfall random sequence for offline
/// source conversion.
/// </summary>
public sealed class ClassicDaggerfallRandom
{
    private uint state;

    /// <summary>Creates a sequence at the supplied classic seed.</summary>
    public ClassicDaggerfallRandom(uint seed)
    {
        state = seed;
    }

    /// <summary>Advances the ANSI-C linear congruential generator once.</summary>
    public uint Next()
    {
        state = unchecked((state * 1_103_515_245U) + 12_345U);
        return (state >> 16) & 0x7FFFU;
    }

    /// <summary>
    /// Returns a uniformly selected integer in the inclusive classic range.
    /// </summary>
    public int NextInclusive(int minimum, int maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                maximum,
                "The inclusive maximum must not be less than the minimum.");
        }

        long range = ((long)maximum - minimum) + 1;
        return (int)(minimum + (Next() % range));
    }
}
