using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Owns the resident cells' nature batches and the ground's layered materials. The ground itself is a heightfield
/// (<see cref="Presentation.DaggerfallHorizon"/>); spatial collision admission remains owned by
/// <see cref="DaggerfallExteriorCellResidency"/>. This coordinator only prepares Graphics resources and the facts that the
/// complete product appearance snapshot must publish.
/// </summary>
internal sealed class DaggerfallExteriorTerrainAppearance : IDisposable
{
    private readonly IContentService _content;
    private readonly IGraphicsService _graphics;
    private readonly Dictionary<NatureSpriteKey, NatureSprite> _natureSprites = [];
    private readonly Dictionary<DaggerfallExteriorCellId, NatureCell> _natureCells = [];
    private readonly List<NatureBatch> _retiredNature = [];
    private DaggerfallSiteProfile? _profile;
    private DaggerfallArtReplacements? _art;
    private Func<bool> _drawRefreshed = () => false;
    private bool _refreshedDrawn;
    private IAuthoredContentService? _authored;
    private DaggerfallGroundTuning _ground = DaggerfallGroundTuning.Default;
    private readonly DaggerfallFlatsTuning _flats;
    private DaggerfallGroundBlendSettings _blend;
    private readonly Dictionary<int, DaggerfallGroundLayers> _groundLayers = [];
    private readonly List<DaggerfallGroundLayers> _retiredLayers = [];
    private DaggerfallExteriorWorldOrigin _origin;
    private bool _hasOrigin;
    private bool _disposed;

    internal DaggerfallExteriorTerrainAppearance(IContentService content, IGraphicsService graphics, DaggerfallSiteProfile? profile = null,
        DaggerfallFlatsTuning? flats = null)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _profile = profile;
        _flats = flats ?? DaggerfallFlatsTuning.Default;
    }

    /// <summary>
    /// Takes the art set: ground layers wear its surfaces whatever the run draws, and nature draws in its flats
    /// whenever <paramref name="refreshed"/> answers that the run does not draw classic art.
    /// </summary>
    internal void UseArt(DaggerfallArtReplacements art, Func<bool> refreshed)
    {
        _art = art;
        _drawRefreshed = refreshed;
    }

    /// <summary>Layers ground by these settings. A heightfield holds its materials, so its owner lets go of them first.</summary>
    internal void UseBlend(IAuthoredContentService authored, DaggerfallGroundTuning ground, DaggerfallGroundBlendSettings settings)
    {
        _authored = authored;
        _ground = ground;
        _blend = settings;
        _retiredLayers.AddRange(_groundLayers.Values);
        _groundLayers.Clear();
    }

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

    /// <summary>Redraws every resident cell's nature in the art now drawn: a batch shares one atlas, so its record's art change is a new batch.</summary>
    private void RedrawNature()
    {
        foreach ((DaggerfallExteriorCellId cell, NatureCell nature) in _natureCells.ToArray())
        {
            NatureCell redrawn = CreateNatureCell(cell, nature.Source);
            _retiredNature.AddRange(nature.Batches);
            _natureCells[cell] = redrawn;
        }
    }

    /// <summary>
    /// Appends the current terrain facts to the complete product snapshot assembled by the
    /// presentation owner. Duplicate object identities are rejected before Engine publication.
    /// </summary>
    internal void AppendFacts(List<AppearanceFact> baseFacts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(baseFacts);
        if (_refreshedDrawn != _drawRefreshed())
        {
            _refreshedDrawn = !_refreshedDrawn;
            RedrawNature();
        }
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

    /// <summary>The stable object identity of one cell's batch of nature sprites drawing one sprite record.</summary>
    internal static ulong NatureBatchObjectId(DaggerfallExteriorCellId cell, int spriteRecord) =>
        DaggerfallPresentationObjectIds.NatureBatch(cell.X, cell.Y, spriteRecord);

    /// <summary>
    /// Reconciles source nature placements against the current environment variants. Sprite resources are opened from the
    /// selected profile's generated media closure, so a climate or season change replaces retained Engine appearances
    /// together with the product facts that select them.
    /// </summary>
    internal void Reconcile(IEnumerable<DaggerfallExteriorCellId> residentCells, DaggerfallExteriorWorldOrigin origin, DaggerfallExteriorEnvironment environment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(residentCells);
        ArgumentNullException.ThrowIfNull(environment);

        DaggerfallExteriorCellId[] desired = residentCells
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
        if (desired.Distinct().Count() != desired.Length)
            throw new ArgumentException("Exterior visual residency contains a duplicate cell.", nameof(residentCells));

        HashSet<DaggerfallExteriorCellId> desiredSet = desired.ToHashSet();
        List<(DaggerfallExteriorCellId Cell, NatureCell Nature)> natureAdditions = [];
        try
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
        catch
        {
            foreach ((DaggerfallExteriorCellId _, NatureCell nature) in natureAdditions)
                foreach (NatureBatch batch in nature.Batches)
                    DisposeNatureBatch(batch);
            throw;
        }

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

        _origin = origin;
        _hasOrigin = true;
    }

    /// <summary>Marks all resident nature for removal from the next complete snapshot.</summary>
    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (NatureCell nature in _natureCells.Values)
            _retiredNature.AddRange(nature.Batches);
        _natureCells.Clear();
        _hasOrigin = false;
    }

    /// <summary>Returns the nature facts that the next complete snapshot must include.</summary>
    internal AppearanceFact[] BuildFacts()
    {
        if (!_hasOrigin) return [];

        List<AppearanceFact> facts = [];
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
        foreach (NatureCell nature in _natureCells.Values.Reverse())
            foreach (NatureBatch batch in nature.Batches.Reverse())
                DisposeNatureBatch(batch, ref failures);
        foreach (NatureBatch batch in _retiredNature.AsEnumerable().Reverse())
            DisposeNatureBatch(batch, ref failures);
        _natureCells.Clear();
        _retiredNature.Clear();
        foreach (NatureSprite sprite in _natureSprites.Values.Reverse())
            DisposeNatureSprite(sprite, ref failures);
        _natureSprites.Clear();
        foreach (DaggerfallGroundLayers layers in _groundLayers.Values.Concat(_retiredLayers))
        {
            try { layers.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    /// <summary>The layers of a ground archive, in the art set's surfaces where it has them and the classic's where it doesn't.</summary>
    internal DaggerfallGroundLayers GroundLayers(int archive)
    {
        if (_groundLayers.TryGetValue(archive, out DaggerfallGroundLayers? layers)) return layers;
        if (_authored is null || _profile is null) throw new InvalidOperationException("Ground layers need a profile and their blend.");
        List<DaggerfallArtSurface> surfaces = [];
        foreach ((int layerArchive, int record) in DaggerfallGroundLayers.Records(archive))
        {
            if (!_profile.TerrainTextures.TryGetValue((layerArchive, record), out NormalizedTerrainTexture? texture))
                throw new InvalidOperationException($"Ground archive {archive} has no texture for layer {layerArchive}/{record}.");
            surfaces.Add(_art?.Surface(texture.TexturePath)
                ?? new(new(texture.TexturePath, texture.TextureSha256), DaggerfallGroundLayers.ClassicSide, DaggerfallGroundLayers.ClassicSide));
        }

        layers = new(_authored, _graphics, _content, archive, surfaces, _blend);
        _groundLayers.Add(archive, layers);
        return layers;
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
        key = new(placement.SpriteArchive, placement.SpriteRecord, Art(placement)?.File.Path ?? sprite.TexturePath);
        return true;
    }

    /// <summary>The refreshed art a nature placement draws now, if any.</summary>
    private DaggerfallArtFlat? Art(DaggerfallExteriorNaturePlacement placement) =>
        _refreshedDrawn && _art?.Flats.GetValueOrDefault((placement.SpriteArchive, placement.SpriteRecord)) is { } art ? art : null;

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
    /// terrain's nature in one batch per atlas. Each normalized record is its own atlas with its own world size, which
    /// a batch shares, so the record is the batch: every sprite of it stands on its cell-local placement by its foot,
    /// its record's initial frame and size, the tuned facing and the default depth, in the lit sprite material leaning in the
    /// wind as flora does.
    /// </summary>
    private NatureCell CreateNatureCell(DaggerfallExteriorCellId cell, IReadOnlyList<DaggerfallExteriorNaturePlacement> placements)
    {
        List<NatureBatch> batches = [];
        // Refreshed nature grows in the flora field instead (DaggerfallFloraField); the donor's placement is classic art's.
        if (_refreshedDrawn) return new(placements, _profile, []);
        try
        {
            foreach (IGrouping<(int Archive, int Record), DaggerfallExteriorNaturePlacement> group in placements
                .GroupBy(placement => (Archive: placement.SpriteArchive, Record: placement.SpriteRecord))
                .OrderBy(group => group.Key.Record).ThenBy(group => group.Key.Archive))
            {
                (NatureSpriteKey key, NormalizedBillboardSprite sprite) = ResolveNatureSprite(group.First());
                DaggerfallArtFlat? art = Art(group.First());
                NatureSprite admitted = GetNatureSprite(key, sprite, art);
                (Vector2 size, Vector2 pivot) = art?.Placed(sprite.Size, NatureFoot) ?? (sprite.Size, NatureFoot);
                SpriteBatchInstance[] instances = [.. group.Select(placement =>
                    new SpriteBatchInstance(placement.LocalPosition, 1F, sprite.InitialFrameId))];
                ulong objectId = NatureBatchObjectId(cell, key.Record);
                if (batches.Any(batch => batch.ObjectId == objectId))
                    throw new InvalidOperationException($"Exterior cell ({cell.X},{cell.Y}) draws nature record {key.Record} from more than one archive.");
                Appearance appearance = _graphics.CreateSpriteBatch(new SpriteBatchRequest(
                    admitted.Atlas,
                    instances,
                    pivot,
                    size,
                    _flats.Nature,
                    0,
                    SpriteDepthPolicy.Default,
                    new Color(1F, 1F, 1F, 1F),
                    _flats.Flora(DaggerfallSiteAppearance.LitSprite)));
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

    /// <summary>
    /// What a nature record draws as flora: its refreshed art where the set has it and its classic sprite otherwise, the
    /// initial frame's place in its image, and the size and pivot it stands at; null where the profile publishes no sprite.
    /// </summary>
    internal DaggerfallFloraLook? FloraLook(int archive, int record)
    {
        if (_profile is null || !_profile.BillboardSprites.TryGetValue((archive, record), out NormalizedBillboardSprite? sprite)) return null;
        int frame = sprite.Frames.Select((value, index) => (value, index)).First(pair => pair.value.Id == sprite.InitialFrameId).index;
        if (_art?.Flats.GetValueOrDefault((archive, record)) is not { } art)
        {
            NormalizedAtlasFrame classic = sprite.Frames[frame];
            return new(new(sprite.TexturePath, sprite.TextureSha256), Uv(classic.X, classic.Y, sprite.AtlasWidth, sprite.AtlasHeight),
                Uv(classic.X + classic.Width, classic.Y + classic.Height, sprite.AtlasWidth, sprite.AtlasHeight), sprite.Size, NatureFoot);
        }

        DaggerfallArtRect rect = art.Frames[frame];
        (Vector2 size, Vector2 pivot) = art.Placed(sprite.Size, NatureFoot);
        return new(art.File, Uv(rect.X, rect.Y, art.Width, art.Height), Uv(rect.X + rect.Width, rect.Y + rect.Height, art.Width, art.Height), size, pivot)
        {
            Tree = _art.Trees.GetValueOrDefault((archive, record)),
            TreeSnow = _art.TreeSnow,
        };
    }

    private static Vector2 Uv(double x, double y, int width, int height) => new((float)(x / width), (float)(y / height));

    /// <summary>A nature placement is a point on the ground, so the sprite hangs from its foot, not the published centre.</summary>
    private static readonly Vector2 NatureFoot = new(.5F, 0F);

    private NatureSprite GetNatureSprite(NatureSpriteKey key, NormalizedBillboardSprite sprite, DaggerfallArtFlat? art)
    {
        if (_natureSprites.TryGetValue(key, out NatureSprite? existing)) return existing;

        RenderResource? resource = null;
        SpriteAtlas? atlas = null;
        try
        {
            resource = art is null ? _graphics.OpenTexture(_content, sprite.TexturePath, sprite.TextureSha256).Handle
                : _graphics.OpenTexture(_content, art.File.Path, art.File.Sha256, TextureFilter.Nearest).Handle;
            SpriteAtlasFrame[] frames = art is null
                ? SpriteAtlasAdapter.ToAtlasFrames(sprite.AtlasWidth, sprite.AtlasHeight, sprite.Frames.Select(frame => new NormalizedSpriteFrame(
                    frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray())
                : SpriteAtlasAdapter.ToAtlasFrames(art.Width, art.Height, art.Frames.Select((frame, index) => new NormalizedSpriteFrame(
                    sprite.Frames[index].Id, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height)).ToArray());
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

    private void DisposeRetired()
    {
        List<Exception>? failures = null;
        foreach (NatureBatch batch in _retiredNature.AsEnumerable().Reverse())
            DisposeNatureBatch(batch, ref failures);
        _retiredNature.Clear();
        foreach (DaggerfallGroundLayers layers in _retiredLayers)
        {
            try { layers.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        _retiredLayers.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
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

    private readonly record struct NatureSpriteKey(int Archive, int Record, string Path);
    private sealed record NatureSprite(NatureSpriteKey Key, RenderResource Resource, SpriteAtlas Atlas);
    private sealed record NatureBatch(ulong ObjectId, NatureSpriteKey Key, Appearance Appearance, int Instances);
    private sealed record NatureCell(IReadOnlyList<DaggerfallExteriorNaturePlacement> Source, DaggerfallSiteProfile? Profile, NatureBatch[] Batches);
}

/// <summary>One nature record as the flora field draws it: an image, the frame's corners in it (0 to 1, top-down), and its world size and pivot.</summary>
internal sealed record DaggerfallFloraLook(DaggerfallArtFile Image, Vector2 UvMin, Vector2 UvMax, Vector2 Size, Vector2 Pivot)
{
    /// <summary>The tree mesh that may stand in for the sprite, where the refreshed set has one.</summary>
    internal DaggerfallTreeArt? Tree { get; init; }

    /// <summary>The shader that lays a winter tree's snow on its leaves.</summary>
    internal DaggerfallArtFile? TreeSnow { get; init; }
}
