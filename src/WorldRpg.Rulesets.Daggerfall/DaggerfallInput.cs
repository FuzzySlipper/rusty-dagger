using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The Daggerfall player's input actions, movement bindings and pad layout.</summary>
internal static class DaggerfallInput
{
    internal static readonly InputActionId ToggleWeapon = new("daggerfall.toggle-weapon");
    internal static readonly InputActionId Attack = new("daggerfall.attack");
    internal static readonly InputActionId Interact = new("daggerfall.interact");
    internal static readonly InputActionId Inventory = new("daggerfall.inventory");
    internal static readonly InputActionId Character = new("daggerfall.character");
    internal static readonly InputActionId Menu = new("daggerfall.menu");
    internal static readonly PlayerControlBindings Controls = new(
        ["move"u8.ToArray(), "movement"u8.ToArray()],
        KeyboardControl.KeyW,
        KeyboardControl.KeyS,
        KeyboardControl.KeyA,
        KeyboardControl.KeyD,
        new DirectionalMovementBindings("move.forward"u8.ToArray(), "move.backward"u8.ToArray(), "move.left"u8.ToArray(), "move.right"u8.ToArray()));
    internal static readonly IReadOnlyList<InputActionBinding> Bindings = [
        new(Attack, "attack"u8.ToArray()),
        new(ToggleWeapon, "toggle-weapon"u8.ToArray()),
        new(Interact, "interact"u8.ToArray()),
        new(Inventory, "inventory"u8.ToArray()),
        new(Character, "character"u8.ToArray()),
        new(Menu, "menu"u8.ToArray()),
    ];

    /// <summary>
    /// The pad's action buttons in the layout the browser shell delivers: the bottom face attacks, the
    /// right face reaches for what the player is facing, the left face readies the weapon, the top
    /// face opens the character sheet, select opens the pack, and start opens the menu. They are
    /// product bindings rather than engine mappings because the Engine publishes positions and this is
    /// the layer that knows what an action means.
    /// </summary>
    internal static readonly IReadOnlyList<ControllerActionBinding> PadActions = [
        new(ControllerButton.Button0, Attack),
        new(ControllerButton.Button1, Interact),
        new(ControllerButton.Button2, ToggleWeapon),
        new(ControllerButton.Button3, Character),
        new(ControllerButton.Button8, Inventory),
        new(ControllerButton.Button9, Menu),
    ];
}
