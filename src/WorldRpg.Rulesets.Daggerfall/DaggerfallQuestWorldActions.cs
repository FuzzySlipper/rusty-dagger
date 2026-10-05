using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestWorldUpdate(string Kind, int? Region, int? Location, string? LocationName, string? Block, int? Building, string Variant);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly Regex DroppedAt = Header(@"^dropped\s+(?<item>[a-zA-Z0-9_.-]+)\s+at\s+(?<place>[a-zA-Z0-9_.-]+)(?:\s+saying\s+(?<message>\d+))?$");
    private static readonly Regex RevealPlace = Header(@"^reveal\s+(?<place>[a-zA-Z0-9_.-]+)(?:\s+(?<map>readmap))?$");
    private static readonly Regex TeleportPlace = Header(@"^(?:teleport\s+pc\s+to\s+(?<place>[a-zA-Z0-9_.-]+)|transfer\s+pc\s+inside\s+(?<place>[a-zA-Z0-9_.-]+)\s+marker\s+(?<marker>\d+))$");
    private static readonly Regex[] WorldUpdates =
    [
        Header(@"^worldupdate\s+(?<kind>location)\s+at\s+(?<location>\d+)\s+in\s+region\s+(?<region>\d+)\s+variant\s+(?<variant>[a-zA-Z0-9_.-]+)$"),
        Header(@"^worldupdate\s+(?<kind>locationnew)\s+named\s+(?<name>.+)\s+in\s+region\s+(?<region>\d+)\s+variant\s+(?<variant>[a-zA-Z0-9_.-]+)$"),
        Header(@"^worldupdate\s+(?<kind>block|building)\s+(?<block>[a-zA-Z0-9_.-]+)(?:\s+(?<building>\d+))?\s+at\s+(?<location>\d+)\s+in\s+region\s+(?<region>\d+)\s+variant\s+(?<variant>[a-zA-Z0-9_.-]+)$"),
        Header(@"^worldupdate\s+(?<kind>blockAll|buildingAll)\s+(?<block>[a-zA-Z0-9_.-]+)(?:\s+(?<building>\d+))?\s+variant\s+(?<variant>[a-zA-Z0-9_.-]+)$"),
    ];
    private static DaggerfallQuestTaskOperation? CompileWorldAction(string line, int sourceLine)
    {
        if (DroppedAt.Match(line) is { Success: true } drop)
            return new(DaggerfallQuestTaskOperationKind.DroppedAt, sourceLine, line, [Canonical(drop.Groups["item"].Value), Canonical(drop.Groups["place"].Value)], [],
                drop.Groups["message"].Success ? Step(drop.Groups["message"].Value, sourceLine) : null);
        if (RevealPlace.Match(line) is { Success: true } reveal)
            return new(DaggerfallQuestTaskOperationKind.RevealPlace, sourceLine, line, [Canonical(reveal.Groups["place"].Value)], [], null, Step: reveal.Groups["map"].Success ? 1 : 0);
        if (TeleportPlace.Match(line) is { Success: true } teleport)
            return new(DaggerfallQuestTaskOperationKind.TeleportPlace, sourceLine, line, [Canonical(teleport.Groups["place"].Value)], [], null,
                MarkerIndex: teleport.Groups["marker"].Success ? Step(teleport.Groups["marker"].Value, sourceLine) : null);
        foreach (var pattern in WorldUpdates)
        {
            var match = pattern.Match(line);
            if (!match.Success) continue;
            string kind = match.Groups["kind"].Value.ToLowerInvariant();
            if (kind.StartsWith("building", StringComparison.Ordinal) != match.Groups["building"].Success && kind is not ("location" or "locationnew")) continue;
            int? Number(string name) => match.Groups[name].Success ? Step(match.Groups[name].Value, sourceLine) : null;
            string? Text(string name) => match.Groups[name].Success ? match.Groups[name].Value : null;
            return new(DaggerfallQuestTaskOperationKind.WorldUpdate, sourceLine, line, [], [], null,
                WorldUpdate: new(kind, Number("region"), Number("location"), Text("name"), Text("block"), Number("building"), match.Groups["variant"].Value));
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
