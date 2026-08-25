namespace RustyDagger.Product;

public readonly record struct WorldPoint(float X, float Y, float Z)
{
    public float HorizontalDistanceTo(WorldPoint other)
    {
        var dx = X - other.X;
        var dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}

public sealed class ActorState
{
    public ActorState(long entityId, ActorDefinition definition, WorldPoint position, int health)
    {
        EntityId = entityId;
        Definition = definition;
        Position = position;
        Health = health;
        MaximumHealth = health;
    }

    public long EntityId { get; }
    public ActorDefinition Definition { get; }
    public WorldPoint Position { get; set; }
    public int Health { get; private set; }
    public int MaximumHealth { get; }
    public bool IsDead => Health <= 0;

    public int ApplyDamage(int amount)
    {
        var before = Health;
        Health = Math.Max(0, Health - Math.Max(0, amount));
        return before - Health;
    }
}

public sealed record ActorDefinition(
    string Id,
    int ArmorValue,
    int Strength,
    int Agility,
    int Luck,
    int LongBlade,
    int HandToHand,
    int Dodging,
    int MinimumHealth,
    int MaximumHealth,
    int MinimumDamage,
    int MaximumDamage,
    float DetectionRange,
    float AttackRange,
    float AttackCooldownSeconds);

public sealed record WeaponDefinition(string Id, int MinimumDamage, int MaximumDamage, string Skill);

public sealed class PlayerState
{
    public WorldPoint Position { get; set; }
    public float YawDegrees { get; set; }
    public float PitchDegrees { get; set; }
    public int Health { get; set; } = 85;
    public int MaximumHealth { get; } = 85;
    public int Stamina { get; set; } = 90;
    public int MaximumStamina { get; } = 90;
    public int Magicka { get; } = 50;
    public int MaximumMagicka { get; } = 50;
    public float AttackCooldown { get; set; }
    public WeaponDefinition EquippedWeapon { get; } = Catalogs.IronLongsword;
}

public sealed record CombatResult(
    bool Accepted,
    string Outcome,
    long? TargetId,
    int? TargetHealthBefore,
    int? TargetHealthAfter,
    int Damage,
    bool Died,
    int Roll,
    int Chance);
