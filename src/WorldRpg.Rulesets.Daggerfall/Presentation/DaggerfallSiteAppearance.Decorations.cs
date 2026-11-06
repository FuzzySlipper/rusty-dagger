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
        DecorationSprite Winter, ulong Ordinal)
    {
        /// <summary>The refreshed mesh drawn in place of its sprite while refreshed art is drawn, when the refreshed set has one.</summary>
        internal Appearance? Mesh { get; set; }
        internal float MeshScale { get; set; } = 1f;
        /// <summary>The refreshed sprites drawn in place of its classic ones while refreshed art is drawn, where the refreshed set has them.</summary>
        internal DecorationSprite? RefreshedSummer { get; set; }
        internal DecorationSprite? RefreshedWinter { get; set; }
    }

    private sealed class DecorationSprite(Appearance visual)
    {
        internal Appearance Visual { get; } = visual;
        internal SpritePlayback? Playback { get; set; }
    }

    private void AdmitDecorations(DaggerfallSiteProfile inputs)
    {
        foreach (var flat in inputs.Decorations)
        {
            var summer = CreateDecorationSprite(flat.Summer, flat.BaseAligned, Material(flat));
            var winter = flat.Summer == flat.Winter ? summer : CreateDecorationSprite(flat.Winter, flat.BaseAligned, Material(flat));
            DecorationVisual visual = new(flat, summer, winter, NextLocationVisualOrdinal());
            AttachDecorationMesh(visual);
            AttachDecorationFlats(visual);
            decorations.Add(visual);
        }
    }

    /// <summary>A decoration takes the light around it, unless it is a lamp: a light source shows at its own brightness.</summary>
    private static SpriteMaterialDescriptor Material(DaggerfallSiteDecoration flat) =>
        flat.Source.Archive == LightFlatArchive ? UnlitSprite : LitSprite;

    private DecorationSprite CreateDecorationSprite(NormalizedBillboardSprite sprite, bool baseAligned, SpriteMaterialDescriptor material)
    {
        if (!decorationAtlases.TryGetValue(sprite.TexturePath, out var atlas))
        {
            var resource = appearance.OpenTexture(content, sprite.TexturePath, sprite.TextureSha256);
            decorationResources.Add(resource.Handle);
            var frames = SpriteAtlasAdapter.ToAtlasFrames(sprite.AtlasWidth, sprite.AtlasHeight,
                sprite.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y,
                    frame.Width, frame.Height, frame.DisplaySize)).ToArray());
            atlas = appearance.CreateSpriteAtlas(new(resource.Handle, frames));
            decorationAtlases.Add(sprite.TexturePath, atlas);
        }
        var result = new DecorationSprite(appearance.CreateSpriteFromAtlas(new(atlas, sprite.InitialFrameId,
            baseAligned ? new Vector2(.5f, 0f) : sprite.Pivot, sprite.Size, BillboardMode.Cylindrical,
            SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1f, 1f, 1f, 1f), material)));
        decorationSprites.Add(result);
        StartDecorationPlayback(result, atlas, sprite);
        return result;
    }

    /// <summary>Gives a placed decoration the refreshed sprites the set has for its summer and winter records.</summary>
    private void AttachDecorationFlats(DecorationVisual flat)
    {
        if (surfaceArt is null) return;
        DaggerfallSiteDecoration definition = flat.Definition;
        if (flat.RefreshedSummer is null && surfaceArt.Flats.TryGetValue(definition.Source, out DaggerfallArtFlat? summer))
            flat.RefreshedSummer = CreateRefreshedDecorationSprite(definition.Summer, summer, definition.BaseAligned, Material(definition));
        if (flat.RefreshedWinter is null && surfaceArt.Flats.TryGetValue(definition.WinterSource, out DaggerfallArtFlat? winter))
            flat.RefreshedWinter = definition.WinterSource == definition.Source ? flat.RefreshedSummer
                : CreateRefreshedDecorationSprite(definition.Winter, winter, definition.BaseAligned, Material(definition));
    }

    /// <exception cref="InvalidOperationException">The refreshed flat does not have its classic sprite's frame count,
    /// so the classic frame identities would name the wrong frames.</exception>
    private DecorationSprite CreateRefreshedDecorationSprite(NormalizedBillboardSprite sprite, DaggerfallArtFlat art, bool baseAligned, SpriteMaterialDescriptor material)
    {
        if (art.Frames.Count != sprite.Frames.Count)
            throw new InvalidOperationException($"Refreshed flat '{art.File.Path}' has {art.Frames.Count} frames; its classic sprite has {sprite.Frames.Count}.");
        if (!decorationAtlases.TryGetValue(art.File.Path, out var atlas))
        {
            var resource = appearance.OpenTexture(content, art.File.Path, art.File.Sha256, TextureFilter.Nearest);
            decorationResources.Add(resource.Handle);
            atlas = appearance.CreateSpriteAtlas(new(resource.Handle, SpriteAtlasAdapter.ToAtlasFrames(art.Width, art.Height,
                art.Frames.Select((frame, index) => new NormalizedSpriteFrame(sprite.Frames[index].Id, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height)).ToArray())));
            decorationAtlases.Add(art.File.Path, atlas);
        }
        (Vector2 size, Vector2 pivot) = art.Placed(sprite.Size, baseAligned ? new Vector2(.5f, 0f) : sprite.Pivot);
        var result = new DecorationSprite(appearance.CreateSpriteFromAtlas(new(atlas, sprite.InitialFrameId, pivot, size,
            BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1f, 1f, 1f, 1f), material)));
        decorationSprites.Add(result);
        StartDecorationPlayback(result, atlas, sprite);
        return result;
    }

    private void StartDecorationPlayback(DecorationSprite result, SpriteAtlas atlas, NormalizedBillboardSprite sprite)
    {
        if (sprite.Frames.Count <= 1 || sprite.FramesPerSecond <= 0f) return;
        result.Playback = appearance.CreateSpritePlayback(new(result.Visual, atlas,
            SpriteAtlasAdapter.ToPlaybackFrames(sprite.Frames.Select(frame => frame.Id).ToArray(), sprite.FramesPerSecond),
            Array.Empty<SpritePlaybackMarker>(), SpritePlaybackLoopMode.Loop, 1d));
        appearance.ControlSpritePlayback(new(result.Playback, SpritePlaybackControl.Start));
    }

    /// <summary>The sprite a decoration draws now: its refreshed one while refreshed art is drawn and the set has it, else its classic one.</summary>
    private DecorationSprite DrawnDecoration(DecorationVisual flat) => winterMaterials
        ? (drawRefreshedEffects() ? flat.RefreshedWinter : null) ?? flat.Winter
        : (drawRefreshedEffects() ? flat.RefreshedSummer : null) ?? flat.Summer;

    private void AppendDecorationFacts(List<AppearanceFact> facts, int slot)
    {
        bool refreshed = drawRefreshedEffects();
        foreach (var flat in decorations)
        {
            if (refreshed && flat.Mesh is { } mesh)
            {
                // The mesh rests on the floor its sprite stands on: the sprite's position less its pivot's share of its height.
                NormalizedBillboardSprite sprite = flat.Definition.Summer;
                float lift = flat.Definition.BaseAligned ? 0f : sprite.Pivot.Y * sprite.Size.Y;
                facts.Add(new(DaggerfallPresentationObjectIds.LocationVisual(slot, flat.Ordinal), false, 0,
                    Compose(worldAppearance.Transform, new Transform(flat.Definition.Position.ToVector() - Vector3.UnitY * lift, Quaternion.Identity, new Vector3(flat.MeshScale))),
                    mesh, worldAppearance.Visible, worldAppearance.Layer));
                continue;
            }
            facts.Add(new(DaggerfallPresentationObjectIds.LocationVisual(slot, flat.Ordinal), false, 0,
                Compose(worldAppearance.Transform, new Transform(flat.Definition.Position.ToVector(), Quaternion.Identity, Vector3.One)),
                DrawnDecoration(flat).Visual, worldAppearance.Visible, worldAppearance.Layer));
        }
    }

    private void AdvanceDecorations()
    {
        foreach (var flat in decorations)
            if (DrawnDecoration(flat).Playback is { } playback)
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
        values.AddRange(decorations.Select(flat => flat.Mesh).OfType<Appearance>());
        values.AddRange(decorationAtlases.Values);
        values.AddRange(decorationResources);
        decorations.Clear();
        decorationSprites.Clear();
        decorationAtlases.Clear();
        decorationResources.Clear();
        return values;
    }
}
