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
/// refreshed art, a pile of one item or one stack stands as that item instead, in its metal or dye, at the size its
/// classic image would have at the treasure piles' scale.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    // A refreshed item image's atlas, and a tint mask's texture, each by its world PNG's path, opened once per location.
    // They are apart because items in different metals or dyes, and the untinted one, share one image.
    private readonly Dictionary<string, SpriteAtlas> groundItemAtlases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RenderResource> groundItemMasks = new(StringComparer.Ordinal);

    /// <summary>A ground pile's billboard, what it stands as (a treasure pile's index or an item's look), and how far it is lifted to stand on the floor.</summary>
    internal sealed record GroundVisual(BillboardVisual Billboard, object Look, float Lift);

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
            object look = groundLook?.Invoke(container.Id) is { } item ? item : TreasurePile(container.Id);
            if (groundVisuals.TryGetValue(container.Id, out GroundVisual? current))
            {
                if (current.Look.Equals(look)) continue;
                groundVisuals.Remove(container.Id);
                Retire(current.Billboard);
            }
            (Appearance visual, float lift) = look is DaggerfallGroundItemLook itemLook ? CreateGroundItem(itemLook) : CreateTreasurePile((int)look);
            groundVisuals.Add(container.Id, new(new BillboardVisual(container.Id, visual), look, lift));
        }
    }

    /// <summary>The treasure pile a container stands as: a spread of its identity over the published piles, so it keeps its pile across saves.</summary>
    private int TreasurePile(long id) => (int)((ulong)id * 0x9E3779B97F4A7C15UL % (ulong)groundContainerSprites.Count);

    /// <summary>
    /// A treasure pile's billboard, lifted by its pivot's share of its height so it stands on the floor whatever pivot
    /// the source publishes, as the donor lifts dropped containers by half their height.
    /// </summary>
    private (Appearance, float) CreateTreasurePile(int index)
    {
        NormalizedBillboardSprite sprite = groundContainerSprites[index];
        Appearance visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(groundContainerAtlases[index],
            sprite.InitialFrameId, sprite.Pivot, sprite.Size, BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default,
            new Color(1F, 1F, 1F, 1F)));
        return (visual, sprite.Pivot.Y * sprite.Size.Y);
    }

    /// <summary>
    /// Metres per paper-doll unit: the doll's 184 units hold a body about 1.8 m tall, so a worn item lies at its size on
    /// that body.
    /// </summary>
    private const float MetresPerDollUnit = .01F;

    /// <summary>
    /// An item's billboard: its world image standing on the floor, as tall as its classic image (an icon at the treasure
    /// piles' metres per classic pixel, a worn item's image at the doll's scale) and as wide as the refreshed image's
    /// proportions make it, and where it is in a metal or dye, that colour multiplied into the pixels its mask covers.
    /// </summary>
    private (Appearance, float) CreateGroundItem(DaggerfallGroundItemLook look)
    {
        NormalizedBillboardSprite pile = groundContainerSprites[0];
        float metresPerPixel = pile.Size.Y / pile.Frames.Single(frame => frame.Id == pile.InitialFrameId).Height;
        float height = (float)look.ClassicHeight * (look.Doll ? MetresPerDollUnit : metresPerPixel);
        Vector2 size = new(height * look.Image.Width / look.Image.Height, height);
        SpriteAtlas atlas = GroundItemAtlas(look.Image);
        RenderResource? mask = look.Mask is { } image ? GroundItemMask(image) : null;
        Color tint = look.Color is { } color ? Linear(color, look.Brightness) : new Color(1F, 1F, 1F, 1F);
        SpriteFromAtlasRequest request = new(atlas, 0, new Vector2(.5F, 0F), size, BillboardMode.Cylindrical, SpriteSizeMode.World, 0,
            SpriteDepthPolicy.Default, tint);
        if (mask is not null)
            request = request with { Material = new SpriteMaterialDescriptor(SpriteLightingMode.Unlit, default, default, 1f, 0f,
                SpriteAlphaMode.Blend, .5f, SpriteShadowPolicy.None) { TintMask = new RenderResourceReference(mask) } };
        return (appearance.CreateSpriteFromAtlas(request), 0F);
    }

    private SpriteAtlas GroundItemAtlas(DaggerfallArtWorldImage image)
    {
        if (groundItemAtlases.TryGetValue(image.File.Path, out SpriteAtlas? opened)) return opened;
        RenderResourceInfo texture = appearance.OpenTexture(content, image.File.Path, image.File.Sha256, TextureFilter.Linear);
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
        RenderResourceInfo texture = appearance.OpenTexture(content, mask.File.Path, mask.File.Sha256, TextureFilter.Linear);
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
