using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// A ground pile of one kind of item standing as that item with refreshed art: its world image, and where it is in
/// a metal or dye, the mask of what <paramref name="Color"/> (a CSS colour) brightened by <paramref name="Brightness"/>
/// recolours. It stands <paramref name="Height"/> metres tall, the entry's own ground height.
/// </summary>
internal sealed record DaggerfallGroundItemLook(DaggerfallArtWorldImage Image, DaggerfallArtWorldImage? Mask, string? Color, double Brightness,
    double Height);
