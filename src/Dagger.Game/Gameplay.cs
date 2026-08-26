using System.Numerics;
using Rusty.Engine;

namespace RustyDagger.Game;

/// <summary>Turns host input facts into persistent Dagger intent; spatial resolution remains Engine-owned.</summary>
public sealed class GameplayInput
{
    private const uint PointerButton = 2;
    private const uint PointerDelta = 3;
    private const uint Clear = 7;
    private const uint DigitalIntent = 8;
    private const uint Press = 1;
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    public void Apply(DaggerGameState state, GameplayUpdate update, IEngineContext engine)
    {
        foreach (var input in update.Inputs)
        {
            if (input.Kind == Clear)
            {
                _held.Clear();
                update.PlanarIntent = default;
            }
            else if (input.Kind == PointerDelta)
            {
                var receipt = engine.Look.Integrate(new LookRequest(
                    new LookState(state.Player.YawRadians, state.Player.PitchRadians),
                    new Vector2(input.X, input.Y),
                    LookConfiguration()));
                state.Player.YawRadians = receipt.State.YawRadians;
                state.Player.PitchRadians = receipt.State.PitchRadians;
            }
            else if (input.Kind == DigitalIntent)
            {
                if (input.Label == "attack" && input.X > 0f) update.AttackRequested = true;
                else if (input.Label is "move" or "movement") update.PlanarIntent = new Vector2(input.X, input.Y);
            }
            else if (input.Kind == 1 && input.Label is "KeyW" or "KeyA" or "KeyS" or "KeyD")
            {
                if (input.Edge == Press) _held.Add(input.Label); else _held.Remove(input.Label);
            }
            else if ((input.Kind == PointerButton && input.Edge == Press) || (input.Kind == 1 && input.Edge == Press && input.Label is "Space" or "Mouse0")) update.AttackRequested = true;
        }

        if (update.PlanarIntent.X == 0f && update.PlanarIntent.Y == 0f)
            update.PlanarIntent = new Vector2(
                (_held.Contains("KeyD") ? 1f : 0f) - (_held.Contains("KeyA") ? 1f : 0f),
                (_held.Contains("KeyW") ? 1f : 0f) - (_held.Contains("KeyS") ? 1f : 0f));
    }

    private static LookConfig LookConfiguration() => new(
        .0035f, .0035f, -1.5533f, 1.5533f, .35f,
        InvertHorizontal: 0, InvertVertical: 0, WrapYaw: 1, Reserved: 0);
}

public sealed class GameplayUpdate(float deltaSeconds)
{
    public float DeltaSeconds { get; } = deltaSeconds;
    public List<ProductInput> Inputs { get; } = [];
    public Vector2 PlanarIntent { get; set; }
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
    public static CombatResult TryMelee(DaggerGameState state, IRandomService rng)
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
        var roll = checked((int)rng.DrawKeyed(new KeyedRngRequest(0xDA66E2UL, "dagger.combat", $"turn:{state.Updates}:hit:{target.EntityId}", 1, 100)).Value);
        if (roll > chance) return SetOutcome(state, new(true, $"Missed {target.Definition.Id} ({roll} vs {chance})", target.EntityId, roll, chance));

        var weapon = player.Equipment.RightHand;
        var damage = CombatMath.Damage(player.Definition.Stats, weapon, rng, state.Updates, target.EntityId);
        var applied = target.ApplyDamage(damage);
        var loot = target.IsDead ? LootService.Award(target, player, rng, state.Updates) : null;
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
    public static void TryActiveEncounterAttack(DaggerGameState state, IRandomService rng)
    {
        if (state.Player.Position is not WorldPoint playerPosition || state.Player.Health == 0) return;
        var encounter = EncounterService.ActiveEncounter(state);
        if (encounter is null || !state.Actors.TryGetValue(encounter.MemberEntityId, out var actor)
            || actor.IsDead || actor.Definition.Attack is not { } attack || actor.AttackCooldownSeconds > 0f
            || actor.Position.HorizontalDistanceTo(playerPosition) > attack.Reach) return;

        actor.AttackCooldownSeconds = attack.CooldownSeconds;
        var chance = CombatMath.HitChance(actor.Definition.Stats.HandToHand, actor.Definition.Stats, state.Player.Definition.Stats, state.Player.Definition.ArmorValue);
        var roll = checked((int)rng.DrawKeyed(new KeyedRngRequest(0xDA66E2UL, "dagger.combat", $"turn:{state.Updates}:enemy-hit:{actor.EntityId}", 1, 100)).Value);
        if (roll > chance) { state.LastOutcome = $"{actor.Definition.Id} missed ({roll} vs {chance})"; return; }
        var damage = CombatMath.DamageRange(attack.MinimumDamage, attack.MaximumDamage, rng, $"turn:{state.Updates}:enemy-damage:{actor.EntityId}");
        state.Player.ApplyDamage(damage);
        state.LastOutcome = $"{actor.Definition.Id} hit for {damage} damage";
    }
}

public static class LootService
{
    public static string? Award(ActorState actor, PlayerState player, IRandomService rng, ulong update)
    {
        if (actor.Definition.Loot is not { } loot) return null;
        var quantity = CombatMath.DamageRange(loot.MinimumQuantity, loot.MaximumQuantity, rng, $"turn:{update}:loot:{actor.EntityId}:{loot.TableKey}");
        player.AddItem(new ItemStack(loot.ItemId, quantity));
        return $"; looted {quantity} {loot.ItemId}";
    }
}

public static class CombatMath
{
    public static int HitChance(int attackSkill, StatBlock attacker, StatBlock target, int targetArmorValue) => Math.Clamp(attackSkill + targetArmorValue - 50 + ((attacker.Luck - target.Luck) / 10) + ((attacker.Agility - target.Agility) / 10) - (target.Dodging / 4), 3, 97);
    public static int Damage(StatBlock attacker, WeaponDefinition weapon, IRandomService rng, ulong update, long targetId) => Math.Max(1, checked((int)rng.DrawKeyed(new KeyedRngRequest(0xDA66E2UL, "dagger.combat", $"turn:{update}:damage:{targetId}", weapon.MinimumDamage, weapon.MaximumDamage)).Value) + ((attacker.Strength - 50) / 5));
    public static int DamageRange(int minimum, int maximum, IRandomService rng, string key) => checked((int)rng.DrawKeyed(new KeyedRngRequest(0xDA66E2UL, "dagger.combat", key, minimum, maximum)).Value);
}

public sealed record CombatResult(bool Accepted, string Outcome, long? TargetId, int Roll, int Chance);
