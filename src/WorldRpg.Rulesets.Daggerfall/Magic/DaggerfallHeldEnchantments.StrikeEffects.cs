using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallHeldEnchantments
{
    /// <summary>
    /// The vampiric power's drain: each live monster or enemy class within its range of the player that is not the
    /// player's ally gives up one health to the player.
    /// </summary>
    /// <param name="nearby">The live actors within a radius of a point, the player excluded.</param>
    /// <param name="liveDefinition">A live, undefeated actor's definition; null for anyone else.</param>
    internal static void DrainNearby(WorldPoint? player, double range, Func<WorldPoint, double, IEnumerable<long>> nearby,
        Func<long, DaggerfallActorDefinition?> liveDefinition, Func<long, string?> team, Action<long> drain)
    {
        if (player is not { } origin) return;
        foreach (long id in nearby(origin, range))
        {
            if (liveDefinition(id) is not { } definition
                || definition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
                || team(id) == "player-ally") continue;
            drain(id);
        }
    }

    /// <summary>Uses this owner's existing fourth-round beat and the calendar's actual served minute.</summary>
    private void ApplyStrikeEnchantmentRound(long minute, bool synthetic)
    {
        foreach (var assignment in _equipment.Read().Assignments.DistinctBy(value => value.Item.EntityId))
        {
            if (!TryEnchantments(assignment, out var payloads)) continue;
            ulong item = _entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value;
            foreach (var payload in payloads)
            {
                if (_stats.GetTrack(TrackId.Parse("health")).Current <= 0) return;
                var metadata = _instances.RequireUnique(item);
                if (payload.Type == DaggerfallEnchantmentSettings.HealthLeechType && !synthetic)
                {
                    long maximumUnused = payload.Param == 1 ? 1440 : payload.Param == 2 ? 7 * 1440 : long.MaxValue;
                    if (minute >= metadata.HealthLeechLastUsedMinute && minute - metadata.HealthLeechLastUsedMinute > maximumUnused)
                        (_damageWearer ?? throw new InvalidOperationException("Timed health leech requires the vitality owner."))(1);
                }
                else if (payload.Type == DaggerfallEnchantmentSettings.VampiricType && payload.Param == 0)
                    (_drainNearby ?? throw new InvalidOperationException("Vampiric range requires the admitted actor/vitality owners."))();
            }
        }
    }
}
