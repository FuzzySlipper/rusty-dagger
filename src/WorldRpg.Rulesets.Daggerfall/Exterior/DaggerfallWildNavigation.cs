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
/// update, again once the player has gone far enough from the box's middle or a while has passed (the ground streams in
/// behind the player), and cleared indoors.
/// </summary>
internal sealed class DaggerfallWildNavigation(ISpatialService spatial, SpatialMovementSystem movement, DaggerfallWildNavigationTuning tuning)
{
    /// <summary>The wild grid's id, beside a location's composed grid on its own.</summary>
    internal const ulong GridId = 2;

    private Vector3? _centre;
    private double _sinceSeconds;
    private bool _pending;
    private bool _published;
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
    /// for once the player has gone <see cref="DaggerfallWildNavigationTuning.MoveDistance"/> from the box's middle or
    /// <see cref="DaggerfallWildNavigationTuning.RefreshSeconds"/> have passed.
    /// </summary>
    internal void Update(WorldPoint? player, bool outdoors, double seconds)
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
        _sinceSeconds += seconds;
        Vector3 at = feet.ToVector();
        if (_centre is { } centre && Vector2.Distance(new(at.X, at.Z), new(centre.X, centre.Z)) < tuning.MoveDistance
            && _sinceSeconds < tuning.RefreshSeconds)
            return;
        Derive(at);
    }

    private void Derive(Vector3 feet)
    {
        // The vertical range snaps to whole reaches, so a box that follows the player keeps it while it walks level.
        float floor = MathF.Floor(feet.Y / tuning.VerticalReach) * tuning.VerticalReach;
        Vector3 min = new(feet.X - tuning.Radius, floor - tuning.VerticalReach, feet.Z - tuning.Radius);
        Vector3 max = new(feet.X + tuning.Radius, floor + 2 * tuning.VerticalReach, feet.Z + tuning.Radius);
        CollisionNavigationConfig config = spatial.DefaultCollisionNavigationConfig() with
        {
            GridId = GridId,
            CellSize = tuning.CellSize,
            MaximumCells = tuning.Cells,
            Character = movement.CurrentController,
        };
        _ = spatial.ReplaceCollisionNavigation(new CollisionNavigationReplaceRequest(movement.Session, min, max, config, Deferred: true));
        _pending = true;
        _centre = feet;
        _sinceSeconds = 0;
        Derivations++;
    }

    private void Clear()
    {
        if (_published || _pending) spatial.ClearNavigation(new NavigationClearRequest(movement.Session, GridId));
        _published = _pending = false;
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
/// <param name="RefreshSeconds">How long, in seconds, before the grid is derived again where the player stands, as the ground streams in.</param>
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
