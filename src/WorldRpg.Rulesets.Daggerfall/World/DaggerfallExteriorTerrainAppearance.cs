using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Owns the retained visual resources for the currently resident exterior terrain cells.
/// Spatial collision admission remains owned by <see cref="DaggerfallExteriorCellResidency"/>;
/// this coordinator only prepares Graphics resources and the facts that the complete product
/// appearance snapshot must publish.
/// </summary>
internal sealed class DaggerfallExteriorTerrainAppearance : IDisposable
{
    private const ulong AppearanceObjectPrefix = 0xD800_0000_0000_0000UL;
    private const int CoordinateBits = 24;
    private const ulong CoordinateMask = 0x00FF_FFFFUL;

    private readonly IGraphicsService _graphics;
    private readonly Material _material;
    private readonly Dictionary<DaggerfallExteriorCellId, TerrainVisual> _visuals = [];
    private readonly List<TerrainVisual> _retired = [];
    private readonly Dictionary<TerrainTextureKey, TerrainMaterial> _terrainMaterials = [];
    private readonly Dictionary<NatureSpriteKey, NatureSprite> _natureSprites = [];
    private readonly Dictionary<ulong, NatureVisual> _natureVisuals = [];
    private readonly List<NatureVisual> _retiredNature = [];
    private DaggerfallSiteProfile? _profile;
    private DaggerfallExteriorWorldOrigin _origin;
    private bool _hasOrigin;
    private bool _disposed;

    /// <summary>
    /// Creates a neutral untextured terrain material. A later Daggerfall presentation owner can
    /// provide authored material policy without changing the retained mesh or collision contract.
    /// </summary>
    internal DaggerfallExteriorTerrainAppearance(IGraphicsService graphics, DaggerfallSiteProfile? profile = null)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _profile = profile;
        _material = _graphics.CreateMaterial(new MaterialRequest(
            new Color(1F, 1F, 1F, 1F),
            default(RenderResourceReference),
            1F,
            new Color(1F, 1F, 1F, 1F),
            Vector3.Zero,
            0F,
            false));
    }

    internal int ActiveCellCount => _visuals.Count;

    internal int RetiredCellCount => _retired.Count;

    internal int ActiveNatureCount => _natureVisuals.Count;

    internal int RetiredNatureCount => _retiredNature.Count;

    /// <summary>Updates the normalized media closure used by newly admitted terrain sprites.</summary>
    internal void ConfigureProfile(DaggerfallSiteProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    /// <summary>
    /// Appends the current terrain facts to the complete product snapshot assembled by the
    /// presentation owner. Duplicate object identities are rejected before Engine publication.
    /// </summary>
    internal void AppendFacts(List<AppearanceFact> baseFacts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(baseFacts);
        HashSet<ulong> ids = [];
        foreach (AppearanceFact fact in baseFacts)
        {
            if (!ids.Add(fact.ObjectId))
                throw new InvalidOperationException($"Complete appearance snapshot contains duplicate object {fact.ObjectId}.");
        }

        foreach (AppearanceFact fact in BuildFacts())
        {
            if (!ids.Add(fact.ObjectId))
            {
                throw new InvalidOperationException(
                    $"Complete appearance snapshot already owns exterior terrain object {fact.ObjectId}.");
            }
            baseFacts.Add(fact);
        }
    }

    /// <summary>Completes the resource transition after the owning Graphics snapshot is accepted.</summary>
    internal void CompleteAcceptedSnapshot() => DisposeRetired();

    /// <summary>The stable object identity used by one exterior terrain cell's visual fact.</summary>
    internal static ulong ObjectId(DaggerfallExteriorCellId cell)
    {
        if ((ulong)(uint)cell.X > CoordinateMask || (ulong)(uint)cell.Y > CoordinateMask)
        {
            throw new ArgumentOutOfRangeException(nameof(cell),
                "Exterior terrain visual coordinates exceed the stable ID range.");
        }

        return AppearanceObjectPrefix
            | ((ulong)(uint)cell.X << CoordinateBits)
            | (ulong)(uint)cell.Y;
    }

    /// <summary>
    /// Reconciles retained visual resources to the collision residency's durable cell set.
    /// Newly created resources are staged before active state changes; a failed admission leaves
    /// the previous visual set intact. Removed resources wait in <see cref="_retired"/> until a
    /// complete snapshot containing the new set has been accepted by Engine.
    /// </summary>
    internal void Reconcile(
        IEnumerable<DaggerfallExteriorCellId> residentCells,
        DaggerfallExteriorWorldOrigin origin,
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory)
        => Reconcile(residentCells, origin, surfaceFactory, null);

    /// <summary>
    /// Reconciles terrain and source nature placements against the current environment variants.
    /// Terrain material and sprite resources are opened from the selected profile's generated media
    /// closure, so a climate/season change replaces actual retained Engine appearances together
    /// with the product facts that select them.
    /// </summary>
    internal void Reconcile(
        IEnumerable<DaggerfallExteriorCellId> residentCells,
        DaggerfallExteriorWorldOrigin origin,
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory,
        DaggerfallExteriorEnvironment? environment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(residentCells);
        ArgumentNullException.ThrowIfNull(surfaceFactory);

        DaggerfallExteriorCellId[] desired = residentCells
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
        if (desired.Distinct().Count() != desired.Length)
            throw new ArgumentException("Exterior visual residency contains a duplicate cell.", nameof(residentCells));

        HashSet<DaggerfallExteriorCellId> desiredSet = desired.ToHashSet();
        Dictionary<DaggerfallExteriorCellId, DaggerfallExteriorTerrainVariant> variants = environment?.TerrainVariants
            .ToDictionary(variant => variant.Cell)
            ?? [];
        List<(DaggerfallExteriorCellId Cell, TerrainVisual Visual)> additions = [];
        List<NatureVisual> natureAdditions = [];
        try
        {
            foreach (DaggerfallExteriorCellId cell in desired)
            {
                TerrainTextureKey? textureKey = variants.TryGetValue(cell, out DaggerfallExteriorTerrainVariant variant)
                    ? ResolveTerrainTexture(variant)
                    : null;
                if (_visuals.TryGetValue(cell, out TerrainVisual? current)
                    && current.TextureKey == textureKey) continue;
                additions.Add((cell, CreateVisual(cell, surfaceFactory(cell), textureKey)));
            }

            if (environment is not null)
            {
                foreach (DaggerfallExteriorNaturePlacement placement in environment.NaturePlacements)
                {
                    if (!TryResolveNatureSprite(placement, out NatureSpriteKey spriteKey, out NormalizedBillboardSprite? sprite))
                        continue;
                    if (_natureVisuals.TryGetValue(placement.StableId, out NatureVisual? current)
                        && current.Key == spriteKey) continue;
                    natureAdditions.Add(CreateNatureVisual(placement, spriteKey, sprite!));
                }
            }
        }
        catch
        {
            foreach ((DaggerfallExteriorCellId _, TerrainVisual visual) in additions)
                DisposeVisual(visual);
            foreach (NatureVisual visual in natureAdditions)
                DisposeNatureVisual(visual);
            throw;
        }

        foreach (DaggerfallExteriorCellId cell in _visuals.Keys
            .Where(cell => !desiredSet.Contains(cell)
                || additions.Any(addition => addition.Cell == cell))
            .ToArray())
        {
            _retired.Add(_visuals[cell]);
            _visuals.Remove(cell);
        }

        foreach ((DaggerfallExteriorCellId cell, TerrainVisual visual) in additions)
            _visuals.Add(cell, visual);

        HashSet<ulong> desiredNature = environment is null
            ? _natureVisuals.Keys.ToHashSet()
            : natureAdditions.Select(visual => visual.ObjectId).ToHashSet();
        if (environment is not null)
        {
            // A placement omitted because its generated source sprite is unavailable must be
            // removed from the complete snapshot as well; keeping an old season's sprite would
            // make the terrain facts and the admitted media closure disagree.
            desiredNature.UnionWith(environment.NaturePlacements
                .Where(placement => TryResolveNatureSprite(placement, out _, out _))
                .Select(placement => placement.StableId));
        }
        foreach (ulong objectId in _natureVisuals.Keys
            .Where(objectId => !desiredNature.Contains(objectId)
                || natureAdditions.Any(addition => addition.ObjectId == objectId))
            .ToArray())
        {
            _retiredNature.Add(_natureVisuals[objectId]);
            _natureVisuals.Remove(objectId);
        }
        foreach (NatureVisual visual in natureAdditions)
            _natureVisuals.Add(visual.ObjectId, visual);

        _origin = origin;
        _hasOrigin = true;
    }

    /// <summary>Marks all current visual cells for removal from the next complete snapshot.</summary>
    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (TerrainVisual visual in _visuals.Values)
            _retired.Add(visual);
        _visuals.Clear();
        foreach (NatureVisual visual in _natureVisuals.Values)
            _retiredNature.Add(visual);
        _natureVisuals.Clear();
        _hasOrigin = false;
    }

    /// <summary>Returns the terrain facts that the next complete snapshot must include.</summary>
    internal AppearanceFact[] BuildFacts()
    {
        if (!_hasOrigin) return [];

        List<AppearanceFact> facts = _visuals
            .OrderBy(pair => pair.Key.Y)
            .ThenBy(pair => pair.Key.X)
            .Select(pair => new AppearanceFact(
                ObjectId(pair.Key),
                false,
                0,
                new Transform(
                    _origin.LocalTranslation(pair.Key),
                    Quaternion.Identity,
                    Vector3.One),
                pair.Value.Appearance,
                true,
                RenderLayer.Scene))
            .ToList();
        foreach (NatureVisual visual in _natureVisuals.Values.OrderBy(value => value.ObjectId))
        {
            facts.Add(new AppearanceFact(
                visual.ObjectId,
                false,
                0,
                new Transform(
                    _origin.LocalTranslation(visual.Placement.Cell) + visual.Placement.LocalPosition,
                    Quaternion.Identity,
                    Vector3.One),
                visual.Appearance,
                true,
                RenderLayer.Scene));
        }
        return facts.ToArray();
    }

    /// <summary>
    /// Releases retained Engine resources after the caller has published a snapshot that no
    /// longer references them. The presentation owner invokes this after its complete snapshot
    /// has been accepted.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (TerrainVisual visual in _visuals.Values.Reverse())
            DisposeVisual(visual, ref failures);
        foreach (TerrainVisual visual in _retired.AsEnumerable().Reverse())
            DisposeVisual(visual, ref failures);
        foreach (NatureVisual visual in _natureVisuals.Values.Reverse())
            DisposeNatureVisual(visual, ref failures);
        foreach (NatureVisual visual in _retiredNature.AsEnumerable().Reverse())
            DisposeNatureVisual(visual, ref failures);
        _visuals.Clear();
        _retired.Clear();
        _natureVisuals.Clear();
        _retiredNature.Clear();
        foreach (NatureSprite sprite in _natureSprites.Values.Reverse())
            DisposeNatureSprite(sprite, ref failures);
        _natureSprites.Clear();
        foreach (TerrainMaterial material in _terrainMaterials.Values.Reverse())
            DisposeTerrainMaterial(material, ref failures);
        _terrainMaterials.Clear();
        try { _material.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private TerrainVisual CreateVisual(
        DaggerfallExteriorCellId cell,
        DaggerfallTerrainSurface surface,
        TerrainTextureKey? textureKey)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.MapPixelX != cell.X || surface.MapPixelY != cell.Y)
        {
            throw new InvalidOperationException(
                $"Terrain visual surface ({surface.MapPixelX},{surface.MapPixelY}) does not match cell ({cell.X},{cell.Y}).");
        }
        if (surface.Vertices.Length < 3 || surface.Triangles.Length == 0)
            throw new InvalidOperationException($"Exterior terrain cell ({cell.X},{cell.Y}) has no renderable geometry.");

        Vector3[] normals = BuildNormals(surface);
        Vector2[] uvs = BuildTerrainUvs(surface);
        uint[] indices = new uint[checked(surface.Triangles.Length * 3)];
        for (int index = 0; index < surface.Triangles.Length; index++)
        {
            Triangle triangle = surface.Triangles[index];
            indices[index * 3] = triangle.A;
            indices[index * 3 + 1] = triangle.B;
            indices[index * 3 + 2] = triangle.C;
        }

        Material material = textureKey is { } key
            ? GetTerrainMaterial(key).Material
            : _material;
        MeshResource? mesh = null;
        try
        {
            mesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
                surface.Vertices,
                normals,
                uvs,
                indices,
                new[] { new MeshGroup(0, 0, checked((uint)indices.Length)) },
                new[] { new MeshMaterialBinding(0, material) }));
            Appearance appearance = _graphics.CreateMeshAppearance(mesh);
            return new(mesh, appearance, textureKey);
        }
        catch
        {
            mesh?.Dispose();
            throw;
        }
    }

    private TerrainTextureKey? ResolveTerrainTexture(DaggerfallExteriorTerrainVariant variant)
    {
        if (_profile is null) return null;
        foreach (KeyValuePair<(int Archive, int Record), NormalizedTerrainTexture> pair in _profile.TerrainTextures
            .Where(pair => pair.Key.Archive == variant.GroundTextureArchive)
            .OrderBy(pair => pair.Key.Record))
        {
            // The terrain mesh is one admitted Engine resource per cell. The source climate
            // archive is the policy-bearing choice; record zero is the canonical tile when the
            // generated closure has it, while a fixture or older closure may expose another
            // source record only. Keeping the path in the key also prevents stale resources
            // surviving a profile replacement under the same archive/record address.
            return new(pair.Key.Archive, pair.Key.Record, pair.Value.TexturePath);
        }
        return null;
    }

    private TerrainMaterial GetTerrainMaterial(TerrainTextureKey key)
    {
        if (_terrainMaterials.TryGetValue(key, out TerrainMaterial? existing)) return existing;
        if (_profile is null
            || !_profile.TerrainTextures.TryGetValue((key.Archive, key.Record), out NormalizedTerrainTexture? texture)
            || !string.Equals(texture.TexturePath, key.Path, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Exterior terrain texture {key.Archive}/{key.Record} was selected without a normalized media resource.");
        }

        RenderResource? resource = null;
        Material? material = null;
        try
        {
            resource = _graphics.OpenResource(new RenderResourceRequest(
                texture.TexturePath, TextureFilter.Nearest, TextureWrap.Repeat)).Handle;
            material = _graphics.CreateMaterial(new MaterialRequest(
                new Color(1F, 1F, 1F, 1F), resource, 1F,
                new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false));
            TerrainMaterial created = new(key, resource, material);
            _terrainMaterials.Add(key, created);
            return created;
        }
        catch
        {
            material?.Dispose();
            resource?.Dispose();
            throw;
        }
    }

    private bool TryResolveNatureSprite(
        DaggerfallExteriorNaturePlacement placement,
        out NatureSpriteKey key,
        out NormalizedBillboardSprite? sprite)
    {
        key = default;
        sprite = null;
        if (_profile is null
            || !_profile.BillboardSprites.TryGetValue((placement.SpriteArchive, placement.SpriteRecord), out sprite))
            return false;
        key = new(placement.SpriteArchive, placement.SpriteRecord, sprite.TexturePath);
        return true;
    }

    private NatureVisual CreateNatureVisual(
        DaggerfallExteriorNaturePlacement placement,
        NatureSpriteKey key,
        NormalizedBillboardSprite sprite)
    {
        NatureSprite admitted = GetNatureSprite(key, sprite);
        Appearance appearance = _graphics.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(
            admitted.Atlas,
            sprite.InitialFrameId,
            sprite.Pivot,
            sprite.Size,
            BillboardMode.Cylindrical,
            SpriteSizeMode.World,
            0,
            SpriteDepthPolicy.Default,
            new Color(1F, 1F, 1F, 1F)));
        return new(placement.StableId, placement, key, appearance);
    }

    private NatureSprite GetNatureSprite(NatureSpriteKey key, NormalizedBillboardSprite sprite)
    {
        if (_natureSprites.TryGetValue(key, out NatureSprite? existing)) return existing;

        RenderResource? resource = null;
        SpriteAtlas? atlas = null;
        try
        {
            resource = _graphics.OpenResource(new RenderResourceRequest(
                sprite.TexturePath, TextureFilter.Nearest, TextureWrap.Clamp)).Handle;
            SpriteAtlasFrame[] frames = SpriteAtlasAdapter.ToAtlasFrames(
                sprite.AtlasWidth,
                sprite.AtlasHeight,
                sprite.Frames.Select(frame => new NormalizedSpriteFrame(
                    frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray());
            atlas = _graphics.CreateSpriteAtlas(new SpriteAtlasCreateRequest(resource, frames));
            NatureSprite created = new(key, resource, atlas);
            _natureSprites.Add(key, created);
            return created;
        }
        catch
        {
            atlas?.Dispose();
            resource?.Dispose();
            throw;
        }
    }

    private static Vector3[] BuildNormals(DaggerfallTerrainSurface surface)
    {
        Vector3[] normals = new Vector3[surface.Vertices.Length];
        for (int index = 0; index < surface.Vertices.Length; index++)
        {
            Vector3 vertex = surface.Vertices[index];
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
            {
                throw new InvalidOperationException($"Terrain visual vertex {index} is non-finite.");
            }
        }

        foreach (Triangle triangle in surface.Triangles)
        {
            uint vertexCount = checked((uint)surface.Vertices.Length);
            if (triangle.A >= vertexCount
                || triangle.B >= vertexCount
                || triangle.C >= vertexCount)
            {
                throw new InvalidOperationException("Terrain visual triangle references a vertex outside its surface.");
            }

            Vector3 normal = Vector3.Cross(
                surface.Vertices[triangle.B] - surface.Vertices[triangle.A],
                surface.Vertices[triangle.C] - surface.Vertices[triangle.A]);
            if (normal.LengthSquared() <= float.Epsilon) continue;
            normals[triangle.A] += normal;
            normals[triangle.B] += normal;
            normals[triangle.C] += normal;
        }

        for (int index = 0; index < normals.Length; index++)
        {
            normals[index] = normals[index].LengthSquared() <= float.Epsilon
                ? Vector3.UnitY
                : Vector3.Normalize(normals[index]);
        }

        return normals;
    }

    private static Vector2[] BuildTerrainUvs(DaggerfallTerrainSurface surface)
    {
        // One map pixel contains 128 source terrain tiles. The normalized terrain media stores
        // one source tile per texture resource, so repeat that resource once per source tile
        // across the retained surface. This keeps the selected climate archive visible through
        // the Engine mesh shader instead of silently submitting an empty UV stream.
        Vector2[] uvs = new Vector2[surface.Vertices.Length];
        for (int index = 0; index < surface.Vertices.Length; index++)
        {
            Vector3 vertex = surface.Vertices[index];
            uvs[index] = new(
                vertex.X / DaggerfallTerrainSurfaceBuilder.SampleSpacing,
                vertex.Z / DaggerfallTerrainSurfaceBuilder.SampleSpacing);
        }
        return uvs;
    }

    private void DisposeRetired()
    {
        List<Exception>? failures = null;
        foreach (TerrainVisual visual in _retired.AsEnumerable().Reverse())
            DisposeVisual(visual, ref failures);
        foreach (NatureVisual visual in _retiredNature.AsEnumerable().Reverse())
            DisposeNatureVisual(visual, ref failures);
        _retired.Clear();
        _retiredNature.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void DisposeVisual(TerrainVisual visual)
    {
        List<Exception>? failures = null;
        DisposeVisual(visual, ref failures);
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void DisposeVisual(TerrainVisual visual, ref List<Exception>? failures)
    {
        try { visual.Appearance.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { visual.Mesh.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private static void DisposeNatureVisual(NatureVisual visual)
    {
        List<Exception>? failures = null;
        DisposeNatureVisual(visual, ref failures);
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void DisposeNatureVisual(NatureVisual visual, ref List<Exception>? failures)
    {
        try { visual.Appearance.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private static void DisposeNatureSprite(NatureSprite sprite, ref List<Exception>? failures)
    {
        try { sprite.Atlas.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { sprite.Resource.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private static void DisposeTerrainMaterial(TerrainMaterial material, ref List<Exception>? failures)
    {
        try { material.Material.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { material.Resource.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private readonly record struct TerrainTextureKey(int Archive, int Record, string Path);
    private readonly record struct NatureSpriteKey(int Archive, int Record, string Path);
    private sealed record TerrainMaterial(TerrainTextureKey Key, RenderResource Resource, Material Material);
    private sealed record NatureSprite(NatureSpriteKey Key, RenderResource Resource, SpriteAtlas Atlas);
    private sealed record NatureVisual(ulong ObjectId, DaggerfallExteriorNaturePlacement Placement, NatureSpriteKey Key, Appearance Appearance);
    private sealed record TerrainVisual(MeshResource Mesh, Appearance Appearance, TerrainTextureKey? TextureKey);
}
