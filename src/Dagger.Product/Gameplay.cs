using Rusty.Engine.Native;

namespace RustyDagger.Product;

/// <summary>Turns host input facts into persistent Dagger intent; spatial resolution remains Engine-owned.</summary>
public sealed class GameplayInput
{
    private const uint PointerButton = 2;
    private const uint PointerDelta = 3;
    private const uint Clear = 7;
    private const uint DigitalIntent = 8;
    private const uint Press = 1;
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    public void Apply(DaggerGameState state, GameplayTurn turn, EngineApi engine)
    {
        foreach (var input in turn.Inputs)
        {
            if (input.Kind == Clear)
            {
                _held.Clear();
                turn.PlanarIntent = default;
            }
            else if (input.Kind == PointerDelta)
            {
                var receipt = engine.Look.Integrate(new NativeLookRequest
                {
                    state = new NativeLookState { yaw_radians = state.Player.YawRadians, pitch_radians = state.Player.PitchRadians },
                    delta = new NativeVec2 { x = input.X, y = input.Y },
                    config = LookConfig(),
                });
                state.Player.YawRadians = receipt.state.yaw_radians;
                state.Player.PitchRadians = receipt.state.pitch_radians;
            }
            else if (input.Kind == DigitalIntent)
            {
                if (input.Label == "attack" && input.X > 0f) turn.AttackRequested = true;
                else if (input.Label is "move" or "movement") turn.PlanarIntent = new NativeVec2 { x = input.X, y = input.Y };
            }
            else if (input.Kind == 1 && input.Label is "KeyW" or "KeyA" or "KeyS" or "KeyD")
            {
                if (input.Edge == Press) _held.Add(input.Label); else _held.Remove(input.Label);
            }
            else if ((input.Kind == PointerButton && input.Edge == Press) || (input.Kind == 1 && input.Edge == Press && input.Label is "Space" or "Mouse0")) turn.AttackRequested = true;
        }

        if (turn.PlanarIntent.x == 0f && turn.PlanarIntent.y == 0f)
            turn.PlanarIntent = new NativeVec2 { x = (_held.Contains("KeyD") ? 1f : 0f) - (_held.Contains("KeyA") ? 1f : 0f), y = (_held.Contains("KeyW") ? 1f : 0f) - (_held.Contains("KeyS") ? 1f : 0f) };
    }

    private static NativeLookConfig LookConfig() => new()
    {
        horizontal_radians_per_unit = .0035f,
        vertical_radians_per_unit = .0035f,
        minimum_pitch_radians = -1.5533f,
        maximum_pitch_radians = 1.5533f,
        maximum_delta_radians = .35f,
        wrap_yaw = 1,
    };
}

public sealed class GameplayTurn(float deltaSeconds)
{
    public float DeltaSeconds { get; } = deltaSeconds;
    public List<ProductInput> Inputs { get; } = [];
    public NativeVec2 PlanarIntent { get; set; }
    public bool AttackRequested { get; set; }
    public void Add(ProductInput input) => Inputs.Add(input);
}

public readonly record struct ProductInput(uint Kind, uint Edge, float X, float Y, string Label);

public static class EncounterService
{
    public static EncounterDefinition? ActiveEncounter(DaggerGameState state)
    {
        if (state.Player.Position is not WorldPoint playerPosition) return null;
        foreach (var encounter in DaggerCatalogs.Encounters.Values)
            if (state.Actors.TryGetValue(encounter.MemberEntityId, out var actor)
                && !actor.IsDead
                && playerPosition.HorizontalDistanceTo(actor.Position) < 12f)
                return encounter;
        return null;
    }
}

public static class CombatService
{
    public static CombatResult TryMelee(DaggerGameState state, RngApi rng)
    {
        var player = state.Player;
        if (player.AttackCooldownSeconds > 0f) return SetOutcome(state, new(false, "Weapon recovering", null, 0, 0));
        if (player.Stamina < 5) return SetOutcome(state, new(false, "Too exhausted to attack", null, 0, 0));
        if (player.Position is not WorldPoint playerPosition) return SetOutcome(state, new(false, "No authored player position", null, 0, 0));

        var encounter = EncounterService.ActiveEncounter(state);
        if (encounter is null || !state.Actors.TryGetValue(encounter.MemberEntityId, out var target)
            || playerPosition.HorizontalDistanceTo(target.Position) > 2.25f)
            return SetOutcome(state, new(false, "No active encounter target in melee reach", null, 0, 0));

        player.Stamina -= 5;
        player.AttackCooldownSeconds = .75f;
        var chance = CombatMath.HitChance(player.Definition.Stats.LongBlade, player.Definition.Stats, target.Definition.Stats, target.Definition.ArmorValue);
        var roll = checked((int)rng.DrawKeyed(0xDA66E2UL, 1, 100, "dagger.combat", $"turn:{state.Turns}:hit:{target.EntityId}").value);
        if (roll > chance) return SetOutcome(state, new(true, $"Missed {target.Definition.Id} ({roll} vs {chance})", target.EntityId, roll, chance));

        var weapon = player.Equipment.RightHand;
        var damage = CombatMath.Damage(player.Definition.Stats, weapon, rng, state.Turns, target.EntityId);
        var applied = target.ApplyDamage(damage);
        var loot = target.IsDead ? LootService.Award(target, player, rng, state.Turns) : null;
        var outcome = target.IsDead
            ? $"Defeated {target.Definition.Id} for {applied} damage; gained {target.Definition.ExperienceReward} XP{loot}"
            : $"Hit {target.Definition.Id} for {applied} damage";
        if (target.IsDead) player.AwardExperience(target.Definition.ExperienceReward);
        return SetOutcome(state, new(true, outcome, target.EntityId, roll, chance));
    }

    private static CombatResult SetOutcome(DaggerGameState state, CombatResult result) { state.LastOutcome = result.Outcome; return result; }
}

public static class EnemyCombatService
{
    public static void TryActiveEncounterAttack(DaggerGameState state, RngApi rng)
    {
        if (state.Player.Position is not WorldPoint playerPosition || state.Player.Health == 0) return;
        var encounter = EncounterService.ActiveEncounter(state);
        if (encounter is null || !state.Actors.TryGetValue(encounter.MemberEntityId, out var actor)
            || actor.IsDead || actor.Definition.Attack is not { } attack || actor.AttackCooldownSeconds > 0f
            || actor.Position.HorizontalDistanceTo(playerPosition) > attack.Reach) return;

        actor.AttackCooldownSeconds = attack.CooldownSeconds;
        var chance = CombatMath.HitChance(actor.Definition.Stats.HandToHand, actor.Definition.Stats, state.Player.Definition.Stats, state.Player.Definition.ArmorValue);
        var roll = checked((int)rng.DrawKeyed(0xDA66E2UL, 1, 100, "dagger.combat", $"turn:{state.Turns}:enemy-hit:{actor.EntityId}").value);
        if (roll > chance) { state.LastOutcome = $"{actor.Definition.Id} missed ({roll} vs {chance})"; return; }
        var damage = CombatMath.DamageRange(attack.MinimumDamage, attack.MaximumDamage, rng, $"turn:{state.Turns}:enemy-damage:{actor.EntityId}");
        state.Player.ApplyDamage(damage);
        state.LastOutcome = $"{actor.Definition.Id} hit for {damage} damage";
    }
}

public static class LootService
{
    public static string? Award(ActorState actor, PlayerState player, RngApi rng, ulong turn)
    {
        if (actor.Definition.Loot is not { } loot) return null;
        var quantity = CombatMath.DamageRange(loot.MinimumQuantity, loot.MaximumQuantity, rng, $"turn:{turn}:loot:{actor.EntityId}:{loot.TableKey}");
        player.AddItem(new ItemStack(loot.ItemId, quantity));
        return $"; looted {quantity} {loot.ItemId}";
    }
}

public static class CombatMath
{
    public static int HitChance(int attackSkill, StatBlock attacker, StatBlock target, int targetArmorValue) => Math.Clamp(attackSkill + targetArmorValue - 50 + ((attacker.Luck - target.Luck) / 10) + ((attacker.Agility - target.Agility) / 10) - (target.Dodging / 4), 3, 97);
    public static int Damage(StatBlock attacker, WeaponDefinition weapon, RngApi rng, ulong turn, long targetId) => Math.Max(1, checked((int)rng.DrawKeyed(0xDA66E2UL, weapon.MinimumDamage, weapon.MaximumDamage, "dagger.combat", $"turn:{turn}:damage:{targetId}").value) + ((attacker.Strength - 50) / 5));
    public static int DamageRange(int minimum, int maximum, RngApi rng, string key) => checked((int)rng.DrawKeyed(0xDA66E2UL, minimum, maximum, "dagger.combat", key).value);
}

public sealed record CombatResult(bool Accepted, string Outcome, long? TargetId, int Roll, int Chance);
