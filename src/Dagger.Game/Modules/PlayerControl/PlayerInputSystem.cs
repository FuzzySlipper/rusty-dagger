using System.Numerics;
using Rusty.Engine;

namespace RustyDagger.Game.Modules.PlayerControl;

internal sealed class PlayerInputSystem(PlayerControlTuning tuning, ILookService look)
{
    private const uint Key = 1;
    private const uint PointerButton = 2;
    private const uint PointerDelta = 3;
    private const uint Clear = 7;
    private const uint DigitalIntent = 8;
    private const uint Press = 1;
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    internal void Apply(PlayerControlState player, ProductUpdateState update)
    {
        foreach (ProductInput input in update.Inputs)
        {
            if (input.Kind == Clear) { _held.Clear(); update.PlanarIntent = default; }
            else if (input.Kind == PointerDelta)
            {
                LookReceipt receipt = look.Integrate(new LookRequest(new LookState(player.YawRadians, player.PitchRadians), new Vector2(input.X, input.Y), LookConfiguration()));
                player.YawRadians = receipt.State.YawRadians;
                player.PitchRadians = receipt.State.PitchRadians;
            }
            else if (input.Kind == DigitalIntent)
            {
                if (input.Label == "attack" && input.X > 0f) update.AttackRequested = true;
                else if (input.Label is "move" or "movement") update.PlanarIntent = new Vector2(input.X, input.Y);
            }
            else if (input.Kind == Key && input.Label is "KeyW" or "KeyA" or "KeyS" or "KeyD")
            { if (input.Edge == Press) _held.Add(input.Label); else _held.Remove(input.Label); }
            else if ((input.Kind == PointerButton && input.Edge == Press) || (input.Kind == Key && input.Edge == Press && input.Label is "Space" or "Mouse0")) update.AttackRequested = true;
        }
        if (update.PlanarIntent == Vector2.Zero)
            update.PlanarIntent = new((_held.Contains("KeyD") ? 1f : 0f) - (_held.Contains("KeyA") ? 1f : 0f), (_held.Contains("KeyW") ? 1f : 0f) - (_held.Contains("KeyS") ? 1f : 0f));
    }

    private LookConfig LookConfiguration() => new(tuning.LookSensitivity, tuning.LookSensitivity, tuning.PitchMinimumRadians, tuning.PitchMaximumRadians, tuning.MaximumLookDeltaRadians, InvertHorizontal: 0, InvertVertical: 0, WrapYaw: 1, Reserved: 0);
}

internal sealed class ProductUpdateState(float deltaSeconds)
{
    internal float DeltaSeconds { get; } = deltaSeconds;
    internal List<ProductInput> Inputs { get; } = [];
    internal Vector2 PlanarIntent { get; set; }
    internal bool AttackRequested { get; set; }
    internal void Add(ProductInput input) => Inputs.Add(input);
}

internal readonly record struct ProductInput(uint Kind, uint Edge, float X, float Y, string Label);
