using WorldRpg.Kit;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player's own requests from one simulation step: the weapon toggle, an attack or a kick, contextual
/// interaction, the panel buttons and the shortcuts.
/// </summary>
internal sealed record DaggerfallPlayerActs(bool ToggleWeapon, bool Attack, bool Kick, bool Interact,
    bool Inventory, bool Character, bool Menu, bool Map, string[] Shortcuts)
{
    /// <summary>The acts one step's input requests.</summary>
    internal static DaggerfallPlayerActs Read(ProductUpdateState update) => new(
        ToggleWeapon: update.IsRequested(DaggerfallInput.ToggleWeapon),
        Attack: update.IsRequested(DaggerfallInput.Attack),
        Kick: update.IsRequested(DaggerfallInput.Kick),
        Interact: update.IsRequested(DaggerfallInput.Interact),
        Inventory: update.IsRequested(DaggerfallInput.Inventory),
        Character: update.IsRequested(DaggerfallInput.Character),
        Menu: update.IsRequested(DaggerfallInput.Menu),
        Map: update.IsRequested(DaggerfallInput.Map),
        Shortcuts: DaggerfallInput.Shortcuts.Where(id => update.IsRequested(DaggerfallInput.Shortcut(id))).ToArray());

    internal bool Any => ToggleWeapon || Attack || Kick || Interact || Inventory || Character || Menu || Map || Shortcuts.Length != 0;

    /// <summary>The acts in the world these requests make, by the names a refusal gives them: the panel buttons and shortcuts are not.</summary>
    internal IEnumerable<string> WorldActs => new[] { (Attack, "Attack"), (Kick, "Kick"), (Interact, "Use"), (ToggleWeapon, "Weapon toggle") }
        .Where(act => act.Item1).Select(act => act.Item2);

    /// <summary>Folds a later step's requests into acts still waiting.</summary>
    internal DaggerfallPlayerActs Then(DaggerfallPlayerActs later) => new(
        ToggleWeapon || later.ToggleWeapon, Attack || later.Attack, Kick || later.Kick, Interact || later.Interact,
        Inventory || later.Inventory, Character || later.Character, Menu || later.Menu, Map || later.Map, Shortcuts.Concat(later.Shortcuts).Distinct(StringComparer.Ordinal).ToArray());

    /// <summary>A menu or mode shortcut consumes the slice's attack, kick and activation, just like a UI request; each is reported dropped.</summary>
    internal DaggerfallPlayerActs AfterShortcuts(DaggerfallInputRefusals refusals)
    {
        if (Shortcuts.Length == 0) return this;
        foreach (var (pressed, name) in new[] { (Attack, "Attack"), (Kick, "Kick"), (Interact, "Use") })
            if (pressed) refusals.Dropped(name, "a shortcut took the same update");
        return this with { Attack = false, Kick = false, Interact = false };
    }

    /// <summary>
    /// Whether the slice's attack and kick reach the weapon: not under a restriction on physical attacks, not with the
    /// weapon sheathed, and not when interaction owns the slice (direct semantic input can carry both intents in the
    /// same Engine delivery, and it follows the same no-attack rule as a DOM loot action). One that does not is reported
    /// to <paramref name="refusals"/>; false with neither pressed.
    /// </summary>
    internal bool Swings(ActorControlRestrictions restrictions, bool weaponDrawn, DaggerfallInputRefusals refusals)
    {
        if (!Attack && !Kick) return false;
        string input = Attack ? "Attack" : "Kick";
        if (restrictions.PhysicalAttacks) refusals.Refused(input, "paralysed");
        else if (Interact) refusals.Dropped(input, "use took the same update");
        else if (!weaponDrawn) refusals.Refused(input, "weapon sheathed");
        else return true;
        return false;
    }
}

/// <summary>
/// When the player's acts run. An enemy hit on the player that is already in flight lands at its damage frame, which the
/// outer admitted update reaches after its steps; the player's own acts wait for that same boundary rather than being
/// dropped, so a lethal frame reached there still resolves first and a frame not reached yet lets the player act in
/// this update, as the donor's independent attack timing does. Once one step defers, the rest of the update defers
/// behind it so the acts keep their order. Acts never wait past the update.
/// </summary>
/// <param name="enemyHitInFlight">Whether an enemy hit on the player is in flight now (Combat's held impact or an appearance's pending hit).</param>
/// <param name="refusals">Where acts that waited and were never performed say why.</param>
internal sealed class DaggerfallPlayerActTiming(Func<bool> enemyHitInFlight, DaggerfallInputRefusals refusals)
{
    private DaggerfallPlayerActs? _deferred;

    /// <summary>The acts to perform in this step, or null when they wait behind an enemy hit.</summary>
    internal DaggerfallPlayerActs? PerformNow(DaggerfallPlayerActs acts)
    {
        if (_deferred is not null || acts.Any && enemyHitInFlight())
        {
            _deferred = _deferred?.Then(acts) ?? acts;
            return null;
        }
        return acts;
    }

    /// <summary>
    /// The acts that waited, once the update's impacts have landed; none when nothing waited, and none performed when a
    /// lethal impact or a legal modal holds the world now, as they would not have been in the step itself.
    /// </summary>
    internal DaggerfallPlayerActs? TakeDeferred(bool worldHeld, bool playerDown)
    {
        if (_deferred is not { } acts) return null;
        _deferred = null;
        if (!worldHeld && !playerDown) return acts;
        foreach (string act in acts.WorldActs) refusals.Dropped(act, playerDown ? "the enemy's hit it waited for downed the player" : "the enemy's hit it waited for brought a modal up");
        return null;
    }

    /// <summary>A paused or held update reached no impact boundary, so what waited is not performed.</summary>
    internal void Drop()
    {
        foreach (string act in _deferred?.WorldActs ?? []) refusals.Dropped(act, "the world held before the enemy's hit it waited for landed");
        _deferred = null;
    }
}
