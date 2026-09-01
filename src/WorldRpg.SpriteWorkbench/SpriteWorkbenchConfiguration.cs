using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.SpriteWorkbench;

/// <summary>
/// Explicit local roots for the development-only authoring product. Immutable
/// publication bytes are supplied by <see cref="Rusty.Engine.ProductContent"/>;
/// the separation root is only a lexical guard preventing authored overlays
/// from being placed inside that generated tree.
/// </summary>
public sealed record SpriteWorkbenchConfiguration(string PublicationSeparationRoot, string AuthoringRoot, string OverlayPath)
{
    public const string ContentPath = "sprite-workbench.json";
    public SpriteWorkbenchPreviewPlacement PreviewPlacement { get; init; } = SpriteWorkbenchPreviewPlacement.Default;

    public SpriteWorkbenchConfiguration Validate()
    {
        if (string.IsNullOrWhiteSpace(PublicationSeparationRoot) || string.IsNullOrWhiteSpace(AuthoringRoot))
            throw new ArgumentException("Sprite workbench publication-separation and authoring roots are required.");
        Daggerfall.Import.Publication.SpriteAuthoredOverlayStore.ValidateRootSeparation(PublicationSeparationRoot, AuthoringRoot);
        Daggerfall.Import.Publication.SpriteAuthoredOverlayStore.ValidateOverlayRelativePath(OverlayPath);
        PreviewPlacement.Validate();
        return this;
    }

    public static SpriteWorkbenchConfiguration Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            SpriteWorkbenchConfiguration value = JsonSerializer.Deserialize<SpriteWorkbenchConfiguration>(bytes,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new FormatException("The sprite workbench configuration is empty.");
            return value.Validate();
        }
        catch (JsonException error)
        {
            throw new FormatException("The sprite workbench configuration is not strict JSON.", error);
        }
    }
}

/// <summary>
/// The retained Engine appearance placement for a screen-centered authoring
/// preview.  The default uses the Engine viewmodel layer because it is
/// camera-relative presentation, not a world entity; all authored placement
/// values remain explicit configuration handles.
/// </summary>
public sealed record SpriteWorkbenchPreviewPlacement(
    ulong EntityId = SpriteWorkbenchPreviewPlacement.DefaultEntityId,
    float PositionX = 0F,
    float PositionY = 0F,
    float PositionZ = 0F,
    float RotationX = 0F,
    float RotationY = 0F,
    float RotationZ = 0F,
    float RotationW = 1F,
    float ScaleX = 1F,
    float ScaleY = 1F,
    float ScaleZ = 1F,
    bool Visible = true,
    RenderLayer Layer = RenderLayer.Viewmodel)
{
    /// <summary>Stable logical owner ID reserved for the workbench preview.</summary>
    public const ulong DefaultEntityId = 0x57524B4250524556UL;

    public static SpriteWorkbenchPreviewPlacement Default { get; } = new();

    internal Transform ToTransform() => new(
        new Vector3(PositionX, PositionY, PositionZ),
        new Quaternion(RotationX, RotationY, RotationZ, RotationW),
        new Vector3(ScaleX, ScaleY, ScaleZ));

    public void Validate()
    {
        if (EntityId == 0
            || !float.IsFinite(PositionX) || !float.IsFinite(PositionY) || !float.IsFinite(PositionZ)
            || !float.IsFinite(RotationX) || !float.IsFinite(RotationY) || !float.IsFinite(RotationZ) || !float.IsFinite(RotationW)
            || !float.IsFinite(ScaleX) || !float.IsFinite(ScaleY) || !float.IsFinite(ScaleZ)
            || ScaleX <= 0F || ScaleY <= 0F || ScaleZ <= 0F
            || (RotationX * RotationX) + (RotationY * RotationY) + (RotationZ * RotationZ) + (RotationW * RotationW) <= 0.000001F
            || !Enum.IsDefined(Layer))
        {
            throw new ArgumentException("Sprite workbench preview placement must contain a stable ID, finite transform, positive scale, and known render layer.", nameof(SpriteWorkbenchPreviewPlacement));
        }
    }
}
