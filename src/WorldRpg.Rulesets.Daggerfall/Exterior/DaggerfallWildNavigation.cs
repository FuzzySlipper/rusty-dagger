using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The wild grid: navigation the Engine derives from live collision (the streamed ground's heightfield, rocks, trees and
/// buildings alike) in a box about the player outdoors, so a walker past a location's own navigation paths around what
/// stands in its way. It stands beside the location's composed grid on its own id (rusty-engine #10942): a walker's step
/// tries the location's grid first and this one after (<see cref="Grids"/>). It is derived on Engine workers off the
/// update, again once the player has gone far enough from the box's middle, or after a while once collision has
/// changed since (the ground streams in behind the player), and cleared indoors. None is derived while the player
/// stands on the location's own grid, which carries the walkers about it there.
/// </summary>
internal sealed class DaggerfallWildNavigation(ISpatialService spatial, SpatialMovementSystem movement, DaggerfallWildNavigationTuning tuning)
{
    /// <summary>The wild grid's id, beside a location's composed grid on its own.</summary>
    internal const ulong GridId = 2;

    /// <summary>How far past a standing body's centre a point may lie above the floor and still stand on the grid, in metres.</summary>
    private const double CentreMargin = .25;

    private Vector3? _centre;
    private double _sinceSeconds;
    private bool _pending;
    private bool _published;
    private bool _onComposition;
    private ulong _collisionRevision;
    private readonly ulong[] _both = new ulong[2], _composition = new ulong[1];

    /// <summary>The receipt of the derivation the grid holds now, with its cost; null before the first is installed.</summary>
    internal CollisionNavigationReplaceReceipt? Installed { get; private set; }

    /// <summary>How many derivations were asked for since the player went outdoors.</summary>
    internal int Derivations { get; private set; }

    /// <summary>Whether a derivation is running or waiting to install.</summary>
    internal bool Pending => _pending;

    /// <summary>Where the box the grid covers is centred, in the session's frame, or null when there is none.</summary>
    internal Vector3? Centre => _centre;

    internal DaggerfallWildNavigationTuning Tuning => tuning;

    /// <summary>Whether the grid is derived at all, for a developer comparing its cost; never saved.</summary>
    internal bool Enabled { get; set; } = true;

    /// <summary>The grids a walker's step tries in order: the location's own, then the wild grid where there is one.</summary>
    internal IReadOnlyList<ulong> Grids(ulong composition)
    {
        _both[0] = _composition[0] = composition;
        _both[1] = GridId;
        return _published ? _both : _composition;
    }

    /// <summary>
    /// One admitted update: indoors the grid is cleared; outdoors a finished derivation is taken up, and another asked
    /// for once the player has gone <see cref="DaggerfallWildNavigationTuning.MoveDistance"/> from the box's middle, or
    /// once <see cref="DaggerfallWildNavigationTuning.RefreshSeconds"/> have passed and collision has changed since. While
    /// the player stands on the <paramref name="composition"/> grid none is asked for; the one it has, if any, stays.
    /// </summary>
    internal void Update(WorldPoint? player, bool outdoors, double seconds, ulong composition)
    {
        if (!outdoors || !Enabled || player is not WorldPoint feet)
        {
            Clear();
            return;
        }
        if (_pending)
        {
            CollisionNavigationReadout readout = spatial.ReadCollisionNavigation(new CollisionNavigationReadRequest(movement.Session, GridId));
            if (readout.Pending) return;
            _pending = false;
            if (!readout.Refused)
            {
                Installed = readout.Installed;
                _published = true;
            }
        }
        Vector3 at = feet.ToVector();
        if (_onComposition)
        {
            if (OnComposition(at, composition)) return;
            _onComposition = false;
            Derive(at);
            return;
        }
        _sinceSeconds += seconds;
        if (_centre is { } centre && Vector2.Distance(new(at.X, at.Z), new(centre.X, centre.Z)) < tuning.MoveDistance)
        {
            if (_sinceSeconds < tuning.RefreshSeconds) return;
            _sinceSeconds = 0;
            // Nothing has streamed in or out since the last derivation: it would derive the same grid.
            if (CollisionRevision == _collisionRevision) return;
        }
        if (OnComposition(at, composition))
        {
            _onComposition = true;
            return;
        }
        Derive(at);
    }

    private ulong CollisionRevision => spatial.ReadProjection(new(movement.Session)).CollisionRevision;

    /// <summary>Whether the player stands on the location's own grid.</summary>
    private bool OnComposition(Vector3 feet, ulong composition) => spatial.EvaluateNavigationStep(new NavigationStepRequest(movement.Session,
        feet, feet, 1F, 1, composition)).Outcome is not (NavigationPathOutcome.StartNotWalkable or NavigationPathOutcome.GoalNotWalkable
        or NavigationPathOutcome.ProjectionUnavailable);

    private void Derive(Vector3 feet)
    {
        // The vertical range snaps to whole reaches, so a box that follows the player keeps it while it walks level.
        float floor = MathF.Floor(feet.Y / tuning.VerticalReach) * tuning.VerticalReach;
        Vector3 min = new(feet.X - tuning.Radius, floor - tuning.VerticalReach, feet.Z - tuning.Radius);
        Vector3 max = new(feet.X + tuning.Radius, floor + 2 * tuning.VerticalReach, feet.Z + tuning.Radius);
        CharacterControllerConfig character = movement.CurrentController;
        CollisionNavigationConfig config = spatial.DefaultCollisionNavigationConfig() with
        {
            GridId = GridId,
            CellSize = tuning.CellSize,
            MaximumCells = tuning.Cells,
            Character = character,
            // Open ground is crossed on the diagonal too, not in a staircase of straight steps.
            DiagonalNeighbors = true,
            // A walker chases the player's centre, half its standing body above its feet: that point stands on the grid.
            SnapAbove = character.Shape.StandingHeight * .5 + character.Shape.ContactSkin + CentreMargin,
        };
        _ = spatial.ReplaceCollisionNavigation(new CollisionNavigationReplaceRequest(movement.Session, min, max, config, Deferred: true));
        _pending = true;
        _collisionRevision = CollisionRevision;
        _centre = feet;
        _sinceSeconds = 0;
        Derivations++;
    }

    private void Clear()
    {
        if (_published || _pending) spatial.ClearNavigation(new NavigationClearRequest(movement.Session, GridId));
        _published = _pending = _onComposition = false;
        _centre = null;
        _sinceSeconds = 0;
        Installed = null;
        Derivations = 0;
    }
}

/// <summary>The wild grid's rule values (<see cref="DaggerfallWildNavigation"/>), the <c>wildNavigation</c> tuning section.</summary>
/// <param name="Radius">How far about the player the grid reaches, in metres, east and west and north and south.</param>
/// <param name="CellSize">Each cell's width, in metres.</param>
/// <param name="MoveDistance">How far the player goes from the box's middle, in metres, before the grid is derived about it again.</param>
/// <param name="RefreshSeconds">How long, in seconds, before the grid is derived again where the player stands, if collision has changed since (the ground streams in).</param>
/// <param name="VerticalReach">The box's height band, in metres: a reach below the player's band, the band, and a reach above it.</param>
internal sealed record DaggerfallWildNavigationTuning(float Radius, float CellSize, float MoveDistance, double RefreshSeconds, float VerticalReach)
{
    /// <summary>The most columns one derivation may cover.</summary>
    private const uint MostCells = 65_536;

    internal static DaggerfallWildNavigationTuning Default { get; } = new(96F, 1F, 32F, 10d, 32F);

    /// <summary>The columns a box of this radius and cell holds.</summary>
    internal uint Cells => (uint)Math.Pow(Math.Ceiling(2 * Radius / CellSize) + 1, 2);

    internal DaggerfallWildNavigationTuning Validate()
    {
        float[] positive = [Radius, CellSize, MoveDistance, VerticalReach];
        if (positive.Any(value => !float.IsFinite(value) || value <= 0) || !double.IsFinite(RefreshSeconds) || RefreshSeconds <= 0)
            throw new ArgumentException("The wild grid's radius, cell, move distance, refresh and vertical reach are above 0.");
        if (MoveDistance >= Radius) throw new ArgumentException("The wild grid is derived again before the player walks off it: its move distance is under its radius.");
        if (Cells > MostCells) throw new ArgumentException($"The wild grid covers at most {MostCells} cells; its radius and cell give {Cells}.");
        return this;
    }

    internal static DaggerfallWildNavigationTuning Read(JsonElement e) => new DaggerfallWildNavigationTuning(
        e.GetProperty("radius").GetSingle(), e.GetProperty("cellSize").GetSingle(), e.GetProperty("moveDistance").GetSingle(),
        e.GetProperty("refreshSeconds").GetDouble(), e.GetProperty("verticalReach").GetSingle()).Validate();
}
