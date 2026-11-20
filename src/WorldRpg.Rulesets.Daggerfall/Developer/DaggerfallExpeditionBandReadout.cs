using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The developer's readout of the expedition map's bands and its staging of one on demand.</summary>
internal sealed class DaggerfallExpeditionBandReadout(DaggerfallExpedition expedition) : IDeveloperTravelMapCommands
{
    public DebugCommandResult Bands()
    {
        if (!expedition.Open) return DaggerfallDeveloperReplies.Refused("The travel map is not open.");
        double partySight = expedition.PartySight, bandSight = expedition.BandSight;
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("partySight", Math.Round(partySight, 2));
            w.WriteNumber("bandSight", Math.Round(bandSight, 2));
            w.WriteStartArray("bands");
            foreach (DaggerfallExpeditionBandView band in expedition.AllBands.OrderBy(band => band.Distance))
            {
                w.WriteStartObject();
                w.WriteNumber("id", band.Id);
                w.WriteString("name", band.Name);
                w.WriteString("kind", band.Kind.ToString().ToLowerInvariant());
                w.WriteBoolean("camp", band.Camp);
                w.WriteNumber("x", Math.Round(band.X, 2));
                w.WriteNumber("y", Math.Round(band.Y, 2));
                w.WriteNumber("distance", Math.Round(band.Distance, 2));
                w.WriteNumber("sight", Math.Round(bandSight, 2));
                w.WriteBoolean("seen", band.Distance <= partySight);
                w.WriteBoolean("spotted", band.Spotted);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult StageBand(double distance, double bearing, string staging)
    {
        if (!(distance is >= .3 and <= 10) || !double.IsFinite(bearing) || staging is not ("camp" or "wander"))
            return DaggerfallDeveloperReplies.Refused("map.stage-band takes DISTANCE 0.3 to 10 map pixels, BEARING in degrees and camp or wander.");
        return expedition.StageBand(distance, (bearing % 360 + 360) % 360, staging == "camp") is { } refusal
            ? DaggerfallDeveloperReplies.Refused(refusal)
            : Bands();
    }
}
