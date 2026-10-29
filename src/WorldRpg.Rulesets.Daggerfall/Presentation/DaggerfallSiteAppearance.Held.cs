using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon beside the classic sprite viewmodel. When the viewmodel tuning asks for a 3D style and
/// the installed first-person art covers the drawn weapon, the art draws it at rest and through every strike it has
/// motion for, and the sprite stays hidden but keeps playing: its playback remains the swing's clock, delivering the
/// hit and releasing the attack. A strike the art has no motion for shows the sprite for that swing.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private (IEngineContext Engine, DaggerfallHeldArt Art, DaggerfallViewmodelStyle Style)? heldArt;
    private DaggerfallHeldViewmodel? held;
    // Whether the 3D weapon draws the current action (false while a strike it has no motion for shows the sprite).
    private bool heldShowing;

    /// <summary>Draws the weapons <paramref name="art"/> covers in 3D in <paramref name="style"/>, from the next weapon drawn.</summary>
    internal void UseHeldArt(IEngineContext engine, DaggerfallHeldArt art, DaggerfallViewmodelStyle style)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(art);
        if (style == DaggerfallViewmodelStyle.Classic) throw new ArgumentOutOfRangeException(nameof(style), style, "The classic style draws no 3D weapon.");
        heldArt = (engine, art, style);
    }

    private void CreateHeld(NormalizedClassicWeapon weapon)
    {
        if (heldArt is not { } chosen || !chosen.Art.Draws(weapon.ResourceId)) return;
        held = new DaggerfallHeldViewmodel(chosen.Engine, chosen.Art, chosen.Style, NextVisualEntityId(), NextVisualEntityId());
        heldShowing = true;
    }

    private void RetireHeld()
    {
        if (held is not { } drawn) return;
        held = null;
        heldShowing = false;
        Retire(drawn);
    }

    // An action starts on the sprite: the 3D weapon rests for idle and moves through a strike it has motion for,
    // timed to the sprite's frames and hit frame; any other strike shows the sprite.
    private void StartHeldAction(string name, double frameSeconds, NormalizedClassicWeaponAction action, int hitFrame)
    {
        if (held is null) return;
        if (name == "idle")
        {
            held.Rest();
            heldShowing = true;
            return;
        }
        heldShowing = held.Moves(name);
        if (!heldShowing) { held.Rest(); return; }
        held.Strike(name, frameSeconds > 0d ? frameSeconds : 1d / action.FramesPerSecond, hitFrame, action.PlayedFrameCount);
    }

    private void AppendViewmodelFacts(List<AppearanceFact> facts, ViewmodelVisual weapon)
    {
        bool sprite = held is null || !heldShowing;
        facts.Add(new AppearanceFact(weapon.EntityId, false, 0, weapon.Transform, weapon.Appearance, sprite, RenderLayer.Viewmodel));
        held?.AppendFacts(facts, !sprite);
    }

    // Once the snapshot is published: the arms reach the weapon where it is drawn.
    private void ReachHeld() => held?.Reach();
}
