using System.Numerics;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The developer staging of walking townspeople (people.stage, people.cross, people.unstage): the nearest loaded ones
/// are moved in front of the player and given a route their wander follows (<see cref="WanderMemoryComponent.Stage"/>),
/// so a handful can be looked at close up wherever the player stands. Their own wander resumes on unstage.
/// </summary>
/// <param name="actors">The actors, whose walking civilians are staged.</param>
/// <param name="walks">Whether an actor is one the population walks (a source civilian of the active site).</param>
/// <param name="player">The player, in front of whom they are staged.</param>
/// <param name="standingAt">Where an actor can stand at a point, clear of all but the actors being moved: the floor there, or null (the placement's).</param>
internal sealed class DaggerfallPeopleStage(ActorsState actors, Func<long, bool> walks, PlayerControlState player,
    Func<Vector3, IReadOnlySet<long>, WorldPoint?> standingAt) : IDeveloperPeopleStageCommands
{
    /// <summary>Crossing walkers keep this far apart front to back, so their paths never meet.</summary>
    private const float RowMetres = 1.5F;

    public DebugCommandResult Stage(int count, double metres, double spread) => Place(count, metres, spread, crossing: false, new HashSet<long>(), out _);

    public DebugCommandResult Cross(int count, double metres, double spread) => Place(count, metres, spread, crossing: true, new HashSet<long>(), out _);

    public DebugCommandResult Unstage()
    {
        int released = 0;
        foreach (ActorState actor in Civilians().Where(actor => actor.Wander.Route is not null))
        {
            actor.Wander.Unstage();
            released++;
        }
        return DebugCommandResult.Success($"{released} walking people wander on their own again.");
    }

    public DebugCommandResult StageEnemies(int count, double metres, double spread)
    {
        if (count is < 1 or > 32 || !(metres is >= 1 and <= 50) || !(spread is >= 0 and <= 40))
            return DaggerfallDeveloperReplies.Refused("enemies.stage takes COUNT 1 to 32, METRES 1 to 50 and SPREAD 0 to 40.");
        if (player.Position is not WorldPoint standing) return DaggerfallDeveloperReplies.Refused("The player has no position.");
        ActorState[] foes = [.. actors.All.Where(actor => actor.DurableId != actors.Player.DurableId && !actor.IsDefeated
            && actor.Actor.TryGet<ActorRenderComponent>(out ActorRenderComponent? render)
            && render.Spec.Kind == ActorVisualKind.DirectionalMobile)
            .OrderBy(actor => actor.Position.HorizontalDistanceTo(standing))];
        if (foes.Length == 0) return DaggerfallDeveloperReplies.Refused("No live enemies are loaded here.");
        int n = Math.Min(count, foes.Length);
        Vector3 origin = standing.ToVector(), forward = ActorHeading.Forward(player.YawRadians), across = new(-forward.Z, 0F, forward.X);
        float width = (float)spread;
        HashSet<long> moving = [.. foes.Take(n).Select(actor => actor.DurableId)];
        WorldPoint[] spots = new WorldPoint[n];
        const int perRow = 5;
        for (int i = 0; i < n; i++)
        {
            Vector3 pt;
            if (n <= perRow)
            {
                float offset = n == 1 ? 0F : (i - (n - 1) / 2F) * width / (n - 1);
                pt = origin + forward * (float)metres + across * offset;
            }
            else
            {
                int rowIndex = i / perRow, colIndex = i % perRow, inRow = Math.Min(perRow, n - rowIndex * perRow);
                float offset = inRow == 1 ? 0F : (colIndex - (inRow - 1) / 2F) * width / Math.Max(1, perRow - 1);
                float stagger = (rowIndex % 2 == 1) ? width / (perRow * 2.5F) : 0F;
                pt = origin + forward * ((float)metres + rowIndex * RowMetres) + across * (offset + stagger);
            }
            if (standingAt(pt, moving) is not { } grounded)
                return DaggerfallDeveloperReplies.Refused($"No clear ground for enemy {i + 1} {metres:0.#} m ahead; face open ground or come closer.");
            spots[i] = grounded;
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 start = spots[i].ToVector();
            foes[i].ApplyPose(new ActorPose(spots[i], ActorHeading.Yaw(origin - start)));
        }
        return DebugCommandResult.Success($"{n} enemies stand in front of the player from {metres:0.#} m ahead, facing them.");
    }

    /// <summary>
    /// Stages the <paramref name="count"/> nearest walkers but <paramref name="skip"/>: standing in a line
    /// <paramref name="metres"/> ahead, or crossing the view in rows from there. Refused, moving nobody, when too few
    /// walk here or the ground is not clear; <paramref name="placed"/> holds those staged.
    /// </summary>
    internal DebugCommandResult Place(int count, double metres, double spread, bool crossing, IReadOnlySet<long> skip, out ActorState[] placed)
    {
        placed = [];
        if (count is < 1 or > 32 || !(metres is >= 1 and <= 50) || !(spread is >= 0 and <= 40) || crossing && spread == 0)
            return DaggerfallDeveloperReplies.Refused($"people.{(crossing ? "cross" : "stage")} takes COUNT 1 to 32, METRES 1 to 50 and SPREAD 0 to 40{(crossing ? " (above 0)" : "")}.");
        if (player.Position is not WorldPoint standing) return DaggerfallDeveloperReplies.Refused("The player has no position.");
        ActorState[] walking = [.. Civilians().Where(actor => walks(actor.DurableId) && !skip.Contains(actor.DurableId))
            .OrderBy(actor => actor.Wander.State == WanderState.Unloaded ? 1 : 0)
            .ThenBy(actor => actor.Position.HorizontalDistanceTo(standing))];
        if (walking.Length < count)
            return DaggerfallDeveloperReplies.Refused($"Only {walking.Length} walking people are loaded here.");
        foreach (ActorState other in walking.Skip(count).Where(actor => actor.Wander.Route is not null)) other.Wander.Unstage();

        Vector3 origin = standing.ToVector(), forward = ActorHeading.Forward(player.YawRadians), across = new(-forward.Z, 0F, forward.X);
        float width = (float)spread;
        // Every walker's route on clear ground, worked out before anyone moves.
        WorldPoint[][] routes = new WorldPoint[count][];
        HashSet<long> moving = [.. walking.Take(count).Select(actor => actor.DurableId)];
        for (int i = 0; i < count; i++)
        {
            Vector3[] points;
            if (crossing)
            {
                // Rows from METRES back, each walking the other way to the one before it.
                float side = i % 2 == 0 ? 1F : -1F;
                Vector3 row = origin + forward * ((float)metres + i * RowMetres);
                points = [row - across * (side * width / 2), row + across * (side * width / 2)];
            }
            else if (count <= 6)
            {
                float offset = count == 1 ? 0F : (i - (count - 1) / 2F) * width / (count - 1);
                points = [origin + forward * (float)metres + across * offset];
            }
            else
            {
                const int perRow = 6;
                int rowIndex = i / perRow, colIndex = i % perRow, inRow = Math.Min(perRow, count - rowIndex * perRow);
                float offset = inRow == 1 ? 0F : (colIndex - (inRow - 1) / 2F) * width / Math.Max(1, perRow - 1);
                float stagger = (rowIndex % 2 == 1) ? width / (perRow * 2.5F) : 0F;
                points = [origin + forward * ((float)metres + rowIndex * RowMetres) + across * (offset + stagger)];
            }
            WorldPoint?[] grounded = [.. points.Select(point => standingAt(point, moving))];
            if (grounded.Any(point => point is null))
                return DaggerfallDeveloperReplies.Refused($"No clear ground for walker {i + 1} {metres:0.#} m ahead; face open ground or come closer.");
            routes[i] = [.. grounded.Select(point => point!.Value)];
        }
        for (int i = 0; i < count; i++)
        {
            Vector3 start = routes[i][0].ToVector();
            Vector3 facing = routes[i].Length > 1 ? routes[i][1].ToVector() - start : origin - start;
            walking[i].ApplyPose(new ActorPose(routes[i][0], ActorHeading.Yaw(facing)));
            walking[i].Wander.Stage(routes[i]);
        }
        placed = walking[..count];
        return DebugCommandResult.Success(crossing
            ? $"{count} walking people cross the view {width:0.#} m wide, in rows from {metres:0.#} m ahead."
            : $"{count} walking people stand {metres:0.#} m ahead in a line {width:0.#} m wide, facing the player.");
    }

    private IEnumerable<ActorState> Civilians() =>
        actors.All.Where(actor => actor.Actor.Has<WanderMemoryComponent>() && !actor.IsDefeated);
}
