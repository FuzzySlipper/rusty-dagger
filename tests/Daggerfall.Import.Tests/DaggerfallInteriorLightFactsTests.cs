using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>The lamp flats' lights: which burn, where each light sits against its flame, and one light for each dungeon lamp.</summary>
public sealed class DaggerfallInteriorLightFactsTests
{
    private static readonly NormalizedVector3 Centre = new(0F, 2F, 0F);

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(12)]
    public void An_extinguished_lamp_gives_no_light(int record) =>
        Assert.False(DaggerfallInteriorLightFacts.TryProject("light-flat/test/0", DaggerfallInteriorLightFacts.Archive, record, Centre, 1F, out _));

    /// <summary>Where DFU left the offset to do, the light sits at the flame the art draws, not at the sprite's centre.</summary>
    [Theory]
    [InlineData(16, .2F)]
    [InlineData(18, .2F)]
    [InlineData(19, .45F)]
    [InlineData(26, -.5F)]
    [InlineData(14, .7F)]
    [InlineData(15, .75F)]
    [InlineData(28, 1.6F)]
    [InlineData(29, 1.5F)]
    public void A_lamp_lights_from_its_flame(int record, float aboveCentre)
    {
        Assert.True(DaggerfallInteriorLightFacts.TryProject("light-flat/test/0", DaggerfallInteriorLightFacts.Archive, record, Centre, 1.9F, out NormalizedLightPlacement light));
        Assert.Equal(Centre.Y + aboveCentre, light.Position.Y, 4);
    }

    [Fact]
    public void A_dungeon_lights_each_lamp_once_by_its_farther_reaching_light()
    {
        NormalizedLightPlacement torch = Light("light-flat/torch", 0F, 10F);
        NormalizedLightPlacement torchRecord = Light("light/torch-record", .6F, 6F);
        NormalizedLightPlacement candleRecord = Light("light/candle-record", 20.5F, 7.5F);
        NormalizedLightPlacement candle = Light("light-flat/candle", 20F, 10F / 3F);
        NormalizedLightPlacement otherCandle = Light("light-flat/other-candle", 21.5F, 10F / 3F);
        NormalizedLightPlacement lone = Light("light/lone", 40F, 5F);
        NormalizedLightPlacement farLamp = Light("light-flat/far", 43F, 10F);

        IReadOnlyList<NormalizedLightPlacement> lights = DaggerfallInteriorLightFacts.OnePerLamp(
            [torchRecord, candleRecord, lone], [torch, candle, otherCandle, farLamp]);

        // The torch outreaches its record and stands for both; the record outreaches its candles and lights them; a record
        // with no lamp within two metres, and a lamp three metres from it, each light alone.
        Assert.Equal(["light/candle-record", "light/lone", "light-flat/torch", "light-flat/far"], lights.Select(light => light.Id));
    }

    private static NormalizedLightPlacement Light(string id, float x, float range) => new(id, new(x, 2F, 0F), range, 1F, new(1F, 1F, 1F));
}
