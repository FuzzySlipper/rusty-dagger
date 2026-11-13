using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the world does to bodies by touch, always with the player involved: open fires burn the player and bodies the
/// player threw into them, and a body the player throws onto a hurt trap springs it on that body. A monster wandering
/// over a trap or through a fire sets nothing off. The traps are Interiors': this owner says when and where a thrown
/// body meets them.
/// </summary>
/// <param name="playerPosition">Where the player stands now, if anywhere.</param>
/// <param name="toProfile">A point of the live world in the loaded site's own frame, where its fires are placed.</param>
/// <param name="siteInputs">The loaded site's content, whose burning flats are the fires.</param>
/// <param name="harm">How a hazard's health damage is applied.</param>
/// <param name="forgetLanded">Forgets which traps the bodies no longer held have sprung, once a step.</param>
/// <param name="springTraps">Springs the hurt traps a thrown body at a point (in the site's frame) touches.</param>
internal sealed class DaggerfallHazards(DaggerfallHazardTuning tuning, ActorsState actors, ActorReactions reactions,
    Func<WorldPoint?> playerPosition, Func<Vector3, Vector3> toProfile, Func<DaggerfallSiteProfile> siteInputs,
    DaggerfallVitalityConsequences vitality, PresentationState presentation, Action<DamageResult> harm,
    Action forgetLanded, Action<ActorState, Vector3> springTraps)
{
    private readonly DaggerfallFires _fires = new(tuning);
    private DaggerfallSiteProfile? _fireInputs;
    private Vector3[] _fireSpots = [];

    internal void Step(double seconds)
    {
        Vector3[] fires = FireSpots();
        if (playerPosition() is WorldPoint player)
        {
            int burn = _fires.BurnPlayer(_fires.Near(toProfile(player.ToVector()), fires), seconds, FireFelt(actors.Player.Stats));
            if (burn > 0)
            {
                Burn(actors.Player.Actor, burn);
                presentation.SetOutcome("The fire burns you.");
            }
        }
        forgetLanded();
        foreach (long thrown in reactions.Held)
        {
            if (!actors.TryGet(thrown, out ActorState actor) || actor.IsDefeated) continue;
            Vector3 at = toProfile(actor.Position.ToVector());
            if (_fires.Near(at, fires)) _fires.Ignite(thrown);
            springTraps(actor, at);
        }
        foreach ((long actor, int damage) in _fires.BurnBodies(seconds, id => actors.TryGet(id, out ActorState body) ? FireFelt(body.Stats) : 0d))
            if (actors.TryGet(actor, out ActorState burning) && !burning.IsDefeated) Burn(burning.Actor, damage);
    }

    private Vector3[] FireSpots()
    {
        DaggerfallSiteProfile inputs = siteInputs();
        if (ReferenceEquals(inputs, _fireInputs)) return _fireSpots;
        _fireInputs = inputs;
        return _fireSpots = [.. inputs.Decorations.Where(flat => tuning.BurningFlats.Contains(flat.Source)).Select(flat => flat.Position.ToVector())];
    }

    /// <summary>The share of a fire that gets through a body's resistance; a negative resistance is a weakness.</summary>
    internal static double FireFelt(StatsComponent stats) =>
        Math.Clamp(100 - (stats.TryGetStat(StatId.Parse(DaggerfallMechanicsIds.ResistanceFire.Value), out Stat? resistance) ? resistance.ValueInt : 0), 0, 200) / 100d;

    private void Burn(Rusty.Engine.Entities.Actor target, int damage) => harm(vitality.ResolveBurn(actors.Player.Actor, target, damage));
}
