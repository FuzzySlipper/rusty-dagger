using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// Daggerfall attack policy. Target acquisition and enemy behavior deliberately
/// remain absent until they have an honest implementation.
/// </summary>
internal sealed class CombatModule
{
    internal void TryPlayerMelee(PlayerControlState playerControl, FactBuffer<IProductFact> facts)
    {
        if (playerControl.Position is null)
        {
            facts.Append(new AttackRejectedFact(AttackRejection.MissingPlayerPosition));
            return;
        }

        facts.Append(new AttackRejectedFact(AttackRejection.NoTargetInReach));
    }
}
