using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The left hand's cast. A held family with a cast clip plays it on the arms to the rules' timing, with the element's
/// missile burning on the palm until the spell leaves. Otherwise the weapon rests and the element's classic casting hand
/// draws over it for as long as the cast plays: its frames carry their own place on the classic screen, so it fills the
/// view as the weapon sprite does. A data pack from before the hands were published draws no hand, and the spell still
/// leaves. With the weapon sheathed no arms are drawn, so the bare hands' come up for the cast alone.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    /// <summary>The frame the spell leaves on, as the classic's casting hands release it.</summary>
    private const int CastReleaseFrame = 5;

    /// <summary>How wide the element burns on the palm (metres).</summary>
    private const float CastGlowWidth = .22F;

    private ViewmodelVisual? castHand;
    // The element burning on the held arms' palm, from the cast's start until the spell leaves.
    private EffectVisual? castGlow;
    // Whether the arms drawn are only there for a cast made with the weapon sheathed.
    private bool castArms;

    private void PlayCast(int element, DaggerfallAttackTiming timing)
    {
        RetireCastHand();
        // A cast can cut into a swing's recovery, which leaves that swing's motion playing.
        StartWeaponAction("idle");
        DrawCastArms();
        if (held is not null && !heldViewed && held.Moves(DaggerfallAttackIds.Cast))
        {
            held.Attack(DaggerfallAttackIds.Cast, timing, CastReleaseFrame);
            StartCastGlow(element);
        }
        else PlayCastHand(element, timing);
    }

    private void DrawCastArms()
    {
        if (held is not null || viewmodel is not null || heldViewed || heldArt is not { } chosen || classicPresentation.UnarmedVisual is not { } bare
            || chosen.Art.Drawing(bare, null, DaggerfallHeldDrawing.Steel) is not { } drawing) return;
        held = NewHeld(Dressed(drawing));
        heldShowing = true;
        castArms = true;
    }

    private void RetireCastArms()
    {
        if (!castArms) return;
        castArms = false;
        RetireHeld();
    }

    private void StartCastGlow(int element)
    {
        if (DaggerfallSpellElements.Name(element) is not { } name || EffectSprite($"{name}Missile", CastGlowWidth) is not { } sprite) return;
        castGlow = new EffectVisual(NextVisualEntityId(), WorldPoint.From(Vector3.Zero), sprite.Atlas, sprite.Visual, sprite.Playback) { Refreshed = sprite.Refreshed };
    }

    // The element on the palm, on the viewmodel layer with the arms; the spell having left, it goes.
    private void AppendCastGlowFact(List<AppearanceFact> facts)
    {
        if (castGlow is not { } glow) return;
        if (held?.CastPalm() is { } palm)
            facts.Add(new AppearanceFact(glow.EntityId, false, 0, new Transform(palm, Quaternion.Identity, Vector3.One), glow.Appearance, true, RenderLayer.Viewmodel));
        else RetireCastGlow();
    }

    private void PlayCastHand(int element, DaggerfallAttackTiming timing)
    {
        if (classicPresentation.Viewmodel is not { } style || DaggerfallSpellElements.Name(element) is not { } name
            || !classicPresentation.Weapons.TryGetValue($"hand.{name}", out NormalizedClassicWeapon? hand)
            || !hand.Actions.TryGetValue(NormalizedClassicWeapon.PlayedStrike, out NormalizedClassicWeaponAction? cast)) return;
        SpriteAtlas? atlas = null;
        Appearance? visual = null;
        SpritePlayback? playback = null;
        try
        {
            SpriteAtlasFrame[] cells = SpriteAtlasAdapter.ToAtlasFrames(hand.AtlasWidth, hand.AtlasHeight,
                hand.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray());
            atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(classicTextures[hand.TexturePath].Handle, cells));
            visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, hand.Frames[0].Id, new Vector2(.5F, 0F),
                new Vector2(hand.Frames[0].Width, hand.Frames[0].Height), BillboardMode.None, SpriteSizeMode.Pixel, style.RenderOrder + 1,
                SpriteDepthPolicy.DepthTestOff, new Color(1F, 1F, 1F, 1F)));
            appearance.SetSpriteViewport(new SpriteViewportUpdateRequest(visual, true, Vector2.Zero, Vector2.One, Vector2.Zero, SpriteViewportFit.Stretch));
            SpritePlaybackFrame[] frames = SpriteAtlasAdapter.ToPlaybackFrames(
                Enumerable.Range(cast.FrameStart, cast.FrameCount).Select(index => hand.Frames.Single(frame => frame.Id == index).Id).ToArray(), checked((float)cast.FramesPerSecond));
            int release = Math.Min(CastReleaseFrame, frames.Length - 1);
            for (int frame = 0; frame < frames.Length; frame++)
                frames[frame] = frames[frame] with
                {
                    DurationSeconds = frame < release ? timing.Windup / Math.Max(1, release) : (timing.Commit + timing.Recovery) / (frames.Length - release),
                };
            playback = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(visual, atlas, frames, ReadOnlyMemory<SpritePlaybackMarker>.Empty, SpritePlaybackLoopMode.OneShot, 1d));
            appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(playback, SpritePlaybackControl.Start));
            castHand = new ViewmodelVisual(hand, NextVisualEntityId(), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), atlas, visual) { Playback = playback };
        }
        catch
        {
            playback?.Dispose();
            visual?.Dispose();
            atlas?.Dispose();
            throw;
        }
    }

    private void AdvanceCastHand(AppearanceOuterUpdate identity)
    {
        if (castGlow is { } glow && glow.LastOuterUpdate != identity)
        {
            appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(glow.Playback));
            glow.LastOuterUpdate = identity;
        }
        if (castHand is not { Playback: { } playback } hand || hand.LastOuterUpdate == identity) return;
        appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(playback));
        hand.LastOuterUpdate = identity;
    }

    private void RetireCastHand()
    {
        RetireCastGlow();
        RetireCastArms();
        if (castHand is not { } hand) return;
        castHand = null;
        Retire(hand);
    }

    private void RetireCastGlow()
    {
        if (castGlow is not { } glow) return;
        castGlow = null;
        Retire(glow);
    }
}
