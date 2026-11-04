using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One physical harm reaching a target, as the physical mitigation owner sees it: the participants, the
/// attack's skill and weapon material, the struck body part, and the admitted step its keyed draws use.
/// </summary>
internal sealed record DaggerfallPhysicalHarm(long AttackerId, long TargetId, DaggerfallActorDefinition Target, string Skill,
    string? WeaponMaterial, int BodyPart, ulong Generation = 0, ulong SimulationStep = 0);

/// <summary>
/// The physical mitigation seam (<see cref="DaggerfallRulesSeams.PhysicalMitigation"/>): the base
/// resolution of the harm pipeline's negation and mitigation events for physical harm.
/// </summary>
internal interface IDaggerfallPhysicalMitigation
{
    /// <summary>Whether the target turns the whole harm away before it is calculated.</summary>
    void Negate(NegationEvent negation, DaggerfallPhysicalHarm harm);

    /// <summary>What the target deflects, absorbs or resists of the calculated harm.</summary>
    void Mitigate(MitigationEvent mitigation, DaggerfallPhysicalHarm harm);
}
