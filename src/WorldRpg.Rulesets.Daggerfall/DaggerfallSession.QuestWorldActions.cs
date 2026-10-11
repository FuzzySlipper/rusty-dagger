using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private bool ApplyQuestWorldAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        if (operation.Kind == DaggerfallQuestTaskOperationKind.WorldUpdate)
        {
            var update = operation.WorldUpdate!;
            var worldProfiles = _sites.Profiles ?? throw new NotSupportedException("WorldUpdate requires an admitted site catalog.");
            DaggerfallSiteId? site = null;
            if (update.Kind == "locationnew")
            {
                var named = Site.Records.Where(value => value.Region == update.Region && value.Name == update.LocationName).ToArray();
                // The donor logs an unresolved new location and continues; nothing is selected.
                if (named.Length != 1)
                {
                    System.Diagnostics.Trace.TraceWarning($"WorldUpdate location '{update.LocationName}' in region {update.Region} resolves to {named.Length} admitted locations; no variant is set.");
                    return true;
                }
                site = named[0].Id;
            }
            else if (update.Region is int region && update.Location is int location)
            {
                site = new(region, location);
                _ = Site.Records.SingleOrDefault(value => value.Id == site) ?? throw new NotSupportedException($"WorldUpdate location '{site}' is absent from the canonical catalog.");
            }
            if (update.Kind is "location" or "locationnew") worldProfiles.SetLocationVariant(site!.Value, update.Variant);
            else worldProfiles.SetBlockVariant(site, update.Block!, update.Building, update.Variant);
            return true;
        }
        if (operation.Kind == DaggerfallQuestTaskOperationKind.RevealPlace)
        {
            var revealBinding = DaggerfallQuestPlacements.Destination(instance.Resources, operation.Targets[0]);
            var targetSite = revealBinding.Places.Single().Require();
            var location = Site.Records.SingleOrDefault(value => value.Id == targetSite)
                ?? throw new NotSupportedException($"Reveal location '{targetSite}' is absent from the canonical catalog.");
            string? note = operation.Step == 1 ? _definitions.Text.RequireInternalEntry("readMap", 0).Replace("%map", location.Name, StringComparison.Ordinal) : null;
            Site.Discover(location.Id);
            if (note is not null) _notebook.Add(note);
            return true;
        }
        var binding = DaggerfallQuestPlacements.Destination(instance.Resources, operation.Targets[0]);
        DaggerfallSiteId place = binding.Places[0].Require();
        DaggerfallWorldProfileKey? key = DaggerfallQuestPlacements.ProfileKey(binding);
        DaggerfallSiteProfile? destination = key is not { } id ? null
            : _sites.Profiles is { } profiles ? (profiles.TryGet(id, out DaggerfallSiteProfile resolved) ? resolved : null)
            : _sites.Projection.Inputs.ProfileKey == id ? _sites.Projection.Inputs : null;
        if (destination is null || !DaggerfallQuestPlacements.Matches(binding, destination))
            throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no admitted world profile.");
        var markers = DaggerfallQuestPlacements.SourceOrder(destination, _site.Require(place), DaggerfallSiteMarkerKind.QuestSpawn);
        if (markers.Length == 0) throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no admitted spawn marker.");
        int index = operation.MarkerIndex is { } requested && requested < markers.Length ? requested : 0;
        var marker = markers[index];
        if (!_sites.TryRelocatePlayer(destination.ProfileKey, new("quest-teleport", marker.Position, 0, 0))) return false;
        _sites.ClearReturnDestination();
        return true;
    }
}
