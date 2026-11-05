namespace WorldRpg.Kit.World;

/// <summary>
/// An analytic erosion filter: branching gullies and ridges cut into a smooth height field, aligned with its slope and
/// evaluated point by point, after Clay John's eroded terrain noise and Rune Skovbo Johansen's "Fast and Gorgeous
/// Erosion Filter". Each octave blends cosine stripes from a jittered lattice, every stripe running down the slope, and
/// the slope each octave follows includes the coarser octaves' detail, so smaller gullies branch off the larger ones.
/// A gully's depth scales with the steepness and its own wavelength: flat land stays flat.
/// </summary>
public sealed class ErosionFilter(ulong seed, ErosionFilterSettings settings)
{
    private const ulong JitterXSalt = 0x3A1C_75E9_02B4_D86FUL, JitterZSalt = 0x9F5E_2C83_61A7_04BDUL, CrestSalt = 0x51D7_33A8_C6E0_9B4FUL;
    private const ulong OctaveSalt = 0x9E37_79B9_7F4A_7C15UL;
    private const int CrestOctaves = 3;
    private const double CrestPersistence = .5, CrestMiddle = .55, Lacunarity = 2.03;

    /// <summary>
    /// The height to add at a point, in metres, given the smooth field's slope there (rise over run). Octaves shorter
    /// than <paramref name="finestMetres"/> are left out, so a coarse sampling of the field doesn't alias them.
    /// </summary>
    public double Offset(double x, double z, double slopeX, double slopeZ, double finestMetres)
    {
        double total = 0, detailX = 0, detailZ = 0, wavelength = settings.LongestMetres;
        for (int octave = 0; octave < settings.Octaves && wavelength >= finestMetres; octave++)
        {
            double gx = slopeX + detailX, gz = slopeZ + detailZ;
            double steep = Math.Sqrt((gx * gx) + (gz * gz));
            if (steep < 1e-6) break;
            (double h, double dx, double dz) = Gullies(x / wavelength, z / wavelength, -gz / steep, gx / steep, octave);
            double amplitude = settings.Strength * Math.Min(steep, settings.SteepestSlope) * wavelength;
            total += h * amplitude;
            detailX += dx * amplitude / wavelength;
            detailZ += dz * amplitude / wavelength;
            wavelength *= .5;
        }

        return total;
    }

    /// <summary>
    /// High ground broken into peaks and cols by ridged noise: the gullies follow the slope and leave a crest line
    /// smooth, and this is what makes a range's skyline jagged.
    /// </summary>
    public double Crest(double x, double z, double elevation)
    {
        if (settings.CrestReliefMetres <= 0 || elevation <= settings.CrestFromMetres) return 0;
        double share = Math.Clamp((elevation - settings.CrestFromMetres) / (settings.CrestFullMetres - settings.CrestFromMetres), 0, 1);
        share = share * share * (3 - (2 * share));
        return settings.CrestReliefMetres * share * (Ridged(x / settings.CrestWavelengthMetres, z / settings.CrestWavelengthMetres) - CrestMiddle);
    }

    private (double H, double Dx, double Dz) Gullies(double px, double pz, double dirX, double dirZ, int octave)
    {
        double ix = Math.Floor(px), iz = Math.Floor(pz), fx = px - ix, fz = pz - iz;
        double sum = 0, sumX = 0, sumZ = 0, weights = 0;
        ulong octaveSeed = seed + ((ulong)octave * OctaveSalt);
        for (int i = -1; i <= 2; i++)
        for (int j = -1; j <= 2; j++)
        {
            long cx = (long)ix + i, cz = (long)iz + j;
            double ox = fx - i - (.5 * Unit(octaveSeed ^ JitterXSalt, cx, cz)), oz = fz - j - (.5 * Unit(octaveSeed ^ JitterZSalt, cx, cz));
            double weight = Math.Exp(-2 * ((ox * ox) + (oz * oz)));
            double phase = ((ox * dirX) + (oz * dirZ)) * Math.Tau;
            sum += Math.Cos(phase) * weight;
            double slope = -Math.Sin(phase) * Math.Tau * weight;
            sumX += slope * dirX;
            sumZ += slope * dirZ;
            weights += weight;
        }

        return (sum / weights, sumX / weights, sumZ / weights);
    }

    private double Ridged(double x, double z)
    {
        double sum = 0, weight = 1, total = 0, frequency = 1, previous = 1;
        for (int octave = 0; octave < CrestOctaves; octave++)
        {
            double ridge = 1 - Math.Abs(Gradient((seed ^ CrestSalt) + ((ulong)octave * OctaveSalt), x * frequency, z * frequency));
            ridge *= ridge;
            sum += weight * ridge * previous;
            total += weight;
            previous = ridge;
            weight *= CrestPersistence;
            frequency *= Lacunarity;
        }

        return sum / total;
    }

    private static double Gradient(ulong noise, double x, double z)
    {
        long cx = (long)Math.Floor(x), cz = (long)Math.Floor(z);
        double fx = x - cx, fz = z - cz;
        double u = Fade(fx), v = Fade(fz);
        double a = Dot(noise, cx, cz, fx, fz), b = Dot(noise, cx + 1, cz, fx - 1, fz);
        double c = Dot(noise, cx, cz + 1, fx, fz - 1), d = Dot(noise, cx + 1, cz + 1, fx - 1, fz - 1);
        return Math.Sqrt(2) * (a + ((b - a) * u) + ((c - a) * v) + ((a - b - c + d) * u * v));
    }

    private static double Dot(ulong noise, long cx, long cz, double dx, double dz)
    {
        double turn = Hash(noise, cx, cz) % 16 * (Math.Tau / 16);
        return (Math.Cos(turn) * dx) + (Math.Sin(turn) * dz);
    }

    private static double Fade(double t) => t * t * t * ((t * ((t * 6) - 15)) + 10);

    /// <summary>A value from -1 to 1 for a lattice point.</summary>
    private static double Unit(ulong noise, long a, long b) => (Hash(noise, a, b) >> 11) * (2.0 / (1UL << 53)) - 1;

    private static ulong Hash(ulong noise, long a, long b)
    {
        ulong value = noise ^ ((ulong)a * 0xBF58_476D_1CE4_E5B9UL) ^ ((ulong)b * 0x94D0_49BB_1331_11EBUL);
        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        return value ^ (value >> 31);
    }
}

/// <param name="LongestMetres">The longest gullies' spacing; each octave halves it.</param>
/// <param name="Octaves">How many octaves.</param>
/// <param name="Strength">A gully's depth as a share of its spacing on a slope of 1 (45 degrees).</param>
/// <param name="SteepestSlope">Slopes steeper than this cut no deeper.</param>
/// <param name="CrestReliefMetres">How far high ground is broken into peaks and cols; 0 for none.</param>
/// <param name="CrestFromMetres">Crest relief starts at this height.</param>
/// <param name="CrestFullMetres">Crest relief is whole by this height.</param>
/// <param name="CrestWavelengthMetres">The spacing of the crest's peaks.</param>
public sealed record ErosionFilterSettings(double LongestMetres, int Octaves, double Strength, double SteepestSlope,
    double CrestReliefMetres, double CrestFromMetres, double CrestFullMetres, double CrestWavelengthMetres);
