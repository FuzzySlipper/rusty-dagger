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
        // The Place names its location, the kind of profile it selected and, inside, its building: that is the
        // profile id, authored or assembled.
        DaggerfallSiteId place = binding.Places[0].Require();
        DaggerfallWorldProfileKey? key = (binding.PlaceSelection?.Kind, binding.Building) switch
        {
            (DaggerfallWorldProfileKind.Exterior, null) => DaggerfallWorldProfileIds.Exterior(place),
            (DaggerfallWorldProfileKind.Dungeon, null) => DaggerfallWorldProfileIds.Dungeon(place),
            (DaggerfallWorldProfileKind.Interior, { } building) => DaggerfallWorldProfileIds.Interior(place, new(building.BlockX, building.BlockY, building.Index)),
            _ => null,
        };
        DaggerfallSiteProfile? destination = key is not { } id ? null
            : _sites.Profiles is { } profiles ? (profiles.TryGet(id, out DaggerfallSiteProfile resolved) ? resolved : null)
            : _sites.Projection.Inputs.ProfileKey == id ? _sites.Projection.Inputs : null;
        if (destination is null || !DaggerfallQuestPlacements.Matches(binding, destination))
            throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no admitted world profile.");
        var markers = destination.QuestMarkers.Where(marker => marker.Kind == DaggerfallSiteMarkerKind.QuestSpawn).ToArray();
        if (markers.Length == 0) throw new NotSupportedException($"Quest teleport Place '{operation.Targets[0]}' has no admitted spawn marker.");
        int index = operation.MarkerIndex is { } requested && requested < markers.Length ? requested : 0;
        var marker = markers[index];
        if (!_sites.TryRelocatePlayer(destination.ProfileKey, new("quest-teleport", marker.Position, 0, 0))) return false;
        _sites.ClearReturnDestination();
        return true;
    }
}
