using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// Where Daggerfall's actors see from and are seen at. A walker's pose names its sprite's base at the floor contact,
/// so its body is looked at halfway up its sprite and it looks from near the sprite's top; the player looks from the
/// camera's eye and is looked at halfway up its standing body; a person is looked at where activation sights them,
/// halfway up their sprite, and looks from near its top as a walker does.
/// Flyers, spectres and swimmers keep the points their poses name. The player's attack looks from its eye to a body;
/// one actor watching another looks along a line lifted evenly off the floor (<see cref="Line"/>).
/// </summary>
/// <param name="definitions">Each live actor's definition.</param>
/// <param name="catalog">Which actors walk (<see cref="DaggerfallDefinitions.Walks"/>).</param>
/// <param name="currentInputs">The active site, whose sprites give each body's height.</param>
/// <param name="playerEye">How far above its feet the player's eye is.</param>
/// <param name="playerBody">How far above its feet the player's body's middle is.</param>
/// <param name="personSightHeight">Where a walking person is sighted, above its feet, or null.</param>
internal sealed class DaggerfallActorSight(IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions, DaggerfallDefinitions catalog,
    Func<DaggerfallSiteProfile> currentInputs, float playerEye, float playerBody, Func<long, double?>? personSightHeight = null) : IActorSight
{
    /// <summary>A walker's eye, as a share of its sprite's height above the floor.</summary>
    private const float EyeShare = .9F;

    /// <summary>
    /// Between an actor and the one it watches, both ends rise by the same height, the lower of the watcher's eye and
    /// the middle of the body it watches: the line clears the floor between them, while the distance and the facing
    /// along it stay those between the poses, which reach and the senses are tuned to.
    /// </summary>
    public (Vector3 From, Vector3 To) Line(long observer, WorldPoint from, long target, WorldPoint to)
    {
        Vector3 lift = Vector3.UnitY * MathF.Min(EyeLift(observer), BodyLift(target));
        return (from.ToVector() + lift, to.ToVector() + lift);
    }

    /// <summary>Where an actor looks from, standing at <paramref name="feet"/>: the player's attack and its looting look from here.</summary>
    internal Vector3 Eye(long durableId, WorldPoint feet) => feet.ToVector() + Vector3.UnitY * EyeLift(durableId);

    /// <summary>Where an actor is looked at, standing at <paramref name="feet"/>: the middle of its body.</summary>
    internal Vector3 Body(long durableId, WorldPoint feet) => feet.ToVector() + Vector3.UnitY * BodyLift(durableId);

    /// <summary>
    /// How far above its feet an actor looks from. A person with no mobile (a townsperson hired or sent to guard a convoy,
    /// #11105, #11181) looks from near the top of the sprite they are sighted halfway up, so a fighting one sees over the
    /// ground as a walker does.
    /// </summary>
    internal float EyeLift(long durableId)
    {
        if (durableId == DaggerfallActorIdentity.PlayerEntityId) return playerEye;
        float walker = WalkerHeight(durableId);
        if (walker > 0F) return walker * EyeShare;
        return personSightHeight?.Invoke(durableId) is double middle ? (float)(middle * 2d) * EyeShare : 0F;
    }

    /// <summary>How far above its feet an actor is looked at.</summary>
    internal float BodyLift(long durableId)
    {
        if (durableId == DaggerfallActorIdentity.PlayerEntityId) return playerBody;
        if (personSightHeight?.Invoke(durableId) is double sight)
            return (float)sight;
        return WalkerHeight(durableId) * .5F;
    }

    /// <summary>How tall a walker's sprite stands, or zero for anything else.</summary>
    private float WalkerHeight(long durableId)
    {
        if (!definitions.TryGetValue(durableId, out DaggerfallActorDefinition? definition) || !catalog.Walks(definition)) return 0F;
        DaggerfallSiteProfile inputs = currentInputs();
        NormalizedActorSprite sprite = inputs.ActorSprites.TryGetValue(durableId, out NormalizedActorSprite? placed)
            ? placed
            : definition.MobileId is int mobileId && inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? mobile)
                ? mobile : throw new InvalidOperationException($"Walking actor {durableId} has no admitted sprite size.");
        return sprite.Size.Y;
    }
}
