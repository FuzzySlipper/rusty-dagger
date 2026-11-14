using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestWorldUpdate(string Kind, int? Region, int? Location, string? LocationName, string Variant);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly Regex DroppedAt = Header(@"^dropped\s+(?<item>[a-zA-Z0-9_.-]+)\s+at\s+(?<place>[a-zA-Z0-9_.-]+)(?:\s+saying\s+(?<message>\d+))?$");
    private static readonly Regex RevealPlace = Header(@"^reveal\s+(?<place>[a-zA-Z0-9_.-]+)(?:\s+(?<map>readmap)|\s+in\s+province\s+(?<region>\d+)\s+at\s+(?<location>\d+))?$");
    private static readonly Regex TeleportPlace = Header(@"^(?:teleport\s+pc\s+to\s+(?<place>[a-zA-Z0-9_.-]+)|transfer\s+pc\s+inside\s+(?<place>[a-zA-Z0-9_.-]+)\s+marker\s+(?<marker>\d+))$");
    private static readonly Regex[] WorldUpdates =
    [
        Header(@"^worldupdate\s+(?<kind>location)\s+at\s+(?<location>\d+)\s+in\s+region\s+(?<region>\d+)\s+variant\s+(?<variant>[a-zA-Z0-9_.-]+)$"),
        Header(@"^worldupdate\s+(?<kind>locationnew)\s+named\s+(?<name>.+)\s+in\s+region\s+(?<region>\d+)\s+variant\s+(?<variant>[a-zA-Z0-9_.-]+)$"),
    ];
    private static DaggerfallQuestTaskOperation? CompileWorldAction(string line, int sourceLine)
    {
        if (DroppedAt.Match(line) is { Success: true } drop)
            return new(DaggerfallQuestTaskOperationKind.DroppedAt, sourceLine, line, [Canonical(drop.Groups["item"].Value), Canonical(drop.Groups["place"].Value)], [],
                drop.Groups["message"].Success ? Step(drop.Groups["message"].Value, sourceLine) : null);
        if (RevealPlace.Match(line) is { Success: true } reveal)
            return new(DaggerfallQuestTaskOperationKind.RevealPlace, sourceLine, line,
                reveal.Groups["region"].Success ? [Canonical(reveal.Groups["place"].Value), reveal.Groups["region"].Value, reveal.Groups["location"].Value]
                    : [Canonical(reveal.Groups["place"].Value)], [], null, Step: reveal.Groups["map"].Success ? 1 : 0);
        if (TeleportPlace.Match(line) is { Success: true } teleport)
            return new(DaggerfallQuestTaskOperationKind.TeleportPlace, sourceLine, line, [Canonical(teleport.Groups["place"].Value)], [], null,
                MarkerIndex: teleport.Groups["marker"].Success ? Step(teleport.Groups["marker"].Value, sourceLine) : null);
        foreach (var pattern in WorldUpdates)
        {
            var match = pattern.Match(line);
            if (!match.Success) continue;
            string kind = match.Groups["kind"].Value.ToLowerInvariant();
            int? Number(string name) => match.Groups[name].Success ? Step(match.Groups[name].Value, sourceLine) : null;
            string? Text(string name) => match.Groups[name].Success ? match.Groups[name].Value : null;
            return new(DaggerfallQuestTaskOperationKind.WorldUpdate, sourceLine, line, [], [], null,
                WorldUpdate: new(kind, Number("region"), Number("location"), Text("name"), match.Groups["variant"].Value));
        }
        return null;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestTaskOperation, bool>? _worldAction;
    internal void BindWorldActions(Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestTaskOperation, bool> apply) => _worldAction = apply;
    bool IDaggerfallQuestTaskLifecycle.WorldAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) =>
        (_worldAction ?? throw new InvalidOperationException("Quest world actions are not composed."))(instance, operation);

    private IEnumerable<(DaggerfallQuestRuntimeInstance Instance, DaggerfallQuestTaskRuntimeState State, int Index, DaggerfallQuestTaskOperation Operation)> DropWatchers(DaggerfallItemInstanceMetadata item)
    {
        if (item.QuestId is not { } quest || item.QuestItemSymbol is not { } symbol || !_instances.TryGetValue(quest, out var instance)
            || instance.Lifecycle != DaggerfallQuestLifecycle.Active) yield break;
        var program = Program(instance.SourceFile);
        for (int task = 0; task < program.Tasks.Count; task++)
            for (int index = 0; index < program.Tasks[task].Operations.Count; index++)
            {
                var operation = program.Tasks[task].Operations[index];
                if (!instance.Tasks[task].IsDropped && operation.Kind == DaggerfallQuestTaskOperationKind.DroppedAt && operation.Targets[0] == symbol
                    && !instance.Tasks[task].OperationCompleted[index])
                    yield return (instance, instance.Tasks[task], index, operation);
            }
    }
    internal string? CanDropItem(DaggerfallItemInstanceMetadata item)
    {
        var watchers = DropWatchers(item).ToArray();
        return watchers.Length == 0 || watchers.Any(watch => IsAtPlace(watch.Instance, watch.Operation.Targets[1]))
            ? null : "This quest item must be dropped at its designated place.";
    }
    internal void ItemDropped(DaggerfallItemInstanceMetadata item)
    {
        foreach (var watch in DropWatchers(item))
            if (IsAtPlace(watch.Instance, watch.Operation.Targets[1]))
                watch.State.OperationState[watch.Index] = watch.State.OperationState[watch.Index] with { ItemDropped = true };
    }
    private bool IsAtPlace(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        var binding = DaggerfallQuestPlacements.Destination(instance.Resources, symbol);
        var current = WorldRead();
        return binding.PlaceSelection?.Kind == DaggerfallWorldProfileKind.Exterior
            ? current.Profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && current.ExteriorLocation?.Id == binding.Places[0].Require()
            : DaggerfallQuestPlacements.Matches(binding, current.Profile);
    }
}

/// <summary>
/// What a quest does to the world: a location's variant (a world update), revealing a place on the map with its note, and
/// sending the player to a quest place's spawn marker.
/// </summary>
internal sealed class DaggerfallQuestWorldActions(DaggerfallSiteContext directory, DaggerfallSiteCrossing sites, DaggerfallDefinitions definitions,
    DaggerfallBookNotebook notebook)
{
    internal bool Apply(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        if (operation.Kind == DaggerfallQuestTaskOperationKind.WorldUpdate)
        {
            var update = operation.WorldUpdate!;
            var worldProfiles = sites.Profiles ?? throw new NotSupportedException("WorldUpdate requires an admitted site catalog.");
            DaggerfallSiteId site;
            if (update.Kind == "locationnew")
            {
                var named = directory.Records.Where(value => value.Region == update.Region && value.Name == update.LocationName).ToArray();
                // The donor logs an unresolved new location and continues; nothing is selected.
                if (named.Length != 1)
                {
                    System.Diagnostics.Trace.TraceWarning($"WorldUpdate location '{update.LocationName}' in region {update.Region} resolves to {named.Length} admitted locations; no variant is set.");
                    return true;
                }
                site = named[0].Id;
            }
            else
            {
                site = new(update.Region!.Value, update.Location!.Value);
                _ = directory.Records.SingleOrDefault(value => value.Id == site) ?? throw new NotSupportedException($"WorldUpdate location '{site}' is absent from the canonical catalog.");
            }
            worldProfiles.SetLocationVariant(site, update.Variant);
            return true;
        }
        if (operation.Kind == DaggerfallQuestTaskOperationKind.RevealPlace)
        {
            var revealBinding = DaggerfallQuestPlacements.Destination(instance.Resources, operation.Targets[0]);
            var targetSite = revealBinding.Places.Single().Require();
            var location = directory.Records.SingleOrDefault(value => value.Id == targetSite)
                ?? throw new NotSupportedException($"Reveal location '{targetSite}' is absent from the canonical catalog.");
            string? note = operation.Step == 1 ? definitions.Text.RequireInternalEntry("readMap", 0).Replace("%map", location.Name, StringComparison.Ordinal) : null;
            directory.Discover(location.Id);
            if (note is not null) notebook.Add(note);
            return true;
        }
        var binding = DaggerfallQuestPlacements.Destination(instance.Resources, operation.Targets[0]);
        DaggerfallSiteId place = binding.Places[0].Require();
        DaggerfallWorldProfileKey? key = DaggerfallQuestPlacements.ProfileKey(binding);
        DaggerfallSiteProfile? destination = key is not { } id ? null
            : sites.Profiles is { } profiles ? (profiles.TryGet(id, out DaggerfallSiteProfile resolved) ? resolved : null)
            : sites.Projection.Inputs.ProfileKey == id ? sites.Projection.Inputs : null;
        if (destination is null || !DaggerfallQuestPlacements.Matches(binding, destination))
            throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no admitted world profile.");
        var markers = DaggerfallQuestPlacements.SourceOrder(destination, directory.Require(place), DaggerfallSiteMarkerKind.QuestSpawn);
        if (markers.Length == 0) throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no admitted spawn marker.");
        int index = operation.MarkerIndex is { } requested && requested < markers.Length ? requested : 0;
        var marker = markers[index];
        if (!sites.TryRelocatePlayer(destination.ProfileKey, new("quest-teleport", marker.Position, 0, 0))) return false;
        sites.ClearReturnDestination();
        return true;
    }
}
