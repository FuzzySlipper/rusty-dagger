using Rusty.Engine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One semantic control action: its id, default controls and what it drives.</summary>
/// <param name="Id">The action id the session dispatches.</param>
/// <param name="DefaultKeys">The default physical controls.</param>
/// <param name="Category">Movement, combat, interface, magic, rest, maps or transport.</param>
public sealed record DaggerfallControlAction(string Id, IReadOnlyList<string> DefaultKeys, string Category);

/// <summary>
/// Semantic controls: the rebindable action catalog with conflict, reserved-key and reset policy.
/// Movement, activation, inventory, spell, rest, map and transport actions rebind through this
/// owner; player preference lives here while authored ruleset tuning stays in DaggerfallTuning.
/// A rebinding that collides with another gameplay action is refused naming the holder, unless
/// the caller swaps them explicitly. Escape is reserved for the menu and never rebinds. Text
/// entry consumes keys before gameplay: while a text field holds focus no rebinding fires a
/// gameplay action, so typing never attacks.
/// </summary>
public sealed class DaggerfallControlSettings
{
    /// <summary>The key no action other than the fixed menu may claim.</summary>
    public const string ReservedKey = "Escape";

    /// <summary>The semantic action catalog with defaults and categories.</summary>
    public static readonly IReadOnlyList<DaggerfallControlAction> Catalog =
    [
        new("move.forward", ["KeyW"], "movement"),
        new("move.backward", ["KeyS"], "movement"),
        new("move.left", ["KeyA"], "movement"),
        new("move.right", ["KeyD"], "movement"),
        new("run", ["ShiftLeft"], "movement"),
        new("crouch", ["ControlLeft"], "movement"),
        new("jump", ["Space"], "movement"),
        new("attack", ["Primary"], "combat"),
        new("interact", ["KeyF"], "combat"),
        new("toggle-weapon", ["KeyZ"], "combat"),
        new("inventory", ["KeyI"], "interface"),
        new("character", ["KeyC"], "interface"),
        new("menu", [ReservedKey], "interface"),
        new("spell", ["KeyV"], "magic"),
        new("rest", ["KeyR"], "rest"),
        new("map", ["KeyM"], "maps"),
        new("transport", ["KeyT"], "transport"),
    ];

    private const string MenuAction = "menu";
    private readonly Dictionary<string, List<string>> _bindings;

    /// <summary>Creates default settings.</summary>
    public DaggerfallControlSettings() => _bindings = CreateDefaultBindings();

    private DaggerfallControlSettings(Dictionary<string, List<string>> bindings) => _bindings = bindings;

    /// <summary>The controls one action answers to.</summary>
    public IReadOnlyList<string> KeysFor(string action) =>
        _bindings.TryGetValue(action, out List<string>? keys) ? keys : throw new ArgumentOutOfRangeException(nameof(action), action, "No semantic control action carries this id.");

    /// <summary>Every action with its controls.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> All => _bindings.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value, StringComparer.Ordinal);

    /// <summary>
    /// Rebinds one action: refuses reserved controls and collisions, naming the holder. Pass swap
    /// to exchange the rebound action's old controls with one conflicting action.
    /// </summary>
    public void Rebind(string action, IReadOnlyList<string> keys, bool swap = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(keys);
        if (!_bindings.ContainsKey(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "No semantic control action carries this id.");
        }

        if (string.Equals(action, MenuAction, StringComparison.Ordinal))
        {
            throw new ArgumentException("The menu always answers Escape and cannot be rebound.", nameof(action));
        }

        List<string> requested = ValidateActionKeys(action, keys, nameof(keys));
        if (_bindings[action].SequenceEqual(requested, StringComparer.Ordinal))
        {
            return;
        }

        string[] holders = _bindings
            .Where(entry => !string.Equals(entry.Key, action, StringComparison.Ordinal)
                && entry.Value.Any(key => requested.Contains(key, StringComparer.Ordinal)))
            .Select(entry => entry.Key)
            .ToArray();
        if (holders.Length != 0 && !swap)
        {
            string holder = holders[0];
            string key = requested.First(candidate => _bindings[holder].Contains(candidate, StringComparer.Ordinal));
            throw new InvalidOperationException($"Key '{key}' already answers '{holder}'; swap them explicitly or choose another key.");
        }

        if (holders.Length > 1)
        {
            throw new InvalidOperationException($"The requested controls collide with multiple actions ({string.Join(", ", holders)}); one rebind can swap with only one action.");
        }

        Dictionary<string, List<string>> candidate = CopyBindings(_bindings);
        candidate[action] = requested;
        if (holders.Length == 1)
        {
            candidate[holders[0]] = _bindings[action].ToList();
        }

        ValidateBindings(candidate, nameof(keys));
        ReplaceBindings(candidate);
    }

    /// <summary>Restores every default.</summary>
    public void Reset() => ReplaceBindings(CreateDefaultBindings());

    /// <summary>Serializes the bindings for the settings holder.</summary>
    public string Serialize() => JsonSerializer.Serialize(_bindings, DaggerfallControlSettingsJsonContext.Default.DictionaryStringListString);

    /// <summary>Parses a complete persisted binding set after validating every action and control.</summary>
    public static DaggerfallControlSettings Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Dictionary<string, List<string>>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, DaggerfallControlSettingsJsonContext.Default.DictionaryStringListString);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Persisted controls parse to no bindings.", nameof(json), exception);
        }

        if (parsed is null)
        {
            throw new ArgumentException("Persisted controls parse to no bindings.", nameof(json));
        }

        ValidateBindings(parsed, nameof(json));
        return new DaggerfallControlSettings(CopyBindings(parsed));
    }

    /// <summary>Complete Engine-owned physical mapping set for the declared semantic catalog.</summary>
    public ProductInputMapping[] PhysicalMappings() => Catalog.SelectMany(action =>
        KeysFor(action.Id).Select((key, index) =>
        {
            bool keyboard = Enum.TryParse(key, out KeyboardControl control) && control != KeyboardControl.None;
            return new ProductInputMapping(
                Encoding.UTF8.GetBytes($"controls.{action.Id}.{index}"), Encoding.UTF8.GetBytes(action.Id),
                keyboard ? InputTriggerKind.Key : InputTriggerKind.PointerButton,
                action.Category == "movement" ? InputEdge.Held : InputEdge.Pressed,
                default, keyboard ? control : default,
                keyboard ? default : Enum.Parse<PointerButton>(key), default, default, default,
                new InputContext("gameplay"u8.ToArray()));
        })).ToArray();

    private static Dictionary<string, List<string>> CreateDefaultBindings() =>
        Catalog.ToDictionary(action => action.Id, action => action.DefaultKeys.ToList(), StringComparer.Ordinal);

    private static Dictionary<string, List<string>> CopyBindings(IReadOnlyDictionary<string, List<string>> bindings) =>
        bindings.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.Ordinal);

    private void ReplaceBindings(Dictionary<string, List<string>> bindings)
    {
        _bindings.Clear();
        foreach ((string action, List<string> keys) in bindings)
        {
            _bindings.Add(action, keys);
        }
    }

    private static void ValidateBindings(IReadOnlyDictionary<string, List<string>> bindings, string parameterName)
    {
        foreach (string action in bindings.Keys)
        {
            if (!Catalog.Any(candidate => string.Equals(candidate.Id, action, StringComparison.Ordinal)))
            {
                throw new ArgumentOutOfRangeException(parameterName, action, "No semantic control action carries this id.");
            }
        }

        Dictionary<string, string> holders = new(StringComparer.Ordinal);
        foreach (DaggerfallControlAction action in Catalog)
        {
            if (!bindings.TryGetValue(action.Id, out List<string>? keys))
            {
                throw new ArgumentException($"Persisted controls omit required action '{action.Id}'.", parameterName);
            }

            foreach (string key in ValidateActionKeys(action.Id, keys, parameterName))
            {
                if (!holders.TryAdd(key, action.Id))
                {
                    throw new ArgumentException($"Key '{key}' is assigned to both '{holders[key]}' and '{action.Id}'.", parameterName);
                }
            }
        }
    }

    private static List<string> ValidateActionKeys(string action, IReadOnlyList<string>? keys, string parameterName)
    {
        if (keys is null)
        {
            throw new ArgumentException($"Action '{action}' has null controls.", parameterName);
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException($"Action '{action}' states no controls.", parameterName);
        }

        if (string.Equals(action, MenuAction, StringComparison.Ordinal)
            && (keys.Count != 1 || !string.Equals(keys[0], ReservedKey, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The menu always answers Escape and cannot be rebound.", parameterName);
        }

        HashSet<string> uniqueKeys = new(StringComparer.Ordinal);
        List<string> validated = new(keys.Count);
        for (int index = 0; index < keys.Count; index++)
        {
            string? key = keys[index];
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException($"Action '{action}' has a null or empty control at index {index}.", parameterName);
            }

            if (!IsSupportedControl(key))
            {
                throw new ArgumentException($"Control '{key}' is not a supported Engine input control.", parameterName);
            }

            if (!string.Equals(action, MenuAction, StringComparison.Ordinal) && string.Equals(key, ReservedKey, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Key '{key}' is reserved for the menu and never rebinds.", parameterName);
            }

            if (!uniqueKeys.Add(key))
            {
                throw new ArgumentException($"Action '{action}' repeats control '{key}'.", parameterName);
            }

            validated.Add(key);
        }

        return validated;
    }

    private static bool IsSupportedControl(string key) =>
        Enum.TryParse(key, out KeyboardControl keyboard)
        && keyboard != KeyboardControl.None
        && Enum.IsDefined(keyboard)
        && string.Equals(key, keyboard.ToString(), StringComparison.Ordinal)
        || Enum.TryParse(key, out PointerButton pointer)
        && pointer != PointerButton.None
        && Enum.IsDefined(pointer)
        && string.Equals(key, pointer.ToString(), StringComparison.Ordinal);
}

[JsonSerializable(typeof(Dictionary<string, List<string>>))]
internal partial class DaggerfallControlSettingsJsonContext : JsonSerializerContext;
