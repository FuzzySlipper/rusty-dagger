using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>Ruleset-owned binding from one positional controller button to a typed product action.</summary>
public sealed record ControllerActionBinding(ControllerButton Button, InputActionId Action);

/// <summary>
/// The ruleset's mapping from the Engine's positional controller vocabulary to player intent.
/// </summary>
/// <remarks>
/// The Engine names controller axes and buttons by position — <c>ControllerAxis.Axis0..Axis3</c> and
/// <c>ControllerButton.Button0..Button15</c> — because it carries no device knowledge. Which index is
/// a stick, which way that stick points, and what a button means are product policy, so they are
/// tuning rather than constants: another pad is a configuration change, not a rebuild.
/// <para>
/// The browser shell delivers the standard gamepad layout, in which the left stick is axes 0 and 1,
/// the right stick is axes 2 and 3, and both vertical axes are positive downwards. Planar intent is
/// positive forward and camera pitch is positive upward, so both vertical axes are inverted by
/// default. Deadzones are per axis and preserve direction while rescaling the live range to the full
/// unit travel, so a stick that has just left the deadzone is a slow move rather than a jump to it.
/// </para>
/// </remarks>
public sealed record ControllerInputTuning(
    ControllerAxis MovementX,
    ControllerAxis MovementY,
    ControllerAxis LookX,
    ControllerAxis LookY,
    float MovementDeadzone,
    float LookDeadzone,
    float MovementStrafeSensitivity,
    float MovementForwardSensitivity,
    float LookYawRadiansPerSecond,
    float LookPitchRadiansPerSecond,
    bool InvertMovementX,
    bool InvertMovementY,
    bool InvertLookX,
    bool InvertLookY,
    IReadOnlyList<ControllerActionBinding> Actions)
{
    /// <summary>The shell's standard gamepad layout with no button meanings attached.</summary>
    public static ControllerInputTuning Standard { get; } = new(
        ControllerAxis.Axis0,
        ControllerAxis.Axis1,
        ControllerAxis.Axis2,
        ControllerAxis.Axis3,
        MovementDeadzone: .2f,
        LookDeadzone: .2f,
        MovementStrafeSensitivity: 1f,
        MovementForwardSensitivity: 1f,
        LookYawRadiansPerSecond: 2.5f,
        LookPitchRadiansPerSecond: 1.75f,
        InvertMovementX: false,
        InvertMovementY: true,
        InvertLookX: false,
        InvertLookY: true,
        Actions: []);

    public ControllerInputTuning Validate()
    {
        Named(MovementX, nameof(MovementX));
        Named(MovementY, nameof(MovementY));
        Named(LookX, nameof(LookX));
        Named(LookY, nameof(LookY));
        // One axis means one thing. A pad that pointed strafe and forward at the same stick direction
        // would drive both from one movement, and the misconfiguration is refused here rather than
        // showing up as gameplay nobody can explain.
        ControllerAxis[] roleAxes = [MovementX, MovementY, LookX, LookY];
        for (int role = 0; role < roleAxes.Length; role++)
        {
            if (Array.IndexOf(roleAxes, roleAxes[role]) != role)
                throw new ArgumentException($"Controller axis '{roleAxes[role]}' is bound to more than one of movement and look, and one axis cannot mean two things.", nameof(MovementX));
        }

        Deadzone(MovementDeadzone, nameof(MovementDeadzone));
        Deadzone(LookDeadzone, nameof(LookDeadzone));
        Positive(MovementStrafeSensitivity, nameof(MovementStrafeSensitivity));
        Positive(MovementForwardSensitivity, nameof(MovementForwardSensitivity));
        Positive(LookYawRadiansPerSecond, nameof(LookYawRadiansPerSecond));
        Positive(LookPitchRadiansPerSecond, nameof(LookPitchRadiansPerSecond));
        ArgumentNullException.ThrowIfNull(Actions);
        HashSet<ControllerButton> bound = [];
        foreach (ControllerActionBinding binding in Actions)
        {
            Named(binding.Button, nameof(binding.Button));
            // An unnamed action is a button that presses nothing, which is indistinguishable from a
            // binding someone believed was working.
            if (string.IsNullOrWhiteSpace(binding.Action.Value))
                throw new ArgumentException($"Controller button '{binding.Button}' is bound to no action.", nameof(Actions));
            if (!bound.Add(binding.Button))
                throw new ArgumentException($"Controller button '{binding.Button}' is bound to more than one action, and one press cannot mean two things.");
        }

        return this;
    }

    private static void Named(ControllerAxis axis, string name)
    {
        if (axis == ControllerAxis.None) throw new ArgumentOutOfRangeException(name, "A controller axis binding must name an axis.");
    }

    private static void Named(ControllerButton button, string name)
    {
        if (button == ControllerButton.None) throw new ArgumentOutOfRangeException(name, "A controller button binding must name a button.");
    }

    private static void Deadzone(float deadzone, string name)
    {
        if (!float.IsFinite(deadzone) || deadzone < 0f || deadzone >= 1f) throw new ArgumentOutOfRangeException(name);
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f) throw new ArgumentOutOfRangeException(name);
    }
}
