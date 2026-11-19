using Rusty.Engine;
using WorldRpg.Kit.Ai;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// Draws walkers as one figure in place of their own art, while a figure is in use: each walker a snapshot shows gets
/// the figure's appearance, faces where it heads (the figure's front is +z, <see cref="PartsRigFigure.Facing"/>), walks
/// while its wander state moves and stands otherwise.
/// </summary>
public interface IWalkerCrowd
{
    /// <summary>Whether a walker in <paramref name="state"/> walks: while it seeks or moves to a waypoint.</summary>
    static bool Walks(WanderState state) => state is WanderState.Moving or WanderState.Seeking;

    /// <summary>
    /// The appearance object <paramref name="id"/> shows in the snapshot being built, for a walker
    /// <paramref name="heightMetres"/> tall, walking or standing; null while no figure is used.
    /// </summary>
    Appearance? Show(ulong id, float heightMetres, bool walking);

    /// <summary>
    /// Before the snapshot <see cref="Show"/> built is published: lets go of what binds a walker it left out to its
    /// object, since that object may now show something else (its own art, a corpse) or nothing.
    /// </summary>
    void Publishing();

    /// <summary>Once the snapshot <see cref="Show"/> built has been accepted: plays what each walker now does and lets go of walkers it left out.</summary>
    void Settle();
}
