using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private bool QuestFoeRestrained(long id) => State.Quests.IsFoeRestrained(id);
    internal string? EffectiveFoeTeam(long id) => _roster.Definitions.TryGetValue(id, out var definition)
        ? State.Quests.FoeTeam(id, definition.Team) : null;

    private void ApplyAllEnemyCommand(bool clear)
    {
        foreach (long id in _roster.Definitions.Where(pair => pair.Value.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
            .Select(pair => pair.Key).ToArray())
        {
            if (clear) _roster.RemoveQuestActor(id);
            else _enemyBehavior.MakeHostile(id, includePlayerAllies: true);
        }
    }

    private PursuitTarget? SelectQuestAwareEnemyTarget(long observerId)
    {
        if (!State.Actors.TryGet(observerId, out var observer) || observer.IsDefeated || State.Quests.IsFoeRestrained(observerId)) return null;
        if (EffectiveFoeTeam(observerId) == "player-ally")
            return State.Quests.AllowsFoeInfighting(observerId) ? SelectAllyTarget(observerId) : null;
        var player = State.PlayerControl.Position;
        PursuitTarget? selected = player is { } position ? new(DaggerfallActorIdentity.PlayerEntityId, position) : null;
        if (!State.Quests.AllowsFoeInfighting(observerId) || !State.Quests.HasInfightingFoes) return selected;
        double best = player is { } target ? Vector3.Distance(observer.Position.ToVector(), target.ToVector()) : double.PositiveInfinity;
        Vector3 forward = ActorHeading.Forward(observer.HeadingYawRadians);
        string? team = EffectiveFoeTeam(observerId);
        foreach (var pair in QueryEnemies(observerId, observer.Position, forward,
            Math.Max(_tuning.EnemyBehavior.DetectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius),
            DaggerfallPerceptionQueryDefaults.MinimumFacingCosine, _ => true)
            .Where(pair => pair.Kind == PerceptionPairKind.Visible).OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target))
        {
            long id = checked((long)pair.Target);
            // Classic quest foes are excluded from AI fights unless the source explicitly opts in.
            // Other actors keep their established player pursuit unless participating in that opt-in.
            if (State.Quests.FoeRelationsFor(observerId)?.Infighting != true && State.Quests.FoeRelationsFor(id)?.Infighting != true) continue;
            if (!State.Quests.AllowsFoeInfighting(id) || EffectiveFoeTeam(id) == team
                || EffectiveFoeTeam(id) == "player-ally" && _enemyBehavior.IsPacified(observerId) || pair.Distance >= best) continue;
            selected = new(id, State.Actors.Get(id).Position); best = pair.Distance;
        }
        return selected;
    }
}
