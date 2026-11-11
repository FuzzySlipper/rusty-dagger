namespace Daggerfall.Import.Normalization;

/// <summary>
/// One level of the atlas as it is drawn: a colour per pixel in floating point, with the antialiased marks the atlas makes
/// on it. Every mark blends its ink over what is beneath by the share of the pixel it covers.
/// </summary>
internal sealed class AtlasCanvas(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float[] R { get; } = new float[checked(width * height)];
    public float[] G { get; } = new float[width * height];
    public float[] B { get; } = new float[width * height];

    public (double R, double G, double B) this[int i] => (R[i], G[i], B[i]);

    public void Paint(int i, (byte R, byte G, byte B) ink, double cover)
    {
        if (cover <= 0) return;
        R[i] = (float)(R[i] + ((ink.R - R[i]) * cover));
        G[i] = (float)(G[i] + ((ink.G - G[i]) * cover));
        B[i] = (float)(B[i] + ((ink.B - B[i]) * cover));
    }

    /// <summary>An antialiased round-capped line; <paramref name="skip"/> names pixels it leaves alone.</summary>
    public void Stroke(double x0, double y0, double x1, double y1, double radius, (byte R, byte G, byte B) ink, double alpha, bool[]? skip = null)
    {
        int left = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radius - 1)), right = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radius + 1));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radius - 1)), bottom = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radius + 1));
        double dx = x1 - x0, dy = y1 - y0, length = (dx * dx) + (dy * dy);
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            int i = (y * Width) + x;
            if (skip is not null && skip[i]) continue;
            double px = x + .5 - x0, py = y + .5 - y0;
            double t = length == 0 ? 0 : Math.Clamp(((px * dx) + (py * dy)) / length, 0, 1);
            double ex = px - (t * dx), ey = py - (t * dy);
            Paint(i, ink, Math.Clamp(radius + .5 - Math.Sqrt((ex * ex) + (ey * ey)), 0, 1) * alpha);
        }
    }

    /// <summary>An antialiased disc.</summary>
    public void Disc(double cx, double cy, double radius, (byte R, byte G, byte B) ink, double alpha)
    {
        int left = Math.Max(0, (int)Math.Floor(cx - radius - 1)), right = Math.Min(Width - 1, (int)Math.Ceiling(cx + radius + 1));
        int top = Math.Max(0, (int)Math.Floor(cy - radius - 1)), bottom = Math.Min(Height - 1, (int)Math.Ceiling(cy + radius + 1));
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            double ex = x + .5 - cx, ey = y + .5 - cy;
            Paint((y * Width) + x, ink, Math.Clamp(radius + .5 - Math.Sqrt((ex * ex) + (ey * ey)), 0, 1) * alpha);
        }
    }

    /// <summary>An antialiased polygon, its cover counted from sixteen samples a pixel by the even-odd rule.</summary>
    public void Fill(ReadOnlySpan<(double X, double Y)> points, (byte R, byte G, byte B) ink, double alpha)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach ((double x, double y) in points)
        {
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        int left = Math.Max(0, (int)Math.Floor(minX)), right = Math.Min(Width - 1, (int)Math.Ceiling(maxX));
        int top = Math.Max(0, (int)Math.Floor(minY)), bottom = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            int inside = 0;
            for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
                if (Contains(points, x + ((sx + .5) / 4), y + ((sy + .5) / 4))) inside++;
            Paint((y * Width) + x, ink, inside / 16.0 * alpha);
        }
    }

    private static bool Contains(ReadOnlySpan<(double X, double Y)> points, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            (double xi, double yi) = points[i];
            (double xj, double yj) = points[j];
            if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi) inside = !inside;
        }
        return inside;
    }

    /// <summary>This canvas at half its size, each pixel the mean of the four it covers.</summary>
    public AtlasCanvas Halve()
    {
        AtlasCanvas half = new(Width / 2, Height / 2);
        foreach ((float[] from, float[] to) in new[] { (R, half.R), (G, half.G), (B, half.B) })
            for (int y = 0; y < half.Height; y++)
            for (int x = 0; x < half.Width; x++)
            {
                int i = (2 * y * Width) + (2 * x);
                to[(y * half.Width) + x] = (from[i] + from[i + 1] + from[i + Width] + from[i + Width + 1]) / 4;
            }
        return half;
    }

    public byte[] ToRgba()
    {
        byte[] rgba = new byte[R.Length * 4];
        for (int i = 0; i < R.Length; i++)
        {
            rgba[i * 4] = (byte)Math.Clamp(Math.Round(R[i]), 0, 255);
            rgba[(i * 4) + 1] = (byte)Math.Clamp(Math.Round(G[i]), 0, 255);
            rgba[(i * 4) + 2] = (byte)Math.Clamp(Math.Round(B[i]), 0, 255);
            rgba[(i * 4) + 3] = 255;
        }
        return rgba;
    }
}
