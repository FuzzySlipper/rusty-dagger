using System.Numerics;

namespace WorldRpg.Kit.Controls;

/// <summary>How one mount moves.</summary>
/// <param name="WalkShare">The walk's share of the mount's top speed, reined in.</param>
/// <param name="TrotShare">The trot's share of its top speed, the pace it keeps unurged.</param>
/// <param name="Acceleration">How fast it builds speed, in metres a second a second.</param>
/// <param name="Coasting">How fast it loses speed when nothing drives it on.</param>
/// <param name="Braking">How fast it loses speed when held back.</param>
/// <param name="ReverseSpeed">How fast it backs, in metres a second.</param>
/// <param name="StandingTurnDegrees">How fast it turns standing, in degrees a second.</param>
/// <param name="FullTurnDegrees">How fast it turns at its top speed.</param>
/// <param name="LookRangeDegrees">How far past its heading the view can look.</param>
/// <param name="UphillDrag">The share of its pace a slope rising as far as it runs takes away.</param>
/// <param name="WaterShare">The share of its pace it keeps in water.</param>
public sealed record RideTuning(
    float WalkShare,
    float TrotShare,
    float Acceleration,
    float Coasting,
    float Braking,
    float ReverseSpeed,
    float StandingTurnDegrees,
    float FullTurnDegrees,
    float LookRangeDegrees,
    float UphillDrag,
    float WaterShare)
{
    public RideTuning Validate()
    {
        if (!(WalkShare > 0f && WalkShare < TrotShare && TrotShare < 1f))
            throw new ArgumentOutOfRangeException(nameof(TrotShare), "A walk must be slower than a trot, and a trot than the top speed.");
        if (!(Acceleration > 0f && Coasting > 0f && Braking > 0f && ReverseSpeed >= 0f && StandingTurnDegrees > 0f && FullTurnDegrees > 0f))
            throw new ArgumentOutOfRangeException(nameof(Acceleration), "A mount must be able to speed up, slow down and turn.");
        if (!(LookRangeDegrees >= 0f && LookRangeDegrees < 180f))
            throw new ArgumentOutOfRangeException(nameof(LookRangeDegrees));
        if (!(UphillDrag >= 0f && UphillDrag <= 1f && WaterShare > 0f && WaterShare <= 1f))
            throw new ArgumentOutOfRangeException(nameof(WaterShare));
        return this;
    }
}

/// <summary>What the rider asks of the mount for one step.</summary>
/// <param name="Planar">Forward and turning intent, as a character step reads it.</param>
/// <param name="Urged">Driven on to its top speed.</param>
/// <param name="ReinedIn">Held to a walk.</param>
/// <param name="MayGallop">Whether the top speed is there to ask for.</param>
/// <param name="InWater">Whether it stands in water.</param>
public readonly record struct RideIntent(Vector2 Planar, bool Urged, bool ReinedIn, bool MayGallop, bool InWater);

/// <summary>Where the mount is going in one step: its speed, negative backing, and heading, and whether it spends itself at the gallop.</summary>
public readonly record struct RideStep(float Speed, float HeadingRadians, bool Galloping)
{
    /// <summary>The character step that carries the rider along the heading, whichever way the view looks.</summary>
    public CharacterStepControls Apply(CharacterStepControls controls) => controls with
    {
        ForwardSpeed = MathF.Abs(Speed),
        BackwardSpeed = MathF.Abs(Speed),
        StrafeSpeed = 0f,
        PlanarIntent = new Vector2(0f, MathF.Sign(Speed)),
        HeadingRadians = HeadingRadians,
    };
}

/// <summary>
/// A mount's motion under its rider: the speed it builds and carries, and a heading of its own. The rider's view leads it:
/// the mount turns toward the view at a rate that falls with speed, and the view can look only so far past it, so a hard
/// turn of the view waits for the mount. Turning keys rein it round directly and carry the view with it.
/// </summary>
public sealed class RideMotion
{
    // Motion the world took less of than this share of the mount's speed is motion it was stopped from making. Anything
    // nearer is a glancing wall or a slope, which it runs on through. A mount setting out moves too little in a step to tell.
    private const float BlockedShare = .8f;
    private const float BlockedFromSpeed = 1f;

    private float? heading;
    private float grade;

    /// <summary>Its speed now, in metres a second, negative backing.</summary>
    public float Speed { get; private set; }

    /// <summary>The heading in radians, or none until the next ride step takes it from the view.</summary>
    public float? HeadingRadians => heading;

    /// <summary>How far the heading lies from a view of <paramref name="yawRadians"/>, in radians.</summary>
    public float TurnFrom(float yawRadians) => heading is { } radians ? Wrapped(radians - yawRadians) : 0f;

    /// <summary>Stands the mount still, to face the view from the next step: on mounting, and wherever the rider is put.</summary>
    public void Reset()
    {
        heading = null;
        Speed = 0f;
        grade = 0f;
    }

    /// <summary>Moves on by one step toward what <paramref name="intent"/> asks, at most <paramref name="topSpeed"/>, keeping <paramref name="player"/>'s view in range.</summary>
    public RideStep Step(RideTuning tuning, float topSpeed, RideIntent intent, PlayerControlState player, float seconds)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topSpeed);
        float forward = Math.Clamp(intent.Planar.Y, -1f, 1f);
        float pace = !intent.ReinedIn && intent.Urged && intent.MayGallop ? 1f : intent.ReinedIn ? tuning.WalkShare : tuning.TrotShare;
        float ground = (intent.InWater ? tuning.WaterShare : 1f) * MathF.Max(0f, 1f - MathF.Max(grade, 0f) * tuning.UphillDrag);
        float wanted = forward > 0f ? forward * topSpeed * pace * ground : forward * tuning.ReverseSpeed;
        float rate = forward < 0f && Speed > wanted ? tuning.Braking
            : MathF.Abs(wanted) > MathF.Abs(Speed) ? tuning.Acceleration : tuning.Coasting;
        Speed = Toward(Speed, wanted, rate * seconds);

        float yaw = player.YawRadians;
        float mount = heading ?? yaw;
        float turn = Radians(float.Lerp(tuning.StandingTurnDegrees, tuning.FullTurnDegrees, MathF.Min(MathF.Abs(Speed) / topSpeed, 1f))) * seconds;
        float reined = Math.Clamp(intent.Planar.X, -1f, 1f) * turn;
        if (reined != 0f)
        {
            mount += reined;
            yaw += reined;
        }
        else mount += Math.Clamp(Wrapped(yaw - mount), -turn, turn);
        float past = Wrapped(yaw - mount);
        float range = Radians(tuning.LookRangeDegrees);
        player.YawRadians = yaw - past + Math.Clamp(past, -range, range);
        heading = Wrapped(mount);
        return new(Speed, heading.Value, intent.Urged && intent.MayGallop && Speed > topSpeed * tuning.TrotShare);
    }

    /// <summary>Takes in where the step went: a mount that was stopped loses what it could not carry, and a slope bears on the next step.</summary>
    public void Moved(Vector3 displacement, float seconds, bool grounded)
    {
        if (!grounded || Speed == 0f) return;
        float run = new Vector2(displacement.X, displacement.Z).Length();
        grade = run > 0f ? displacement.Y / run * MathF.Sign(Speed) : 0f;
        float carried = displacement.Length() / seconds;
        if (MathF.Abs(Speed) > BlockedFromSpeed && carried < MathF.Abs(Speed) * BlockedShare)
            Speed = MathF.Sign(Speed) * MathF.Max(carried, BlockedFromSpeed);
    }

    private static float Toward(float value, float target, float step) =>
        value < target ? MathF.Min(value + step, target) : MathF.Max(value - step, target);

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static float Wrapped(float radians) => MathF.IEEERemainder(radians, MathF.Tau);
}
