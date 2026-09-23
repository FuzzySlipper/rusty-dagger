namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The vertical movement owner selected from the current Daggerfall capabilities.</summary>
internal enum DaggerfallVerticalMovementMode
{
    Gravity,
    Swimming,
    Climbing,
    Levitation,
}

/// <summary>Current movement facts and the effect grant supplied by the ruleset effect lifecycle.</summary>
internal readonly record struct DaggerfallLevitationContext(
    bool Granted,
    bool Swimming,
    bool Climbing,
    bool CanMove,
    bool UpHeld,
    bool DownHeld,
    float VerticalSpeed)
{
    internal DaggerfallLevitationContext Validate()
    {
        if (!float.IsFinite(VerticalSpeed) || VerticalSpeed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(VerticalSpeed));
        return this;
    }
}

/// <summary>Readable effect capability and selected owner for one admitted movement decision.</summary>
internal readonly record struct DaggerfallLevitationStep(
    bool Granted,
    DaggerfallVerticalMovementMode Mode,
    float? VerticalVelocity)
{
    internal bool IsLevitating => Mode == DaggerfallVerticalMovementMode.Levitation;
}

/// <summary>
/// Applies the Daggerfall vertical movement precedence to one set of current facts.
/// The effect lifecycle supplies <see cref="DaggerfallLevitationContext.Granted"/>;
/// the Engine still resolves the resulting velocity through its character solver.
/// </summary>
internal sealed class DaggerfallLevitationPolicy
{
    internal DaggerfallLevitationStep Resolve(DaggerfallLevitationContext context)
    {
        context.Validate();

        DaggerfallVerticalMovementMode mode = context.Climbing
            ? DaggerfallVerticalMovementMode.Climbing
            : context.Granted
                ? DaggerfallVerticalMovementMode.Levitation
                : context.Swimming
                    ? DaggerfallVerticalMovementMode.Swimming
                    : DaggerfallVerticalMovementMode.Gravity;

        float? verticalVelocity = mode == DaggerfallVerticalMovementMode.Levitation
            ? context.CanMove
                ? context.UpHeld
                    ? context.VerticalSpeed
                    : context.DownHeld
                        ? -context.VerticalSpeed
                        : 0f
                : 0f
            : null;

        return new DaggerfallLevitationStep(context.Granted, mode, verticalVelocity);
    }
}
