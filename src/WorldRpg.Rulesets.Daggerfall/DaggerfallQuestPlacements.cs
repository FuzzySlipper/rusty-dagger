using Rusty.Engine;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallQuestMarkerPreference { Default, QuestSpawn, Any }
internal sealed record DaggerfallQuestAdmittedPlacement(DaggerfallWorldProfileKey Profile, string MarkerId);

/// <summary>One durable placement request; an admitted marker records actual application.</summary>
internal sealed record DaggerfallQuestPlacementOperation(string Id, string ResourceSymbol, string PlaceSymbol,
    int? MarkerIndex = null, DaggerfallQuestMarkerPreference Preference = DaggerfallQuestMarkerPreference.Default)
{
    public DaggerfallQuestAdmittedPlacement? Applied { get; init; }
    [JsonRequired] public bool PendingReapplication { get; init; }
    [JsonRequired] public bool AutomaticHome { get; init; }
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (DaggerfallQuestInstanceSave.Canonical(ResourceSymbol, "placement resource") != ResourceSymbol
            || DaggerfallQuestInstanceSave.Canonical(PlaceSymbol, "placement place") != PlaceSymbol
            || MarkerIndex is < 0 || !Enum.IsDefined(Preference))
            throw new ArgumentException($"Quest placement '{Id}' carries invalid source references.");
        if (PendingReapplication && Applied is null)
            throw new ArgumentException($"Quest placement '{Id}' cannot reapply without its previous admitted marker.");
        if (Applied is { } applied)
        {
            applied.Profile.Validate();
            ArgumentException.ThrowIfNullOrWhiteSpace(applied.MarkerId);
        }
    }
}

/// <summary>The session's canonical world owners apply a quest placement inside an admitted update.</summary>
internal interface IDaggerfallQuestWorldAdmission
{
    DaggerfallQuestResourceBinding? Place(string instanceId, DaggerfallQuestResourceState resource,
        DaggerfallSiteProfile profile, DaggerfallSiteMarker marker);
}

internal static class DaggerfallQuestPlacements
{
    internal static DaggerfallQuestResourceBinding Destination(IEnumerable<DaggerfallQuestResourceState> resources, string symbol)
    {
        foreach (var resource in resources)
        {
            string canonical = DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "placement resource");
            if (canonical == symbol && resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Place)
                return resource.Binding;
            if (canonical + ".home" == symbol && resource.SelectedPerson?.Home is { } home)
                return home.Binding;
        }
        throw new ArgumentException($"Quest placement refers to unavailable Place '{symbol}'.");
    }

    internal static bool Matches(DaggerfallQuestResourceBinding destination, DaggerfallSiteProfile profile)
    {
        if (profile.Site != destination.Places[0].Require() || destination.PlaceSelection?.Kind != profile.ProfileKind) return false;
        if (destination.Building is not { } claim) return profile.InteriorBuilding is null;
        return profile.InteriorBuilding is { } interior && claim.SourceKey == interior.Building.SourceKey
            && claim.Index == interior.Building.Index && claim.BlockX == interior.BlockX && claim.BlockY == interior.BlockY;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private DaggerfallNpcRegistry? _placementNpcs;
    internal void BindPlacementNpcs(DaggerfallNpcRegistry npcs)
    {
        _placementNpcs = npcs;
        foreach (var instance in _instances.Values) ValidateOperationReceipts(instance, Program(instance.SourceFile));
    }
    void IDaggerfallQuestTaskLifecycle.PlaceResource(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, string task, int operationIndex)
    {
        if (operation.Kind == DaggerfallQuestTaskOperationKind.ReservePlace)
        {
            // Place allocation already owns the actual site/building claim. The legacy
            // reservation leaf validates that selection instead of creating a second registry.
            if (DaggerfallQuestPlacements.Destination(instance.Resources, operation.Targets.Single()).PlaceSelection is null)
                throw new ArgumentException($"Quest action at line {operation.SourceLine} has no selected Place profile.");
            return;
        }
        string symbol = operation.Targets[0];
        var resource = instance.Resources.SingleOrDefault(value => value.Symbol == symbol)
            ?? throw new ArgumentException($"Quest placement at line {operation.SourceLine} has no resource '{symbol}'.");
        bool correctKind = operation.Kind switch
        {
            DaggerfallQuestTaskOperationKind.PlaceFoe => resource.SelectedFoe is not null,
            DaggerfallQuestTaskOperationKind.PlaceItem => resource.SelectedItem is not null,
            DaggerfallQuestTaskOperationKind.PlaceNpc => resource.SelectedPerson is not null,
            _ => false,
        };
        if (!correctKind) throw new ArgumentException($"Quest placement at line {operation.SourceLine} does not name the required resource kind.");
        if (resource.IsNpcDestroyed) return;
        if (resource.SelectedPerson is not null && resource.Binding.ActorIds.Length == 1
            && _placementNpcs?.Require(resource.Binding.ActorIds[0]).Presence == DaggerfallNpcPresence.Removed) return;
        if (resource.SelectedPerson?.Individual == true && _definitions.QuestSources.Resources.Single(value => value.SourceFile == instance.SourceFile
            && value.CanonicalId == symbol && value.Kind == "person").Person?.AtHome == true)
        {
            System.Diagnostics.Trace.TraceWarning($"Quest placement at line {operation.SourceLine} retains individual Person '{symbol}' at its declared Home.");
            return;
        }
        RequestPlacement(instance.InstanceId, $"task:{task}:{operationIndex}", symbol, operation.Targets[1], operation.MarkerIndex, operation.MarkerPreference, reapply: true);
        if (resource.SelectedPerson is not null && resource.IsHidden) SetResource(instance.InstanceId, resource with { IsHidden = false });
    }

    /// <summary>Task actions and Person homes use this same durable queue before their target site exists.</summary>
    internal void RequestPlacement(string instanceId, string operationId, string resourceSymbol, string placeSymbol,
        int? markerIndex = null, DaggerfallQuestMarkerPreference preference = DaggerfallQuestMarkerPreference.Default, bool automaticHome = false, bool reapply = false)
    {
        var instance = Active(instanceId);
        string resource = DaggerfallQuestInstanceSave.Canonical(resourceSymbol, "placement resource");
        string place = DaggerfallQuestInstanceSave.Canonical(placeSymbol, "placement place");
        var operation = new DaggerfallQuestPlacementOperation(operationId, resource, place, markerIndex, preference) { AutomaticHome = automaticHome };
        operation.Validate();
        var selected = instance.Resources.SingleOrDefault(value => DaggerfallQuestInstanceSave.Canonical(value.Symbol, "placement resource") == resource)
            ?? throw new ArgumentException($"Quest placement refers to unavailable resource '{resource}'.");
        if (selected.SelectedPerson is null && selected.SelectedFoe is null && selected.SelectedItem is null)
            throw new ArgumentException($"Quest resource '{resource}' has no selected world meaning to place.");
        if (DaggerfallQuestPlacements.Destination(instance.Resources, place).PlaceSelection is null)
            throw new ArgumentException($"Quest Place '{place}' requires its selected profile kind before placement.");
        if (instance.Placements.SingleOrDefault(value => value.Id == operationId) is { } existing)
        {
            if (existing with { Applied = null, PendingReapplication = false } != operation)
                throw new ArgumentException($"Quest placement '{operationId}' already names a different operation.");
            // A retry of pending work is idempotent. An action the task runner has
            // explicitly rearmed repeats assignment through this same queue entry,
            // retaining the last actual marker for subsequent no-index assignment.
            if (reapply && existing.Applied is not null)
                instance.Placements = instance.Placements.Select(value => value.Id == operationId ? existing with { PendingReapplication = true } : value).ToArray();
            return;
        }
        if (!automaticHome)
            instance.Placements = [.. instance.Placements.Where(value => value.ResourceSymbol != resource || !value.AutomaticHome || value.Applied is not null)];
        instance.Placements = [.. instance.Placements, operation];
    }

    /// <summary>Consumes queued requests once the matching actual profile is admitted.</summary>
    internal void AdmitPlacements(DaggerfallSiteProfile profile, IDaggerfallQuestWorldAdmission world)
    {
        foreach (var instance in _instances.Values.Where(value => value.Lifecycle == DaggerfallQuestLifecycle.Active))
            for (int index = 0; index < instance.Placements.Length; index++)
            {
                var operation = instance.Placements[index];
                if (operation.Applied is not null && !operation.PendingReapplication) continue;
                var destination = DaggerfallQuestPlacements.Destination(instance.Resources, operation.PlaceSymbol);
                if (!DaggerfallQuestPlacements.Matches(destination, profile)) continue;
                var resource = instance.Resources.Single(value => DaggerfallQuestInstanceSave.Canonical(value.Symbol, "placement resource") == operation.ResourceSymbol);
                if (resource.SelectedPerson is not null && (resource.IsHidden || resource.IsNpcDestroyed)) continue;
                var marker = SelectPlacementMarker(instance, operation, resource, profile);
                var binding = world.Place(instance.InstanceId, resource, profile, marker);
                if (binding is null)
                {
                    // A known removed Person is intentionally absent, never an applied placement.
                    instance.Placements = instance.Placements.Where(value => value.Id != operation.Id).ToArray();
                    index--;
                    continue;
                }
                binding.Validate(resource.Symbol);
                if (binding.Kind != (resource.SelectedItem is null ? DaggerfallQuestResourceBindingKind.Actor : DaggerfallQuestResourceBindingKind.Item))
                    throw new InvalidOperationException($"Quest resource '{resource.Symbol}' was not admitted to its actual world owner.");
                instance.Resources = instance.Resources.Select(value => value.Symbol == resource.Symbol ? value with { Binding = binding } : value).ToArray();
                instance.Placements[index] = operation with { Applied = new(profile.ProfileKey, marker.Id), PendingReapplication = false };
            }
    }

    private DaggerfallSiteMarker SelectPlacementMarker(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestPlacementOperation operation,
        DaggerfallQuestResourceState resource, DaggerfallSiteProfile profile)
    {
        if (operation.MarkerIndex is null && instance.Placements.FirstOrDefault(value => value.PlaceSymbol == operation.PlaceSymbol && value.Applied?.Profile == profile.ProfileKey)?.Applied is { } previous)
            return profile.QuestMarkers.SingleOrDefault(value => value.Id == previous.MarkerId)
                ?? throw new InvalidOperationException($"Admitted quest marker '{previous.MarkerId}' is absent from '{profile.ProfileKey.LogicalId}'.");
        var preferred = resource.SelectedItem is not null && operation.Preference != DaggerfallQuestMarkerPreference.QuestSpawn
            ? DaggerfallSiteMarkerKind.QuestItem : DaggerfallSiteMarkerKind.QuestSpawn;
        // Source GetSiteMarker orders the combined pool spawn-first. Any ignores an index
        // for initial selection; subsequent explicit indices still address the preferred pool.
        var spawn = profile.QuestMarkers.Where(value => value.Kind == DaggerfallSiteMarkerKind.QuestSpawn).ToArray();
        var items = profile.QuestMarkers.Where(value => value.Kind == DaggerfallSiteMarkerKind.QuestItem).ToArray();
        bool alreadySelected = instance.Placements.Any(value => value.PlaceSymbol == operation.PlaceSymbol && value.Applied?.Profile == profile.ProfileKey);
        var markers = preferred == DaggerfallSiteMarkerKind.QuestSpawn ? spawn : items;
        int? markerIndex = operation.MarkerIndex;
        if (!alreadySelected && operation.Preference == DaggerfallQuestMarkerPreference.Any)
        {
            markers = [.. spawn, .. items];
            markerIndex = null;
        }
        else if (!alreadySelected && markers.Length == 0)
        {
            markers = spawn.Length > 0 ? spawn : items;
            markerIndex = null;
        }
        if (markers.Length == 0) throw new NotSupportedException($"Place '{operation.PlaceSymbol}' has no published quest marker for resource '{resource.Symbol}'.");
        int selected = markerIndex ?? checked((int)_random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.quest.marker",
            instance.InstanceId + "/" + operation.PlaceSymbol, 0, markers.Length - 1)).Value);
        if (selected >= markers.Length) throw new ArgumentException($"Quest marker index {selected} exceeds the {markers.Length} admitted markers at '{operation.PlaceSymbol}'.");
        return markers[selected];
    }
}
