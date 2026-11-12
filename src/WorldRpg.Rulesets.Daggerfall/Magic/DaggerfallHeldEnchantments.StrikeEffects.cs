using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallHeldEnchantments
{
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
