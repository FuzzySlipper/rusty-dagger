using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One physical harm reaching a target, as physical mitigation sees it: the participants, the attack's skill
/// and weapon material, the struck body part, and the admitted step its keyed draws use.
/// </summary>
internal sealed record DaggerfallPhysicalHarm(long AttackerId, long TargetId, DaggerfallActorDefinition Target, string Skill,
    string? WeaponMaterial, int BodyPart, ulong Generation = 0, ulong SimulationStep = 0);
