using System.Numerics;
using Rusty.Engine;

namespace RustyDagger.Game;

public readonly record struct WorldPoint(float X, float Y, float Z)
{
    public float HorizontalDistanceTo(WorldPoint other)
    {
        var dx = X - other.X;
        var dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public Vector3 ToVector() => new(X, Y, Z);
    public static WorldPoint From(Vector3 value) => new(value.X, value.Y, value.Z);
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
    public ulong Updates { get; set; }
    public string LastOutcome { get; set; } = "Ready";

    public static DaggerGameState CreatePrivateersHold(ProjectFacts project)
    {
        var player = new PlayerState(project.PlayerPosition, DaggerCatalogs.Player);
        var actors = new Dictionary<long, ActorState>();
        foreach (var authored in project.Actors.Values)
        {
            var definition = DaggerCatalogs.ForAuthoredName(authored.Name);
            if (definition is not null) actors[authored.EntityId] = new ActorState(authored.EntityId, definition, authored.Position, authored.Sprite);
        }
        return new DaggerGameState(player, actors);
    }

    public void AdvanceTime(float deltaSeconds)
    {
        Player.AttackCooldownSeconds = Math.Max(0f, Player.AttackCooldownSeconds - deltaSeconds);
        foreach (var actor in Actors.Values) actor.AttackCooldownSeconds = Math.Max(0f, actor.AttackCooldownSeconds - deltaSeconds);
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
    public WorldPoint? Position { get; private set; }
    public CharacterMotion Motion { get; set; }
    public float YawRadians { get; set; } = MathF.PI;
    public float PitchRadians { get; set; }
    public int Health { get; private set; }
    public int Stamina { get; set; }
    public int Magicka { get; }
    public int Experience { get; private set; }
    public float AttackCooldownSeconds { get; set; }
    public InventoryState Inventory { get; private set; }
    public EquipmentState Equipment { get; }

    public void MoveTo(Vector3 position) => Position = WorldPoint.From(position);
    public void AwardExperience(int amount) => Experience += Math.Max(0, amount);
    public int ApplyDamage(int amount) { var applied = Math.Min(Health, Math.Max(0, amount)); Health -= applied; return applied; }
    public void AddItem(ItemStack item) => Inventory = new InventoryState([.. Inventory.Items, item]);
}

public sealed class ActorState
{
    public ActorState(long entityId, ActorDefinition definition, WorldPoint position, AuthoredSprite? sprite)
    {
        EntityId = entityId;
        Definition = definition;
        Position = position;
        Health = definition.MaximumHealth;
        Sprite = sprite;
    }

    public long EntityId { get; }
    public ActorDefinition Definition { get; }
    public WorldPoint Position { get; }
    public AuthoredSprite? Sprite { get; }
    public float AttackCooldownSeconds { get; set; }
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
