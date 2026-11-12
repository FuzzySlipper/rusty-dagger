using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The Daggerfall player's input actions, movement bindings and pad layout.</summary>
internal static class DaggerfallInput
{
    internal static readonly InputActionId ToggleWeapon = new("daggerfall.toggle-weapon");
    internal static readonly InputActionId Attack = new("daggerfall.attack");
    internal static readonly InputActionId Kick = new("daggerfall.kick");
    internal static readonly InputActionId Block = new("daggerfall.block");
    internal static readonly InputActionId Interact = new("daggerfall.interact");
    internal static readonly InputActionId Inventory = new("daggerfall.inventory");
    internal static readonly InputActionId Character = new("daggerfall.character");
    internal static readonly InputActionId Map = new("daggerfall.map");
    internal static readonly InputActionId Menu = new("daggerfall.menu");
    internal static readonly PlayerControlBindings Controls = new(
        ["move"u8.ToArray(), "movement"u8.ToArray()],
        KeyboardControl.KeyW,
        KeyboardControl.KeyS,
        KeyboardControl.KeyA,
        KeyboardControl.KeyD,
        new DirectionalMovementBindings("move.forward"u8.ToArray(), "move.backward"u8.ToArray(), "move.left"u8.ToArray(), "move.right"u8.ToArray()));
    internal static readonly string[] Shortcuts = ["spell", "rest", "transport", "activation.steal", "activation.grab",
        "activation.info", "activation.talk", "quick-save", "quick-load", "logbook", "notebook", "travel-map", "use-magic-item", "recast", "abort-spell",
        "radial"];
    internal static InputActionId Shortcut(string id) => new("daggerfall." + id);
    internal static readonly IReadOnlyList<InputActionBinding> Bindings = [
        .. Shortcuts.Select(id => new InputActionBinding(Shortcut(id), System.Text.Encoding.UTF8.GetBytes(id))),
        new(Attack, "attack"u8.ToArray()),
        new(Kick, "kick"u8.ToArray()),
        new(Block, "block"u8.ToArray()),
        new(ToggleWeapon, "toggle-weapon"u8.ToArray()),
        new(Interact, "interact"u8.ToArray()),
        new(Inventory, "inventory"u8.ToArray()),
        new(Character, "character"u8.ToArray()),
        new(Menu, "menu"u8.ToArray()),
        new(Map, "map"u8.ToArray()),
    ];

    /// <summary>
    /// How one input event moves a held control: true when <paramref name="intent"/>'s binding or a pad button for
    /// <paramref name="action"/> goes down, false when it comes up, and null for any other event.
    /// </summary>
    internal static bool? HeldChange(ProductInputEvent input, ReadOnlySpan<byte> intent, InputActionId action, IReadOnlyList<ControllerActionBinding> pad)
    {
        if (input.Kind is InputEventKind.MappedDigital or InputEventKind.DirectDigital && input.Intent.Span.SequenceEqual(intent))
            return input.Edge != InputEdge.Released && input.X > 0f;
        if (input.Kind != InputEventKind.ControllerButton || input.Edge is not (InputEdge.Pressed or InputEdge.Released)) return null;
        foreach (ControllerActionBinding binding in pad)
            if (binding.Button == input.ControllerButton && binding.Action == action) return input.Edge == InputEdge.Pressed;
        return null;
    }

    /// <summary>
    /// The pad's action buttons in the layout the browser shell delivers: the bottom face attacks, the
    /// right face reaches for what the player is facing, the left face readies the weapon, the top
    /// face opens the command radial, the left shoulder guards, the right shoulder kicks, select opens
    /// the pack, and start opens the menu. They are
    /// product bindings rather than engine mappings because the Engine publishes positions and this is
    /// the layer that knows what an action means.
    /// </summary>
    internal static readonly IReadOnlyList<ControllerActionBinding> PadActions = [
        new(ControllerButton.Button0, Attack),
        new(ControllerButton.Button1, Interact),
        new(ControllerButton.Button2, ToggleWeapon),
        new(ControllerButton.Button3, Shortcut("radial")),
        new(ControllerButton.Button4, Block),
        new(ControllerButton.Button5, Kick),
        new(ControllerButton.Button8, Inventory),
        new(ControllerButton.Button9, Menu),
    ];
}
