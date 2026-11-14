namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// A dye drawn over a neutral image: the image brightened by <paramref name="Brightness"/>, then the pixels of
/// <paramref name="Mask"/> (an image the size and place of the layer) multiplied by <paramref name="Color"/>.
/// </summary>
internal sealed record PaperDollTint(string Color, double Brightness, string Mask);
