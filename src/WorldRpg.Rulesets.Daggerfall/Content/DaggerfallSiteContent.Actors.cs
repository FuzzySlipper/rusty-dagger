using WorldRpg.Kit.Controls;
using Rusty.Engine;
using WorldRpg.Kit;
using System.Globalization;
using System.Text.Json;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal static partial class DaggerfallSiteContent
{
    /// <summary>The durable identity of a location's fixed source actor: its profile id and its placed source identity.</summary>
    internal static long FixedActorIdentity(string profile, string sourceId) =>
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

    /// <summary>
    /// A fixed actor's sprite: its mobile's media in the actor's gender, with the authored actor's presentation over it
    /// (the state it rests in and any playback rate it overrides), each refused unless the media publishes that state.
    /// </summary>
    internal static NormalizedActorSprite ActorPresentation(AuthoredActor actor, DaggerfallActorDefinition definition, NormalizedActorSprite sprite,
        DaggerfallContentDiagnostics diagnostics)
    {
        sprite = sprite.ForGender(actor.Gender);
        DaggerfallActorPresentationDefinition presentation = definition.Presentation;
        if (presentation.PreferredRestState is not null && !sprite.States.ContainsKey(presentation.PreferredRestState))
            diagnostics.Add($"Actor '{actor.ActorId.Value}' preferredRestState '{presentation.PreferredRestState}' is not published by its normalized media.");
        foreach (string state in presentation.EffectiveFramesPerSecond.Keys)
            if (!sprite.States.ContainsKey(state)) diagnostics.Add($"Actor '{actor.ActorId.Value}' effective playback override '{state}' is not published by its normalized media.");
        IReadOnlyDictionary<string, NormalizedSpriteState> states = new System.Collections.ObjectModel.ReadOnlyDictionary<string, NormalizedSpriteState>(sprite.States.ToDictionary(
            pair => pair.Key,
            pair => presentation.EffectiveFramesPerSecond.TryGetValue(pair.Key, out float framesPerSecond)
                ? pair.Value with { EffectiveFramesPerSecond = framesPerSecond }
                : pair.Value,
            StringComparer.Ordinal));
        return sprite with { PreferredRestState = presentation.PreferredRestState ?? sprite.PreferredRestState, States = states };
    }
}
