using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Where a blow comes from and whom the player's harm goes through: an actor's strike pose, whether an attack starts
/// aimed at a civilian, and the bodies a player's blow or spell passes, allies always and civilians unless it started
/// aimed at one.
/// </summary>
/// <param name="playerPosition">Where the player stands now, if anywhere.</param>
/// <param name="look">Which way the player looks now.</param>
/// <param name="eyeHeight">The player's eye above the feet.</param>
/// <param name="environment">The world environment contact queries run against now.</param>
/// <param name="aimRange">How far an attack's starting look reaches to find a civilian.</param>
/// <param name="isPlayerAlly">Whether an actor fights on the player's side.</param>
/// <param name="isCivilian">Whether an actor is a civilian.</param>
internal sealed class DaggerfallStrikeAim(ActorsState actors, Func<WorldPoint?> playerPosition, Func<Vector3> look, float eyeHeight,
    ActorContact contact, Func<CharacterStepEnvironment?> environment, double aimRange, Func<long, bool> isPlayerAlly, Func<long, bool> isCivilian)
{
    /// <summary>
    /// Where an actor strikes or looses from and which way it faces: the player from the camera eye along
    /// the current look, a site actor from near the top of its body along its heading.
    /// </summary>
    internal DaggerfallStrikePose? StrikePose(long actor)
    {
        if (actor == actors.Player.DurableId)
            return playerPosition() is WorldPoint player
                ? new(player.ToVector() + Vector3.UnitY * eyeHeight, look())
                : null;
        return actors.TryGet(actor, out ActorState site) && !site.IsDefeated
            ? new(site.Position.ToVector() + Vector3.UnitY * (DaggerfallContactBody.Envelope.Height * DaggerfallContactBody.EyeFraction), ActorHeading.Forward(site.HeadingYawRadians))
            : null;
    }

    /// <summary>
    /// Whether the view meets a civilian's body as an attack starts, allies and world aside. That one look decides for the
    /// attack's whole life: a player fighting beside civilians doesn't harm them, and one who means to harms any it meets.
    /// </summary>
    internal bool AimedAtCivilian()
    {
        if (StrikePose(DaggerfallActorIdentity.PlayerEntityId) is not { } pose) return false;
        SegmentContact aim = contact.Segment(DaggerfallActorIdentity.PlayerEntityId, pose.Eye, pose.Eye + pose.Forward * (float)aimRange,
            environment(), isPlayerAlly);
        return aim.ActorId is long met && isCivilian(met);
    }

    /// <summary>Whether a player's attack goes through a body: always an ally, and a civilian unless it started aimed at one.</summary>
    internal bool PassesThrough(long actor, bool harmsCivilians) => isPlayerAlly(actor) || !harmsCivilians && isCivilian(actor);
}
