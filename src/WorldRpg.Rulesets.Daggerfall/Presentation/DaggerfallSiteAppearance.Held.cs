using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon beside the classic sprite viewmodel. When the viewmodel tuning asks for a 3D style and
/// a family of the installed first-person art draws the drawn weapon, the family's clip-led arms loop its idle at rest
/// and play its clip for every attack it has one for, and the sprite stays hidden. Both follow the rules' attack clock.
/// An attack the family has no clip for shows the sprite for that swing. The wielded item chooses the family and look and tints the look by its metal, so a swap
/// between weapons of one classic visual redraws the 3D weapon while the sprite stays. While the developer workbench
/// views a drawing (<see cref="ViewHeld"/>), it is drawn whatever the hands wield and only the workbench plays it.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private (IEngineContext Engine, DaggerfallHeldArt Art, DaggerfallViewmodelStyle Style, IReadOnlyDictionary<string, DaggerfallDyeTint> Metals,
        Func<UniqueInventoryItem, (int? Template, string Metal)> ItemOf, Func<EquipmentRead, (int Template, string Metal)?> ShieldOf)? heldArt;
    private DaggerfallHeldViewmodel? held;
    // The item the hands wield, which the 3D weapon is drawn for; null for bare hands and a beast's claws.
    private UniqueInventoryItem? heldItem;
    // The shield on the off arm, which the 3D weapon draws too; null for none.
    private (int Template, string Metal)? heldShield;
    // Whether the 3D weapon draws the current action (false while a strike its family has no clip for shows the sprite).
    private bool heldShowing;
    // Whether the developer workbench views the art, which then owns the 3D weapon.
    private bool heldViewed;

    /// <summary>
    /// Draws the weapons <paramref name="art"/> covers in 3D in <paramref name="style"/>, from the next weapon drawn,
    /// each model's metal tinted by <paramref name="metals"/>; <paramref name="itemOf"/> reads a wielded item's template
    /// and metal, and <paramref name="shieldOf"/> those of the shield worn.
    /// </summary>
    internal void UseHeldArt(IEngineContext engine, DaggerfallHeldArt art, DaggerfallViewmodelStyle style, IReadOnlyDictionary<string, DaggerfallDyeTint> metals,
        Func<UniqueInventoryItem, (int? Template, string Metal)> itemOf, Func<EquipmentRead, (int Template, string Metal)?> shieldOf)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(metals);
        ArgumentNullException.ThrowIfNull(itemOf);
        ArgumentNullException.ThrowIfNull(shieldOf);
        if (style == DaggerfallViewmodelStyle.Classic) throw new ArgumentOutOfRangeException(nameof(style), style, "The classic style draws no 3D weapon.");
        heldArt = (engine, art, style, metals, itemOf, shieldOf);
    }

    /// <summary>
    /// The developer workbench's view: a drawing's 3D weapon, drawn whatever the hands wield, for the workbench alone to
    /// play; null when this appearance draws no 3D weapon.
    /// </summary>
    internal DaggerfallHeldViewmodel? ViewHeld(DaggerfallHeldDrawing drawing)
    {
        if (heldArt is not { } chosen) return null;
        if (heldShield is { } shield) drawing = chosen.Art.WithShield(drawing, shield.Template, shield.Metal);
        if (held?.Drawing != drawing)
        {
            // Built before the drawn one retires, so a drawing that cannot be drawn leaves the drawn one in place.
            DaggerfallHeldViewmodel viewed = NewHeld(drawing);
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
        if (!heldViewed || held is null) return;
        heldViewed = false;
        RefreshHeld();
    }

    /// <summary>The classic weapon a visual names, whose actions time the art's strikes; null when the session has none.</summary>
    internal NormalizedClassicWeapon? ClassicWeapon(string resourceId) => classicPresentation.Weapons.GetValueOrDefault(resourceId);

    private void CreateHeld(NormalizedClassicWeapon weapon)
    {
        RetireCastArms();
        if (heldViewed || Drawing(weapon) is not { } drawing) return;
        held = NewHeld(drawing);
        heldShowing = true;
    }

    private DaggerfallHeldViewmodel NewHeld(DaggerfallHeldDrawing drawing)
    {
        var chosen = heldArt!.Value;
        return new DaggerfallHeldViewmodel(chosen.Engine, chosen.Art, drawing, chosen.Metals.GetValueOrDefault(drawing.Metal),
            drawing.ShieldMetal is { } metal ? chosen.Metals.GetValueOrDefault(metal) : null, chosen.Style,
            DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld());
    }

    // What draws the wielded weapon: the item the hands hold chooses, its classic visual otherwise.
    private DaggerfallHeldDrawing? Drawing(NormalizedClassicWeapon weapon)
    {
        if (heldArt is not { } chosen) return null;
        var (template, metal) = heldItem is { } item ? chosen.ItemOf(item) : (null, DaggerfallHeldDrawing.Steel);
        return chosen.Art.Drawing(weapon.ResourceId, template, metal) is { } drawing && heldShield is { } shield
            ? chosen.Art.WithShield(drawing, shield.Template, shield.Metal) : chosen.Art.Drawing(weapon.ResourceId, template, metal);
    }

    // The hands changed what they wield, or the workbench gave them back: the 3D weapon is drawn anew if the wielded
    // item draws differently, as a sword swapped for another of the same classic visual does. With no weapon drawn
    // nothing is: a sheathed weapon leaves the workbench's view nothing to hand back to.
    private void RefreshHeld()
    {
        if (heldViewed) return;
        if (viewmodel is not { } weapon)
        {
            RetireHeld();
            return;
        }
        if (held is { } shown && shown.Drawing == Drawing(weapon.Weapon))
        {
            shown.Rest(IdleFrame(weapon.Weapon));
            heldShowing = true;
            return;
        }
        RetireHeld();
        CreateHeld(weapon.Weapon);
        held?.Rest(IdleFrame(weapon.Weapon));
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
    // The arms return to the idle loop with the sprite.
    private void RestHeldAction(NormalizedClassicWeaponAction idle)
    {
        if (held is null || heldViewed) return;
        held.Rest(1F / idle.FramesPerSecond);
        heldShowing = true;
    }

    // Plays an attack's clip to the rules' timing, or shows the sprite for an attack the family has no clip for. The retro
    // style samples a frame of the windup's own length, so the hit shows the strike mark.
    private void PlayHeldAction(string attack, DaggerfallAttackTiming timing, int hitFrame)
    {
        if (held is null || heldViewed) return;
        heldShowing = held.Moves(attack);
        if (heldShowing) held.Attack(attack, timing, hitFrame);
        else held.Rest((float)(timing.Windup / hitFrame));
    }

    // Keeps the charged power attack where the family holds it.
    private void HoldHeldAction()
    {
        if (held is null || heldViewed || !held.Moves(DaggerfallAttackIds.PowerHold)) return;
        heldShowing = true;
        held.Hold();
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
        AppendCastGlowFact(facts);
        if (castHand is { } hand) facts.Add(new AppearanceFact(hand.EntityId, false, 0, hand.Transform, hand.Appearance, true, RenderLayer.Viewmodel));
    }

    // Once the snapshot is published: the arms are posed by their clip.
    private void ReachHeld() => held?.Reach();
}
