namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Shaped combat inputs selected by product content rather than by the combat module.</summary>
internal sealed record WeaponDefinition(string Id, int MinimumDamage, int MaximumDamage, string Skill);
internal sealed record EnemyAttackDefinition(string Id, int MinimumDamage, int MaximumDamage, float Reach, float CooldownSeconds);

internal sealed record CombatantProfile(
    string AttackSkill,
    string Strength,
    string Agility,
    string Luck,
    string Dodging,
    string DamageTrack,
    string? AttackCostTrack,
    int ArmorValue,
    EnemyAttackDefinition? Attack);
