using System.Numerics;

namespace WorldRpg.Kit.Controls;

/// <summary>A current projection of ruleset-owned conditions; the effect owner retains sources and lifetime.</summary>
public readonly record struct ActorControlRestrictions(bool Movement = false, bool PhysicalAttacks = false)
{
    public ActorControlRestrictions Combine(ActorControlRestrictions other) =>
        new(Movement || other.Movement, PhysicalAttacks || other.PhysicalAttacks);

    /// <summary>
    /// Suppresses voluntary drive while the ordinary Engine character proposal retains support and gravity. The stance
    /// asked for is kept: it holds the body as it is rather than driving it, so a paralysed crouched body stays crouched.
    /// </summary>
    public CharacterStepControls Restrict(CharacterStepControls controls) => !Movement ? controls : controls with
    {
        PlanarIntent = Vector2.Zero, JumpPressed = false, JumpHeld = false,
        VerticalVelocity = null,
    };
}
