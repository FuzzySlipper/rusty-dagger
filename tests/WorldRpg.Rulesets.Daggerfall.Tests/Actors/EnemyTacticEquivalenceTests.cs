using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The legacy tactic decides exactly as enemies decided before the decision point existed. A seeded fight on the
/// developer combat start (its warrior and barbarian, with an orc shaman caster and an archer brought in beside them) runs
/// while the player circles in and out of every band, behind a pillar and past a blocked lane, and each enemy's traced
/// decisions, step by step with every roll and value, must equal the baseline recorded from the code before the seam.
/// Any drift in a motor roll, a maneuver, a spell pick or an attack's admission fails it, at the first line that differs.
/// </summary>
/// <remarks>The baseline is <see cref="BaselinePath"/>. Set WORLDRPG_WRITE_TACTIC_BASELINE=1 to write it again, only
/// when a change means to alter what the legacy tactic decides.</remarks>
public sealed class EnemyTacticEquivalenceTests
{
    private const string Bundle = "daggerfall.combat-start";
    private const int Steps = 600;
    private static readonly string BaselinePath = Path.Combine(TestData.RepositoryRoot,
        "tests", "WorldRpg.Rulesets.Daggerfall.Tests", "Actors", "EnemyTacticEquivalence.baseline.txt.gz");

    [Fact]
    public void The_legacy_tactic_decides_every_step_as_the_baseline_did()
    {
        List<string> lines = [];
        foreach (string enemy in new[] { "encounter-warrior", "encounter-barbarian", "orc-shaman", "archer" })
            lines.AddRange(Fight(enemy));
        string actual = string.Join('\n', lines) + "\n";

        // The fight reaches every part of the decision, or the comparison proves little.
        Assert.Contains(lines, line => line.StartsWith("orc-shaman ", StringComparison.Ordinal) && line.Contains("spell.ranged-roll:Passed", StringComparison.Ordinal)
            && line.Contains("spell.cast:Passed", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("orc-shaman ", StringComparison.Ordinal) && line.Contains("spell.touch-pick:Chose", StringComparison.Ordinal)
            && line.Contains("spell.cast:Passed", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("orc-shaman ", StringComparison.Ordinal) && line.Contains("spell.path:Refused:path-blocked", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("archer ", StringComparison.Ordinal) && line.Contains("motor.maneuver:Chose:ranged-", StringComparison.Ordinal)
            && line.Contains("attack.physical:Passed:began", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("encounter-warrior ", StringComparison.Ordinal) && line.Contains("attack.physical:Passed:began", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("motor.advance-roll:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("motor.strafe-roll:Passed", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("maneuver:Chose:retreat:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(" Search |", StringComparison.Ordinal) || line.Contains(" TargetLost |", StringComparison.Ordinal));

        if (Environment.GetEnvironmentVariable("WORLDRPG_WRITE_TACTIC_BASELINE") == "1")
        {
            using GZipStream written = new(File.Create(BaselinePath), CompressionLevel.SmallestSize);
            written.Write(Encoding.UTF8.GetBytes(actual));
            return;
        }
        using StreamReader baseline = new(new GZipStream(File.OpenRead(BaselinePath), CompressionMode.Decompress), Encoding.UTF8);
        string[] expected = baseline.ReadToEnd().Split('\n');
        string[] got = actual.Split('\n');
        for (int index = 0; index < Math.Min(expected.Length, got.Length); index++)
            Assert.True(expected[index] == got[index], $"Decision {index + 1} drifted.\nbaseline: {expected[index]}\nnow:      {got[index]}");
        Assert.Equal(expected.Length, got.Length);
    }

    /// <summary>One run of the fight, tracing <paramref name="traced"/>: each new decision of it, or repeat, as one line.</summary>
    private static IEnumerable<string> Fight(string traced)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile dungeon = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, dungeon);
        SpatialFake spatial = SpatialFake.Create(dungeon.Geometry.Spatial[0].Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        // Every playback completes as it advances, so a swing or shot clears and the next can begin.
        AppearanceFake appearance = new(releases) { AdvanceReceiptForAll = CompletedMarker(1) };
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service,
            random: HashedRandom.Create());
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), new TestDaggerfallRuleset(videosEnabled: true, gameSeed: 0),
            new GameBundleId(Bundle));
        product.Start();
        // Floor a metre below any straight-down look; a body overlaps another actor's clearance box.
        spatial.FloorHit = request => request.Direction.Y < -.99f
            ? default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY }
            : default;
        spatial.OverlapHit = request => new SpatialHit
        {
            Present = request.Entities.ToArray().Any(collider => request.Center.X >= collider.Min.X && request.Center.X <= collider.Max.X
                && request.Center.Z >= collider.Min.Z && request.Center.Z <= collider.Max.Z),
        };
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"begin"}""")]));
        FinishLoading(product, 1);
        DaggerfallSession game = RunningSession(product);
        ulong step = 2;
        for (; step < 5; step++) product.Update(new ProductUpdate(OuterUpdate(step), []));

        Vector3 centre = game.State.PlayerControl.Position!.Value.ToVector();
        Dictionary<string, long> fighters = game.DynamicActors.OrderBy(actor => actor.Key).TakeLast(2).ToDictionary(actor => actor.Value.Value, actor => actor.Key);
        fighters["orc-shaman"] = game.SpawnActor("orc-shaman", new ActorPose(WorldPoint.From(centre + new Vector3(3, 0, 0)), MathF.PI / 2), level: 8);
        fighters["archer"] = game.SpawnActor("archer", new ActorPose(WorldPoint.From(centre + new Vector3(-12, 0, 0)), -MathF.PI / 2), level: 5);
        foreach (long id in fighters.Values) game.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        // The hold's own population waits far off, so only the fight's four decide near the player.
        foreach (ActorState other in game.State.Actors.All.Where(actor => actor.DurableId != game.State.Actors.Player.DurableId && !fighters.ContainsValue(actor.DurableId)))
            other.ApplyPose(new(WorldPoint.From(centre + new Vector3(0, 0, 400)), 0));
        long warrior = fighters["encounter-warrior"];
        long tracedId = fighters[traced];
        game.EnemyBehavior.Trace.Follow(tracedId);

        // A ray meets the first actor body in its way, else the hold's wall thirty metres round the fight; a straight-down
        // look still finds the floor.
        spatial.FloorHit = request =>
        {
            if (request.Direction.Y < -.99f)
                return default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY };
            float nearest = (float)request.MaxDistance;
            ulong? struck = null;
            foreach (SpatialEntityCollider body in request.Entities.ToArray())
                if (RayBox(request.Origin, request.Direction, body.Min, body.Max) is float at && at < nearest) (nearest, struck) = (at, body.Entity);
            if (struck is null && Wall(request.Origin - centre, request.Direction) is float wall && wall < nearest) nearest = wall;
            else if (struck is null) return default;
            return new SpatialHit { Present = true, Kind = struck is null ? SpatialHitKind.StaticMesh : SpatialHitKind.Entity, Entity = struck ?? 0,
                Point = request.Origin + request.Direction * nearest, Distance = nearest };
        };
        // Every route is found, so a maneuver never turns into Blocked.
        PlanarNavCell[] path = [new(0, 0, 0), new(1, 0, 0)];
        spatial.NavigationResult = request => new(path, path.Select(_ => new NavigationPathEdge(NavigationEdgeKind.Walk)).ToArray(),
            NavigationPathOutcome.Reached, request.From, path[1], NavigationEdgeKind.Walk, 0, 1, 2, 1, 1, 1, false, default, default);
        ulong first = step;
        // The Engine's sight: in range and inside the observer's cone, unless the pillar stands between them.
        perception.Responder = request => Receipt([.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
        {
            Vector3 toward = target.Center - observer.Origin;
            double distance = toward.Length();
            double facing = distance > 0 ? Vector3.Dot(Vector3.Normalize(observer.Forward), toward / (float)distance) : 1d;
            bool pillar = step - first is >= 260 and < 330;
            PerceptionPairKind kind = distance > observer.MaximumDistance || pillar ? PerceptionPairKind.Occluded
                : facing < observer.MinimumFacingCosine ? PerceptionPairKind.FacingRejected : PerceptionPairKind.Visible;
            return new PerceptionPair(observer.Entity, target.Entity, distance, facing, kind, kind == PerceptionPairKind.Visible ? 1d : 0d);
        }))]);
        // The warrior steps into every missile lane for a while.
        spatial.CapsuleCastHit = request => step - first is >= 225 and < 255
            ? new SpatialHit { Present = true, Kind = SpatialHitKind.Entity, Entity = game.State.Actors.Get(warrior).Actor.Entity.Value }
            : default;

        HashSet<(DaggerfallDecisionRecord, int)> seen = [];
        for (int index = 0; index < Steps; index++, step++)
        {
            // The player circles the fight, in to touch and out past the casting band, facing its middle.
            double angle = index * 2 * Math.PI / 450;
            double radius = 1.2 + 15 * (.5 + .5 * Math.Cos(index * 2 * Math.PI / 300));
            Vector3 at = centre + new Vector3((float)(Math.Cos(angle) * radius), 0, (float)(Math.Sin(angle) * radius));
            game.State.PlayerControl.MoveTo(at);
            game.State.PlayerControl.YawRadians = ActorHeading.Yaw(centre - at);
            // Each fighter turns to the player where it stands, as its controller would.
            foreach (long id in fighters.Values)
            {
                ActorState fighter = game.State.Actors.Get(id);
                if (!fighter.IsDefeated) fighter.ApplyPose(new(fighter.Position, ActorHeading.Yaw(at - fighter.Position.ToVector())));
            }
            product.Update(new ProductUpdate(OuterUpdate(step), []));
            foreach (DaggerfallDecisionRecord record in game.EnemyBehavior.Trace.Recent)
                if (seen.Add((record, record.Repeats)))
                    yield return Line(traced, record);
        }
    }

    /// <summary>How far along a ray it enters a box, if it does.</summary>
    private static float? RayBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
    {
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = direction[axis];
            if (MathF.Abs(d) < 1e-6f) { if (o < min[axis] || o > max[axis]) return null; continue; }
            float a = (min[axis] - o) / d, b = (max[axis] - o) / d;
            near = MathF.Max(near, MathF.Min(a, b));
            far = MathF.Min(far, MathF.Max(a, b));
            if (near > far) return null;
        }
        return near;
    }

    /// <summary>How far along a ray from <paramref name="offset"/> (from the fight's middle) it meets the ring wall.</summary>
    private static float? Wall(Vector3 offset, Vector3 direction)
    {
        Vector2 o = new(offset.X, offset.Z), d = new(direction.X, direction.Z);
        float a = Vector2.Dot(d, d), b = 2 * Vector2.Dot(o, d), c = Vector2.Dot(o, o) - 30 * 30;
        float disc = b * b - 4 * a * c;
        return a < 1e-6f || disc < 0 ? null : (-b + MathF.Sqrt(disc)) / (2 * a);
    }

    private static string Line(string actor, DaggerfallDecisionRecord record)
    {
        StringBuilder line = new($"{actor} {record.LastStep} {record.State} |");
        foreach (DaggerfallDecisionGate gate in record.Gates)
        {
            line.Append(' ').Append(gate.Policy).Append('.').Append(gate.Gate).Append(':').Append(gate.Outcome).Append(':').Append(gate.Reason);
            foreach (DaggerfallGateValue value in gate.Values)
                line.Append(',').Append(value.Name).Append('=').Append(value.Value.ToString("0.###", CultureInfo.InvariantCulture));
            line.Append(';');
        }
        return line.ToString();
    }

    /// <summary>Every keyed draw a fixed hash of its request, so each roll is spread over its range and the same in every run.</summary>
    private class HashedRandom : SessionRandom
    {
        internal new static IRandomService Create() => DispatchProxy.Create<IRandomService, HashedRandom>();

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed) || arguments![0] is not KeyedRngRequest request) return base.Invoke(method, arguments);
            ulong hash = 14695981039346656037UL;
            foreach (char value in request.ToString()) hash = unchecked((hash ^ value) * 1099511628211UL);
            long span = (long)request.Maximum - request.Minimum + 1;
            return new KeyedRngReceipt(request.Minimum + (long)(hash % (ulong)span));
        }
    }
}
