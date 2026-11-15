using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Interaction;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Read-only inspection of the interaction and spatial owners the player moves through (interaction.*, spatial.grid,
/// spatial.probe, spatial.clearance, playtest.jump-plan). A probe or clearance answer is also drawn as the overlays'
/// probe set. Nothing here moves the player or advances time.
/// </summary>
/// <param name="engine">The Engine, whose spatial service the snapshots read.</param>
/// <param name="generation">The latest admitted update's generation, or null before one.</param>
/// <param name="step">The latest admitted simulation step, or null before one.</param>
internal sealed class DaggerfallSpatialInspection(IEngineContext engine, SpatialMovementSystem spatial, DaggerfallSiteCrossing sites,
    PlayerControlState player, PlayerInputSystem input, ActorsState actors, DaggerfallActivation activation, DaggerfallPlayerControls controls,
    Func<ulong?> generation, Func<ulong?> step, DaggerfallDebugOverlays overlays)
{
    internal WorldInteraction CreateInteractionInspection() =>
        (activation.Module ?? throw new InvalidOperationException("The activation owner is unavailable."))
            .CreateInspection(actors.Player.Actor.Entity, player, input.ResolveCurrentLook(player));

    internal DebugCommandResult ReadGrid(int radius, int verticalRadius, double cellSize)
    {
        if (radius is < 0 or > 15 || verticalRadius is < 0 or > 15 || !double.IsFinite(cellSize) || cellSize <= 0)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Grid radii must be 0–15 and cell size positive and finite.");
        Vector3 center = (player.Position ?? throw new InvalidOperationException("Spatial inspection requires a player position.")).ToVector();
        Vector3 origin = center - new Vector3((float)((radius + .5) * cellSize),
            (float)((verticalRadius + .5) * cellSize), (float)((radius + .5) * cellSize));
        uint width = (uint)(radius * 2 + 1), layers = (uint)(verticalRadius * 2 + 1);
        return DebugCommandResult.Success(SpatialGridSnapshot.Capture(engine.Spatial,
            new SpatialMapRequest(spatial.Session, origin, cellSize, width, width,
                origin.Y, origin.Y + cellSize, origin.Y, origin.Y + cellSize, ReadOnlyMemory<SpatialEntityCollider>.Empty),
            layers, new SpatialMapObservation($"{generation()}:{step()}", center,
                input.ResolveCurrentLook(player).Forward)));
    }

    internal DebugCommandResult ReadProbe(double distance)
    {
        if (!double.IsFinite(distance) || distance is <= 0 or > 8)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Probe distance must be finite and within (0, 8].");
        var config = spatial.CurrentController;
        float height = player.Motion.Stance == CharacterStance.Crouched ? config.Shape.CrouchedHeight : config.Shape.StandingHeight;
        Vector3 feet = (player.Position ?? throw new InvalidOperationException("Spatial inspection requires a player position.")).ToVector() - Vector3.UnitY * (height * .5f);
        string probe = PlaytestTraversal.Probe(engine.Spatial, spatial.Session,
            feet, height, config.Surface.MaximumStepHeight, (float)distance, SpatialMovementSystem.SpatialColliders(LiveCollision()));
        overlays.PublishProbe(probe);
        return DebugCommandResult.Success(probe);
    }

    internal DebugCommandResult ReadClearance(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Clearance target must be finite.");
        var config = spatial.CurrentController;
        Vector3 center = (player.Position ?? throw new InvalidOperationException("Spatial inspection requires a player position.")).ToVector();
        float height = player.Motion.Stance == CharacterStance.Crouched ? config.Shape.CrouchedHeight : config.Shape.StandingHeight;
        string clearance = SpatialClearanceSnapshot.Capture(engine.Spatial, spatial.Session, center, new((float)x, (float)y, (float)z),
            height, config, SpatialMovementSystem.SpatialColliders(LiveCollision()));
        overlays.PublishProbe(clearance);
        return DebugCommandResult.Success(clearance);
    }

    internal DebugCommandResult ReadJumpPlan(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Jump target must be finite.");
        var config = spatial.CurrentController;
        Vector3 center = (player.Position ?? throw new InvalidOperationException("Jump inspection requires a player position.")).ToVector();
        float height = player.Motion.Stance == CharacterStance.Crouched ? config.Shape.CrouchedHeight : config.Shape.StandingHeight;
        var plan = PlaytestTraversal.JumpToward(center - Vector3.UnitY * (height * .5f),
            input.ResolveCurrentLook(player).Forward, new Vector3((float)x, (float)y, (float)z),
            config, player.Motion.Grounded,
            controls.Settings.KeysFor("jump").First(), controls.Settings.KeysFor("move.forward").First());
        return DebugCommandResult.Success(JsonSerializer.Serialize(plan, DaggerfallPlaytestJsonContext.Default.PlaytestJumpPlan));
    }

    /// <summary>The collision movement meets now: the admitted scene plus the live doors and supports.</summary>
    private CharacterStepEnvironment LiveCollision() => sites.Exterior.CharacterEnvironment(player.Motion);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlaytestJumpPlan))]
internal partial class DaggerfallPlaytestJsonContext : JsonSerializerContext;
