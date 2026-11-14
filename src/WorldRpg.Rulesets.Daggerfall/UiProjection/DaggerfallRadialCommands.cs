using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The command radial's commands in ring order, as the rules stand now: each one's words, whether it can be used and, when
/// it cannot, why. The DOM draws and runs them; it decides none of this.
/// </summary>
internal sealed class DaggerfallRadialCommands(DaggerfallTransportPolicy transport, DaggerfallRacialOverrides racialOverrides,
    Func<bool> weaponDrawn)
{
    internal IReadOnlyList<DaggerfallRadialCommand> Read(string? activationMode)
    {
        bool hands = transport.WeaponHandsFree;
        bool beast = racialOverrides.Current?.SuppressInventory == true;
        return
        [
            new("weapon", weaponDrawn() ? "Sheathe weapon" : "Ready weapon", hands,
                Reason: hands ? null : "The reins take your hands while you ride."),
            .. DaggerfallHudText.RadialModes.Select(mode => new DaggerfallRadialCommand($"mode-{mode}", DaggerfallHudText.ModeLabel(mode), true,
                Current: activationMode == mode)),
            .. DaggerfallHudText.RadialPanels.Select(panel => panel.Id == DaggerfallPanel.Inventory && beast
                ? new DaggerfallRadialCommand(panel.Id, panel.Label, false, Reason: DaggerfallRacialOverrideView.InventoryRefusal)
                : new DaggerfallRadialCommand(panel.Id, panel.Label, true)),
        ];
    }
}
