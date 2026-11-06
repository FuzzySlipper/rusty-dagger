using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon beside the classic sprite viewmodel. When the viewmodel tuning asks for a 3D style and
/// a family of the installed first-person art draws the drawn weapon, the family's clip-led arms loop its idle at rest
/// and play its clip for every attack it has one for, and the sprite stays hidden. Both follow the rules' attack clock.
/// An attack the family has no clip for shows the sprite for that swing. The wielded item chooses the family and look and tints the look by its metal, so a swap
/// between weapons of one classic visual redraws the 3D weapon while the sprite stays. The arms are the set the player's
/// gender and arm armour draw, changing once they are at rest. While the developer workbench
/// views a drawing (<see cref="ViewHeld"/>), it is drawn whatever the hands wield and only the workbench plays it.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private (IEngineContext Engine, DaggerfallHeldArt Art, DaggerfallViewmodelStyle Style, IReadOnlyDictionary<string, DaggerfallDyeTint> Metals,
        Func<UniqueInventoryItem, (int? Template, string Metal, bool Enchanted)> ItemOf, Func<EquipmentRead, (int Template, string Metal)?> ShieldOf,
        Func<EquipmentRead, string> ArmsOf)? heldArt;
    private DaggerfallHeldViewmodel? held;
    // The item the hands wield, which the 3D weapon is drawn for; null for bare hands and a beast's claws.
    private UniqueInventoryItem? heldItem;
    // The shield on the off arm, which the 3D weapon draws too; null for none.
    private (int Template, string Metal)? heldShield;
    // The arm set the player wears now, which the arms drawn change to at rest.
    private string? heldArms;
    // Whether the 3D weapon draws the current action (false while a strike its family has no clip for shows the sprite).
    private bool heldShowing;
    // Whether the developer workbench views the art, which then owns the 3D weapon.
    private bool heldViewed;
    // What the player rides, as the session reports it, and what is drawn for it.
    private Func<(DaggerfallTransportMode Mode, float Speed, float Pitch, float Turn)> playerRide = () => (DaggerfallTransportMode.Foot, 0, 0, 0);
    private DaggerfallHeldMount? mount;
    private DaggerfallTransportMode? mounted;
    // Whether the arms drawn are only there to hold the reins.
    private bool reinArms;
    private const string ReinsFamily = "reins";

    // Where the view's light comes from, and the tweens that carry it to the sprites; null in a session with no Engine view.
    private (ITweenService Tween, DaggerfallViewLight Light)? viewLight;

    /// <summary>Tints the first-person view by <paramref name="light"/>; <paramref name="tween"/> carries it to the sprites.</summary>
    internal void UseViewLight(ITweenService tween, DaggerfallViewLight light) => viewLight = (tween, light);

    /// <summary>Reads what the player rides, how fast in metres a second, the view's pitch and how far the mount's heading lies from the view in radians, for the mount drawn in the held styles.</summary>
    internal void UsePlayerRide(Func<(DaggerfallTransportMode Mode, float Speed, float Pitch, float Turn)> ride) => playerRide = ride;

    /// <summary>
    /// Draws the weapons <paramref name="art"/> covers in 3D in <paramref name="style"/>, from the next weapon drawn,
    /// each model's metal tinted by <paramref name="metals"/>; <paramref name="itemOf"/> reads a wielded item's template
    /// and metal, <paramref name="shieldOf"/> those of the shield worn, and <paramref name="armsOf"/> the arm set worn.
    /// </summary>
    internal void UseHeldArt(IEngineContext engine, DaggerfallHeldArt art, DaggerfallViewmodelStyle style, IReadOnlyDictionary<string, DaggerfallDyeTint> metals,
        Func<UniqueInventoryItem, (int? Template, string Metal, bool Enchanted)> itemOf, Func<EquipmentRead, (int Template, string Metal)?> shieldOf, Func<EquipmentRead, string> armsOf)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(metals);
        ArgumentNullException.ThrowIfNull(itemOf);
        ArgumentNullException.ThrowIfNull(shieldOf);
        ArgumentNullException.ThrowIfNull(armsOf);
        if (style == DaggerfallViewmodelStyle.Classic) throw new ArgumentOutOfRangeException(nameof(style), style, "The classic style draws no 3D weapon.");
        heldArt = (engine, art, style, metals, itemOf, shieldOf, armsOf);
    }

    /// <summary>
    /// The developer workbench's view: a drawing's 3D weapon, drawn whatever the hands wield, for the workbench alone to
    /// play; null when this appearance draws no 3D weapon.
    /// </summary>
    internal DaggerfallHeldViewmodel? ViewHeld(DaggerfallHeldDrawing drawing)
    {
        if (heldArt is not { } chosen) return null;
        if (heldShield is { } shield) drawing = chosen.Art.WithShield(drawing, shield.Template, shield.Metal);
        drawing = Dressed(drawing);
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
            DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld(),
            DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld());
    }

    // What draws the wielded weapon: the item the hands hold chooses, its classic visual otherwise.
    private DaggerfallHeldDrawing? Drawing(NormalizedClassicWeapon weapon)
    {
        if (heldArt is not { } chosen) return null;
        var (template, metal, enchanted) = heldItem is { } item ? chosen.ItemOf(item) : (null, DaggerfallHeldDrawing.Steel, false);
        if (chosen.Art.Drawing(weapon.ResourceId, template, metal) is not { } drawing) return null;
        return Dressed((heldShield is { } shield ? chosen.Art.WithShield(drawing, shield.Template, shield.Metal) : drawing) with { Enchanted = enchanted });
    }

    // A drawing in the arms the player wears, unless it names its own.
    private DaggerfallHeldDrawing Dressed(DaggerfallHeldDrawing drawing) => drawing with { Arms = drawing.Arms ?? heldArms };

    // The player's arms changed: the set they now wear takes over once the arms drawn are at rest, on the same moment of
    // the idle loop, so neither a swing nor the loop jumps.
    private void DressHeld()
    {
        if (heldViewed || castArms || reinArms || held is not { Moving: false } shown || shown.Drawing.Arms == heldArms) return;
        RefreshHeld();
        held?.TakeOver(shown);
    }

    // The hands changed what they wield, or the workbench gave them back: the 3D weapon is drawn anew if the wielded
    // item draws differently, as a sword swapped for another of the same classic visual does. With no weapon drawn
    // nothing is: a sheathed weapon leaves the workbench's view nothing to hand back to.
    private void RefreshHeld()
    {
        if (heldViewed) return;
        if (viewmodel is not { } weapon)
        {
            if (!reinArms) RetireHeld();
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

    // The world seconds this update admitted, on which the arms' clips and the mount's gaits play.
    private void StepHeld(ProductUpdateFacts update)
    {
        float seconds = (float)(update.FixedDeltaSeconds * update.AdmittedStepCount);
        held?.Step(seconds);
        StepMount(seconds);
    }

    // The mount follows what the player rides: drawn anew for a change, retired on foot.
    private void StepMount(float seconds)
    {
        var (mode, speed, _, _) = playerRide();
        DaggerfallTransportMode? wanted = heldArt is { Art.Document.Mount: not null } && mode is DaggerfallTransportMode.Horse or DaggerfallTransportMode.Cart ? mode : null;
        if (wanted != mounted)
        {
            if (mount is not null) Retire(mount);
            mount = null;
            mounted = wanted;
            if (wanted is not null && heldArt is { } chosen)
                mount = new(chosen.Engine, chosen.Art, chosen.Art.Document.Mount!, wanted == DaggerfallTransportMode.Cart, chosen.Style == DaggerfallViewmodelStyle.HeldRetro,
                    DaggerfallPresentationObjectIds.TakeHeld(), DaggerfallPresentationObjectIds.TakeHeld());
        }
        mount?.Step(seconds, speed);
        DrawReins();
    }

    // A rider's hands hold the reins, the arms' whole job while the weapon is put away; they go with the mount.
    private void DrawReins()
    {
        if (reinArms && mount is null)
        {
            reinArms = false;
            RetireHeld();
        }
        else if (mount is not null && !reinArms && held is null && !heldViewed && viewmodel is null && heldArt is { } chosen)
        {
            held = NewHeld(Dressed(new DaggerfallHeldDrawing(ReinsFamily, null, DaggerfallHeldDrawing.Steel)));
            held.Rest(0);
            heldShowing = true;
            reinArms = true;
        }
    }

    /// <summary>How far above where it stands on foot the eye sits for what the player rides, <paramref name="standing"/> high on foot.</summary>
    internal float MountEyeLift(float standing) => mount?.EyeLift(standing) ?? 0;

    // The sprite viewmodel, hidden while the 3D weapon draws, and the 3D weapon (alone while the workbench views it
    // and the hands wield no drawn weapon).
    private void AppendViewmodelFacts(List<AppearanceFact> facts, ViewmodelVisual? weapon)
    {
        bool sprite = held is null || !heldShowing;
        if (weapon is not null)
            facts.Add(new AppearanceFact(weapon.EntityId, false, 0, weapon.Transform, weapon.Appearance, sprite, RenderLayer.Viewmodel));
        var ride = playerRide();
        if (reinArms && mount is not null) held?.Level(mount.Level(ride.Pitch, ride.Turn));
        held?.AppendFacts(facts, !sprite);
        mount?.AppendFacts(facts, ride.Pitch, ride.Turn, true);
        AppendCastGlowFact(facts);
        if (castHand is { } hand) facts.Add(new AppearanceFact(hand.EntityId, false, 0, hand.Transform, hand.Appearance, true, RenderLayer.Viewmodel));
    }

    // Once the snapshot is published: the arms are posed by their clip.
    private void ReachHeld()
    {
        held?.Reach();
        mount?.Posed();
        LightView();
    }

    // The meshes tint their material factors, once the level changes. The sprites run a playback, which holds their
    // appearance from being replaced with another tint, so theirs is an Engine tween held paused on the level.
    private void LightView()
    {
        if (viewLight is not { } view) return;
        held?.Light(view.Light.Level);
        mount?.Light(view.Light.Level);
        if (viewmodel is { } weapon) LightSprite(weapon, view);
        if (castHand is { } hand) LightSprite(hand, view);
    }

    private static void LightSprite(ViewmodelVisual sprite, (ITweenService Tween, DaggerfallViewLight Light) view)
    {
        if (sprite.Light == view.Light.Level) return;
        sprite.Light = view.Light.Level;
        Vector4 level = new(sprite.Light.R, sprite.Light.G, sprite.Light.B, 1);
        TweenSegment[] held = [TweenSegment.Tint(level, level, 1, TweenEasing.Linear)];
        view.Tween.Control(new(view.Tween.Start(new TweenStartRequest(sprite.EntityId, held)).Tween, TweenControl.Pause));
    }
}
