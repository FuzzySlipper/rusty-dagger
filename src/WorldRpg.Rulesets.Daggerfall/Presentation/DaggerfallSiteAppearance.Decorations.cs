using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed partial class DaggerfallSiteAppearance
{
    private readonly List<DecorationVisual> decorations = [];
    private readonly List<DecorationSprite> decorationSprites = [];
    private readonly Dictionary<string, SpriteAtlas> decorationAtlases = new(StringComparer.Ordinal);
    private readonly List<RenderResource> decorationResources = [];

    private sealed record DecorationVisual(DaggerfallSiteDecoration Definition, DecorationSprite Summer,
        DecorationSprite Winter, ulong Ordinal);

    private sealed class DecorationSprite(Appearance visual)
    {
        internal Appearance Visual { get; } = visual;
        internal SpritePlayback? Playback { get; set; }
    }

    private void AdmitDecorations(DaggerfallSiteProfile inputs)
    {
        foreach (var flat in inputs.Decorations)
        {
            var summer = CreateDecorationSprite(flat.Summer, flat.BaseAligned);
            var winter = flat.Summer == flat.Winter ? summer : CreateDecorationSprite(flat.Winter, flat.BaseAligned);
            decorations.Add(new(flat, summer, winter, NextLocationVisualOrdinal()));
        }
    }

    private DecorationSprite CreateDecorationSprite(NormalizedBillboardSprite sprite, bool baseAligned)
    {
        if (!decorationAtlases.TryGetValue(sprite.TexturePath, out var atlas))
        {
            var resource = appearance.OpenResource(new(sprite.TexturePath));
            decorationResources.Add(resource.Handle);
            var frames = SpriteAtlasAdapter.ToAtlasFrames(sprite.AtlasWidth, sprite.AtlasHeight,
                sprite.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y,
                    frame.Width, frame.Height, frame.DisplaySize)).ToArray());
            atlas = appearance.CreateSpriteAtlas(new(resource.Handle, frames));
            decorationAtlases.Add(sprite.TexturePath, atlas);
        }
        var result = new DecorationSprite(appearance.CreateSpriteFromAtlas(new(atlas, sprite.InitialFrameId,
            baseAligned ? new Vector2(.5f, 0f) : sprite.Pivot, sprite.Size, BillboardMode.Cylindrical,
            SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1f, 1f, 1f, 1f))));
        decorationSprites.Add(result);
        if (sprite.Frames.Count > 1 && sprite.FramesPerSecond > 0f)
        {
            result.Playback = appearance.CreateSpritePlayback(new(result.Visual, atlas,
                SpriteAtlasAdapter.ToPlaybackFrames(sprite.Frames.Select(frame => frame.Id).ToArray(), sprite.FramesPerSecond),
                Array.Empty<SpritePlaybackMarker>(), SpritePlaybackLoopMode.Loop, 1d));
            appearance.ControlSpritePlayback(new(result.Playback, SpritePlaybackControl.Start));
        }
        return result;
    }

    private void AppendDecorationFacts(List<AppearanceFact> facts, int slot)
    {
        foreach (var flat in decorations)
            facts.Add(new(DaggerfallPresentationObjectIds.LocationVisual(slot, flat.Ordinal), false, 0,
                Compose(worldAppearance.Transform, new Transform(flat.Definition.Position.ToVector(), Quaternion.Identity, Vector3.One)),
                (winterMaterials ? flat.Winter : flat.Summer).Visual, worldAppearance.Visible, worldAppearance.Layer));
    }

    private void AdvanceDecorations()
    {
        foreach (var flat in decorations)
            if ((winterMaterials ? flat.Winter : flat.Summer).Playback is { } playback)
                appearance.AdvanceSpritePlayback(new(playback));
    }

    private IEnumerable<IDisposable> TakeDecorationResources()
    {
        List<IDisposable> values = [];
        foreach (var sprite in decorationSprites)
        {
            if (sprite.Playback is { } playback) values.Add(playback);
            values.Add(sprite.Visual);
        }
        values.AddRange(decorationAtlases.Values);
        values.AddRange(decorationResources);
        decorations.Clear();
        decorationSprites.Clear();
        decorationAtlases.Clear();
        decorationResources.Clear();
        return values;
    }
}
