using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// Rain or snow falling over the travel map where the world's weather has it fall. The map holds the world while it is
/// open, so nothing the Engine moves by the world's time moves; the map moves its own drops on the update's real seconds,
/// as it runs its march. Each kind is a pool of drops in the map's backdrop: a dead drop starts again at a random point the
/// view shows, short of the land, if that point's weather falls as its kind (as often as it falls hard), and falls a share
/// of the view's distance a second, so a shower reads alike from the nearest zoom and from tens of kilometres up. The land
/// hides drops behind it. Each update a kind is drawn as one sprite batch, each drop sized by its distance to stay a share
/// of the view's height, and fading in and out through its atlas's frames. The Engine lets an appearance go only once a
/// snapshot without it is published, so a batch drawn over, or taken away as the map closes, is let go an update later.
/// </summary>
internal sealed class DaggerfallOverlandFall : IDisposable
{
    /// <summary>The frames of a drop's atlas, from fully seen to faintest, side by side.</summary>
    internal const int FadeFrames = 4;

    private readonly IGraphicsService _graphics;
    private readonly DaggerfallOverlandFallTuning _tuning;
    private readonly Drops _rain, _snow;
    private uint _random = 0x9E3779B9;
    /// <summary>Whether the pools have been filled since the map opened, so the first fill starts at every age.</summary>
    private bool _filled;

    /// <param name="rain">A rain streak's atlas, <see cref="FadeFrames"/> frames from seen to faint.</param>
    /// <param name="snow">A snowflake's atlas, as the rain's.</param>
    internal DaggerfallOverlandFall(IGraphicsService graphics, DaggerfallOverlandFallTuning tuning, SpriteAtlas rain, SpriteAtlas snow)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _rain = new(tuning.Rain, rain, snow: false);
        _snow = new(tuning.Snow, snow, snow: true);
    }

    /// <summary>The rain drawn now, or none while none falls in view.</summary>
    internal Appearance? Rain => _rain.Batch;

    /// <summary>The snow drawn now, or none while none falls in view.</summary>
    internal Appearance? Snow => _snow.Batch;

    /// <summary>How many drops are falling now.</summary>
    internal int Falling => _rain.Live + _snow.Live;

    /// <summary>
    /// Moves the drops <paramref name="seconds"/> on, starts dead ones again where the weather falls, and draws them.
    /// <paramref name="view"/> is the map's backdrop view from <paramref name="eye"/> (backdrop units) with a vertical
    /// <paramref name="fieldOfView"/> in degrees, <paramref name="distance"/> its distance in map metres, <paramref name="ground"/>
    /// the height in backdrop units it looks at, and <paramref name="daylight"/> (0 to 1) lights the drops.
    /// </summary>
    internal void Update(CameraDescriptor view, double aspect, Vector3 eye, double fieldOfView, double distance, double ground,
        Func<double, double, DaggerfallWeatherConditions> weatherAt, float daylight, double seconds)
    {
        float light = _tuning.Night + ((1 - _tuning.Night) * Math.Clamp(daylight, 0, 1));
        // A drop's size in backdrop units per unit of its distance from the eye, for a share of the view's height.
        float perDistance = 2 * MathF.Tan((float)(fieldOfView * Math.PI / 360));
        Fall(_rain, view, aspect, distance, ground, weatherAt, (float)seconds);
        Fall(_snow, view, aspect, distance, ground, weatherAt, (float)seconds);
        _filled = true;
        Draw(_rain, eye, perDistance, light);
        Draw(_snow, eye, perDistance, light);
    }

    /// <summary>Takes every drop away, as the map closes; its batches go once a snapshot without them is out.</summary>
    internal void Clear()
    {
        _rain.Clear();
        _snow.Clear();
        _filled = false;
    }

    /// <summary>Lets go of the batches drawn over or taken away before the last snapshot.</summary>
    internal void Release()
    {
        _rain.Release();
        _snow.Release();
    }

    public void Dispose()
    {
        Clear();
        Release();
    }

    private void Fall(Drops drops, CameraDescriptor view, double aspect, double distance, double ground,
        Func<double, double, DaggerfallWeatherConditions> weatherAt, float seconds)
    {
        DaggerfallOverlandDrops tuning = drops.Tuning;
        double scale = DaggerfallOverlandTerrain.Scale;
        float down = (float)(tuning.Speed * distance * scale);
        for (int index = 0; index < drops.Pool.Length; index++)
        {
            ref Drop drop = ref drops.Pool[index];
            if (drop.Live)
            {
                drop.Age += seconds;
                drop.At += drop.Velocity * seconds;
                if (drop.Age < drop.Life) continue;
                drop.Live = false;
            }
            // A point the view shows: a random point of the screen, at a random depth short of the land under it.
            CameraRay ray = CameraQueries.Ray(view, aspect, new Vector2(Next(), Next()));
            double reach = ray.Direction.Y < 0 ? Math.Min((ground - ray.Origin.Y) / ray.Direction.Y, distance * scale * 2) : distance * scale * 1.3;
            Vector3 at = ray.Origin + (ray.Direction * (float)(reach * (.3 + (.7 * Next()))));
            DaggerfallWeatherConditions weather = weatherAt(at.X / scale, at.Z / scale);
            if (weather.Falling <= 0 || (weather.Kind == DaggerfallWeatherKind.Snow) != drops.Snow || Next() >= weather.Falling) continue;
            // Snow drifts downwind; rain falls near straight, a little spread so a shower is not a curtain.
            Vector2 wind = drops.Snow ? Blowing(weather) * (float)(_tuning.Drift * weather.Wind * distance * scale) : Vector2.Zero;
            float spread = down * (drops.Snow ? .25f : .04f);
            float life = (float)tuning.Seconds * (.8f + (.4f * Next()));
            drop = new Drop
            {
                Live = true,
                At = at,
                Velocity = new(wind.X + (spread * ((2 * Next()) - 1)), -down * (.88f + (.24f * Next())), wind.Y + (spread * ((2 * Next()) - 1))),
                Life = life,
                // The first fill after the map opens starts every drop part-way, so the shower is there at once.
                Age = _filled ? 0 : life * Next(),
            };
        }
    }

    private void Draw(Drops drops, Vector3 eye, float perDistance, float light)
    {
        int count = 0;
        float size = drops.Tuning.Size * perDistance;
        foreach (Drop drop in drops.Pool)
        {
            if (!drop.Live) continue;
            // Faint as it starts and as it ends, fully seen between.
            float share = drop.Age / drop.Life;
            float seen = MathF.Min(Math.Clamp(share / .15f, 0, 1), Math.Clamp((1 - share) / .15f, 0, 1));
            uint frame = (uint)Math.Clamp((int)MathF.Round((1 - seen) * (FadeFrames - 1)), 0, FadeFrames - 1);
            drops.Instances[count++] = new SpriteBatchInstance(drop.At, MathF.Max(Vector3.Distance(drop.At, eye) * size, 1e-6f), frame);
        }
        drops.Live = count;
        drops.Release();
        Appearance? old = drops.Batch;
        drops.Batch = count == 0 ? null : _graphics.CreateSpriteBatch(new SpriteBatchRequest(drops.Atlas, drops.Instances.AsMemory(0, count),
            new Vector2(.5f, .5f), Vector2.One, BillboardMode.Spherical, 0, SpriteDepthPolicy.Default,
            new Color(drops.Tuning.Color.X * light, drops.Tuning.Color.Y * light, drops.Tuning.Color.Z * light, drops.Tuning.Opacity),
            new SpriteMaterialDescriptor(SpriteLightingMode.Unlit, default, default, 1, 0, SpriteAlphaMode.Blend, .5f, SpriteShadowPolicy.None,
                SpriteBlendMode.Alpha, 0)));
        if (old is not null) drops.Retired.Add(old);
    }

    /// <summary>The way the wind blows over the map: x east and z south, from its compass degree.</summary>
    private static Vector2 Blowing(DaggerfallWeatherConditions weather)
    {
        float angle = weather.WindDegrees * MathF.PI / 180;
        return new(MathF.Sin(angle), -MathF.Cos(angle));
    }

    /// <summary>A share from 0 up to 1, from a small generator of the map's own: where drops fall is not a rule's.</summary>
    private float Next()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return (_random >> 8) / (float)(1 << 24);
    }

    private struct Drop
    {
        internal bool Live;
        internal Vector3 At, Velocity;
        internal float Age, Life;
    }

    /// <summary>One kind of drop: its pool, the batch that draws it now, and its look.</summary>
    private sealed class Drops(DaggerfallOverlandDrops tuning, SpriteAtlas atlas, bool snow)
    {
        internal DaggerfallOverlandDrops Tuning { get; } = tuning;
        internal SpriteAtlas Atlas { get; } = atlas;
        internal bool Snow { get; } = snow;
        internal Drop[] Pool { get; } = new Drop[(int)tuning.Drops];
        internal SpriteBatchInstance[] Instances { get; } = new SpriteBatchInstance[(int)tuning.Drops];
        internal Appearance? Batch { get; set; }
        internal int Live { get; set; }
        /// <summary>Batches drawn over or taken away, still in the snapshot published last.</summary>
        internal List<Appearance> Retired { get; } = [];

        internal void Clear()
        {
            Array.Clear(Pool);
            if (Batch is not null) Retired.Add(Batch);
            Batch = null;
            Live = 0;
        }

        internal void Release()
        {
            foreach (Appearance batch in Retired) batch.Dispose();
            Retired.Clear();
        }
    }
}

/// <summary>
/// The travel map's rain and snow: the <c>expedition.map.fall</c> section of the ruleset tuning.
/// </summary>
/// <param name="Drift">How far snow drifts downwind at full wind, as a share of the view's distance a second.</param>
/// <param name="Night">How bright drops stay at the dark of night, as a share of their daylight colour.</param>
internal sealed record DaggerfallOverlandFallTuning(DaggerfallOverlandDrops Rain, DaggerfallOverlandDrops Snow, double Drift, float Night)
{
    internal static DaggerfallOverlandFallTuning Default { get; } = new(
        new(900, .9, .45, .035f, DaggerfallMapPalette.Srgb("#c8d2e0"), .6f),
        new(700, 3.2, .07, .011f, DaggerfallMapPalette.Srgb("#ffffff"), .9f), .05, .45f);

    internal DaggerfallOverlandFallTuning Validate()
    {
        Rain.Validate("rain");
        Snow.Validate("snow");
        if (!double.IsFinite(Drift) || Drift < 0) throw new InvalidOperationException("Expedition tuning 'map.fall.drift' must be 0 or more.");
        if (!float.IsFinite(Night) || Night is < 0 or > 1) throw new InvalidOperationException("Expedition tuning 'map.fall.night' must be a share from 0 to 1.");
        return this;
    }

    internal static DaggerfallOverlandFallTuning Read(JsonElement section) => new DaggerfallOverlandFallTuning(
        DaggerfallOverlandDrops.Read(section.GetProperty("rain")), DaggerfallOverlandDrops.Read(section.GetProperty("snow")),
        section.GetProperty("drift").GetDouble(), section.GetProperty("night").GetSingle()).Validate();
}

/// <summary>One kind of drop on the travel map.</summary>
/// <param name="Drops">How many fall at once where it falls hardest over the whole view.</param>
/// <param name="Seconds">How long a drop falls, from fading in to fading out.</param>
/// <param name="Speed">How fast it falls, as a share of the view's distance a second.</param>
/// <param name="Size">How tall it is drawn, as a share of the view's height.</param>
/// <param name="Color">Its linear colour by day.</param>
/// <param name="Opacity">How opaque it is at its fullest.</param>
internal sealed record DaggerfallOverlandDrops(double Drops, double Seconds, double Speed, float Size, Vector3 Color, float Opacity)
{
    internal void Validate(string name)
    {
        if (!double.IsFinite(Drops) || Drops is < 1 or > 4_000 || !double.IsFinite(Seconds) || Seconds <= 0 || !double.IsFinite(Speed) || Speed <= 0
            || !float.IsFinite(Size) || Size is <= 0 or > 1 || !float.IsFinite(Opacity) || Opacity is <= 0 or > 1)
            throw new InvalidOperationException($"Expedition tuning 'map.fall.{name}' must fall 1 to 4000 drops for a positive time at a positive speed, a share of the view's height, at an opacity up to 1.");
    }

    internal static DaggerfallOverlandDrops Read(JsonElement section) => new(section.GetProperty("drops").GetDouble(), section.GetProperty("seconds").GetDouble(),
        section.GetProperty("speed").GetDouble(), section.GetProperty("size").GetSingle(),
        DaggerfallMapPalette.Srgb(section.GetProperty("color").GetString()!), section.GetProperty("opacity").GetSingle());
}
