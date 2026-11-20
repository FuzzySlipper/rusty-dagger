using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>
/// How loose items lie, settle, are carried and are thrown (<see cref="DaggerfallLooseItems"/>). They err toward staying
/// put: high friction and damping. They sleep by the Engine's own thresholds until it lets a body's be set (rusty-engine #10945).
/// </summary>
/// <param name="ActivationRadius">How near the player, in metres, a loose item has a body; farther ones lie still where they rest.</param>
/// <param name="StepsPerSecond">How often the bodies step, in steps a second of world time.</param>
/// <param name="Friction">Each item's friction against the floor and other items.</param>
/// <param name="Restitution">How much an item bounces, from 0 (not at all) to 1.</param>
/// <param name="LinearDamping">How quickly an item's motion dies away.</param>
/// <param name="MinimumKilograms">The least an item's body weighs, so a weightless item (an arrow, a map) still has mass.</param>
/// <param name="DropHeight">How high above the player's feet a dropped item is let go, in metres.</param>
/// <param name="DropSpeed">How fast a dropped item is tossed ahead of the player, in metres a second.</param>
/// <param name="Reach">How far from the eye, in metres, the player can take hold of an item.</param>
/// <param name="HoldDistance">How far in front of the eye a carried item is held, in metres.</param>
/// <param name="FollowSeconds">How quickly a carried item closes on where it is held: the gap is closed at this many seconds' pace.</param>
/// <param name="CarrySpeed">The fastest a carried item moves toward where it is held, in metres a second.</param>
/// <param name="BreakDistance">How far a carried item may lag where it is held, caught on something, before it is let go, in metres.</param>
/// <param name="CarryKilogramsPerStrength">The heaviest item the player can take hold of, in kilograms per point of Strength.</param>
/// <param name="ThrowImpulsePerStrength">A throw's impulse, in newton-seconds per point of Strength: a heavier item flies slower.</param>
/// <param name="MaxThrowSpeed">The fastest a thrown item leaves the hand, in metres a second, however light it is.</param>
internal sealed record DaggerfallLooseItemTuning(float ActivationRadius, int StepsPerSecond, float Friction, float Restitution, float LinearDamping,
    float MinimumKilograms, float DropHeight, float DropSpeed, float Reach, float HoldDistance,
    float FollowSeconds, float CarrySpeed, float BreakDistance, float CarryKilogramsPerStrength, float ThrowImpulsePerStrength, float MaxThrowSpeed)
{
    internal static DaggerfallLooseItemTuning Default { get; } = new(16f, 60, 1.2f, 0f, .5f, .1f, .9f, 1.5f, 2.5f, 1.2f, .08f, 8f, 1f, .5f, .2f, 14f);

    internal DaggerfallLooseItemTuning Validate()
    {
        if (StepsPerSecond is < 10 or > 480) throw new ArgumentException("Loose items step 10 to 480 times a second.");
        float[] positive = [ActivationRadius, Friction, MinimumKilograms, Reach, HoldDistance, FollowSeconds, CarrySpeed, BreakDistance,
            CarryKilogramsPerStrength, ThrowImpulsePerStrength, MaxThrowSpeed];
        float[] none = [LinearDamping, DropHeight, DropSpeed];
        if (positive.Any(value => !float.IsFinite(value) || value <= 0) || none.Any(value => !float.IsFinite(value) || value < 0))
            throw new ArgumentException("Loose items' radius, friction, mass, reach, hold, carry and throw are above 0; damping and the drop 0 or more.");
        if (!float.IsFinite(Restitution) || Restitution is < 0 or > 1) throw new ArgumentException("A loose item's restitution is 0 to 1.");
        if (HoldDistance > Reach) throw new ArgumentException("A carried item is held within reach.");
        return this;
    }

    internal static DaggerfallLooseItemTuning Read(JsonElement e) => new DaggerfallLooseItemTuning(
        e.GetProperty("activationRadius").GetSingle(), e.GetProperty("stepsPerSecond").GetInt32(), e.GetProperty("friction").GetSingle(),
        e.GetProperty("restitution").GetSingle(), e.GetProperty("linearDamping").GetSingle(),
        e.GetProperty("minimumKilograms").GetSingle(), e.GetProperty("dropHeight").GetSingle(),
        e.GetProperty("dropSpeed").GetSingle(), e.GetProperty("reach").GetSingle(), e.GetProperty("holdDistance").GetSingle(),
        e.GetProperty("followSeconds").GetSingle(), e.GetProperty("carrySpeed").GetSingle(), e.GetProperty("breakDistance").GetSingle(),
        e.GetProperty("carryKilogramsPerStrength").GetSingle(), e.GetProperty("throwImpulsePerStrength").GetSingle(),
        e.GetProperty("maxThrowSpeed").GetSingle()).Validate();
}
