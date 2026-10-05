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
            if (update.Kind != "location") throw new NotSupportedException($"WorldUpdate '{update.Kind}' requires a normalized variant owner that is not admitted; supported form is location.");
            var site = new DaggerfallSiteId(update.Region!.Value, update.Location!.Value);
            _ = Site.Records.SingleOrDefault(value => value.Id == site) ?? throw new NotSupportedException($"WorldUpdate location '{site}' is absent from the canonical catalog.");
            (_sites.Profiles ?? throw new NotSupportedException("WorldUpdate requires an admitted site catalog.")).SetLocationVariant(site, update.Variant);
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
        var profiles = _sites.Profiles?.Keys.Select(key => _sites.Profiles.Require(key)).ToArray() ?? [_sites.Projection.Inputs];
        var destinations = profiles.Where(profile => DaggerfallQuestPlacements.Matches(binding, profile)).ToArray();
        if (destinations.Length != 1) throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no unique admitted world profile.");
        var destination = destinations[0];
        var markers = destination.QuestMarkers.Where(marker => marker.Kind == DaggerfallSiteMarkerKind.QuestSpawn).ToArray();
        if (markers.Length == 0) throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no admitted spawn marker.");
        int index = operation.MarkerIndex is { } requested && requested < markers.Length ? requested : 0;
        var marker = markers[index];
        if (!_sites.TryRelocatePlayer(destination.ProfileKey, new("quest-teleport", marker.Position, 0, 0))) return false;
        _sites.ClearReturnDestination();
        return true;
    }
}
