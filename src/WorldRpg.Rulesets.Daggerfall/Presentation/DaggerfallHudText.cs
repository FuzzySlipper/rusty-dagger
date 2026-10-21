using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>What the player holds: a drawn weapon (or bare hands) or a sheathed one.</summary>
internal sealed record DaggerfallWeaponView(bool Drawn, string Name);

/// <summary>What ordinary use would do to the target the player aims at, in the active activation mode.</summary>
internal sealed record DaggerfallInteractionPromptView(string Action, string Target);

/// <summary>
/// The words the HUD shows: activation modes, interaction verbs, the weapon state, detector kinds and
/// distances. The DOM draws them and decides none of them.
/// </summary>
internal static class DaggerfallHudText
{
    /// <summary>The activation modes in the order the menu offers them, with their player names.</summary>
    internal static readonly IReadOnlyList<(string Id, string Label)> ActivationModes =
        [("grab", "Grab"), ("info", "Information"), ("talk", "Talk"), ("steal", "Steal / Lockpick"), ("bash", "Bash")];

    internal static string ModeLabel(string mode) =>
        ActivationModes.FirstOrDefault(row => row.Id == mode).Label ?? mode;

    /// <summary>
    /// What ordinary use does to a target of this kind in this mode, as the prompt's verb, where the kind
    /// alone decides it; null where use would be refused, so no prompt promises it. A door's verb depends
    /// on its state, so its owner words it (<c>IDaggerfallDoorActivationOwner.PromptAction</c>) and this is
    /// only its fallback.
    /// </summary>
    internal static string? PromptAction(DaggerfallActivationMode mode, DaggerfallActivationTargetKind kind) => (mode, kind) switch
    {
        // Information names a static person and opens a conversation with a passer-by; either way it is about them.
        (DaggerfallActivationMode.Info, DaggerfallActivationTargetKind.Npc) => "Ask about",
        (DaggerfallActivationMode.Info, _) => "Examine",
        // A person is not approached by force, and a door does not answer talk.
        (DaggerfallActivationMode.Bash, DaggerfallActivationTargetKind.Npc) => null,
        (DaggerfallActivationMode.Talk, DaggerfallActivationTargetKind.Door) => null,
        (DaggerfallActivationMode.Steal, DaggerfallActivationTargetKind.Door) => "Pick the lock of",
        (DaggerfallActivationMode.Steal, DaggerfallActivationTargetKind.Npc) => "Pickpocket",
        (DaggerfallActivationMode.Steal, _) => "Steal from",
        (DaggerfallActivationMode.Bash, _) => "Bash",
        (_, DaggerfallActivationTargetKind.Npc) => "Talk to",
        (_, DaggerfallActivationTargetKind.Door) => "Open",
        (_, DaggerfallActivationTargetKind.Portal) => "Go through",
        (_, DaggerfallActivationTargetKind.Corpse) => "Search",
        (_, DaggerfallActivationTargetKind.Item) => "Take",
        _ => "Open",
    };

    /// <summary>The prompt's name for a target that carries no label of its own.</summary>
    internal static string TargetName(DaggerfallActivationTargetKind kind) => kind switch
    {
        DaggerfallActivationTargetKind.Door => "the door",
        DaggerfallActivationTargetKind.Portal => "the exit",
        DaggerfallActivationTargetKind.Container => "the container",
        DaggerfallActivationTargetKind.Corpse => "the body",
        DaggerfallActivationTargetKind.Item => "the item",
        _ => "them",
    };

    /// <summary>The weapon state line: what is drawn, or that it is sheathed.</summary>
    internal static string WeaponState(DaggerfallWeaponView weapon) => weapon.Drawn ? "Drawn" : "Sheathed";

    /// <summary>The detector kind as the HUD names it.</summary>
    internal static string DetectorLabel(string kind) => kind switch
    {
        "magic" => "Detect magic",
        "enemy" => "Detect enemy",
        "treasure" => "Detect treasure",
        _ => "Detect",
    };

    /// <summary>A distance in whole metres.</summary>
    internal static string Distance(double metres) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, Math.Round(metres)):0} m");
}
