namespace WorldRpg.Kit.World;

/// <summary>
/// A process-independent 64-bit FNV-1a hash over UTF-16 code units, for deriving stable numeric
/// identities from authored keys. Unlike <see cref="string.GetHashCode()"/> it is the same in every
/// run, so an identity derived from it survives save and reload.
/// </summary>
public static class StableHash
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Fnv1a64(ReadOnlySpan<char> value)
    {
        ulong hash = Offset;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= Prime;
        }
        return hash;
    }

    /// <summary>
    /// The hash with its bits mixed through (MurmurHash3's finalizer), for drawing from: FNV-1a's low bits follow the
    /// value's last characters, so keys that differ only in a seed's low digits would otherwise draw alike.
    /// </summary>
    public static ulong Mixed(ReadOnlySpan<char> value)
    {
        ulong mixed = Fnv1a64(value);
        mixed = (mixed ^ mixed >> 33) * 0xFF51AFD7ED558CCDUL;
        mixed = (mixed ^ mixed >> 33) * 0xC4CEB9FE1A85EC53UL;
        return mixed ^ mixed >> 33;
    }

    /// <summary>The hash, with zero (reserved by identity owners as "none") replaced by one.</summary>
    public static ulong NonZeroFnv1a64(ReadOnlySpan<char> value)
    {
        ulong hash = Fnv1a64(value);
        return hash == 0 ? 1UL : hash;
    }
}
