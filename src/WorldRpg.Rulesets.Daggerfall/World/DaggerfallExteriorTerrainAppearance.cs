using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Owns the retained visual resources for the currently resident exterior terrain cells.
/// Spatial collision admission remains owned by <see cref="DaggerfallExteriorCellResidency"/>;
/// this coordinator only prepares Graphics resources and the facts that the complete product
/// appearance snapshot must publish.
/// </summary>
internal sealed class DaggerfallExteriorTerrainAppearance : IDisposable
{
    private readonly IGraphicsService _graphics;
    private readonly Material _material;
    private readonly Dictionary<DaggerfallExteriorCellId, TerrainVisual> _visuals = [];
    private readonly List<TerrainVisual> _retired = [];
    private readonly Dictionary<TerrainTextureKey, TerrainMaterial> _terrainMaterials = [];
    private readonly Dictionary<NatureSpriteKey, NatureSprite> _natureSprites = [];
    private readonly Dictionary<DaggerfallExteriorCellId, NatureCell> _natureCells = [];
    private readonly List<NatureBatch> _retiredNature = [];
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

    /// <summary>The nature sprite batches the next snapshot draws, one per resident cell and sprite record.</summary>
    internal int ActiveNatureBatchCount => _natureCells.Values.Sum(cell => cell.Batches.Length);

    /// <summary>The nature sprites those batches draw.</summary>
    internal int ActiveNatureCount => _natureCells.Values.Sum(cell => cell.Source.Count);

    internal int RetiredNatureBatchCount => _retiredNature.Count;

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
    internal static ulong ObjectId(DaggerfallExteriorCellId cell) => DaggerfallPresentationObjectIds.TerrainCell(cell.X, cell.Y);

    /// <summary>The stable object identity of one cell's batch of nature sprites drawing one sprite record.</summary>
    internal static ulong NatureBatchObjectId(DaggerfallExteriorCellId cell, int spriteRecord) =>
        DaggerfallPresentationObjectIds.NatureBatch(cell.X, cell.Y, spriteRecord);

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
        List<(DaggerfallExteriorCellId Cell, NatureCell Nature)> natureAdditions = [];
        try
        {
            foreach (DaggerfallExteriorCellId cell in desired)
            {
                IReadOnlyList<DaggerfallExteriorTerrainTile> terrainTiles = environment?.TerrainTilesFor(cell) ?? [];
                bool hasVariant = variants.TryGetValue(cell, out DaggerfallExteriorTerrainVariant variant);
                // The environment hands back the same tile list while a cell's facts are unchanged, so a retained
                // cell is recognised without resolving its 16,384 tiles again on every movement step.
                if (_visuals.TryGetValue(cell, out TerrainVisual? current)
                    && ReferenceEquals(current.SourceTiles, terrainTiles)
                    && ReferenceEquals(current.Profile, _profile)
                    && current.Variant == (hasVariant ? variant : null)) continue;
                IReadOnlyList<TerrainTextureKey> textureKeys = hasVariant
                    ? ResolveTerrainTextures(variant, terrainTiles)
                    : [];
                if (current is not null
                    && TextureKeysEqual(current.TextureKeys, textureKeys)
                    && current.TerrainTiles.SequenceEqual(terrainTiles))
                {
                    _visuals[cell] = current with { SourceTiles = terrainTiles, Profile = _profile, Variant = hasVariant ? variant : null };
                    continue;
                }
                additions.Add((cell, CreateVisual(cell, surfaceFactory(cell), terrainTiles, textureKeys) with
                {
                    SourceTiles = terrainTiles,
                    Profile = _profile,
                    Variant = hasVariant ? variant : null,
                }));
            }

            if (environment is not null)
            {
                foreach (DaggerfallExteriorCellId cell in desired)
                {
                    IReadOnlyList<DaggerfallExteriorNaturePlacement> placements = environment.NatureFor(cell);
                    if (_natureCells.TryGetValue(cell, out NatureCell? current)
                        && ReferenceEquals(current.Source, placements)
                        && ReferenceEquals(current.Profile, _profile)) continue;
                    natureAdditions.Add((cell, CreateNatureCell(cell, placements)));
                }
            }
        }
        catch
        {
            foreach ((DaggerfallExteriorCellId _, TerrainVisual visual) in additions)
                DisposeVisual(visual);
            foreach ((DaggerfallExteriorCellId _, NatureCell nature) in natureAdditions)
                foreach (NatureBatch batch in nature.Batches)
                    DisposeNatureBatch(batch);
            throw;
        }

        HashSet<DaggerfallExteriorCellId> replacedCells = additions.Select(addition => addition.Cell).ToHashSet();
        foreach (DaggerfallExteriorCellId cell in _visuals.Keys
            .Where(cell => !desiredSet.Contains(cell) || replacedCells.Contains(cell))
            .ToArray())
        {
            _retired.Add(_visuals[cell]);
            _visuals.Remove(cell);
        }

        foreach ((DaggerfallExteriorCellId cell, TerrainVisual visual) in additions)
            _visuals.Add(cell, visual);

        if (environment is not null)
        {
            HashSet<DaggerfallExteriorCellId> replacedNature = natureAdditions.Select(addition => addition.Cell).ToHashSet();
            foreach (DaggerfallExteriorCellId cell in _natureCells.Keys
                .Where(cell => !desiredSet.Contains(cell) || replacedNature.Contains(cell))
                .ToArray())
            {
                _retiredNature.AddRange(_natureCells[cell].Batches);
                _natureCells.Remove(cell);
            }
            foreach ((DaggerfallExteriorCellId cell, NatureCell nature) in natureAdditions)
                _natureCells.Add(cell, nature);
        }

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
        foreach (NatureCell nature in _natureCells.Values)
            _retiredNature.AddRange(nature.Batches);
        _natureCells.Clear();
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
        // A batch's sprites sit in its cell's frame, so the cell's translation places them all, and an origin
        // rebase moves the batch rather than rewriting its instances.
        foreach ((DaggerfallExteriorCellId cell, NatureCell nature) in _natureCells.OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X))
        {
            Transform transform = new(_origin.LocalTranslation(cell), Quaternion.Identity, Vector3.One);
            foreach (NatureBatch batch in nature.Batches)
                facts.Add(new AppearanceFact(batch.ObjectId, false, 0, transform, batch.Appearance, true, RenderLayer.Scene));
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
        foreach (NatureCell nature in _natureCells.Values.Reverse())
            foreach (NatureBatch batch in nature.Batches.Reverse())
                DisposeNatureBatch(batch, ref failures);
        foreach (NatureBatch batch in _retiredNature.AsEnumerable().Reverse())
            DisposeNatureBatch(batch, ref failures);
        _visuals.Clear();
        _retired.Clear();
        _natureCells.Clear();
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
        IReadOnlyList<DaggerfallExteriorTerrainTile> terrainTiles,
        IReadOnlyList<TerrainTextureKey> textureKeys)
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
        Vector3[] positions;
        Vector2[] uvs;
        uint[] indices;
        MeshGroup[] groups;
        MeshMaterialBinding[] bindings;
        if (textureKeys.Count == 0)
        {
            positions = surface.Vertices;
            uvs = BuildTerrainUvs(surface);
            indices = new uint[checked(surface.Triangles.Length * 3)];
            for (int index = 0; index < surface.Triangles.Length; index++)
            {
                Triangle triangle = surface.Triangles[index];
                indices[index * 3] = triangle.A;
                indices[index * 3 + 1] = triangle.B;
                indices[index * 3 + 2] = triangle.C;
            }
            groups = [new MeshGroup(0, 0, checked((uint)indices.Length))];
            bindings = [new MeshMaterialBinding(0, _material)];
        }
        else
        {
            (positions, normals, uvs, indices, groups, bindings) = BuildTiledMesh(surface, normals, terrainTiles, textureKeys);
        }
        MeshResource? mesh = null;
        try
        {
            mesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
                positions,
                normals,
                uvs,
                indices,
                groups,
                bindings));
            Appearance appearance = _graphics.CreateMeshAppearance(mesh);
            return new(mesh, appearance, textureKeys.ToArray(), terrainTiles.ToArray());
        }
        catch
        {
            mesh?.Dispose();
            throw;
        }
    }

    private IReadOnlyList<TerrainTextureKey> ResolveTerrainTextures(
        DaggerfallExteriorTerrainVariant variant,
        IReadOnlyList<DaggerfallExteriorTerrainTile> terrainTiles)
    {
        if (_profile is null)
            throw new InvalidOperationException("Exterior terrain variants require a configured site media profile.");
        if (terrainTiles.Count != checked(DaggerfallTerrainSurfaceBuilder.SampleDimension - 1)
            * (DaggerfallTerrainSurfaceBuilder.SampleDimension - 1))
        {
            throw new InvalidOperationException($"Exterior terrain cell ({variant.Cell.X},{variant.Cell.Y}) published {terrainTiles.Count} tiles instead of the donor 128x128 tilemap.");
        }

        TerrainTextureKey[] keys = new TerrainTextureKey[terrainTiles.Count];
        for (int index = 0; index < terrainTiles.Count; index++)
        {
            DaggerfallExteriorTerrainTile tile = terrainTiles[index];
            if (!_profile.TerrainTextures.TryGetValue((variant.GroundTextureArchive, tile.TextureRecord), out NormalizedTerrainTexture? texture))
            {
                throw new InvalidOperationException(
                    $"Exterior terrain texture {variant.GroundTextureArchive}/{tile.TextureRecord} was selected without a normalized media resource.");
            }
            keys[index] = new(variant.GroundTextureArchive, tile.TextureRecord, texture.TexturePath);
        }
        return keys;
    }

    private static bool TextureKeysEqual(IReadOnlyList<TerrainTextureKey> first, IReadOnlyList<TerrainTextureKey> second) =>
        first.Count == second.Count && first.SequenceEqual(second);

    private (Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, uint[] Indices,
        MeshGroup[] Groups, MeshMaterialBinding[] Bindings) BuildTiledMesh(
        DaggerfallTerrainSurface surface,
        Vector3[] sourceNormals,
        IReadOnlyList<DaggerfallExteriorTerrainTile> terrainTiles,
        IReadOnlyList<TerrainTextureKey> textureKeys)
    {
        const int tileDimension = DaggerfallTerrainSurfaceBuilder.SampleDimension - 1;
        if (terrainTiles.Count != tileDimension * tileDimension || textureKeys.Count != terrainTiles.Count)
            throw new InvalidOperationException("Exterior terrain tile facts do not match the terrain surface tilemap.");

        List<Vector3> positions = new(terrainTiles.Count * 4);
        List<Vector3> normals = new(terrainTiles.Count * 4);
        List<Vector2> uvs = new(terrainTiles.Count * 4);
        Dictionary<TerrainTextureKey, List<uint>> groupedIndices = [];
        for (int tileY = 0; tileY < tileDimension; tileY++)
        {
            for (int tileX = 0; tileX < tileDimension; tileX++)
            {
                int tileIndex = (tileY * tileDimension) + tileX;
                DaggerfallExteriorTerrainTile tile = terrainTiles[tileIndex];
                TerrainTextureKey key = textureKeys[tileIndex];
                if (!groupedIndices.TryGetValue(key, out List<uint>? tileIndices))
                    groupedIndices.Add(key, tileIndices = []);

                int vertex = positions.Count;
                int lowerLeft = tileY * DaggerfallTerrainSurfaceBuilder.SampleDimension + tileX;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + DaggerfallTerrainSurfaceBuilder.SampleDimension;
                int upperRight = upperLeft + 1;
                int[] source = [lowerLeft, lowerRight, upperLeft, upperRight];
                Vector2[] tileUvs = [new(0F, 0F), new(1F, 0F), new(0F, 1F), new(1F, 1F)];
                for (int corner = 0; corner < source.Length; corner++)
                {
                    positions.Add(surface.Vertices[source[corner]]);
                    normals.Add(sourceNormals[source[corner]]);
                    uvs.Add(TransformTileUv(tileUvs[corner], tile.Rotated, tile.Flipped));
                }
                tileIndices.AddRange([(uint)vertex, (uint)(vertex + 3), (uint)(vertex + 1),
                    (uint)vertex, (uint)(vertex + 2), (uint)(vertex + 3)]);
            }
        }

        List<uint> indices = [];
        List<MeshGroup> groups = [];
        List<MeshMaterialBinding> bindings = [];
        uint start = 0;
        uint slot = 0;
        foreach ((TerrainTextureKey key, List<uint> tileIndices) in groupedIndices.OrderBy(entry => entry.Key.Archive)
            .ThenBy(entry => entry.Key.Record).ThenBy(entry => entry.Key.Path, StringComparer.Ordinal))
        {
            indices.AddRange(tileIndices);
            groups.Add(new MeshGroup(slot, start, checked((uint)tileIndices.Count)));
            bindings.Add(new MeshMaterialBinding(slot, GetTerrainMaterial(key).Material));
            start = checked(start + (uint)tileIndices.Count);
            slot++;
        }
        return (positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray(), groups.ToArray(), bindings.ToArray());
    }

    internal static Vector2 TransformTileUv(Vector2 uv, bool rotated, bool flipped)
    {
        if (rotated) uv = new(uv.Y, 1F - uv.X);
        if (flipped) uv.Y = 1F - uv.Y;
        return uv;
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

    private (NatureSpriteKey Key, NormalizedBillboardSprite Sprite) ResolveNatureSprite(
        DaggerfallExteriorNaturePlacement placement)
    {
        if (!TryResolveNatureSprite(placement, out NatureSpriteKey key, out NormalizedBillboardSprite? sprite)
            || sprite is null)
        {
            throw new InvalidOperationException(
                $"Exterior nature sprite {placement.SpriteArchive}/{placement.SpriteRecord} was selected without a normalized media resource.");
        }
        return (key, sprite);
    }

    /// <summary>
    /// Draws one cell's nature as one batch per sprite record, as the donor's DaggerfallBillboardBatch draws a
    /// terrain's nature in one batch per atlas. Each normalized record is its own atlas with its own pivot and world
    /// size, which a batch shares, so the record is the batch: every sprite of it keeps its cell-local placement,
    /// its record's initial frame and size, the cylindrical facing and the default depth and sprite material it
    /// had as a sprite of its own.
    /// </summary>
    private NatureCell CreateNatureCell(DaggerfallExteriorCellId cell, IReadOnlyList<DaggerfallExteriorNaturePlacement> placements)
    {
        List<NatureBatch> batches = [];
        try
        {
            foreach (IGrouping<(int Archive, int Record), DaggerfallExteriorNaturePlacement> group in placements
                .GroupBy(placement => (Archive: placement.SpriteArchive, Record: placement.SpriteRecord))
                .OrderBy(group => group.Key.Record).ThenBy(group => group.Key.Archive))
            {
                (NatureSpriteKey key, NormalizedBillboardSprite sprite) = ResolveNatureSprite(group.First());
                NatureSprite admitted = GetNatureSprite(key, sprite);
                SpriteBatchInstance[] instances = [.. group.Select(placement =>
                    new SpriteBatchInstance(placement.LocalPosition, 1F, sprite.InitialFrameId))];
                ulong objectId = NatureBatchObjectId(cell, key.Record);
                if (batches.Any(batch => batch.ObjectId == objectId))
                    throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) draws nature record {key.Record} from more than one archive.");
                Appearance appearance = _graphics.CreateSpriteBatch(new SpriteBatchRequest(
                    admitted.Atlas,
                    instances,
                    sprite.Pivot,
                    sprite.Size,
                    BillboardMode.Cylindrical));
                batches.Add(new(objectId, key, appearance, instances.Length));
            }
        }
        catch
        {
            foreach (NatureBatch batch in batches) DisposeNatureBatch(batch);
            throw;
        }
        return new(placements, _profile, [.. batches]);
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
        foreach (NatureBatch batch in _retiredNature.AsEnumerable().Reverse())
            DisposeNatureBatch(batch, ref failures);
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

    private static void DisposeNatureBatch(NatureBatch batch)
    {
        List<Exception>? failures = null;
        DisposeNatureBatch(batch, ref failures);
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void DisposeNatureBatch(NatureBatch batch, ref List<Exception>? failures)
    {
        try { batch.Appearance.Dispose(); }
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
    private sealed record NatureBatch(ulong ObjectId, NatureSpriteKey Key, Appearance Appearance, int Instances);
    /// <summary>One cell's nature batches and the environment placements and media profile they were made from.</summary>
    private sealed record NatureCell(IReadOnlyList<DaggerfallExteriorNaturePlacement> Source, DaggerfallSiteProfile? Profile, NatureBatch[] Batches);
    private sealed record TerrainVisual(MeshResource Mesh, Appearance Appearance, IReadOnlyList<TerrainTextureKey> TextureKeys, IReadOnlyList<DaggerfallExteriorTerrainTile> TerrainTiles)
    {
        /// <summary>The environment's tile list, media profile and variant the visual was last reconciled against.</summary>
        internal IReadOnlyList<DaggerfallExteriorTerrainTile>? SourceTiles { get; init; }
        internal DaggerfallSiteProfile? Profile { get; init; }
        internal DaggerfallExteriorTerrainVariant? Variant { get; init; }
    }
}
