using System.Security.Cryptography;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The Engine's random service as one game sees it: each keyed draw takes the game's seed mixed into its own, so the same
/// key draws differently in another game and alike in this one, and a save restores the seed. Scoped streams and the
/// classic LCG pass through unchanged (<see cref="DaggerfallRandomKey"/> says which draws are which).
/// </summary>
internal sealed class DaggerfallGameRandom(IRandomService engine, ulong seed) : IRandomService
{
    /// <summary>The seed this game's keyed draws take, made once as a new character is committed and kept in its saves.</summary>
    internal ulong Seed { get; } = seed;

    /// <summary>A seed for a new game, from no input any other game shares.</summary>
    internal static ulong NewSeed() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));

    /// <summary>
    /// The draw's own seed with the game's mixed in (a SplitMix64 finalizer over the game's seed); a zero game seed leaves it
    /// as it was.
    /// </summary>
    internal static ulong Mix(ulong game, ulong own)
    {
        if (game == 0) return own;
        ulong z = unchecked(game + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return own ^ z ^ (z >> 31);
    }

    public KeyedRngReceipt DrawKeyed(KeyedRngRequest request) => engine.DrawKeyed(request with { Seed = Mix(Seed, request.Seed) });

    public Rng CreateScoped(ScopedRngCreateRequest request) => engine.CreateScoped(request);
    public Rng ForkScoped(ScopedRngForkRequest request) => engine.ForkScoped(request);
    public RngValue NextU64(Rng stream) => engine.NextU64(stream);
    public RngValue NextBoundedU32(ScopedRngBoundedRequest request) => engine.NextBoundedU32(request);
    public RngValue NextBool(Rng stream) => engine.NextBool(stream);
    public Lcg15Receipt DrawLcg15(Lcg15Request request) => engine.DrawLcg15(request);
}
