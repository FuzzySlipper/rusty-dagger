using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.VegetationTiles;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Refreshed nature, grown by the Engine over near ground (<see cref="DaggerfallHorizonGround.SampleFlora"/>): each nature
/// record is a sprite layer that grows on its own climate's ground, as often as the others, where the ground's look keeps the
/// donor's chance, and shrinks away before its reach, so nothing pops at the edge. Its units are the map's, (0, 0) at map
/// pixel (0, 0), so the same ground grows the same plants whichever way the player arrived; it stands in near ground's
/// session and is made again when that frame moves. Nature a location places (<see cref="Place"/>) grows here too where its
/// record grows as a mesh, as its record's own copies. Classic art keeps the donor's placement in the walked cells instead.
/// </summary>
internal sealed class DaggerfallFloraField : IDisposable
{
    /// <summary>Flora samples the ground at the walked spacing, on which the walked surface is straight between vertices.</summary>
    private const double CellMetres = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
    /// <summary>A layer's copies on a tile are one patch the renderer selects from, so larger tiles mean fewer of them.</summary>
    private const uint TileCells = 32;
    /// <summary>The records of a nature archive, which the donor chooses among alike.</summary>
    private const int Records = 31;
    /// <summary>The donor's chance is a tile's, and a tile is the walked spacing square.</summary>
    private const float TileArea = DaggerfallTerrainSurfaceBuilder.SampleSpacing * DaggerfallTerrainSurfaceBuilder.SampleSpacing;
    /// <summary>A layer may hold this many times the copies its density promises, before its nearest tiles are kept.</summary>
    private const float Headroom = 1.5f;

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonGround _ground;
    private readonly DaggerfallFloraTuning _tuning;
    private readonly DaggerfallFloraArt _art;
    /// <summary>Each nature archive's share of the whole's density, where its climate's tuning names one.</summary>
    private readonly IReadOnlyDictionary<int, float> _archiveDensity;
    private readonly (float TownShare, double TownRamp)? _grass;
    private bool _treeMeshes;
    /// <summary>Whether mesh records stand as bodies near the player; a developer may turn them off to compare their cost.</summary>
    private bool _bodies = true;
    /// <summary>The whole's density for this run, which a developer may change.</summary>
    private float _density;
    private Func<int, int, DaggerfallFloraLook?>? _looks;
    private readonly Dictionary<uint, DaggerfallFloraAsset> _layers = [];
    private readonly HashSet<int> _grown = [];
    /// <summary>Layers of another kind growing on a climate's ground, set again whenever the field is made.</summary>
    private readonly Dictionary<uint, VegetationLayerRequest> _extras = [];
    private VegetationField? _field;
    private VegetationStreamer? _streamer;
    private (int MapX, int MapY) _frame;
    private bool _seen;
    private int _winter = -1;
    /// <summary>The nature archives the workers have found ground of, a bit each from the first.</summary>
    private int _found;
    /// <summary>
    /// The nature the drawn locations place, and its summer and winter archives, a bit each from the first: the season's
    /// grow wherever the ground is.
    /// </summary>
    private IReadOnlyList<DaggerfallPlacedNature> _placed = [];
    private (int Summer, int Winter) _placedArchives;
    /// <summary>
    /// The placed nature whose records grow as meshes, as copies of their layers: what the workers add to each tile. Set
    /// again when the placed nature or the layers change.
    /// </summary>
    private PlacedCopy[] _copies = [];
    private bool _copiesStale;
    private bool _disposed;

    /// <param name="grass">
    /// For a field of grass (ground cover's tufts) in place of the donor's flora: the share of its cover a location's
    /// flattened ground keeps, and how far beyond it the cover rises back to full. Such a field's cover is the ground's
    /// grass, in towns too, rather than the donor's chance, which keeps out of every clearing.
    /// </param>
    internal DaggerfallFloraField(IEngineContext engine, DaggerfallHorizonGround ground, DaggerfallFloraTuning tuning, DaggerfallFlatsTuning flats,
        IReadOnlyDictionary<int, float> archiveDensity, (float TownShare, double TownRamp)? grass = null)
    {
        _grass = grass;
        _treeMeshes = tuning.TreeMeshes;
        _archiveDensity = archiveDensity ?? throw new ArgumentNullException(nameof(archiveDensity));
        _density = tuning.Density;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _art = new(engine, flats ?? throw new ArgumentNullException(nameof(flats)));
    }

    /// <summary>
    /// Grows flora about a camera at <paramref name="local"/> in near ground's frame, whose (0, 0) stands at map pixel
    /// <paramref name="frame"/>. A climate's records are set as layers once the workers first find its ground.
    /// </summary>
    internal void Show(SpatialSession session, (int MapX, int MapY) frame, Vector3 local, bool winter, Func<int, int, DaggerfallFloraLook?> looks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _looks = looks;
        if (_field is not null && _frame != frame) Drop();
        if (_field is null)
        {
            _frame = frame;
            _winter = winter ? 1 : 0;
            // The field's (0, 0) is the map's: the Engine places copies by a lattice hashed in field units, so a field whose
            // origin were the arrival's would grow other trees on the same ground after a different arrival.
            _field = _engine.Vegetation.Create(new VegetationFieldCreateRequest(session, TileCells * CellMetres, TileCells, Focus(local))
                with { Origin = FieldOrigin(_frame) });
            _streamer = new VegetationStreamer(_engine, _field, new Sampler(this));
            _seen = true;
            foreach (VegetationLayerRequest extra in _extras.Values) _engine.Vegetation.SetLayer(extra with { Field = _field });
        }

        if (!_seen)
        {
            _seen = true;
            _engine.Vegetation.SetVisible(new(_field, true));
        }

        if ((winter ? 1 : 0) != Volatile.Read(ref _winter))
        {
            // The season swaps a climate's records for its winter set, which is another ground class: every tile is sampled again.
            Volatile.Write(ref _winter, winter ? 1 : 0);
            _engine.Vegetation.Invalidate(new(_field, double.MinValue / 4, double.MinValue / 4, double.MaxValue / 4, double.MaxValue / 4));
        }

        _engine.Vegetation.SetFocus(new(_field, Focus(local)));
        // A field asks for tiles only within its layers' reach, so the climate underfoot grows at once; the workers find the rest.
        int underfoot = _ground.NatureArchive(local.X + (frame.MapX * DaggerfallExteriorCellResidency.CellSize),
            local.Z + (frame.MapY * DaggerfallExteriorCellResidency.CellSize), winter) - DaggerfallNatureArchives.First;
        Interlocked.Or(ref _found, 1 << underfoot);
        int found = Volatile.Read(ref _found) | (winter ? _placedArchives.Winter : _placedArchives.Summer);
        for (int index = 0; index < DaggerfallNatureArchives.Count; index++)
            if ((found & (1 << index)) != 0 && _grown.Add(index)) Grow(DaggerfallNatureArchives.First + index, looks);
        if (_copiesStale) Copy();
        _streamer!.Update();
    }

    /// <summary>
    /// Grows the nature the drawn locations place where they place it, as its season's record: one that grows as a mesh
    /// is a copy of its layer there, with the layer's card, shade, wind and snow, and the location draws the rest
    /// (<see cref="GrowsPlaced"/>). The season's archives grow whether or not the workers have found their ground.
    /// </summary>
    internal void Place(IReadOnlyList<DaggerfallPlacedNature> placed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _placed = placed ?? throw new ArgumentNullException(nameof(placed));
        _placedArchives = placed.Aggregate((Summer: 0, Winter: 0),
            (bits, nature) => (bits.Summer | Bit(nature.Summer.Archive), bits.Winter | Bit(nature.Winter.Archive)));
        _copiesStale = true;
    }

    private static int Bit(int archive) => DaggerfallNatureArchives.Contains(archive)
        ? 1 << (archive - DaggerfallNatureArchives.First)
        : throw new ArgumentException($"Placed nature names archive {archive}, which is no nature archive.");

    /// <summary>
    /// Whether placed nature of a record grows in the field now, so its location leaves it out: the field is shown and grows
    /// the record as a mesh.
    /// </summary>
    internal bool GrowsPlaced((int Archive, int Record) record) =>
        _field is not null && _seen && _layers.TryGetValue(Layer(record.Archive, record.Record), out DaggerfallFloraAsset? grown) && grown.Mesh;

    /// <summary>
    /// Sets the copies the workers add for placed nature from the layers grown now, with each mesh layer's budget, and has
    /// each tile whose copies changed sampled again, and no other: a town arriving beside another resamples its own ground.
    /// </summary>
    private void Copy()
    {
        _copiesStale = false;
        foreach (int index in _grown)
        for (int record = 1; record <= Records; record++)
        {
            uint layer = Layer(DaggerfallNatureArchives.First + index, record);
            if (_layers.TryGetValue(layer, out DaggerfallFloraAsset? grown) && grown.Mesh) Set(DaggerfallNatureArchives.First + index, layer, grown);
        }
        PlacedCopy[] old = _copies;
        PlacedCopy[] copies = [.. _placed
            .Select(nature => new PlacedCopy(nature.X, nature.Height, nature.Z, MeshLayer(nature.Summer), MeshLayer(nature.Winter), Yaw(nature)))
            .Where(copy => copy.Summer is not null || copy.Winter is not null)];
        Volatile.Write(ref _copies, copies);
        const double tile = TileCells * CellMetres;
        foreach (PlacedCopy moved in old.Except(copies).Concat(copies.Except(old)).DistinctBy(copy => (Math.Floor(copy.X / tile), Math.Floor(copy.Z / tile))))
            _engine.Vegetation.Invalidate(new(_field!, moved.X, moved.Z, moved.X, moved.Z));
    }

    private uint? MeshLayer((int Archive, int Record) record) =>
        _layers.TryGetValue(Layer(record.Archive, record.Record), out DaggerfallFloraAsset? grown) && grown.Mesh ? Layer(record.Archive, record.Record) : null;

    /// <summary>
    /// How far placed nature turns about the vertical, as a wild copy turns its own way: a hash of the metre it stands in,
    /// so it stands the same way on every visit, whatever frame its point was measured in.
    /// </summary>
    private static float Yaw(DaggerfallPlacedNature nature)
    {
        ulong key = unchecked(((ulong)(long)Math.Round(nature.X) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(long)Math.Round(nature.Z) * 0xC2B2AE3D27D4EB4FUL));
        key = unchecked((key ^ (key >> 33)) * 0xFF51AFD7ED558CCDUL);
        key ^= key >> 33;
        return (float)((key >> 11) * (1.0 / (1UL << 53)) * Math.Tau);
    }

    /// <summary>One piece of placed nature as the workers copy it: its field point, its summer and winter layers (none where that record is no mesh) and its turn.</summary>
    private readonly record struct PlacedCopy(double X, float Height, double Z, uint? Summer, uint? Winter, float Yaw);

    /// <summary>
    /// Where the field's (0, 0, 0) stands in near ground's frame at map pixel <paramref name="frame"/>: at the map's own
    /// (0, 0), so a field unit is a map metre whatever the frame.
    /// </summary>
    internal static Vector3 FieldOrigin((int MapX, int MapY) frame) =>
        new((float)-(frame.MapX * DaggerfallExteriorCellResidency.CellSize), 0, (float)-(frame.MapY * DaggerfallExteriorCellResidency.CellSize));

    /// <summary>A point <paramref name="local"/> in near ground's frame at map pixel <paramref name="frame"/>, as a point of the field: its map point.</summary>
    internal static (double X, double Y, double Z) FieldPoint((int MapX, int MapY) frame, Vector3 local) =>
        (local.X + (frame.MapX * DaggerfallExteriorCellResidency.CellSize), local.Y, local.Z + (frame.MapY * DaggerfallExteriorCellResidency.CellSize));

    private VegetationFocus Focus(Vector3 local)
    {
        (double x, double y, double z) = FieldPoint(_frame, local);
        return VegetationFocus.At(x, y, z);
    }

    /// <summary>Stops drawing flora, keeping its tiles for the next time outdoors.</summary>
    internal void Hide()
    {
        if (_field is null || !_seen) return;
        _seen = false;
        _engine.Vegetation.SetVisible(new(_field, false));
    }

    /// <summary>
    /// Lets go of the field and its layers' sprites; the next showing grows them anew. The Engine removes a field's copies
    /// ahead of the call's other work, so its sprites can go in the same call.
    /// </summary>
    internal void Drop()
    {
        _streamer?.Dispose(); _streamer = null;
        _field?.Dispose(); _field = null;
        foreach (DaggerfallFloraAsset grown in _layers.Values)
            grown.Dispose();
        _art.ReleaseSnowShader();
        _layers.Clear();
        _grown.Clear();
        Volatile.Write(ref _found, 0);
        Volatile.Write(ref _copies, []);
        _copiesStale = true;
    }

    /// <summary>
    /// Grows every climate at <paramref name="density"/> times the donor's count for this run, in place of the tuned
    /// whole, each climate keeping its own share; the layers are set again with the sprites they have, and grow as calls allow.
    /// </summary>
    internal void UseDensity(float density)
    {
        _density = density;
        if (_field is null || _looks is null) return;
        foreach (int index in _grown) Grow(DaggerfallNatureArchives.First + index, _looks);
    }

    /// <summary>
    /// Sets an archive's records as layers, each its own sprite growing on the archive's ground alone. A layer set again
    /// keeps its sprite, since its copies draw it until the new ones are placed.
    /// </summary>
    private void Grow(int archive, Func<int, int, DaggerfallFloraLook?> looks)
    {
        for (int record = 1; record <= Records; record++)
        {
            uint layer = Layer(archive, record);
            if (!_layers.TryGetValue(layer, out DaggerfallFloraAsset? grown))
            {
                if (looks(archive, record) is not { } look) continue;
                grown = _treeMeshes && look.Tree is { } tree ? _art.Tree(tree, look) : _art.Sprite(look);
                _layers.Add(layer, grown);
                _copiesStale |= grown.Mesh;
            }

            Set(archive, layer, grown);
            // Far enough out to be small on screen, a tree mesh is a card the renderer bakes of it, as cheap as its sprite.
            if (grown.Mesh) _engine.Vegetation.SetLayerDetail(new VegetationLayerDetailRequest(_field!, layer,
                new[] { VegetationLod.ImpostorOf(grown.Look, Math.Max(_tuning.TreeImpostorHeights * grown.Height, NearestImpostor)) }, ImpostorBand,
                ReadOnlyMemory<VegetationThinningPoint>.Empty, 1));
        }
    }

    /// <summary>Sets a record's layer as it grows now; the Engine keeps a layer set again unchanged as it is.</summary>
    private void Set(int archive, uint layer, DaggerfallFloraAsset grown)
    {
        float density = _density * _archiveDensity.GetValueOrDefault(archive, 1) / TileArea / Records;
        float reach = _tuning.ReachOf(grown.Height);
        _engine.Vegetation.SetLayer(new VegetationLayerRequest(_field!, layer, grown.Look, default, density, reach) with
        {
            Grounds = VegetationLayerRequest.Ground((uint)(archive - DaggerfallNatureArchives.First)),
            SlopeLimitDegrees = _tuning.SlopeLimitDegrees,
            // The Engine wants a fade short of the reach; a short record's reach is all fade otherwise.
            Fade = Math.Min(_tuning.Fade, reach / 2),
            Seed = layer,
            // Placed copies count against the budget as grown ones do, so they are added to it, however thin the wild growth.
            MaximumInstances = (uint)Math.Min(uint.MaxValue, Math.Ceiling(MathF.PI * reach * reach * density * Headroom)
                + (grown.Mesh ? _placed.Count(nature => Layer(nature.Summer.Archive, nature.Summer.Record) == layer || Layer(nature.Winter.Archive, nature.Winter.Record) == layer) : 0)),
            // A tree in the round casts its shade; a billboard can cast none.
            CastsShadows = grown.Mesh,
            // A tree's trunk or a rock's bulk stops bodies near the player, so walkers path round it; a sprite has no body.
            ColliderRadius = _bodies ? grown.Body?.Radius ?? 0 : 0,
            ColliderHeight = _bodies ? grown.Body?.Height ?? 0 : 0,
            ColliderReach = _bodies && grown.Body is not null ? _tuning.ColliderReach : 0,
        });
    }

    /// <summary>How far a tree mesh and its card overlap, dithered, as one gives way to the other.</summary>
    private const float ImpostorBand = 6;
    /// <summary>
    /// The nearest a card takes over. The Engine refuses a level whose crossfade would begin at the eye (its start within
    /// a band of it), and the desert's smallest plants, 0.15 m tall, would start theirs inside it at their heights; such a
    /// mesh is drawn whole within a band of the eye and gives way over the next.
    /// </summary>
    private const float NearestImpostor = 2 * ImpostorBand;

    /// <summary>Has mesh records stand as bodies, or none, for a developer to compare their cost; each layer is set again as it grows.</summary>
    internal void UseBodies(bool bodies)
    {
        if (_bodies == bodies) return;
        _bodies = bodies;
        if (_field is null) return;
        foreach ((uint layer, DaggerfallFloraAsset grown) in _layers)
            if (grown.Mesh) Set((int)(layer / 32), layer, grown);
    }

    /// <summary>Grows records with tree meshes as the meshes, or as their sprites, for a developer to compare them.</summary>
    internal void UseTreeMeshes(bool meshes)
    {
        if (_treeMeshes == meshes) return;
        _treeMeshes = meshes;
        Drop();
    }

    /// <summary>What the field holds and draws, for looking at by eye.</summary>
    internal string Readout()
    {
        if (_field is null) return "not grown";
        VegetationReadout read = _engine.Vegetation.Read(_field);
        string archives = string.Join(",", _grown.Order().Select(index => DaggerfallNatureArchives.First + index));
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{(read.Visible ? "shown" : "hidden")}, archives [{archives}] in {_layers.Count} layers, {read.Tiles} tiles ({read.PendingTiles} waiting), {read.Instances} copies in {read.Patches} patches, {read.OverBudget} over budget, {read.Colliders} standing as bodies");
    }

    /// <summary>
    /// Grows another kind on a nature archive's ground, by the same cover, each copy tinted between two colours; the caller
    /// keeps its look while it grows.
    /// </summary>
    internal void GrowOn(int natureArchive, uint layer, Appearance look, float density, float reach, float fade, float scaleMin, float scaleMax,
        Vector3 tintLow, Vector3 tintHigh)
    {
        VegetationLayerRequest request = new VegetationLayerRequest(_field!, layer, look, default, density, reach) with
        {
            Grounds = VegetationLayerRequest.Ground((uint)(natureArchive - DaggerfallNatureArchives.First)),
            TintLow = tintLow,
            TintHigh = tintHigh,
            SlopeLimitDegrees = _tuning.SlopeLimitDegrees,
            Fade = fade,
            Seed = layer,
            ScaleMin = scaleMin,
            ScaleMax = scaleMax,
            MaximumInstances = (uint)Math.Min(uint.MaxValue, Math.Ceiling(MathF.PI * reach * reach * density * Headroom)),
            CastsShadows = false,
        };
        _extras[layer] = request;
        if (_field is not null) _engine.Vegetation.SetLayer(request);
    }

    /// <summary>Stops growing a kind <see cref="GrowOn"/> set.</summary>
    internal void StopGrowing(uint layer)
    {
        if (_extras.Remove(layer) && _field is not null) _engine.Vegetation.RemoveLayer(new(_field, layer));
    }

    /// <summary>A record's layer: its archive and record, so no two climates share one.</summary>
    internal static uint Layer(int archive, int record) => (uint)((archive * 32) + record);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Drop();
    }

    /// <summary>Samples flora's ground on the streamer's workers, noting each climate's archive it finds, and copies the placed nature standing on it.</summary>
    private sealed class Sampler(DaggerfallFloraField owner) : IVegetationSampler
    {
        public void Sample(VegetationTileRequest tile, VegetationTileSamples samples)
        {
            bool winter = Volatile.Read(ref owner._winter) == 1;
            owner._ground.SampleFlora(tile, samples, winter, owner._tuning, owner._grass);
            int found = 0;
            foreach (uint ground in samples.Grounds ?? []) found |= 1 << (int)ground;
            Interlocked.Or(ref owner._found, found);
            double size = tile.TileCells * tile.CellSize;
            foreach (PlacedCopy copy in Volatile.Read(ref owner._copies))
            {
                double x = copy.X - tile.OriginX, z = copy.Z - tile.OriginZ;
                if (x >= 0 && x < size && z >= 0 && z < size && (winter ? copy.Winter : copy.Summer) is uint layer)
                    samples.Records.Add(new VegetationRecord(layer, new Vector3((float)x, copy.Height, (float)z), copy.Yaw, 1));
            }
        }
    }
}

/// <summary>
/// Nature a location places, as the flora field grows it: its foot as a point of the field (map metres, and near ground's
/// height), and the record it is in summer and in winter.
/// </summary>
internal readonly record struct DaggerfallPlacedNature(double X, float Height, double Z, (int Archive, int Record) Summer, (int Archive, int Record) Winter);
