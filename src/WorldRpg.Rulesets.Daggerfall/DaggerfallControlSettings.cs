namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One semantic control action: its id, default keys and what it drives.</summary>
/// <param name="Id">The action id the session dispatches.</param>
/// <param name="DefaultKeys">The default keyboard keys.</param>
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
    /// <summary>The key no action may claim.</summary>
    public const string ReservedKey = "Escape";

    /// <summary>The semantic action catalog with defaults and categories.</summary>
    public static readonly IReadOnlyList<DaggerfallControlAction> Catalog =
    [
        new("move.forward", ["KeyW"], "movement"),
        new("move.backward", ["KeyS"], "movement"),
        new("move.left", ["KeyA"], "movement"),
        new("move.right", ["KeyD"], "movement"),
        new("attack", ["Mouse0"], "combat"),
        new("interact", ["KeyF"], "combat"),
        new("toggle-weapon", ["KeyZ"], "combat"),
        new("inventory", ["KeyI"], "interface"),
        new("character", ["KeyC"], "interface"),
        new("menu", ["Escape"], "interface"),
        new("spell", ["KeyV"], "magic"),
        new("rest", ["KeyR"], "rest"),
        new("map", ["KeyM"], "maps"),
        new("transport", ["KeyT"], "transport"),
    ];

    private readonly Dictionary<string, List<string>> _bindings;

    /// <summary>Creates default settings.</summary>
    public DaggerfallControlSettings() =>
        _bindings = Catalog.ToDictionary(action => action.Id, action => action.DefaultKeys.ToList(), StringComparer.Ordinal);

    private DaggerfallControlSettings(Dictionary<string, List<string>> bindings) => _bindings = bindings;

    /// <summary>The keys one action answers to.</summary>
    public IReadOnlyList<string> KeysFor(string action) =>
        _bindings.TryGetValue(action, out List<string>? keys) ? keys : throw new ArgumentOutOfRangeException(nameof(action), action, "No semantic control action carries this id.");

    /// <summary>Every action with its keys.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> All => _bindings.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value, StringComparer.Ordinal);

    /// <summary>
    /// Rebinds one action: refuses reserved keys and collisions, naming the holder. Pass swap
    /// to move the holder's keys onto the rebound action instead of refusing.
    /// </summary>
    public void Rebind(string action, IReadOnlyList<string> keys, bool swap = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(keys);
        if (!_bindings.ContainsKey(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "No semantic control action carries this id.");
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException("A rebinding states no keys.", nameof(keys));
        }

        if (_bindings[action].SequenceEqual(keys, StringComparer.Ordinal))
        {
            return;
        }

        foreach (string key in keys)
        {
            if (string.Equals(key, ReservedKey, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Key '{key}' is reserved for the menu and never rebinds.", nameof(keys));
            }

            string? holder = _bindings.FirstOrDefault(entry => !string.Equals(entry.Key, action, StringComparison.Ordinal) && entry.Value.Contains(key, StringComparer.Ordinal)).Key;
            if (holder is not null)
            {
                if (!swap)
                {
                    throw new InvalidOperationException($"Key '{key}' already answers '{holder}'; swap them explicitly or choose another key.");
                }

                (_bindings[holder], _bindings[action]) = (_bindings[action], keys.ToList());
                return;
            }
        }

        _bindings[action] = keys.ToList();
    }

    /// <summary>Restores every default.</summary>
    public void Reset()
    {
        _bindings.Clear();
        foreach (DaggerfallControlAction action in Catalog)
        {
            _bindings.Add(action.Id, action.DefaultKeys.ToList());
        }
    }

    /// <summary>Serializes the bindings for the settings holder.</summary>
    public string Serialize() =>
        System.Text.Json.JsonSerializer.Serialize(_bindings.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));

    /// <summary>Parses persisted bindings: unknown actions, reserved keys and collisions are refused.</summary>
    public static DaggerfallControlSettings Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Dictionary<string, List<string>>? parsed;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("Persisted controls parse to no bindings.", nameof(json), exception);
        }

        if (parsed is null)
        {
            throw new ArgumentException("Persisted controls parse to no bindings.", nameof(json));
        }

        DaggerfallControlSettings settings = new();
        settings._bindings.Clear();
        foreach (DaggerfallControlAction action in Catalog)
        {
            settings._bindings.Add(action.Id, action.DefaultKeys.ToList());
        }

        foreach ((string action, List<string> keys) in parsed)
        {
            settings.Rebind(action, keys);
        }

        return settings;
    }
}
