using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.VegetationTiles;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Refreshed nature, grown by the Engine over near ground (<see cref="DaggerfallHorizonGround.SampleFlora"/>): each nature
/// record is a sprite layer that grows on its own climate's ground, as often as the others, where the ground's look keeps the
/// donor's chance, and shrinks away before its reach, so nothing pops at the edge. It stands in near ground's frame and is
/// made again when that frame moves. Classic art keeps the donor's placement in the walked cells instead.
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
    private readonly DaggerfallFlatsTuning _flats;
    /// <summary>Each nature archive's share of the whole's density, where its climate's tuning names one.</summary>
    private readonly IReadOnlyDictionary<int, float> _archiveDensity;
    /// <summary>The whole's density for this run, which a developer may change.</summary>
    private float _density;
    private Func<int, int, DaggerfallFloraLook?>? _looks;
    private readonly Dictionary<uint, Grown> _layers = [];
    private readonly HashSet<int> _grown = [];
    private VegetationField? _field;
    private VegetationStreamer? _streamer;
    private (int MapX, int MapY) _frame;
    private bool _seen;
    private int _winter = -1;
    /// <summary>The nature archives the workers have found ground of, a bit each from the first.</summary>
    private int _found;
    private bool _disposed;

    internal DaggerfallFloraField(IEngineContext engine, DaggerfallHorizonGround ground, DaggerfallFloraTuning tuning, DaggerfallFlatsTuning flats,
        IReadOnlyDictionary<int, float> archiveDensity)
    {
        _archiveDensity = archiveDensity ?? throw new ArgumentNullException(nameof(archiveDensity));
        _density = tuning.Density;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _flats = flats ?? throw new ArgumentNullException(nameof(flats));
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
            double offsetX = frame.MapX * DaggerfallExteriorCellResidency.CellSize, offsetZ = frame.MapY * DaggerfallExteriorCellResidency.CellSize;
            _field = _engine.Vegetation.Create(new VegetationFieldCreateRequest(session, TileCells * CellMetres, TileCells,
                VegetationFocus.At(local.X, local.Y, local.Z)));
            _streamer = new VegetationStreamer(_engine, _field, new Sampler(this, offsetX, offsetZ));
            _seen = true;
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

        _engine.Vegetation.SetFocus(new(_field, VegetationFocus.At(local.X, local.Y, local.Z)));
        // A field asks for tiles only within its layers' reach, so the climate underfoot grows at once; the workers find the rest.
        int underfoot = _ground.NatureArchive(local.X + (frame.MapX * DaggerfallExteriorCellResidency.CellSize),
            local.Z + (frame.MapY * DaggerfallExteriorCellResidency.CellSize), winter) - DaggerfallExteriorEnvironment.FirstNatureArchive;
        Interlocked.Or(ref _found, 1 << underfoot);
        int found = Volatile.Read(ref _found);
        for (int index = 0; index < DaggerfallExteriorEnvironment.NatureArchiveCount; index++)
            if ((found & (1 << index)) != 0 && _grown.Add(index)) Grow(DaggerfallExteriorEnvironment.FirstNatureArchive + index, looks);
        _streamer!.Update();
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
        foreach (Grown grown in _layers.Values)
        {
            grown.Sprite.Dispose();
            grown.Image.Dispose();
        }
        _layers.Clear();
        _grown.Clear();
        Volatile.Write(ref _found, 0);
    }

    /// <summary>
    /// Grows every climate at <paramref name="density"/> times the donor's count for this run, in place of the tuned
    /// whole, each climate keeping its own share; the layers are set again with the sprites they have, and grow as calls allow.
    /// </summary>
    internal void UseDensity(float density)
    {
        _density = density;
        if (_field is null || _looks is null) return;
        foreach (int index in _grown) Grow(DaggerfallExteriorEnvironment.FirstNatureArchive + index, _looks);
    }

    /// <summary>
    /// Sets an archive's records as layers, each its own sprite growing on the archive's ground alone. A layer set again
    /// keeps its sprite, since its copies draw it until the new ones are placed.
    /// </summary>
    private void Grow(int archive, Func<int, int, DaggerfallFloraLook?> looks)
    {
        float density = _density * _archiveDensity.GetValueOrDefault(archive, 1) / TileArea / Records;
        for (int record = 1; record <= Records; record++)
        {
            uint layer = Layer(archive, record);
            if (!_layers.TryGetValue(layer, out Grown? grown))
            {
                if (looks(archive, record) is not { } look) continue;
                RenderResource image = _engine.Graphics.OpenTexture(_engine.Content, look.Image.Path, look.Image.Sha256, TextureFilter.NearestMipmapped).Handle;
                try
                {
                    grown = new(new VegetationSprite(_engine, new RenderResourceReference(image), look.UvMin, look.UvMax, look.Size, look.Pivot,
                        _flats.Flora(DaggerfallSiteAppearance.LitSprite), _flats.Nature), image, look.Size.Y);
                }
                catch
                {
                    image.Dispose();
                    throw;
                }

                _layers.Add(layer, grown);
            }

            float reach = _tuning.ReachOf(grown.Height);
            _engine.Vegetation.SetLayer(new VegetationLayerRequest(_field!, layer, grown.Sprite.Look, default, density, reach) with
            {
                Grounds = VegetationLayerRequest.Ground((uint)(archive - DaggerfallExteriorEnvironment.FirstNatureArchive)),
                SlopeLimitDegrees = _tuning.SlopeLimitDegrees,
                // The Engine wants a fade short of the reach; a short record's reach is all fade otherwise.
                Fade = Math.Min(_tuning.Fade, reach / 2),
                Seed = layer,
                MaximumInstances = (uint)Math.Min(uint.MaxValue, Math.Ceiling(MathF.PI * reach * reach * density * Headroom)),
                CastsShadows = false,
            });
        }
    }

    /// <summary>A record's sprite as a layer grows it, the image it draws from, and its height, which sets its reach.</summary>
    private sealed record Grown(VegetationSprite Sprite, RenderResource Image, float Height);

    /// <summary>What the field holds and draws, for looking at by eye.</summary>
    internal string Readout()
    {
        if (_field is null) return "not grown";
        VegetationReadout read = _engine.Vegetation.Read(_field);
        string archives = string.Join(",", _grown.Order().Select(index => DaggerfallExteriorEnvironment.FirstNatureArchive + index));
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{(read.Visible ? "shown" : "hidden")}, archives [{archives}] in {_layers.Count} layers, {read.Tiles} tiles ({read.PendingTiles} waiting), {read.Instances} copies in {read.Patches} patches, {read.OverBudget} over budget");
    }

    /// <summary>A record's layer: its archive and record, so no two climates share one.</summary>
    internal static uint Layer(int archive, int record) => (uint)((archive * 32) + record);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Drop();
    }

    /// <summary>Samples flora's ground on the streamer's workers, noting each climate's archive it finds.</summary>
    private sealed class Sampler(DaggerfallFloraField owner, double offsetX, double offsetZ) : IVegetationSampler
    {
        public void Sample(VegetationTileRequest tile, VegetationTileSamples samples)
        {
            owner._ground.SampleFlora(tile, samples, offsetX, offsetZ, Volatile.Read(ref owner._winter) == 1, owner._tuning);
            int found = 0;
            foreach (uint ground in samples.Grounds ?? []) found |= 1 << (int)ground;
            Interlocked.Or(ref owner._found, found);
        }
    }
}
