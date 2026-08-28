namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Shaped combat inputs selected by product content rather than by the combat module.</summary>
internal sealed record WeaponDefinition(string Id, int MinimumDamage, int MaximumDamage, string Skill);

internal sealed record CombatantProfile(string? AttackCostTrack);
