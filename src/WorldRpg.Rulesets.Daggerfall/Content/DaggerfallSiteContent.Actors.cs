using WorldRpg.Kit.Controls;
using Rusty.Engine;
using WorldRpg.Kit;
using System.Globalization;
using System.Text.Json;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal static partial class DaggerfallSiteContent
{
    private static long FixedActorIdentity(string profile, string sourceId) =>
        (long)(StableHash.NonZeroFnv1a64($"daggerfall.fixed-actor:{profile}:{sourceId}") & 0x3fff_ffff_ffffUL) | 0x4000_0000_0000L;

    internal static Dictionary<long, AuthoredActor> ReadFixedActors(ReadOnlyMemory<byte> normalized, string profile,
        DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        using JsonDocument document = JsonDocument.Parse(normalized);
        JsonElement world = DaggerfallBaseContent.Property(document.RootElement, "world", diagnostics);
        Dictionary<long, AuthoredActor> actors = [];
        foreach (JsonElement placement in DaggerfallBaseContent.Array(world, "actors", diagnostics))
        {
            string sourceId = DaggerfallBaseContent.Text(placement, "id", diagnostics);
            string resource = DaggerfallBaseContent.Text(placement, "actorResourceId", diagnostics);
            const string prefix = "actor/mobile-";
            if (!resource.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(resource.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int mobileId)
                || !definitions.Mobiles.Mobiles.TryGetValue(mobileId, out var mobile))
            {
                diagnostics.Add($"Fixed actor '{sourceId}' has unsupported mobile resource '{resource}'.");
                continue;
            }
            int distance = DaggerfallBaseContent.Integer(placement, "spawnDistance", diagnostics);
            string gender = DaggerfallBaseContent.Text(placement, "gender", diagnostics);
            JsonElement passive = DaggerfallBaseContent.Property(placement, "passive", diagnostics);
            if (distance is < 0 or > 6 || gender is not ("unspecified" or "male" or "female")
                || passive.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                diagnostics.Add($"Fixed actor '{sourceId}' has invalid spawn distance, reaction or gender.");
                continue;
            }
            long id = FixedActorIdentity(profile, sourceId);
            var point = ObjectVector3(DaggerfallBaseContent.Property(placement, "position", diagnostics), "fixed actor position", diagnostics);
            var actor = new AuthoredActor(id, DaggerfallEncounterActors.ActorFor(mobile),
                new WorldPoint(point.X, point.Y, point.Z),
                distance, passive.GetBoolean(), gender);
            if (!definitions.Actors.ContainsKey(actor.ActorId)) diagnostics.Add($"Fixed actor '{sourceId}' has no definition '{actor.ActorId}'.");
            if (!actors.TryAdd(id, actor)) diagnostics.Add($"Fixed actor '{sourceId}' collides with identity {id} in '{profile}'.");
        }
        return actors;
    }
}
