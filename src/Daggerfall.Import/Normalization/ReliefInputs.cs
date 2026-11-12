using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Relief;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// Reads what the relief is baked from out of the source wilderness and the sections the import published. It lies
/// outside the bake's own source (<c>src/Daggerfall.Import/Relief</c>) on purpose: a kept bake is recorded against the
/// input this returns, so how it is read can change without the bake going stale unless the input itself does.
/// </summary>
public static class ReliefInputs
{
    /// <summary>The bake's input from the source wilderness, the published climate grid and the locations.</summary>
    public static ReliefInput Read(WoodsFile woods, DaggerfallClimateGrid climate, DaggerfallLocations locations)
    {
        ArgumentNullException.ThrowIfNull(woods);
        ArgumentNullException.ThrowIfNull(climate);
        ArgumentNullException.ThrowIfNull(locations);
        const int width = WoodsReader.MapWidth, height = WoodsReader.MapHeight, centre = WoodsReader.CellSamples / 2;
        double[] classic = new double[width * height], rain = new double[width * height];
        for (int i = 0; i < classic.Length; i++) classic[i] = ReliefBake.ClassicMetres(woods.Heightmap[i], woods.Cells[i].Samples[centre]);
        Array.Fill(rain, 1);
        foreach (DaggerfallGridRow row in climate.Rows)
        {
            // The grid's first column is a sentinel, so its column x is map pixel x - 1.
            int x = -1;
            foreach (DaggerfallGridRun run in row.Runs)
            {
                for (int i = Math.Max(x, 0); i < Math.Min(x + run.Count, width); i++) rain[(row.Y * width) + i] = Rain(run.Value);
                x += run.Count;
            }
        }

        ReliefPlace[] places = [.. locations.Locations.Where(location => location.Exterior is not null)
            .Select(location => new ReliefPlace(location.Exterior!.MapPixelX, location.Exterior.MapPixelY, location.Exterior.Width * location.Exterior.Height))];
        return new(width, height, classic, rain, places);
    }

    private static double Rain(int climate) => climate switch
    {
        224 => .35, 225 => .5, 226 => .9, 227 => 1.7, 228 => 1.5, 230 or 231 => 1.2, 232 => 1.1, _ => 1,
    };
}
