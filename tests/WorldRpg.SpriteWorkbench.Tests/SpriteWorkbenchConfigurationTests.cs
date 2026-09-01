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
