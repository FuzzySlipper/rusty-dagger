using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The banners of arms a town hangs on its gates, palace and offices (#11144). Each banner is one shared hanging cloth,
/// a unit wide and a unit deep from an iron rod along its top, scaled to the banner's size and faced along its wall. Its
/// cloth shows the arms the session names for the site now (<see cref="UseBanners"/>), read every time the location's
/// facts are drawn, so a change of liege on the board's Sunday hangs the new arms at once. One cloth mesh is made per
/// arms the site has shown, and released with the location.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private const uint BannerClothSlot = 0, BannerRodSlot = 1;
    private readonly Dictionary<string, Appearance> bannerAppearances = new(StringComparer.Ordinal);
    private readonly List<IDisposable> bannerResources = [];
    private readonly List<(DaggerfallSiteBanner Definition, ulong Ordinal)> banners = [];
    private IReadOnlyDictionary<string, DaggerfallHeraldryArt> bannerArt = new Dictionary<string, DaggerfallHeraldryArt>();
    private Func<int, string?> bannerArms = _ => null;
    private Material? bannerRod;

    /// <summary>
    /// Hangs the site's banners in the arms <paramref name="arms"/> names now for its classic region, drawn from
    /// <paramref name="art"/>; a site whose arms have no art, or are none, hangs none.
    /// </summary>
    internal void UseBanners(IReadOnlyDictionary<string, DaggerfallHeraldryArt> art, Func<int, string?> arms)
    {
        bannerArt = art ?? throw new ArgumentNullException(nameof(art));
        bannerArms = arms ?? throw new ArgumentNullException(nameof(arms));
    }

    private void AdmitBanners(DaggerfallSiteProfile inputs)
    {
        foreach (DaggerfallSiteBanner banner in inputs.Banners)
            banners.Add((banner, NextLocationVisualOrdinal()));
    }

    /// <summary>The arms id the banners show now, if the session names one with art.</summary>
    internal string? BannerArmsShown => inputs.Site is { } site && bannerArms(site.Region) is { } arms && bannerArt.ContainsKey(arms) ? arms : null;

    private void AppendBannerFacts(List<AppearanceFact> facts, int slot)
    {
        if (banners.Count == 0 || BannerArmsShown is not { } arms) return;
        if (!bannerAppearances.TryGetValue(arms, out Appearance? mesh))
            bannerAppearances.Add(arms, mesh = CreateBannerAppearance(bannerArt[arms]));
        foreach ((DaggerfallSiteBanner banner, ulong ordinal) in banners)
        {
            Transform localPose = new(banner.Top.ToVector(), NoticeBoardRotation(banner.SurfaceNormal), new Vector3(banner.Width, banner.Drop, 1f));
            facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.LocationVisual(slot, ordinal), false, 0,
                Compose(worldAppearance.Transform, localPose), mesh, worldAppearance.Visible, worldAppearance.Layer, ShadowCasting.Cast));
        }
    }

    private Appearance CreateBannerAppearance(DaggerfallHeraldryArt art)
    {
        RenderResourceInfo texture = appearance.OpenTexture(content, art.Banner.Path, art.Banner.Sha256, TextureFilter.Linear);
        Material cloth = appearance.CreateMaterial(new MaterialRequest(new Color(1f, 1f, 1f, 1f), new RenderResourceReference(texture.Handle), .92f,
            new Color(1f, 1f, 1f, 1f), Vector3.Zero, 0f, false) with { AlphaMode = MaterialAlphaMode.Mask, AlphaCutoff = .5f });
        bannerResources.Add(cloth);
        if (bannerRod is null)
        {
            bannerRod = appearance.CreateMaterial(new MaterialRequest(Color.FromSrgb(.16f, .14f, .13f), default(RenderResourceReference), .6f,
                new Color(1f, 1f, 1f, 1f), Vector3.Zero, 0f, false) with { FlatShading = true });
            bannerResources.Add(bannerRod);
        }
        DaggerfallBannerMeshData data = BuildBannerMeshData();
        MeshResource mesh = appearance.CreateMeshResource(new MeshResourceCreateRequest(data.Positions, data.Normals, data.Uvs, data.Indices, data.Groups,
            new MeshMaterialBinding[] { new(BannerClothSlot, cloth), new(BannerRodSlot, bannerRod) }));
        bannerResources.Add(mesh);
        Appearance shown = appearance.CreateMeshAppearance(mesh);
        bannerResources.Add(shown);
        return shown;
    }

    private IEnumerable<IDisposable> TakeBannerResources()
    {
        List<IDisposable> retired = [.. bannerResources.AsEnumerable().Reverse()];
        bannerResources.Clear();
        bannerAppearances.Clear();
        banners.Clear();
        bannerRod = null;
        return retired;
    }

    /// <summary>
    /// The unit banner in local space: its cloth a unit wide (<c>X</c> from -½ to ½) hanging a unit down from <c>Y = 0</c>,
    /// its front facing <c>+Z</c> with the arms the right way round and its back mirrored, as cloth shows them, and an
    /// iron rod along its top a little proud of the wall.
    /// </summary>
    internal static DaggerfallBannerMeshData BuildBannerMeshData()
    {
        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<Vector2> uvs = [];
        List<uint> indices = [];
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            uint at = (uint)positions.Count;
            positions.AddRange([a, b, c, d]);
            normals.AddRange([normal, normal, normal, normal]);
            uvs.AddRange([ua, ub, uc, ud]);
            indices.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
        }
        const float Front = .03f, Back = .025f;
        // Counter-clockwise about each face's normal; the image's top (v = 0) at the rod.
        Quad(new(-.5f, -1f, Front), new(.5f, -1f, Front), new(.5f, 0f, Front), new(-.5f, 0f, Front), Vector3.UnitZ,
            new(0, 1), new(1, 1), new(1, 0), new(0, 0));
        Quad(new(.5f, -1f, Back), new(-.5f, -1f, Back), new(-.5f, 0f, Back), new(.5f, 0f, Back), -Vector3.UnitZ,
            new(1, 1), new(0, 1), new(0, 0), new(1, 0));
        uint clothIndices = (uint)indices.Count;
        Vector3 min = new(-.56f, -.015f, 0f), max = new(.56f, .02f, .06f);
        Vector3[] corners = [new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)];
        Vector2 z = Vector2.Zero;
        Quad(corners[4], corners[5], corners[6], corners[7], Vector3.UnitZ, z, z, z, z);
        Quad(corners[1], corners[0], corners[3], corners[2], -Vector3.UnitZ, z, z, z, z);
        Quad(corners[5], corners[1], corners[2], corners[6], Vector3.UnitX, z, z, z, z);
        Quad(corners[0], corners[4], corners[7], corners[3], -Vector3.UnitX, z, z, z, z);
        Quad(corners[7], corners[6], corners[2], corners[3], Vector3.UnitY, z, z, z, z);
        Quad(corners[0], corners[1], corners[5], corners[4], -Vector3.UnitY, z, z, z, z);
        return new([.. positions], [.. normals], [.. uvs], [.. indices],
            [new MeshGroup(BannerClothSlot, 0, clothIndices), new MeshGroup(BannerRodSlot, clothIndices, (uint)indices.Count - clothIndices)]);
    }
}

/// <summary>The unit banner's mesh: positions, normals, texture coordinates, indices and its cloth and rod groups.</summary>
internal readonly record struct DaggerfallBannerMeshData(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, uint[] Indices, MeshGroup[] Groups);
