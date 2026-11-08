using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed partial class DaggerfallSiteAppearance
{
    private readonly List<DecorationVisual> decorations = [];
    private readonly List<DecorationSprite> decorationSprites = [];
    private readonly Dictionary<string, SpriteAtlas> decorationAtlases = new(StringComparer.Ordinal);
    private readonly List<RenderResource> decorationResources = [];
    private readonly Dictionary<string, RenderResource> decorationGlowMaps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RenderResource> decorationTextures = new(StringComparer.Ordinal);
    private readonly List<Appearance> glowingDecorations = [];
    private float lampLevel;

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
            var summer = CreateDecorationSprite(flat.Summer, flat.BaseAligned, Material(flat), GivesLight(flat.Source));
            var winter = flat.Summer == flat.Winter ? summer
                : CreateDecorationSprite(flat.Winter, flat.BaseAligned, Material(flat), GivesLight(flat.WinterSource));
            DecorationVisual visual = new(flat, summer, winter, NextLocationVisualOrdinal());
            AttachDecorationMesh(visual);
            AttachDecorationFlats(visual);
            decorations.Add(visual);
        }
    }

    /// <summary>
    /// The classic flats that give light, as the donor marks them emissive: the fireplace, lamps, lanterns, torches, candles,
    /// braziers, the glowing statue, the brewing potion, the Mantellan Crux's fire and the magic decorations.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, int[]> LightFlats = new Dictionary<int, int[]>
    {
        [87] = [0], [101] = [2, 3, 5, 6, 7, 8, 9, 11, 12], [190] = [3, 4, 5], [200] = [7, 8, 9, 10], [202] = [2], [208] = [2],
        [210] = [0, 1, 2, 3, 4, 5, 6, 8, 9, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29],
        [253] = [10, 17, 18, 19, 22, 41, 48, 49, 50, 51, 52, 75, 77], [356] = [0, 2, 3], [380] = [3], [434] = [3],
    };

    private static bool GivesLight((int Archive, int Record) source) =>
        LightFlats.TryGetValue(source.Archive, out int[]? records) && records.Contains(source.Record);

    /// <summary>A decoration takes the light around it. A tree or plant from the nature archives leans in the wind as wild nature does.</summary>
    private SpriteMaterialDescriptor Material(DaggerfallSiteDecoration flat) =>
        DaggerfallExteriorEnvironment.IsNatureArchive(flat.Source.Archive) ? flats.Flora(LitSprite) : LitSprite;

    /// <summary>A flat that gives light, under <paramref name="material"/>: what <paramref name="map"/> holds shines at the lamps' level.</summary>
    private SpriteMaterialDescriptor Lamp(SpriteMaterialDescriptor material, RenderResource map) => Glowing(material, map, lampLevel);

    /// <summary>
    /// A classic decoration's sprite. A light source takes the light around it like any flat and shines over it through
    /// emission, with its own texture as the map, as the donor reuses a lamp's colours for its glow.
    /// </summary>
    private DecorationSprite CreateDecorationSprite(NormalizedBillboardSprite sprite, bool baseAligned, SpriteMaterialDescriptor material, bool givesLight)
    {
        if (!decorationAtlases.TryGetValue(sprite.TexturePath, out var atlas))
        {
            var resource = appearance.OpenTexture(content, sprite.TexturePath, sprite.TextureSha256);
            decorationResources.Add(resource.Handle);
            decorationTextures.Add(sprite.TexturePath, resource.Handle);
            var frames = SpriteAtlasAdapter.ToAtlasFrames(sprite.AtlasWidth, sprite.AtlasHeight,
                sprite.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y,
                    frame.Width, frame.Height, frame.DisplaySize)).ToArray());
            atlas = appearance.CreateSpriteAtlas(new(resource.Handle, frames));
            decorationAtlases.Add(sprite.TexturePath, atlas);
        }
        var result = new DecorationSprite(appearance.CreateSpriteFromAtlas(new(atlas, sprite.InitialFrameId,
            baseAligned ? new Vector2(.5f, 0f) : sprite.Pivot, sprite.Size, flats.Decorations,
            SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1f, 1f, 1f, 1f),
            givesLight ? Lamp(material, decorationTextures[sprite.TexturePath]) : material)));
        if (givesLight) glowingDecorations.Add(result.Visual);
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
            flat.RefreshedSummer = CreateRefreshedDecorationSprite(definition.Summer, summer, definition.BaseAligned, Material(definition), GivesLight(definition.Source));
        if (flat.RefreshedWinter is null && surfaceArt.Flats.TryGetValue(definition.WinterSource, out DaggerfallArtFlat? winter))
            flat.RefreshedWinter = definition.WinterSource == definition.Source ? flat.RefreshedSummer
                : CreateRefreshedDecorationSprite(definition.Winter, winter, definition.BaseAligned, Material(definition), GivesLight(definition.WinterSource));
    }

    /// <summary>
    /// A refreshed decoration's sprite. A light source shines through the emission map cut from its art, or, where the set
    /// has none, through its own texture as a classic one does.
    /// </summary>
    /// <exception cref="InvalidOperationException">The refreshed flat does not have its classic sprite's frame count,
    /// so the classic frame identities would name the wrong frames.</exception>
    private DecorationSprite CreateRefreshedDecorationSprite(NormalizedBillboardSprite sprite, DaggerfallArtFlat art, bool baseAligned, SpriteMaterialDescriptor material, bool givesLight)
    {
        if (art.Frames.Count != sprite.Frames.Count)
            throw new InvalidOperationException($"Refreshed flat '{art.File.Path}' has {art.Frames.Count} frames; its classic sprite has {sprite.Frames.Count}.");
        if (!decorationAtlases.TryGetValue(art.File.Path, out var atlas))
        {
            var resource = appearance.OpenTexture(content, art.File.Path, art.File.Sha256, TextureFilter.Nearest);
            decorationResources.Add(resource.Handle);
            decorationTextures.Add(art.File.Path, resource.Handle);
            atlas = appearance.CreateSpriteAtlas(new(resource.Handle, SpriteAtlasAdapter.ToAtlasFrames(art.Width, art.Height,
                art.Frames.Select((frame, index) => new NormalizedSpriteFrame(sprite.Frames[index].Id, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height)).ToArray())));
            decorationAtlases.Add(art.File.Path, atlas);
        }
        (Vector2 size, Vector2 pivot) = art.Placed(sprite.Size, baseAligned ? new Vector2(.5f, 0f) : sprite.Pivot);
        var result = new DecorationSprite(appearance.CreateSpriteFromAtlas(new(atlas, sprite.InitialFrameId, pivot, size,
            flats.Decorations, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1f, 1f, 1f, 1f),
            art.Emission is { } emission ? Lamp(material, GlowMap(emission))
                : givesLight ? Lamp(material, decorationTextures[art.File.Path]) : material)));
        if (art.Emission is not null || givesLight) glowingDecorations.Add(result.Visual);
        decorationSprites.Add(result);
        StartDecorationPlayback(result, atlas, sprite);
        return result;
    }

    /// <summary>A refreshed lamp's emission map, opened once however many lamps share it.</summary>
    private RenderResource GlowMap(DaggerfallArtFile emission)
    {
        if (!decorationGlowMaps.TryGetValue(emission.Path, out RenderResource? map))
        {
            map = appearance.OpenTexture(content, emission.Path, emission.Sha256, TextureFilter.Nearest).Handle;
            decorationResources.Add(map);
            decorationGlowMaps.Add(emission.Path, map);
        }
        return map;
    }

    /// <summary>Lights the lamps at a level, which is zero while they are out.</summary>
    internal void UpdateLampGlow(float level)
    {
        if (level == lampLevel) return;
        lampLevel = level;
        foreach (Appearance lamp in glowingDecorations)
            appearance.SetSpriteEmission(new(lamp, level));
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
        decorationGlowMaps.Clear();
        decorationTextures.Clear();
        glowingDecorations.Clear();
        return values;
    }
}
