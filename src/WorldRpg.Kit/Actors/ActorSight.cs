using System.Numerics;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Kit.Actors;

/// <summary>
/// The line sight runs along between an observer and a target, each standing at the feet its pose names. A line
/// between two floor contacts grazes the ground they stand on, so on uneven ground it reads as blocked; the ruleset
/// knows how tall each body is and lifts the line off the floor.
/// </summary>
public interface IActorSight
{
    /// <summary>The line from the observer standing at <paramref name="from"/> to the target standing at <paramref name="to"/>.</summary>
    (Vector3 From, Vector3 To) Line(long observer, WorldPoint from, long target, WorldPoint to);
}

/// <summary>Sight between the poses themselves, for a product whose poses already name the points it sees between.</summary>
public sealed class PoseSight : IActorSight
{
    public static PoseSight Instance { get; } = new();

    public (Vector3 From, Vector3 To) Line(long observer, WorldPoint from, long target, WorldPoint to) => (from.ToVector(), to.ToVector());
}
