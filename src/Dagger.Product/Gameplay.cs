namespace RustyDagger.Product;

/// <summary>Turns generated input facts into Dagger look/action intent. Movement is intentionally not advanced without Engine spatial resolution.</summary>
public sealed class GameplayInput
{
    private const uint PointerButton = 2;
    private const uint PointerDelta = 3;
    private const uint DigitalIntent = 8;
    private const uint Press = 1;
    private const float LookDegreesPerUnit = 12f;

    public void Apply(PlayerState player, GameplayTurn turn)
    {
        foreach (var input in turn.Inputs)
        {
            if (input.Kind == PointerDelta)
            {
                player.YawDegrees = NormalizeDegrees(player.YawDegrees + input.X * LookDegreesPerUnit);
                player.PitchDegrees = Math.Clamp(player.PitchDegrees - input.Y * LookDegreesPerUnit, -89f, 89f);
            }
            else if ((input.Kind == PointerButton && input.Edge == Press)
                || (input.Kind == DigitalIntent && input.Label == "attack" && input.X > 0f)
                || (input.Kind == 1 && input.Edge == Press && input.Label is "Space" or "Mouse0"))
                turn.AttackRequested = true;
        }
    }

    private static float NormalizeDegrees(float value) => (value % 360f + 360f) % 360f;
}

public sealed class GameplayTurn(float deltaSeconds)
{
    public float DeltaSeconds { get; } = deltaSeconds;
    public List<ProductInput> Inputs { get; } = [];
    public bool AttackRequested { get; set; }
    public void Add(ProductInput input) => Inputs.Add(input);
}

public readonly record struct ProductInput(uint Kind, uint Edge, float X, float Y, string Label);

public static class EncounterService
{
    public static EncounterDefinition? ActiveEncounter(DaggerGameState state)
    {
        foreach (var encounter in DaggerCatalogs.Encounters.Values)
        {
            if (state.Player.Position is WorldPoint playerPosition
                && state.Actors[encounter.MemberEntityId] is { IsDead: false } actor
                && actor.Position is WorldPoint actorPosition
                && playerPosition.HorizontalDistanceTo(actorPosition) < 12f)
                return encounter;
        }
        return null;
    }
}

public static class CombatService
{
    public static CombatResult TryMelee(DaggerGameState state)
    {
        var player = state.Player;
        if (player.AttackCooldownSeconds > 0f) return SetOutcome(state, new(false, "Weapon recovering", null, 0, 0));
        if (player.Stamina < 5) return SetOutcome(state, new(false, "Too exhausted to attack", null, 0, 0));

        if (player.Position is not WorldPoint playerPosition)
            return SetOutcome(state, new(false, "No authored player position", null, 0, 0));

        var target = state.Actors.Values
            .Where(actor => !actor.IsDead && actor.Position.HasValue)
            .OrderBy(actor => playerPosition.HorizontalDistanceTo(actor.Position!.Value))
            .FirstOrDefault();
        if (target is null || playerPosition.HorizontalDistanceTo(target.Position!.Value) > 2.25f)
            return SetOutcome(state, new(false, "No target in melee reach", null, 0, 0));

        player.Stamina -= 5;
        player.AttackCooldownSeconds = .75f;
        var chance = CombatMath.HitChance(player.Definition.Stats, target.Definition.Stats, target.Definition.ArmorValue);
        var roll = Random.Shared.Next(1, 101);
        if (roll > chance) return SetOutcome(state, new(true, $"Missed {target.Definition.Id} ({roll} vs {chance})", target.EntityId, roll, chance));

        var weapon = player.Equipment.RightHand;
        var damage = CombatMath.Damage(player.Definition.Stats, weapon);
        var applied = target.ApplyDamage(damage);
        var outcome = target.IsDead ? $"Defeated {target.Definition.Id} for {applied} damage" : $"Hit {target.Definition.Id} for {applied} damage";
        return SetOutcome(state, new(true, outcome, target.EntityId, roll, chance));
    }

    private static CombatResult SetOutcome(DaggerGameState state, CombatResult result)
    {
        state.LastOutcome = result.Outcome;
        return result;
    }
}

public static class CombatMath
{
    public static int HitChance(StatBlock attacker, StatBlock target, int targetArmorValue) => Math.Clamp(
        attacker.LongBlade + targetArmorValue - 50
        + ((attacker.Luck - target.Luck) / 10)
        + ((attacker.Agility - target.Agility) / 10)
        - (target.Dodging / 4), 3, 97);

    public static int Damage(StatBlock attacker, WeaponDefinition weapon) => Math.Max(
        1,
        Random.Shared.Next(weapon.MinimumDamage, weapon.MaximumDamage + 1) + ((attacker.Strength - 50) / 5));
}

public sealed record CombatResult(bool Accepted, string Outcome, long? TargetId, int Roll, int Chance);
