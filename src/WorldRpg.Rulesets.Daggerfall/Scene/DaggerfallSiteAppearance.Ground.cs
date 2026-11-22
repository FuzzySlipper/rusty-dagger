using System.Globalization;
using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Ground piles: dropped items and treasure. As in the classic, a pile stands as one of the treasure archive's piles,
/// which one fixed by the container's identity as the classic picks one at random when the pile is made. With
/// refreshed art, a pile of one item or one stack stands as that item instead, in its metal or dye, at its own height,
/// and a pile of several stands as its treasure pile's refreshed sprite where the set redraws it, on its own foot. A classic
/// pile is opened only when it is drawn.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    // A refreshed item image's atlas, and a tint mask's texture, each by its world PNG's path, opened once per location.
    // They are apart because items in different metals or dyes, and the untinted one, share one image.
    private readonly Dictionary<string, SpriteAtlas> groundItemAtlases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RenderResource> groundItemMasks = new(StringComparer.Ordinal);

    /// <summary>
    /// A ground pile's billboard, what it stands as (a treasure pile's index or an item's look), how far it is lifted to stand
    /// on the floor, and its width and height in metres.
    /// </summary>
    internal sealed record GroundVisual(BillboardVisual Billboard, object Look, float Lift, Vector2 Size);

    /// <summary>
    /// The width and height a ground pile's billboard is drawn at, standing on the pile's position, or null before it is
    /// first published.
    /// </summary>
    internal Vector2? GroundSize(long id) => groundVisuals.TryGetValue(id, out GroundVisual? visual) ? visual.Size : null;

    private void ReconcileGroundVisuals(IReadOnlyDictionary<long, DaggerfallGroundContainer> containers, Func<long, DaggerfallGroundItemLook?>? groundLook)
    {
        foreach (long id in groundVisuals.Keys.Where(id => !containers.ContainsKey(id)).ToArray())
        {
            groundVisuals.Remove(id, out GroundVisual? retired);
            Retire(retired!.Billboard);
        }
        if (containers.Count == 0) return;
        if (groundContainerSprites.Count == 0)
            throw new InvalidOperationException("Ground containers require the normalized treasure billboard visuals.");
        foreach (DaggerfallGroundContainer container in containers.Values.OrderBy(container => container.Id))
        {
            // A treasure pile is drawn anew when the art changes; an item's look already names the art it is drawn in.
            object look = groundLook?.Invoke(container.Id) is { } item ? item : (TreasurePile(container.Id), RefreshedPile(TreasurePile(container.Id)) is not null);
            if (groundVisuals.TryGetValue(container.Id, out GroundVisual? current))
            {
                if (current.Look.Equals(look)) continue;
                groundVisuals.Remove(container.Id);
                Retire(current.Billboard);
            }
            (Appearance visual, float lift, Vector2 size) = look is DaggerfallGroundItemLook itemLook ? CreateGroundItem(itemLook) : CreateTreasurePile(((int, bool))look);
            groundVisuals.Add(container.Id, new(new BillboardVisual(container.Id, visual), look, lift, size));
        }
    }

    /// <summary>The treasure pile a container stands as: a spread of its identity over the published piles, so it keeps its pile across saves.</summary>
    private int TreasurePile(long id) => (int)((ulong)id * 0x9E3779B97F4A7C15UL % (ulong)groundContainerSprites.Count);

    /// <summary>The refreshed sprite a treasure pile is drawn as while refreshed art is drawn, or null where it stays classic.</summary>
    private DaggerfallArtSprite? RefreshedPile(int index) => drawRefreshedEffects() && groundContainerSprites[index].Source is { } source
        ? surfaceArt?.Scenery.GetValueOrDefault(source) : null;

    /// <summary>
    /// A treasure pile's billboard: its refreshed sprite standing on its own foot, or the classic pile lifted by its pivot's
    /// share of its height so it stands on the floor whatever pivot the source publishes, as the donor lifts dropped
    /// containers by half their height.
    /// </summary>
    private (Appearance, float, Vector2) CreateTreasurePile((int Index, bool Refreshed) pile)
    {
        if (pile.Refreshed && RefreshedPile(pile.Index) is { } art)
            return (appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(RefreshedPileAtlas(art), 0, art.Foot, art.Size, sprites.Items,
                SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F), LitSprite)), 0F, art.Size);
        NormalizedBillboardSprite sprite = groundContainerSprites[pile.Index];
        Appearance visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(ClassicTreasureAtlas(pile.Index),
            sprite.InitialFrameId, sprite.Pivot, sprite.Size, sprites.Items, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default,
            new Color(1F, 1F, 1F, 1F), LitSprite));
        return (visual, sprite.Pivot.Y * sprite.Size.Y, sprite.Size);
    }

    /// <summary>A treasure pile's classic atlas, opened the first time classic art draws it.</summary>
    private SpriteAtlas ClassicTreasureAtlas(int index)
    {
        if (groundContainerAtlases.TryGetValue(index, out SpriteAtlas? opened)) return opened;
        NormalizedBillboardSprite sprite = groundContainerSprites[index];
        RenderResourceInfo texture = appearance.OpenTexture(content, sprite.TexturePath, sprite.TextureSha256);
        ownedResources.Add(texture.Handle);
        SpriteAtlas atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, SpriteAtlasAdapter.ToAtlasFrames(sprite.AtlasWidth, sprite.AtlasHeight,
            sprite.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray())));
        atlases.Add(atlas);
        groundContainerAtlases.Add(index, atlas);
        return atlas;
    }

    /// <summary>A treasure pile's refreshed sprite's atlas, opened once for the site.</summary>
    private SpriteAtlas RefreshedPileAtlas(DaggerfallArtSprite art)
    {
        if (groundItemAtlases.TryGetValue(art.File.Path, out SpriteAtlas? opened)) return opened;
        RenderResourceInfo texture = appearance.OpenTexture(content, art.File.Path, art.File.Sha256, TextureFilter.Nearest);
        ownedResources.Add(texture.Handle);
        SpriteAtlas atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, RefreshedSpriteFrames(art)));
        atlases.Add(atlas);
        groundItemAtlases.Add(art.File.Path, atlas);
        return atlas;
    }

    /// <summary>
    /// An item's billboard: its world image standing on the floor at the height its entry states, as wide as the
    /// image's proportions make it, and where it is in a metal or dye, that colour multiplied into the pixels its mask covers.
    /// </summary>
    private (Appearance, float, Vector2) CreateGroundItem(DaggerfallGroundItemLook look)
    {
        float height = (float)look.Height;
        Vector2 size = new(height * look.Image.Width / look.Image.Height, height);
        SpriteAtlas atlas = GroundItemAtlas(look.Image);
        RenderResource? mask = look.Mask is { } image ? GroundItemMask(image) : null;
        Color tint = look.Color is { } color ? Linear(color, look.Brightness) : new Color(1F, 1F, 1F, 1F);
        SpriteFromAtlasRequest request = new(atlas, 0, new Vector2(.5F, 0F), size, sprites.Items, SpriteSizeMode.World, 0,
            SpriteDepthPolicy.Default, tint, LitSprite);
        if (mask is not null) request = request with { Material = LitSprite with { TintMask = new RenderResourceReference(mask) } };
        return (appearance.CreateSpriteFromAtlas(request), 0F, size);
    }

    private SpriteAtlas GroundItemAtlas(DaggerfallArtWorldImage image)
    {
        if (groundItemAtlases.TryGetValue(image.File.Path, out SpriteAtlas? opened)) return opened;
        RenderResourceInfo texture = appearance.OpenTexture(content, image.File.Path, image.File.Sha256, TextureFilter.Nearest);
        ownedResources.Add(texture.Handle);
        SpriteAtlas atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle,
            SpriteAtlasAdapter.ToAtlasFrames(image.Width, image.Height, [new NormalizedSpriteFrame(0, 0, 0, image.Width, image.Height)])));
        atlases.Add(atlas);
        groundItemAtlases.Add(image.File.Path, atlas);
        return atlas;
    }

    private RenderResource GroundItemMask(DaggerfallArtWorldImage mask)
    {
        if (groundItemMasks.TryGetValue(mask.File.Path, out RenderResource? opened)) return opened;
        RenderResourceInfo texture = appearance.OpenTexture(content, mask.File.Path, mask.File.Sha256, TextureFilter.Nearest);
        ownedResources.Add(texture.Handle);
        groundItemMasks.Add(mask.File.Path, texture.Handle);
        return texture.Handle;
    }

    /// <summary>
    /// A dye's CSS colour brightened by <paramref name="brightness"/>, as linear RGB: the inventory multiplies the sRGB
    /// pixels by it, and the renderer multiplies linear ones, so the product is carried through the transfer curve.
    /// </summary>
    private static Color Linear(string color, double brightness)
    {
        float Channel(int at) => (float)Math.Pow(int.Parse(color.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d * brightness, 2.2);
        return new Color(Channel(1), Channel(3), Channel(5), 1F);
    }
}
