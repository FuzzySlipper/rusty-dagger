using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The refreshed title as a scene the page animates: a town seen through a window at night. The town painting is the
/// <c>screen.title</c> replacement itself, so a page that draws no scene still shows the town; these layers go over it.
/// Places and sizes are in town pixels, except the window's own and the logo's, which are in window pixels.
/// </summary>
/// <param name="Clouds">The clouds alone, drifting <see cref="DaggerfallTitleClouds.Drift"/> pixels over a cycle across the town's sky.</param>
/// <param name="Sea">The bay's water as a vertical strip of frames laid over the town at its place.</param>
/// <param name="Window">The window and sill the town is seen through, with its opening keyed out.</param>
/// <param name="Lights">The town's lit windows and street lamps, where the Engine frame the painting was made from shows them.</param>
/// <param name="Logo">The game's logo, which the page draws in place of the game's name.</param>
/// <param name="TownWidth">The town painting's width in pixels, the unit the layers are placed in.</param>
/// <param name="TownHeight">The town painting's height in pixels.</param>
internal sealed record DaggerfallTitleScene(DaggerfallTitleClouds Clouds, DaggerfallTitleSea Sea, DaggerfallTitleWindow Window,
    IReadOnlyList<DaggerfallTitleLight> Lights, DaggerfallTitleLogo Logo, int TownWidth, int TownHeight)
{
    /// <summary>The scene's own images, each a media identity the page draws.</summary>
    internal IEnumerable<DaggerfallArtImage> Images => [Clouds.Image, Sea.Image, Window.Image, Logo.Image];

    /// <summary>Reads the <c>titleScene</c> section of the art pack's payload.</summary>
    /// <exception cref="InvalidOperationException">A layer without a usable image, size or place, a light outside the town or a logo outside the window; names the pack.</exception>
    internal static DaggerfallTitleScene Read(JsonElement section, string packId, int townWidth, int townHeight)
    {
        try
        {
            JsonElement clouds = section.GetProperty("clouds"), sea = section.GetProperty("sea"), window = section.GetProperty("window");
            JsonElement logo = section.GetProperty("logo");
            DaggerfallTitleScene scene = new(
                new(Image(clouds), Positive(clouds, "drift"), Positive(clouds, "cycleSeconds")),
                new(Image(sea), Rect(sea), Positive(sea, "frames"), Positive(sea, "framesPerSecond")),
                new(Image(window), Positive(window, "width"), Positive(window, "height"), Rect(window.GetProperty("opening")),
                    [.. window.GetProperty("flames").EnumerateArray().Select(flame => new DaggerfallTitleFlame(flame.GetProperty("x").GetInt32(),
                        flame.GetProperty("y").GetInt32(), Positive(flame, "radius")))]),
                [.. section.GetProperty("lights").EnumerateArray().Select(light => new DaggerfallTitleLight(light.GetProperty("x").GetInt32(),
                    light.GetProperty("y").GetInt32(), light.GetProperty("lamp").GetBoolean()))],
                new(Image(logo), Rect(logo)), townWidth, townHeight);
            if (scene.Sea.Place.X < 0 || scene.Sea.Place.Y < 0 || scene.Sea.Place.X + scene.Sea.Place.Width > townWidth || scene.Sea.Place.Y + scene.Sea.Place.Height > townHeight)
                throw new InvalidOperationException("its sea lies outside the town");
            if (scene.Lights.Any(light => light.X < 0 || light.Y < 0 || light.X >= townWidth || light.Y >= townHeight))
                throw new InvalidOperationException("a light lies outside the town");
            if (!Within(scene.Window.Opening, scene.Window))
                throw new InvalidOperationException("its window's opening lies outside the window");
            if (!Within(scene.Logo.Place, scene.Window))
                throw new InvalidOperationException("its logo lies outside the window");
            return scene;
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"Content pack '{packId}' states an unusable title scene: {exception.Message}", exception);
        }

        DaggerfallArtImage Image(JsonElement layer)
        {
            string mediaId = layer.GetProperty("mediaId").GetString()!, path = layer.GetProperty("path").GetString()!;
            long byteLength = layer.GetProperty("byteLength").GetInt64();
            if (string.IsNullOrWhiteSpace(mediaId) || string.IsNullOrWhiteSpace(path) || byteLength <= 0)
                throw new InvalidOperationException("a layer without a usable identity, path or length");
            return new(mediaId, path, byteLength);
        }

        static DaggerfallTitleRect Rect(JsonElement rect) =>
            new(rect.GetProperty("x").GetInt32(), rect.GetProperty("y").GetInt32(), Positive(rect, "width"), Positive(rect, "height"));

        static bool Within(DaggerfallTitleRect rect, DaggerfallTitleWindow window) =>
            rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= window.Width && rect.Y + rect.Height <= window.Height;

        static int Positive(JsonElement value, string name) =>
            value.GetProperty(name).GetInt32() is > 0 and var positive ? positive : throw new InvalidOperationException($"its {name} is not above zero");
    }
}

/// <summary>A rectangle in whole pixels.</summary>
internal sealed record DaggerfallTitleRect(int X, int Y, int Width, int Height);

/// <summary>The cloud layer: its image, how far it drifts in a cycle and how long the cycle is.</summary>
internal sealed record DaggerfallTitleClouds(DaggerfallArtImage Image, int Drift, int CycleSeconds);

/// <summary>The sea: its strip of <paramref name="Frames"/> frames, each <see cref="DaggerfallTitleRect.Height"/> tall, and its place over the town.</summary>
internal sealed record DaggerfallTitleSea(DaggerfallArtImage Image, DaggerfallTitleRect Place, int Frames, int FramesPerSecond);

/// <summary>The window: its image, its size, its opening, and the flames whose light flickers on it, all in window pixels.</summary>
internal sealed record DaggerfallTitleWindow(DaggerfallArtImage Image, int Width, int Height, DaggerfallTitleRect Opening, IReadOnlyList<DaggerfallTitleFlame> Flames);

/// <summary>A flame in the window art and how far its glow reaches.</summary>
internal sealed record DaggerfallTitleFlame(int X, int Y, int Radius);

/// <summary>The game's logo and where it is drawn, in window pixels.</summary>
internal sealed record DaggerfallTitleLogo(DaggerfallArtImage Image, DaggerfallTitleRect Place);

/// <summary>A light in the town: a lit window, or a street lamp.</summary>
internal sealed record DaggerfallTitleLight(int X, int Y, bool Lamp);
