using Rusty.Engine;
using WorldRpg.Kit.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// Touch delivery under the contact + mitigation variant. A touch spell reaches every actor its touch volume
/// contacts at the cast frame, the way a swing reaches whatever its blade crosses: no capsule pick, and no
/// promise from an enemy's behavior that the player is in reach. A touch that contacts no one is still
/// released and paid for, and misses.
/// </summary>
internal sealed class DaggerfallContactTouch(DaggerfallTouchOperations ops, DaggerfallContactTuning tuning) : IDaggerfallTouchDelivery
{
    public DaggerfallTouchReach Reach(DaggerfallTouchCast cast)
    {
        (SpatialShape volume, SpatialPose at) = DaggerfallContactVolume.Ahead(cast.Eye, cast.Forward, tuning.TouchWidth, tuning.TouchHeight, tuning.TouchDrop, tuning.TouchReach);
        VolumeContact touched = ops.Contact.Volume(cast.CasterId, volume, at, cast.Eye, ops.Environment());
        return new(true, [.. touched.Actors.Select(contact => contact.ActorId)]);
    }
}
