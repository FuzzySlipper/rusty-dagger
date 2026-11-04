using System.Numerics;
using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>What in the world hurts by touch: open fires, and how far a sprung hurt trap reaches.</summary>
/// <param name="BurningFlats">The flats that burn, by texture archive and record: campfires and braziers, not torches.</param>
/// <param name="FireRadius">How close to a fire's foot a body burns, across the ground, in metres.</param>
/// <param name="FireHeight">How far above or below a fire's foot a body can be and still burn.</param>
/// <param name="BodyBurnPerSecond">The health a body thrown into a fire loses each second.</param>
/// <param name="BodyBurnSeconds">How long such a body goes on burning after its last touch of the fire.</param>
/// <param name="PlayerBurnStart">The health a player loses each second on first touching a fire, so brushing one does little.</param>
/// <param name="PlayerBurnRamp">How much that rate rises for each second the player stays in.</param>
/// <param name="PlayerBurnMaximum">The most it rises to.</param>
/// <param name="PlayerCoolRate">How many seconds of heat each second out of the fire takes off.</param>
/// <param name="TrapRadius">How far from the player a hurt trap the player sets off reaches others, in metres.</param>
internal sealed record DaggerfallHazardTuning(IReadOnlySet<(int Archive, int Record)> BurningFlats, float FireRadius, float FireHeight,
    double BodyBurnPerSecond, double BodyBurnSeconds, double PlayerBurnStart, double PlayerBurnRamp, double PlayerBurnMaximum, double PlayerCoolRate, float TrapRadius)
{
    internal static DaggerfallHazardTuning Default { get; } = new(new HashSet<(int, int)> { (210, 0), (210, 1), (210, 19) }, .7f, 2f, 5d, 3d, .5d, 2d, 10d, 2d, 3f);

    internal static DaggerfallHazardTuning Read(JsonElement root) => new(
        new HashSet<(int, int)>(root.GetProperty("burningFlats").EnumerateArray().Select(flat => (flat[0].GetInt32(), flat[1].GetInt32()))),
        (float)Positive(root, "fireRadius"), (float)Positive(root, "fireHeight"), Positive(root, "bodyBurnPerSecond"), Positive(root, "bodyBurnSeconds"),
        Positive(root, "playerBurnStart"), Positive(root, "playerBurnRamp"), Positive(root, "playerBurnMaximum"), Positive(root, "playerCoolRate"), (float)Positive(root, "trapRadius"));

    private static double Positive(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is > 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Hazard tuning '{name}' must be positive.");
}

/// <summary>
/// Who is burning. The player burns while in a fire, at a rate that builds the longer they stay, so brushing past does
/// little and standing in it ramps. A body the player threw catches, and goes on burning for a moment after. Other
/// bodies aren't touched: a monster wandering through a brazier out of sight sets nothing off. Burning lasts moments, so
/// it isn't saved.
/// </summary>
internal sealed class DaggerfallFires(DaggerfallHazardTuning tuning)
{
    private sealed class Burn
    {
        internal double Seconds;
        internal double Carry;
    }

    private readonly Dictionary<long, Burn> _bodies = [];
    private double _heat;
    private double _playerCarry;

    internal bool Near(Vector3 body, IReadOnlyList<Vector3> fires)
    {
        foreach (Vector3 fire in fires)
        {
            Vector2 across = new(body.X - fire.X, body.Z - fire.Z);
            if (across.LengthSquared() <= tuning.FireRadius * tuning.FireRadius && MathF.Abs(body.Y - fire.Y) <= tuning.FireHeight) return true;
        }
        return false;
    }

    /// <summary>One step of the player's burn; the whole points of health it costs.</summary>
    internal int BurnPlayer(bool inFire, double seconds)
    {
        if (!inFire)
        {
            _heat = Math.Max(0d, _heat - seconds * tuning.PlayerCoolRate);
            return 0;
        }
        _playerCarry += Math.Min(tuning.PlayerBurnMaximum, tuning.PlayerBurnStart + _heat * tuning.PlayerBurnRamp) * seconds;
        _heat += seconds;
        return Take(ref _playerCarry);
    }

    /// <summary>A thrown body touched a fire: it burns on from now.</summary>
    internal void Ignite(long actor)
    {
        if (!_bodies.TryGetValue(actor, out Burn? burn)) _bodies[actor] = burn = new();
        burn.Seconds = tuning.BodyBurnSeconds;
    }

    /// <summary>One step of every burning body: who loses whole points of health, and how many.</summary>
    internal IReadOnlyList<(long Actor, int Damage)> BurnBodies(double seconds)
    {
        List<(long, int)> burned = [];
        foreach ((long actor, Burn burn) in _bodies.OrderBy(pair => pair.Key).ToArray())
        {
            burn.Carry += tuning.BodyBurnPerSecond * Math.Min(seconds, burn.Seconds);
            burn.Seconds -= seconds;
            if (Take(ref burn.Carry) is > 0 and var damage) burned.Add((actor, damage));
            if (burn.Seconds <= 0d) _bodies.Remove(actor);
        }
        return burned;
    }

    private static int Take(ref double carry)
    {
        int whole = (int)carry;
        carry -= whole;
        return whole;
    }
}
