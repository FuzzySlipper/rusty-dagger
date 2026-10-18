using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One physical harm reaching a target, as the physical mitigation owner sees it: the participants, the
/// attack's skill and weapon material, and the struck body part.
/// </summary>
internal sealed record DaggerfallPhysicalHarm(long AttackerId, long TargetId, DaggerfallActorDefinition Target, string Skill,
    string? WeaponMaterial, int BodyPart);

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

/// <summary>
/// Classic physical mitigation: the weapon-material requirement as immunity, decided before the damage
/// roll as the donor rejects the attempt before rolling. A player whose racial override requires silver
/// is cut only by silver or better. Classic has no physical deflection or absorption; armor lowers the
/// chance to hit instead.
/// </summary>
internal sealed class DaggerfallClassicPhysicalMitigation(Func<DaggerfallCharacterState?> character) : IDaggerfallPhysicalMitigation
{
    public void Negate(NegationEvent negation, DaggerfallPhysicalHarm harm)
    {
        string? minimum = harm.TargetId == DaggerfallActorIdentity.PlayerEntityId && character()?.RacialOverrides?.Current?.RequiresSilver == true
            ? "silver" : harm.Target.MinimumMaterial;
        if (harm.WeaponMaterial is not null
            && !DaggerfallFormulaPolicy.CanHitMaterial(harm.WeaponMaterial, minimum, DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks))
            negation.Negation = HarmNegation.Immune;
    }

    public void Mitigate(MitigationEvent mitigation, DaggerfallPhysicalHarm harm) { }
}
