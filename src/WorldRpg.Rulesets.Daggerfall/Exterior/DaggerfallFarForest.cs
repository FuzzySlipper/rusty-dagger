using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.VegetationTiles;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Forest on the far hills: a vegetation field in the backdrop over far ground, in its frame (map metres at 1:1000), so
/// ridges hide what stands behind them and the world draws over it. It grows each climate's tall flora sprites, those at
/// least <see cref="DaggerfallFloraTuning.FarTreeMetres"/> high, by the same cover as near flora but at
/// <see cref="DaggerfallFloraTuning.FarShare"/> of its density and <see cref="DaggerfallFloraTuning.FarScale"/> its size,
/// from where near flora fades out to <see cref="DaggerfallFloraTuning.FarReach"/>. Copies stand on far ground's own
/// lifted heights, sampled again wherever far ground is.
/// </summary>
internal sealed class DaggerfallFarForest : IDisposable
{
    /// <summary>Far ground's finest cell, so copies stand on its triangles.</summary>
    private const double CellMetres = DaggerfallHorizonGround.FineCellMetres;
    /// <summary>A tile is a map pixel, far ground's own tile.</summary>
    private const uint TileCells = DaggerfallHorizonGround.TileCells;
    /// <summary>The records of a nature archive.</summary>
    private const int Records = 31;
    /// <summary>The donor's chance is a tile's, and a tile is the walked spacing square.</summary>
    private const float TileArea = DaggerfallTerrainSurfaceBuilder.SampleSpacing * DaggerfallTerrainSurfaceBuilder.SampleSpacing;
    /// <summary>A layer may hold this many times the copies its density promises.</summary>
    private const float Headroom = 1.5f;
    /// <summary>Layers past every near flora record's and ground cover's.</summary>
    private const uint LayerBase = 0x2000;

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonGround _ground;
    private readonly DaggerfallFloraTuning _tuning;
    private readonly DaggerfallFlatsTuning _flats;
    private readonly IReadOnlyDictionary<int, float> _archiveDensity;
    private readonly Dictionary<uint, (VegetationSprite Sprite, RenderResource Image)> _layers = [];
    private readonly HashSet<int> _grown = [];
    private VegetationField? _field;
    private VegetationStreamer? _streamer;
    private bool _seen;
    private int _winter = -1;
    /// <summary>The nature archives the workers have found ground of, a bit each from the first.</summary>
    private int _found;

    internal DaggerfallFarForest(IEngineContext engine, DaggerfallHorizonGround ground, DaggerfallFloraTuning tuning, DaggerfallFlatsTuning flats,
        IReadOnlyDictionary<int, float> archiveDensity)
    {
        _engine = engine;
        _ground = ground;
        _tuning = tuning;
        _flats = flats;
        _archiveDensity = archiveDensity;
    }

    /// <summary>Grows the forest about map point (x, z), with far ground; a season's change samples every tile again.</summary>
    internal void Show(double x, double z, bool winter, Func<int, int, DaggerfallFloraLook?> looks)
    {
        if (_field is null)
        {
            _winter = winter ? 1 : 0;
            _field = _engine.Vegetation.Create(new VegetationFieldCreateRequest(default, CellMetres * TileCells, TileCells, VegetationFocus.At(x, 0, z))
                with { Layer = RenderLayer.Backdrop, Origin = Vector3.Zero, Scale = 1 / 1_000f });
            _streamer = new VegetationStreamer(_engine, _field, new Sampler(this));
            _seen = true;
        }

        if (!_seen)
        {
            _seen = true;
            _engine.Vegetation.SetVisible(new(_field, true));
        }

        if ((winter ? 1 : 0) != Volatile.Read(ref _winter))
        {
            Volatile.Write(ref _winter, winter ? 1 : 0);
            Invalidate(double.MinValue / 4, double.MinValue / 4, double.MaxValue / 4, double.MaxValue / 4);
        }

        _engine.Vegetation.SetFocus(new(_field, VegetationFocus.At(x, 0, z)));
        int underfoot = _ground.NatureArchive(x, z, winter) - DaggerfallExteriorEnvironment.FirstNatureArchive;
        Interlocked.Or(ref _found, 1 << underfoot);
        int found = Volatile.Read(ref _found);
        for (int index = 0; index < DaggerfallExteriorEnvironment.NatureArchiveCount; index++)
            if ((found & (1 << index)) != 0 && _grown.Add(index)) Grow(DaggerfallExteriorEnvironment.FirstNatureArchive + index, looks);
        _streamer!.Update();
    }

    /// <summary>Samples a stretch of map again, where far ground's lifted heights have changed.</summary>
    internal void Invalidate(double minX, double minZ, double maxX, double maxZ)
    {
        if (_field is not null) _engine.Vegetation.Invalidate(new(_field, minX, minZ, maxX, maxZ));
    }

    /// <summary>Stops drawing the forest, keeping its tiles for the next time outdoors.</summary>
    internal void Hide()
    {
        if (_field is null || !_seen) return;
        _seen = false;
        _engine.Vegetation.SetVisible(new(_field, false));
    }

    /// <summary>Lets go of the field and its sprites; the next showing grows them anew.</summary>
    internal void Drop()
    {
        _streamer?.Dispose(); _streamer = null;
        _field?.Dispose(); _field = null;
        foreach ((VegetationSprite sprite, RenderResource image) in _layers.Values)
        {
            sprite.Dispose();
            image.Dispose();
        }
        _layers.Clear();
        _grown.Clear();
        Volatile.Write(ref _found, 0);
    }

    /// <summary>What the field holds and draws, for looking at by eye.</summary>
    internal string Readout()
    {
        if (_field is null) return "not grown";
        VegetationReadout read = _engine.Vegetation.Read(_field);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{(read.Visible ? "shown" : "hidden")}, {_layers.Count} layers, {read.Tiles} tiles ({read.PendingTiles} waiting), {read.Instances} copies, {read.OverBudget} over budget");
    }

    /// <summary>A climate's tall records as backdrop layers, fading in where near flora fades out.</summary>
    private void Grow(int archive, Func<int, int, DaggerfallFloraLook?> looks)
    {
        float density = _tuning.Density * _archiveDensity.GetValueOrDefault(archive, 1) * _tuning.FarShare / TileArea / Records;
        for (int record = 1; record <= Records; record++)
        {
            if (looks(archive, record) is not { } look || look.Size.Y < _tuning.FarTreeMetres) continue;
            uint layer = LayerBase + (uint)(((archive - DaggerfallExteriorEnvironment.FirstNatureArchive) * 32) + record);
            if (!_layers.TryGetValue(layer, out var grown))
            {
                RenderResource image = _engine.Graphics.OpenTexture(_engine.Content, look.Image.Path, look.Image.Sha256, TextureFilter.NearestMipmapped).Handle;
                try
                {
                    grown = (new VegetationSprite(_engine, new RenderResourceReference(image), look.UvMin, look.UvMax, look.Size, look.Pivot,
                        _flats.Flora(DaggerfallSiteAppearance.LitSprite), _flats.Nature), image);
                }
                catch
                {
                    image.Dispose();
                    throw;
                }

                _layers.Add(layer, grown);
            }

            float near = _tuning.ReachOf(look.Size.Y), reach = _tuning.FarReach;
            _engine.Vegetation.SetLayer(new VegetationLayerRequest(_field!, layer, grown.Sprite.Look, default, density, reach) with
            {
                Grounds = VegetationLayerRequest.Ground((uint)(archive - DaggerfallExteriorEnvironment.FirstNatureArchive)),
                SlopeLimitDegrees = _tuning.SlopeLimitDegrees,
                // Fading in over the band near flora fades out over, so the two cross.
                Near = near - Math.Min(_tuning.Fade, near / 2),
                NearFade = Math.Min(_tuning.Fade, near / 2),
                Fade = _tuning.Fade,
                Seed = layer,
                ScaleMin = _tuning.FarScale,
                ScaleMax = _tuning.FarScale,
                MaximumInstances = (uint)Math.Min(uint.MaxValue, Math.Ceiling(MathF.PI * reach * reach * density * Headroom)),
                CastsShadows = false,
            });
        }
    }

    public void Dispose() => Drop();

    /// <summary>Samples far ground on the streamer's workers, noting each climate's archive it finds.</summary>
    private sealed class Sampler(DaggerfallFarForest owner) : IVegetationSampler
    {
        public void Sample(VegetationTileRequest tile, VegetationTileSamples samples)
        {
            owner._ground.SampleFarFlora(tile, samples, Volatile.Read(ref owner._winter) == 1, owner._tuning);
            int found = 0;
            foreach (uint ground in samples.Grounds ?? []) found |= 1 << (int)ground;
            Interlocked.Or(ref owner._found, found);
        }
    }
}
