using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What <see cref="DaggerfallNearbySpawn.Bring"/> did: the actors brought, and why it brought no more.</summary>
internal sealed record DaggerfallNearbySpawnResult(long[] Brought, string? Refusal);

/// <summary>
/// Brings actors of a published definition in close around the player: each on clear floor with room for its whole
/// body, clear of the player and everyone else, with nothing between it and the player, ahead of them where there is
/// room and anywhere around them otherwise, facing them. An enemy comes hostile and knowing where the player is, so it
/// comes for them at once rather than waiting to see them in the dark. The developer's <c>world.spawn</c>
/// brings them, and so does a start that names opening foes, as soon as the floor it begins on is there.
/// </summary>
/// <param name="actors">Every published actor definition by id.</param>
/// <param name="presented">Whether the active place can draw a mobile (its sprites are admitted there).</param>
/// <param name="spawn">Registers one actor of a definition at a pose (the roster's spawn).</param>
/// <param name="turnOnPlayer">Turns an enemy on the player: hostile, and provoked so it knows where they are.</param>
/// <param name="report">Puts a line on the message line.</param>
internal sealed class DaggerfallNearbySpawn(IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, Func<int, bool> presented,
    DaggerfallActorPlacement placement, PlayerControlState player, Func<string, ActorPose, long> spawn, Action<long> turnOnPlayer, Action<string> report)
{
    /// <summary>The nearest an actor is brought, so it never stands on top of the player.</summary>
    internal const float MinimumMetres = 2F;
    /// <summary>The farthest an actor is brought.</summary>
    internal const float MaximumMetres = 30F;
    /// <summary>The most actors brought at once.</summary>
    internal const int MaximumCount = 8;
    /// <summary>How many simulation steps a start's opening foes look for clear floor before giving up.</summary>
    internal const int ExpectedSteps = 60;
    private const string RandomScope = "daggerfall.nearby-spawn";

    private int _sequence;
    private DaggerfallStartFoes? _expected;
    private int _expectedSteps;

    /// <summary>
    /// Brings <paramref name="count"/> of <paramref name="actor"/> to between half <paramref name="metres"/> (at least
    /// <see cref="MinimumMetres"/>) and <paramref name="metres"/> from the player. Each is placed before the next is
    /// looked for, so they stand clear of each other; the refusal says why no more could be brought.
    /// </summary>
    internal DaggerfallNearbySpawnResult Bring(string actor, int count, float metres)
    {
        if (count is < 1 or > MaximumCount) return new([], $"A count is 1 to {MaximumCount}.");
        if (!(metres is >= MinimumMetres and <= MaximumMetres)) return new([], $"A distance is {MinimumMetres:0} to {MaximumMetres:0} metres.");
        if (!actors.TryGetValue(new DaggerfallActorId(actor), out DaggerfallActorDefinition? definition) || definition.Kind == DaggerfallActorKinds.Player)
            return new([], $"'{actor}' is no actor definition that can be spawned.");
        if (definition.MobileId is int mobile && !presented(mobile)) return new([], $"'{actor}' cannot be drawn here: mobile {mobile} is not admitted in this place.");
        if (player.Position is not WorldPoint standing) return new([], "The player has no position.");
        int sequence = _sequence++;
        float nearest = MathF.Max(MinimumMetres, metres / 2F);
        // Looked for from a step above the feet, so a low ledge does not stop the look and lower floor is found too.
        WorldPoint origin = WorldPoint.From(standing.ToVector() + Vector3.UnitY * DaggerfallActorPlacement.SummonSeparation);
        List<long> brought = [];
        for (int index = 0; index < count; index++)
        {
            string operation = $"bring:{sequence}:{index}";
            if (!placement.TrySpawnPose(operation, RandomScope, origin, nearest, metres, out ActorPose pose, SpawnDirection.Ahead)
                && !placement.TrySpawnPose(operation + ":around", RandomScope, origin, nearest, metres, out pose, SpawnDirection.Any))
                return new([.. brought], $"No clear floor within {metres:0.#} m of the player, in sight of them, for {(count - index == 1 ? "one more" : $"{count - index} more")}.");
            long id = spawn(actor, pose);
            if (definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass) turnOnPlayer(id);
            brought.Add(id);
        }
        return new([.. brought], null);
    }

    /// <summary>A new game's opening foes, brought by <see cref="BringExpected"/> once the floor it begins on is there.</summary>
    internal void Expect(DaggerfallStartFoes? foes)
    {
        _expected = foes;
        _expectedSteps = 0;
    }

    /// <summary>
    /// Brings the expected opening foes, each in turn, once there is clear floor for them; after
    /// <see cref="ExpectedSteps"/> steps without it, says on the message line which were not brought and why.
    /// </summary>
    internal void BringExpected()
    {
        if (_expected is not { } foes) return;
        for (int index = 0; index < foes.Actors.Length; index++)
        {
            DaggerfallNearbySpawnResult result = Bring(foes.Actors[index], 1, foes.Metres);
            if (result.Refusal is null) continue;
            _expected = foes with { Actors = foes.Actors[index..] };
            if (++_expectedSteps < ExpectedSteps) return;
            report($"The start's foes {string.Join(", ", foes.Actors[index..])} were not brought: {result.Refusal}");
            break;
        }
        _expected = null;
    }
}
