using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The developer switch (<see cref="IDeveloperPeopleSession"/>) that draws every walking townsperson as the art set's
/// parts-rig test figure (<see cref="DaggerfallPeopleRig"/>) in one of its view sets, through every site appearance, and
/// back to their own art.
/// </summary>
internal sealed partial class DaggerfallSession : IDeveloperPeopleSession
{
    /// <summary>The rig pages are painted far finer than a classic person's pixels, so they are sampled smooth.</summary>
    private const TextureFilter PeopleRigFilter = TextureFilter.Linear;

    /// <summary>The figure takes the light around it as a person's sprite does, and casts her shape into the sun's shadow.</summary>
    private static readonly SpriteMaterialDescriptor PeopleRigMaterial = DaggerfallSiteAppearance.CastingSprite;

    private PartsRigCrowd? _peopleRigs;

    public DebugCommandResult RigPeople(string set)
    {
        if (set == "off")
        {
            _peopleRigs?.Use(null);
            return DebugCommandResult.Success("Walking people show their own art again.");
        }
        if (_composition.ArtReplacements?.PeopleRig is not { } rig) return Refused("The art set carries no people rig.");
        if (!rig.Directions.ContainsKey(set))
            return Refused($"People rig {rig.Name} has no view set '{set}'; it has {string.Join(", ", rig.Directions.Keys.Order(StringComparer.Ordinal))}, or off.");
        PartsRigFigure figure = rig.Figure(set, PeopleRigFilter, PeopleRigMaterial);
        (_peopleRigs ??= new(() => _engine.PartsRigs)).Use(figure);
        int walking = State.Actors.All.Count(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian && actor.Wander.State != WanderState.Unloaded);
        return DebugCommandResult.Success($"Walking people draw as {rig.Name} seen from {figure.Views.Count} directions; {walking} walk here now.");
    }
}
