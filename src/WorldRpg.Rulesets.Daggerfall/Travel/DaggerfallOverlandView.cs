using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// Where the travel map is looked at from: a point on the land, the way the view faces, how steeply it looks down and how
/// far the eye stands off. It follows the party until it is panned by hand, and turns, tilts and zooms about the point.
/// Map metres run x east and z south, map pixel (X, Y) spanning x from X and z back from Y, as the exterior ground's do;
/// a heading of 0 faces north and 90 east, as the Engine's camera yaw does. It is presentation only and never saved.
/// </summary>
internal sealed class DaggerfallOverlandView(DaggerfallOverlandTuning tuning)
{
    /// <summary>How many times nearer one step of zoom brings the eye.</summary>
    internal const double ZoomStep = 1.25;
    private const double Cell = DaggerfallWorldGrid.HorizontalSize;

    private readonly DaggerfallOverlandTuning _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));

    internal double TargetX { get; private set; }
    internal double TargetZ { get; private set; }
    internal double Heading { get; private set; }
    internal double Tilt { get; private set; } = tuning.Tilt.Opening;
    internal double Distance { get; private set; } = tuning.Distance.Opening;
    internal bool Following { get; private set; } = true;

    /// <summary>A world-map point in pixels, as the expedition counts the party's, in map metres.</summary>
    internal static (double X, double Z) Metres(double pixelX, double pixelY) => (pixelX * Cell, (pixelY - 1) * Cell);

    /// <summary>The map pixel a map point lies in.</summary>
    internal static DaggerfallTravelMapPixel Pixel(double x, double z) => new(
        Math.Clamp((int)Math.Floor(x / Cell), 0, DaggerfallTravelMapPixel.Width - 1),
        Math.Clamp((int)Math.Floor((z / Cell) + 1), 0, DaggerfallTravelMapPixel.Height - 1));

    /// <summary>The map opens on the party, following it, at the turn, tilt and distance it was last left at.</summary>
    internal void Open(double partyX, double partyZ)
    {
        Following = true;
        (TargetX, TargetZ) = (partyX, partyZ);
    }

    /// <summary>Follows the party again, as "I'm at" does.</summary>
    internal void Follow() => Following = true;

    /// <summary>Keeps the view on the party while it follows.</summary>
    internal void Track(double partyX, double partyZ)
    {
        if (Following) (TargetX, TargetZ) = (partyX, partyZ);
    }

    /// <summary>Looks at a map point, which stops following the party.</summary>
    internal void Look(double x, double z)
    {
        Following = false;
        (TargetX, TargetZ) = Bounded(x, z);
    }

    /// <summary>
    /// Moves the view: <paramref name="turn"/> degrees clockwise, <paramref name="tilt"/> degrees steeper, a pan of
    /// <paramref name="right"/> and <paramref name="forward"/> eye distances across the land as it is faced (which stops
    /// following), and <paramref name="zoom"/> steps nearer.
    /// </summary>
    internal void Move(double turn, double tilt, double right, double forward, double zoom)
    {
        if (!double.IsFinite(turn) || !double.IsFinite(tilt) || !double.IsFinite(right) || !double.IsFinite(forward) || !double.IsFinite(zoom))
            throw new ArgumentOutOfRangeException(nameof(turn), "A travel map view move is finite.");
        Heading = ((Heading + turn) % 360 + 360) % 360;
        Tilt = _tuning.Tilt.Clamp(Tilt + tilt);
        if (right != 0 || forward != 0)
        {
            double radians = Heading * Math.PI / 180, step = Distance;
            Look(TargetX + (((Math.Cos(radians) * right) + (Math.Sin(radians) * forward)) * step),
                TargetZ + (((Math.Sin(radians) * right) - (Math.Cos(radians) * forward)) * step));
        }
        Distance = _tuning.Distance.Clamp(Distance * Math.Pow(ZoomStep, -zoom));
    }

    /// <summary>Where the eye stands, in map metres, over the point it looks at standing <paramref name="ground"/> high.</summary>
    internal Vector3 Eye(double ground)
    {
        double heading = Heading * Math.PI / 180, tilt = Tilt * Math.PI / 180, level = Math.Cos(tilt) * Distance;
        return new((float)(TargetX - (Math.Sin(heading) * level)), (float)(ground + (Math.Sin(tilt) * Distance)), (float)(TargetZ + (Math.Cos(heading) * level)));
    }

    /// <summary>The point looked at, in map metres, standing <paramref name="ground"/> high.</summary>
    internal Vector3 Target(double ground) => new((float)TargetX, (float)ground, (float)TargetZ);

    private static (double X, double Z) Bounded(double x, double z) =>
        (Math.Clamp(x, 0, DaggerfallTravelMapPixel.Width * Cell), Math.Clamp(z, -Cell, (DaggerfallTravelMapPixel.Height - 1) * Cell));
}
