using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>The reins take the hands while riding, so the weapon is readied or sheathed only on foot or aboard ship.</summary>
    private bool WeaponHandsFree => State.Transport.IsOnFoot || State.Transport.OnShip;

    /// <summary>
    /// The command radial's commands in ring order, as the rules stand now: each one's words, whether it can be used and, when
    /// it cannot, why. The DOM draws and runs them; it decides none of this.
    /// </summary>
    internal IReadOnlyList<DaggerfallRadialCommand> RadialCommands(string? activationMode)
    {
        bool hands = WeaponHandsFree;
        bool beast = State.RacialOverrides.Current?.SuppressInventory == true;
        return
        [
            new("weapon", State.WeaponDrawn ? "Sheathe weapon" : "Ready weapon", hands,
                Reason: hands ? null : "The reins take your hands while you ride."),
            .. DaggerfallHudText.RadialModes.Select(mode => new DaggerfallRadialCommand($"mode-{mode}", DaggerfallHudText.ModeLabel(mode), true,
                Current: activationMode == mode)),
            .. DaggerfallHudText.RadialPanels.Select(panel => panel.Id == DaggerfallPanel.Inventory && beast
                ? new DaggerfallRadialCommand(panel.Id, panel.Label, false, Reason: DaggerfallRacialOverrideView.InventoryRefusal)
                : new DaggerfallRadialCommand(panel.Id, panel.Label, true)),
        ];
    }
}
