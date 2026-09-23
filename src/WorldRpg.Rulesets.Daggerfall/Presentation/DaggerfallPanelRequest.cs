namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>One panel the player asked for, and the revision that makes it a request rather than state.</summary>
internal sealed record DaggerfallPanelRequest(string Panel, ulong Revision);

/// <summary>
/// The names of the DOM's own menu actions, spelled exactly as the DOM's buttons carry them.
/// </summary>
/// <remarks>
/// A pad button that opens a panel asks for one of these rather than for a product-side panel
/// identity, so the menu's vocabulary stays where the menu is and the product never has to keep a
/// second copy of it in step. The panel the player sees, and whether it is open, remain the DOM's.
/// </remarks>
internal static class DaggerfallPanel
{
    internal const string Inventory = "inventory";
    internal const string Character = "character";
    internal const string Journal = "journal";

    /// <summary>The menu toggle the DOM's own Escape key performs.</summary>
    internal const string Menu = "menu";
}
