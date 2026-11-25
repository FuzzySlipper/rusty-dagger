using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The developer's readout of the expedition map's bands and its staging of one on demand.</summary>
internal sealed class DaggerfallExpeditionBandReadout(
    DaggerfallExpedition expedition,
    DaggerfallOverlandPartyCoordinator? overlandParties = null,
    ActorsState? actors = null,
    DaggerfallNpcRegistry? npcs = null) : IDeveloperTravelMapCommands
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
                w.WriteBoolean("persistent", band.PersistentBandId is not null);
                if (band.PersistentBandId is not null)
                    w.WriteString("bandId", band.PersistentBandId);
                if (band.ConditionTokenId is not null)
                    w.WriteString("conditionTokenId", band.ConditionTokenId);
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

    public DebugCommandResult DispatchPersistentBand(string bandId, double distance, double bearing)
    {
        if (string.IsNullOrWhiteSpace(bandId) || !(distance is >= .1 and <= 10) || !double.IsFinite(bearing))
            return DaggerfallDeveloperReplies.Refused("map.dispatch-persistent takes BAND_ID, DISTANCE (0.1 to 10) and BEARING in degrees.");
        if (!expedition.Open || overlandParties is null || actors is null || npcs is null)
            return DaggerfallDeveloperReplies.Refused("Open the travel map outdoors first.");

        DaggerfallNpc? candidate = npcs.All.FirstOrDefault(npc =>
            npc.Presence == DaggerfallNpcPresence.Active
            && actors.TryGet(npc.DurableId, out ActorState? live)
            && !live.IsDefeated);
        if (candidate is null)
            return DaggerfallDeveloperReplies.Refused("No live active person is available here to dispatch.");

        (double px, double py) = expedition.Position;
        double radians = ((bearing % 360 + 360) % 360) * Math.PI / 180;
        double x = px + Math.Sin(radians) * distance;
        double y = py - Math.Cos(radians) * distance;
        DaggerfallTravelMapPixel pixel = new((int)Math.Floor(x), (int)Math.Floor(y));
        if (!pixel.IsInBounds)
            return DaggerfallDeveloperReplies.Refused("That point lies outside the map bounds.");

        DaggerfallExpeditionMark? destination = expedition.Pin;
        DaggerfallWorldProfileKey? destProfile = destination?.Place is { } destSite
            ? DaggerfallWorldProfileIds.Exterior(destSite)
            : null;
        string name = $"{candidate.DisplayName ?? candidate.Role}'s Party";
        _ = overlandParties.DispatchOverlandBand(
            bandId.Trim(), name, DaggerfallBandKind.Travellers, [candidate.DurableId],
            x, y, speedPixelsPerHour: 0.35, destination, destProfile, camp: destination is null);
        return Bands();
    }
}
