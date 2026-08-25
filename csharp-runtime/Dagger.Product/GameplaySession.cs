using System.Buffers;
using System.Text;
using System.Text.Json;

namespace RustyDagger.Product;

public sealed class GameplaySession
{
    private const float MoveSpeed = 3.5f; // gameplay catalog authority, intentionally overrides old project controller speed.
    private const float StepUp = .75f;
    private const float FallSpeed = 12f;
    private const float LookDegreesPerUnit = 12f;
    private readonly NavigationGrid _navigation;
    private readonly HashSet<string> _heldKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<long, ActorState> _actors;
    private ulong _turns;
    private ulong _projectionSequence;
    private ulong? _lastObservedNanoseconds;
    private bool _renderCreated;
    private string _lastOutcome = "Ready";
    private static readonly (int X, int Z)[] NavSampleOffsets =
    [
        (-2, -2), (0, -2), (2, -2),
        (-2, 0),  (0, 0),  (2, 0),
        (-2, 2),  (0, 2),  (2, 2),
    ];

    public GameplaySession(NavigationGrid navigation, WorldPoint playerStart)
    {
        _navigation = navigation;
        Player = new PlayerState { Position = navigation.Snap(playerStart), YawDegrees = 180f };
        var yaw = Player.YawDegrees * MathF.PI / 180f;
        var forwardX = MathF.Sin(yaw);
        var forwardZ = -MathF.Cos(yaw);
        var rightX = MathF.Cos(yaw);
        var rightZ = MathF.Sin(yaw);
        _actors = new Dictionary<long, ActorState>
        {
            // The player is the named project spawn. These encounter members
            // are snapped from a small, same-floor Privateer's Hold nav slice
            // ahead/right of that spawn so the rat begins in melee reach.
            [2007] = new(2007, Catalogs.Rat, navigation.WalkableOffset(Player.Position, forwardX * 1.5f, forwardZ * 1.5f), 12),
            [2000] = new(2000, Catalogs.SkeletalWarrior, navigation.WalkableOffset(Player.Position, (forwardX * 3.5f) + (rightX * 1.5f), (forwardZ * 3.5f) + (rightZ * 1.5f)), 42),
        };
    }

    public PlayerState Player { get; }
    public int InputEventsLastTurn { get; private set; }

    public string ToCreateJson(long frees) => $"{{\"turns\":0,\"frees\":{frees},\"duplicateFrees\":0}}";

    public void ApplyTurn(ulong observedTimeOrStep, bool realtime, IReadOnlyList<PhysicalInput> events)
    {
        InputEventsLastTurn = events.Count;
        var deltaSeconds = realtime ? TimeDelta(observedTimeOrStep) : 1f / 60f;
        var attack = false;
        foreach (var input in events)
        {
            if (input.Kind == InputKind.Key)
            {
                if (input.Edge == InputEdge.Press) _heldKeys.Add(input.Label);
                if (input.Edge == InputEdge.Release) _heldKeys.Remove(input.Label);
                attack |= input.Edge == InputEdge.Press && (input.Label is "Space" or "Mouse0");
            }
            else if (input.Kind == InputKind.PointerButton)
            {
                attack |= input.Edge == InputEdge.Press;
            }
            else if (input.Kind == InputKind.PointerDelta)
            {
                Player.YawDegrees = NormalizeDegrees(Player.YawDegrees + input.X * LookDegreesPerUnit);
                Player.PitchDegrees = Math.Clamp(Player.PitchDegrees - input.Y * LookDegreesPerUnit, -89f, 89f);
            }
            else if (input.Kind == InputKind.Clear)
            {
                _heldKeys.Clear();
            }
            else if (input.Kind == InputKind.DigitalIntent && input.Label == "attack" && input.X > 0f)
            {
                attack = true;
            }
        }

        Move(deltaSeconds);
        Player.AttackCooldown = Math.Max(0f, Player.AttackCooldown - deltaSeconds);
        if (attack) TryAttack();
        _turns++;
    }

    public string ToOutputJson(long frees)
    {
        var bytes = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(bytes))
        {
            json.WriteStartObject();
            json.WriteNumber("turns", _turns);
            json.WriteNumber("frees", frees);
            json.WriteNumber("duplicateFrees", 0);
            json.WriteNumber("inputEvents", InputEventsLastTurn);
            json.WritePropertyName("ui"); WriteUiEnvelope(json);
            json.WritePropertyName("frame"); WriteFrame(json);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(bytes.WrittenSpan);
    }

    private EncounterDefinition? ActiveEncounter()
    {
        foreach (var encounter in Catalogs.Encounters.Values)
        {
            var actor = _actors[encounter.MemberEntityId];
            if (!actor.IsDead && Player.Position.HorizontalDistanceTo(actor.Position) < 12f)
                return encounter;
        }
        return null;
    }

    private void WriteUiEnvelope(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("artifact", "rusty.product.ui-projection");
        json.WritePropertyName("runtime"); json.WriteStartObject(); json.WriteString("instanceId", "1"); json.WriteString("generation", "1"); json.WriteString("controlRevision", "1"); json.WriteEndObject();
        _projectionSequence++;
        json.WriteString("sequence", _projectionSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        json.WriteString("stream", "dagger.hud"); json.WriteString("contract", "dagger.ui.snapshot.v1");
        json.WritePropertyName("value");
        WriteUiValue(json);
        json.WriteEndObject();
    }

    private void WriteUiValue(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WritePropertyName("player"); json.WriteStartObject();
        WritePoint(json, "position", Player.Position);
        json.WriteNumber("yawDegrees", Player.YawDegrees); json.WriteNumber("pitchDegrees", Player.PitchDegrees);
        json.WriteNumber("health", Player.Health); json.WriteNumber("maximumHealth", Player.MaximumHealth);
        json.WriteNumber("stamina", Player.Stamina); json.WriteNumber("maximumStamina", Player.MaximumStamina);
        json.WriteNumber("magicka", Player.Magicka); json.WriteNumber("maximumMagicka", Player.MaximumMagicka);
        json.WriteEndObject();
        var encounter = ActiveEncounter();
        if (encounter is not null)
        {
            json.WritePropertyName("activeEncounter"); json.WriteStartObject();
            json.WriteString("id", encounter.Id); json.WriteString("name", encounter.Name);
            json.WriteString("objective", encounter.Objective); json.WriteString("status", "active"); json.WriteEndObject();
        }
        else json.WriteNull("activeEncounter");
        json.WritePropertyName("actors"); json.WriteStartArray();
        foreach (var actor in _actors.Values)
        {
            json.WriteStartObject(); json.WriteNumber("entityId", actor.EntityId); json.WriteString("id", actor.Definition.Id);
            WritePoint(json, "position", actor.Position); json.WriteNumber("health", actor.Health);
            json.WriteNumber("maximumHealth", actor.MaximumHealth); json.WriteBoolean("dead", actor.IsDead); json.WriteEndObject();
        }
        json.WriteEndArray(); json.WriteString("lastOutcome", _lastOutcome); json.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter json, string property, WorldPoint point)
    {
        json.WritePropertyName(property); json.WriteStartArray(); json.WriteNumberValue(point.X); json.WriteNumberValue(point.Y); json.WriteNumberValue(point.Z); json.WriteEndArray();
    }

    private void WriteFrame(Utf8JsonWriter json)
    {
        json.WriteStartObject(); json.WriteNumber("schemaVersion", 1); json.WritePropertyName("ops"); json.WriteStartArray();
        var navSample = NavSample();
        if (!_renderCreated)
        {
            // The current Engine browser host has a fixed initial camera. This
            // is a camera-relative presentation tableau, not a second world:
            // gameplay/HUD retain the raw content-space points above.
            WriteCreate(json, 5001, "player-bearing", new WorldPoint(0f, .35f, .5f), new[] { .78f, .68f, .35f, 1f }, new[] { .38f, .38f, .38f }, true);
            foreach (var actor in _actors.Values)
                WriteCreate(json, actor.EntityId, actor.Definition.Id, LocalActorPoint(actor.Position), actor.Definition.Id == "rat" ? new[] { .45f, .3f, .18f, 1f } : new[] { .75f, .75f, .70f, 1f }, actor.Definition.Id == "rat" ? new[] { .7f, .45f, .95f } : new[] { .9f, 1.5f, .9f }, !actor.IsDead);
            for (var index = 0; index < NavSampleOffsets.Length; index++)
            {
                var point = navSample[index];
                WriteCreate(json, 6000 + index, "nav-walkable", point is null ? new WorldPoint(0f, 0f, 0f) : LocalNavPoint(point.Value), new[] { .18f, .31f, .26f, 1f }, new[] { .34f, .06f, .34f }, point is not null);
            }
            _renderCreated = true;
        }
        else
        {
            WriteUpdate(json, 5001, new WorldPoint(0f, .35f, .5f), new[] { .38f, .38f, .38f }, true);
            foreach (var actor in _actors.Values) WriteUpdate(json, actor.EntityId, LocalActorPoint(actor.Position), actor.Definition.Id == "rat" ? new[] { .7f, .45f, .95f } : new[] { .9f, 1.5f, .9f }, !actor.IsDead);
            for (var index = 0; index < NavSampleOffsets.Length; index++)
            {
                var point = navSample[index];
                WriteUpdate(json, 6000 + index, point is null ? new WorldPoint(0f, 0f, 0f) : LocalNavPoint(point.Value), new[] { .34f, .06f, .34f }, point is not null);
            }
        }
        json.WriteEndArray(); json.WriteEndObject();
    }

    private WorldPoint?[] NavSample() => NavSampleOffsets
        .Select(offset => _navigation.WalkableNeighbor(Player.Position, offset.X, offset.Z))
        .ToArray();

    private WorldPoint LocalActorPoint(WorldPoint worldPoint)
    {
        var local = LocalPoint(worldPoint);
        return new WorldPoint(local.X, .95f + Math.Clamp((worldPoint.Y - Player.Position.Y) * .1f, -.35f, .65f), local.Z);
    }

    private WorldPoint LocalNavPoint(WorldPoint worldPoint)
    {
        var local = LocalPoint(worldPoint);
        return new WorldPoint(local.X, .05f, local.Z);
    }

    private WorldPoint LocalPoint(WorldPoint worldPoint)
    {
        var dx = worldPoint.X - Player.Position.X;
        var dz = worldPoint.Z - Player.Position.Z;
        var yaw = Player.YawDegrees * MathF.PI / 180f;
        var right = (dx * MathF.Cos(yaw)) + (dz * MathF.Sin(yaw));
        var forward = (dx * MathF.Sin(yaw)) - (dz * MathF.Cos(yaw));
        return new WorldPoint(Math.Clamp(right * 1.45f, -5.5f, 5.5f), 0f, Math.Clamp(-forward * 1.45f, -6f, 1.5f));
    }

    private static void WriteCreate(Utf8JsonWriter json, long handle, string label, WorldPoint point, float[] color, float[] scale, bool visible)
    {
        json.WriteStartObject(); json.WriteString("op", "create"); json.WriteNumber("handle", handle); json.WriteNull("parent");
        json.WritePropertyName("node"); json.WriteStartObject(); json.WritePropertyName("geometry"); json.WriteStartObject(); json.WriteString("kind", "cube"); json.WriteEndObject();
        json.WritePropertyName("material"); json.WriteStartObject(); json.WritePropertyName("color"); WriteFloatArray(json, color); json.WriteBoolean("wireframe", false); json.WriteEndObject();
        WriteTransform(json, point, scale); json.WriteBoolean("visible", visible); json.WriteString("layer", "scene");
        json.WritePropertyName("metadata"); json.WriteStartObject(); json.WriteNumber("sourceEntity", handle); json.WriteNull("sourceSceneNode"); json.WritePropertyName("tags"); json.WriteStartArray(); json.WriteStringValue("dagger"); json.WriteEndArray(); json.WriteString("label", label); json.WriteEndObject();
        json.WriteEndObject(); json.WriteEndObject();
    }

    private static void WriteUpdate(Utf8JsonWriter json, long handle, WorldPoint point, float[] scale, bool visible)
    {
        json.WriteStartObject(); json.WriteString("op", "update"); json.WriteNumber("handle", handle); WriteTransform(json, point, scale); json.WriteBoolean("visible", visible); json.WriteEndObject();
    }

    private static void WriteTransform(Utf8JsonWriter json, WorldPoint point, float[] scale)
    {
        json.WritePropertyName("transform"); json.WriteStartObject(); WritePoint(json, "translation", point);
        json.WritePropertyName("rotation"); WriteFloatArray(json, [0f, 0f, 0f, 1f]); json.WritePropertyName("scale"); WriteFloatArray(json, scale); json.WriteEndObject();
    }

    private static void WriteFloatArray(Utf8JsonWriter json, IEnumerable<float> values)
    {
        json.WriteStartArray(); foreach (var value in values) json.WriteNumberValue(value); json.WriteEndArray();
    }

    private void Move(float deltaSeconds)
    {
        var forward = (_heldKeys.Contains("KeyW") ? 1f : 0f) - (_heldKeys.Contains("KeyS") ? 1f : 0f);
        var right = (_heldKeys.Contains("KeyD") ? 1f : 0f) - (_heldKeys.Contains("KeyA") ? 1f : 0f);
        if (forward == 0f && right == 0f) return;
        var magnitude = MathF.Sqrt(forward * forward + right * right);
        forward /= magnitude;
        right /= magnitude;
        var yaw = Player.YawDegrees * MathF.PI / 180f;
        var desiredX = Player.Position.X + ((MathF.Sin(yaw) * forward) + (MathF.Cos(yaw) * right)) * MoveSpeed * deltaSeconds;
        var desiredZ = Player.Position.Z + ((-MathF.Cos(yaw) * forward) + (MathF.Sin(yaw) * right)) * MoveSpeed * deltaSeconds;
        if (!_navigation.TryMove(Player.Position, desiredX, desiredZ, StepUp, FallSpeed * deltaSeconds, out var moved))
            _lastOutcome = "Blocked by Privateer's Hold navigation";
        else
            Player.Position = moved;
    }

    private void TryAttack()
    {
        if (Player.AttackCooldown > 0f) { _lastOutcome = "Weapon recovering"; return; }
        if (Player.Stamina < 5) { _lastOutcome = "Too exhausted to attack"; return; }
        var target = _actors.Values.Where(actor => !actor.IsDead)
            .OrderBy(actor => Player.Position.HorizontalDistanceTo(actor.Position)).FirstOrDefault();
        if (target is null || Player.Position.HorizontalDistanceTo(target.Position) > 2.25f)
        {
            _lastOutcome = "No target in melee reach";
            return;
        }
        Player.Stamina -= 5;
        Player.AttackCooldown = .75f;
        var roll = Random.Shared.Next(1, 101);
        var chance = Math.Clamp(
            Catalogs.Player.LongBlade + target.Definition.ArmorValue - 50
            + ((Catalogs.Player.Luck - target.Definition.Luck) / 10)
            + ((Catalogs.Player.Agility - target.Definition.Agility) / 10)
            - (target.Definition.Dodging / 4), 3, 97);
        if (roll > chance)
        {
            _lastOutcome = $"Missed {target.Definition.Id} ({roll} vs {chance})";
            return;
        }
        var weaponDamage = Random.Shared.Next(Player.EquippedWeapon.MinimumDamage, Player.EquippedWeapon.MaximumDamage + 1);
        var strengthModifier = (Catalogs.Player.Strength - 50) / 5;
        var damage = target.ApplyDamage(Math.Max(1, weaponDamage + strengthModifier));
        _lastOutcome = target.IsDead
            ? $"Defeated {target.Definition.Id} for {damage} damage"
            : $"Hit {target.Definition.Id} for {damage} damage";
    }

    private float TimeDelta(ulong observedNanoseconds)
    {
        if (_lastObservedNanoseconds is not ulong previous || observedNanoseconds <= previous)
        {
            _lastObservedNanoseconds = observedNanoseconds;
            return 1f / 60f;
        }
        _lastObservedNanoseconds = observedNanoseconds;
        return Math.Clamp((observedNanoseconds - previous) / 1_000_000_000f, 0.001f, .08f);
    }

    private static float NormalizeDegrees(float value) => (value % 360f + 360f) % 360f;
}
