using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The element's classic casting hand, drawn over whichever weapon the right hand holds in either style, for as long as
/// the rules' cast plays. Its frames carry their own place on the classic screen, so the hand fills the view as the
/// weapon sprite does. A data pack from before the hands were published draws no hand, and the spell still leaves.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    /// <summary>The frame the spell leaves on, as the classic's casting hands release it.</summary>
    private const int CastReleaseFrame = 5;

    private ViewmodelVisual? castHand;

    private void PlayCastHand(int element, DaggerfallAttackTiming timing)
    {
        RetireCastHand();
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
        if (castHand is not { Playback: { } playback } hand || hand.LastOuterUpdate == identity) return;
        appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(playback));
        hand.LastOuterUpdate = identity;
    }

    private void RetireCastHand()
    {
        if (castHand is not { } hand) return;
        castHand = null;
        Retire(hand);
    }
}
