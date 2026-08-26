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

public sealed class DaggerGameState
{
    private DaggerGameState(PlayerState player, IReadOnlyDictionary<long, ActorState> actors)
    {
        Player = player;
        Actors = actors;
    }

    public PlayerState Player { get; }
    public IReadOnlyDictionary<long, ActorState> Actors { get; }
    public ulong Turns { get; set; }
    public string LastOutcome { get; set; } = "Ready";

    public static DaggerGameState CreatePrivateersHold(PrivateersHoldPositions positions)
    {
        var player = new PlayerState(positions.Player, DaggerCatalogs.Player);
        var actors = new Dictionary<long, ActorState>
        {
            [2007] = new(2007, DaggerCatalogs.Rat, positions.ForEntity(2007)),
            [2000] = new(2000, DaggerCatalogs.SkeletalWarrior, positions.ForEntity(2000)),
        };
        return new DaggerGameState(player, actors);
    }

    public void AdvanceTime(float deltaSeconds)
    {
        Player.AttackCooldownSeconds = Math.Max(0f, Player.AttackCooldownSeconds - deltaSeconds);
    }
}

public sealed class PlayerState
{
    public PlayerState(WorldPoint? position, ActorDefinition definition)
    {
        Position = position;
        Definition = definition;
        Health = definition.MaximumHealth;
        Stamina = definition.MaximumStamina;
        Magicka = definition.MaximumMagicka;
        Inventory = new InventoryState([new ItemStack(DaggerCatalogs.IronLongsword.Id, 1), new ItemStack("iron-dagger", 1), new ItemStack("iron-cuirass", 1), new ItemStack("gold-piece", 25)]);
        Equipment = new EquipmentState(DaggerCatalogs.IronLongsword);
    }

    public ActorDefinition Definition { get; }
    public WorldPoint? Position { get; }
    public float YawDegrees { get; set; } = 180f;
    public float PitchDegrees { get; set; }
    public int Health { get; private set; }
    public int Stamina { get; set; }
    public int Magicka { get; }
    public float AttackCooldownSeconds { get; set; }
    public InventoryState Inventory { get; }
    public EquipmentState Equipment { get; }
}

public sealed class ActorState
{
    public ActorState(long entityId, ActorDefinition definition, WorldPoint? position)
    {
        EntityId = entityId;
        Definition = definition;
        Position = position;
        Health = definition.MaximumHealth;
    }

    public long EntityId { get; }
    public ActorDefinition Definition { get; }
    public WorldPoint? Position { get; }
    public int Health { get; private set; }
    public bool IsDead => Health == 0;

    public int ApplyDamage(int amount)
    {
        var before = Health;
        Health = Math.Max(0, Health - Math.Max(0, amount));
        return before - Health;
    }
}

public sealed record InventoryState(IReadOnlyList<ItemStack> Items);
public sealed record EquipmentState(WeaponDefinition RightHand);
public sealed record ItemStack(string ItemId, int Quantity);
