using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon beside the classic sprite viewmodel. When the viewmodel tuning asks for a 3D style and
/// a family of the installed first-person art draws the drawn weapon, the family's clip-led arms loop its idle at rest
/// and play its clip for every strike it has one for, and the sprite stays hidden but keeps playing: its playback
/// remains the swing's clock, delivering the hit and releasing the attack. A strike the family has no clip for shows
/// the sprite for that swing. While the developer workbench views a family (<see cref="ViewHeld"/>), it is drawn
/// whatever the hands wield and only the workbench plays it; the swings still play and land on their hidden sprites.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private (IEngineContext Engine, DaggerfallHeldArt Art, DaggerfallViewmodelStyle Style)? heldArt;
    private DaggerfallHeldViewmodel? held;
    // Whether the 3D weapon draws the current action (false while a strike its family has no clip for shows the sprite).
    private bool heldShowing;
    // Whether the developer workbench views the art, which then owns the 3D weapon.
    private bool heldViewed;

    /// <summary>Draws the weapons <paramref name="art"/> covers in 3D in <paramref name="style"/>, from the next weapon drawn.</summary>
    internal void UseHeldArt(IEngineContext engine, DaggerfallHeldArt art, DaggerfallViewmodelStyle style)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(art);
        if (style == DaggerfallViewmodelStyle.Classic) throw new ArgumentOutOfRangeException(nameof(style), style, "The classic style draws no 3D weapon.");
        heldArt = (engine, art, style);
    }

    /// <summary>
    /// The developer workbench's view: a family's 3D weapon, drawn whatever the hands wield, for the workbench alone to
    /// play; null when this appearance draws no 3D weapon.
    /// </summary>
    internal DaggerfallHeldViewmodel? ViewHeld(string family)
    {
        if (heldArt is not { } chosen) return null;
        if (held?.Family != family)
        {
            // Built before the drawn one retires, so a family that cannot be drawn leaves the drawn one in place.
            DaggerfallHeldViewmodel viewed = new(chosen.Engine, chosen.Art, family, chosen.Style, NextVisualEntityId(), NextVisualEntityId());
            if (held is { } other) Retire(other);
            held = viewed;
        }
        heldViewed = true;
        heldShowing = true;
        return held;
    }

    /// <summary>The 3D weapon drawn now, in play or in the workbench's view; null when the sprites are drawn.</summary>
    internal DaggerfallHeldViewmodel? DrawnHeld => heldShowing ? held : null;

    /// <summary>Ends the workbench's view: the hands show what they wield again, at rest.</summary>
    internal void EndHeldView()
    {
        if (!heldViewed || held is not { } shown) return;
        heldViewed = false;
        if (viewmodel is { } weapon && heldArt?.Art.Family(weapon.Weapon.ResourceId) == shown.Family)
        {
            shown.Rest(IdleFrame(weapon.Weapon));
            heldShowing = true;
        }
        else
        {
            RetireHeld();
            if (viewmodel is { } wielded) CreateHeld(wielded.Weapon);
        }
    }

    /// <summary>The classic weapon a visual names, whose actions time the art's strikes; null when the session has none.</summary>
    internal NormalizedClassicWeapon? ClassicWeapon(string resourceId) => classicPresentation.Weapons.GetValueOrDefault(resourceId);

    private void CreateHeld(NormalizedClassicWeapon weapon)
    {
        if (heldViewed || heldArt is not { } chosen || chosen.Art.Family(weapon.ResourceId) is not { } family) return;
        held = new DaggerfallHeldViewmodel(chosen.Engine, chosen.Art, family, chosen.Style, NextVisualEntityId(), NextVisualEntityId());
        heldShowing = true;
    }

    private void RetireHeld()
    {
        if (heldViewed || held is not { } drawn) return;
        held = null;
        heldShowing = false;
        Retire(drawn);
    }

    // An action starts on the sprite: the arms loop their idle for idle and play a strike's clip, timed to the sprite's
    // frames and hit frame; a strike without a clip shows the sprite.
    private void StartHeldAction(string name, double frameSeconds, NormalizedClassicWeaponAction action, int hitFrame)
    {
        if (held is null || heldViewed) return;
        double frame = frameSeconds > 0d ? frameSeconds : 1d / action.FramesPerSecond;
        if (name == "idle")
        {
            held.Rest((float)frame);
            heldShowing = true;
            return;
        }
        heldShowing = held.Moves(name);
        if (!heldShowing) { held.Rest((float)frame); return; }
        held.Strike(name, frame, hitFrame, action.PlayedFrameCount);
    }

    // One classic frame of a weapon's idle, at which the retro style samples the idle loop.
    private static float IdleFrame(NormalizedClassicWeapon weapon) =>
        weapon.Actions.TryGetValue("idle", out NormalizedClassicWeaponAction? idle) ? 1F / idle.FramesPerSecond : 0;

    // The world seconds this update admitted, on which the arms' clips play.
    private void StepHeld(ProductUpdateFacts update) => held?.Step((float)(update.FixedDeltaSeconds * update.AdmittedStepCount));

    // The sprite viewmodel, hidden while the 3D weapon draws, and the 3D weapon (alone while the workbench views it
    // and the hands wield no drawn weapon).
    private void AppendViewmodelFacts(List<AppearanceFact> facts, ViewmodelVisual? weapon)
    {
        bool sprite = held is null || !heldShowing;
        if (weapon is not null)
            facts.Add(new AppearanceFact(weapon.EntityId, false, 0, weapon.Transform, weapon.Appearance, sprite, RenderLayer.Viewmodel));
        held?.AppendFacts(facts, !sprite);
    }

    // Once the snapshot is published: the arms are posed by their clip.
    private void ReachHeld() => held?.Reach();
}
