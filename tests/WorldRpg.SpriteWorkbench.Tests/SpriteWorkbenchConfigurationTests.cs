using Xunit;

namespace WorldRpg.SpriteWorkbench.Tests;

public sealed class SpriteWorkbenchConfigurationTests
{
    [Fact]
    public void ReadsOnlyStrictExplicitAuthoringConfiguration()
    {
        SpriteWorkbenchConfiguration value = SpriteWorkbenchConfiguration.Read("""
            { "publicationSeparationRoot": "/tmp/generated", "authoringRoot": "/tmp/authored", "overlayPath": "sprites/overlays.json" }
            """u8);

        Assert.Equal("/tmp/generated", value.PublicationSeparationRoot);
        // Browser presentation IDs must survive the renderer's safe-integer contract.
        Assert.InRange(value.PreviewPlacement.EntityId, 1UL, 9_007_199_254_740_991UL);
        Assert.True(value.PreviewPlacement.PositionZ < 0, "The preview must sit in front of the camera.");
        Assert.Throws<FormatException>(() => SpriteWorkbenchConfiguration.Read("""
            { "publicationSeparationRoot": "/tmp/generated", "authoringRoot": "/tmp/authored", "overlayPath": "sprites/overlays.json", "unknown": true }
            """u8));
    }

    [Fact]
    public void RejectsGeneratedAndAuthoredRootOverlap()
    {
        Assert.Throws<ArgumentException>(() => new SpriteWorkbenchConfiguration("/tmp/generated", "/tmp/generated/authored", "sprites/overlays.json").Validate());
    }
}
