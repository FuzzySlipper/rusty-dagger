using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSkyMediaTests
{
    [Fact]
    public void Selection_uses_complete_climate_weather_season_night_and_mirrored_time_resources()
    {
        var fixture = Fixture();
        var media = DaggerfallSkyMedia.Read(fixture.Content);
        var summer = new DaggerfallCalendar(405,5,0,12,0,0);
        Assert.Equal("sky-2-32", media.Select(226, summer, DaggerfallWeatherKind.Sunny, 0).First);
        Assert.Equal("sky-13-32", media.Select(224, summer, DaggerfallWeatherKind.Thunder, 1).First);
        Assert.Equal("sky-30-32", media.Select(229, summer, DaggerfallWeatherKind.Snow, 0).First);
        Assert.Equal("night-2", media.Select(231, summer with {Hour=0}, DaggerfallWeatherKind.Overcast, 0).First);
        Assert.Equal("sky-21-0", media.Select(231, summer with {Hour=0}, DaggerfallWeatherKind.Fog, 1).First);
        Assert.Equal("sky-16-32", media.Select(227, summer with {Month=0}, DaggerfallWeatherKind.Sunny, 0).First);
        Assert.Equal("particle-snow", media.Particle(true));
        var graphics = new AppearanceFake([]);
        using var resource = media.Open(graphics, "sky-13-32");
        Assert.Equal((DaggerfallSkyMedia.BundleId, "sky-13-32.png"), fixture.Service.OpenedReferences.Single());
        Assert.Single(graphics.OpenResourceContentRequests);
        Assert.Equal(TextureWrap.Clamp, graphics.OpenResourceContentRequests[0].Wrap);
        Assert.Equal(new Color(.1f,.2f,.3f,1), media.ClearColor(media.Select(226, summer, DaggerfallWeatherKind.Sunny,0)));
    }

    [Fact]
    public void Missing_selection_night_or_wrong_artifact_refuses_composition()
    {
        var fixture = Fixture();
        var node = JsonNode.Parse(fixture.Manifest)!;
        node["selections"]!.AsArray().RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => DaggerfallSkyMedia.Read(fixture.WithManifest(node)));
        node = JsonNode.Parse(fixture.Manifest)!;
        node["nightResources"]!.AsArray().RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => DaggerfallSkyMedia.Read(fixture.WithManifest(node)));
        node = JsonNode.Parse(fixture.Manifest)!;
        node["resources"]![0]!["contentHash"] = new string('0',64);
        Assert.Contains("digest", Assert.Throws<InvalidOperationException>(() => DaggerfallSkyMedia.Read(fixture.WithManifest(node))).Message);
    }

    internal static SkyFixture Fixture()
    {
        var service = new BundleContentFake();
        byte[] body = [1];
        string digest = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var resources = new List<object>(); var selections = new List<object>(); var nights = new List<object>(); var particles = new List<object>();
        object Resource(string id, int width, int height, int? night = null, string? kind = null)
        {
            service.Add(DaggerfallSkyMedia.BundleId, id + ".png", body);
            return new {id, relativePath="media/sky/resources/"+id+".png", contentHash=digest, byteLength=1,
                width,height,nightIndex=night,kind,clearColor=new[]{.1f,.2f,.3f}};
        }
        for (int sky=0;sky<32;sky++) for(int frame=0;frame<64;frame++)
        {
            string id=$"sky-{sky}-{frame}";
            resources.Add(Resource(id,1024,512)); selections.Add(new {skyIndex=sky,timeFrame=frame,resourceId=id});
        }
        for (int night=0;night<4;night++) nights.Add(Resource($"night-{night}",1024,512,night));
        particles.Add(Resource("particle-rain",32,64,kind:"rain")); particles.Add(Resource("particle-snow",32,32,kind:"snow"));
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new {resources,selections,nightResources=nights,weatherParticles=particles,
            daylightFrameCurve=new[]{new{time=0f,value=0f,tangent=1f},new{time=.5f,value=.5f,tangent=1f},new{time=1f,value=1f,tangent=1f}}});
        return new(manifest,service);
    }

    internal sealed record SkyFixture(byte[] Manifest, BundleContentFake Service)
    {
        internal ProductContent Content => ContentFor(Manifest);
        internal ProductContent WithManifest(JsonNode node) => ContentFor(Encoding.UTF8.GetBytes(node.ToJsonString()));
        private ProductContent ContentFor(byte[] manifest) => new(new[]{new ProductContentFile(Encoding.UTF8.GetBytes(DaggerfallSkyMedia.ManifestPath),manifest)},Service);
    }
}
