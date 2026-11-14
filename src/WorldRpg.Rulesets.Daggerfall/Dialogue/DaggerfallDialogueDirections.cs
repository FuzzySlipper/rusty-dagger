using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A place or person an active quest names that the "Where is" directory can point to.
/// </summary>
/// <param name="People">The people it is, when it is a person: each one's NPC id.</param>
/// <param name="Site">The one site its place is at (a person's home for a person), or null when it names no single site.</param>
/// <param name="Building">The building at that site its place names, if any.</param>
internal sealed record DaggerfallDialogueQuestSubject(string InstanceId, string Symbol, string Name, IReadOnlyList<long> People,
    DaggerfallSiteId? Site, DaggerfallSiteBuildingId? Building);

/// <summary>
/// The "Where is" directory: the places, people and quest places a speaker can point to, and what an answer
/// discloses. What the quests name, which buildings they claim and whose contacts people are come from Quests as
/// plain inputs; the speaker and whether a person can be talked to come from the live conversation.
/// </summary>
internal sealed class DaggerfallDialogueDirections(
    DaggerfallSiteContext site,
    DaggerfallNpcRegistry npcs,
    DaggerfallDefinitions definitions,
    PlayerControlState player,
    Func<DaggerfallDialogueService?> dialogue,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<WorldPoint, WorldPoint> exteriorSitePosition,
    Func<IReadOnlyList<DaggerfallDialogueQuestSubject>> questSubjects,
    Func<DaggerfallSiteId, DaggerfallSiteBuildingSource, bool> questClaimsBuilding,
    Func<long, IReadOnlyList<DaggerfallQuestContact>> questContacts)
{
    /// <summary>The way to the building selected on the map, as a name and a hint, or null when none is selected.</summary>
    internal (string Name, string Hint)? SelectedBuilding()
    {
        if (site.SelectedBuilding is not { } selected) return null;
        DaggerfallDialogueDestination? destination = Resolve(
            $"direction:building:{selected.Building.BlockX}:{selected.Building.BlockY}:{selected.Building.Index}");
        return destination is { } value ? (value.Name, value.Hint) : null;
    }

    /// <summary>Builds the talk directory from the site/map/NPC owners already used by map and activation.</summary>
    internal IReadOnlyList<DaggerfallDialogueTopicOption> Directory()
    {
        DaggerfallSiteRecord? here = site.ActiveSite;
        if (here is null) return [];
        List<DaggerfallDialogueTopicOption> entries = [];
        List<(int Order, DaggerfallDialogueTopicOption Topic)> buildings = [];
        foreach (DaggerfallSiteBuildingSource source in site.BuildingsAt(here.Id)
            .OrderBy(value => value.Id.BlockY).ThenBy(value => value.Id.BlockX).ThenBy(value => value.Id.Index))
        {
            DaggerfallSiteBuildingRecord building;
            try { building = site.RequireBuilding(here.Id, source.Id); }
            catch (InvalidOperationException) { continue; }
            // Only the kinds of building the directory lists: no houses, walls or other unnamed fabric.
            if (string.IsNullOrWhiteSpace(building.Name)
                || !DaggerfallDialogueTopicGroups.BuildingCategories.TryGetValue(building.BuildingType, out var category)) continue;
            string id = $"direction:building:{source.Id.BlockX}:{source.Id.BlockY}:{source.Id.Index}";
            buildings.Add((category.Order, new(id, $"Where is {building.Name}?", Category: category.Label)));
        }
        entries.AddRange(buildings.OrderBy(entry => entry.Order).ThenBy(entry => entry.Topic.Label, StringComparer.Ordinal).Select(entry => entry.Topic));

        foreach (DaggerfallNpc npc in npcs.All
            .Where(npc => dialogue()?.IsLiveTalkTarget(npc) == true)
            .OrderBy(npc => npc.DurableId))
        {
            string display = npc.DisplayName ?? npc.Role;
            entries.Add(new($"direction:npc:{npc.DurableId}", $"Where is {display}?", Category: DaggerfallDialogueTopicGroups.People));
        }

        foreach (DaggerfallSiteRecord place in site.Records
            .Where(place => place.Id.Region == here.Id.Region && place.Id != here.Id)
            .OrderBy(place => place.Name, StringComparer.Ordinal).ThenBy(place => place.Id.Index))
            entries.Add(new($"direction:site:{place.Id.Region}:{place.Id.Index}", $"Where is {place.Name}?", Category: DaggerfallDialogueTopicGroups.Elsewhere));
        foreach (DaggerfallDialogueQuestSubject subject in questSubjects())
        {
            string key = $"direction:quest:{Uri.EscapeDataString(subject.InstanceId)}:{Uri.EscapeDataString(subject.Symbol)}";
            if (Resolve(key) is { } destination) entries.Add(new(key, $"Where is {destination.Name}?", Category: DaggerfallDialogueTopicGroups.QuestPlaces));
        }
        return entries;
    }

    /// <summary>
    /// Resolves a directory key against the same live site/NPC/map records that own world state. Site
    /// answers reveal the destination through the existing durable discovery owner; building answers
    /// select the existing transient map marker so the next map projection shows the disclosed target.
    /// </summary>
    internal DaggerfallDialogueDestination? Resolve(string? target)
    {
        DaggerfallSiteRecord? active = site.ActiveSite;
        if (active is null || string.IsNullOrWhiteSpace(target)) return null;
        if (target.StartsWith("direction:quest:", StringComparison.Ordinal))
        {
            if (QuestDirectionTarget(target) is not { } translated) return null;
            var destination = Resolve(translated.Target);
            return destination is null ? null : destination with { Name = translated.Name };
        }
        if (target.StartsWith("direction:building:", StringComparison.Ordinal))
        {
            string[] parts = target[19..].Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int blockX)
                || !int.TryParse(parts[1], out int blockY) || !int.TryParse(parts[2], out int index)) return null;
            DaggerfallSiteBuildingId id = new(blockX, blockY, index);
            // Asked outdoors or inside one of the town's buildings; a dungeon has no streets to point along.
            if (activeProfile().Kind == DaggerfallWorldProfileKind.Dungeon) return null;
            try
            {
                DaggerfallSiteBuildingRecord building = site.RequireBuilding(active.Id, id);
                bool sameBuilding = dialogue()?.CurrentNpc() is { } speaker
                    && string.Equals(speaker.Site.Building, $"{blockX}/{blockY}/{index}", StringComparison.Ordinal);
                return new(target, building.Name, BuildingHint(active.Id, id), Known: site.IsBuildingDiscovered(active.Id, id))
                {
                    SameBuilding = sameBuilding,
                    QuestLocality = questClaimsBuilding(active.Id, building.Source),
                };
            }
            catch (InvalidOperationException) { return null; }
        }

        if (target.StartsWith("direction:npc:", StringComparison.Ordinal)
            && long.TryParse(target[14..], out long npcId))
        {
            DaggerfallNpc? npc = npcs.All.SingleOrDefault(value => value.DurableId == npcId);
            if (npc is null || dialogue()?.TryReadLiveTalkPosition(npc, out WorldPoint npcPosition) != true) return null;
            string hint = activeProfile().Kind == DaggerfallWorldProfileKind.Exterior
                && player.Position is WorldPoint standing
                ? CardinalHint(exteriorSitePosition(npcPosition), exteriorSitePosition(standing)) : "here";
            DaggerfallNpc? speaker = dialogue()?.CurrentNpc();
            bool sameBuilding = speaker is not null && !string.IsNullOrEmpty(speaker.Site.Building)
                && string.Equals(speaker.Site.Building, npc.Site.Building, StringComparison.Ordinal);
            bool sameOrganization = speaker is not null && FactionsShareOrganization(
                speaker.Appearance.FactionId, npc.Appearance.FactionId);
            bool questLocality = speaker is not null
                && speaker.Site.Region == npc.Site.Region
                && string.Equals(speaker.Site.Location, npc.Site.Location, StringComparison.Ordinal)
                && questContacts(npc.DurableId).Count != 0;
            return new(target, npc.DisplayName ?? npc.Role, hint, Known: true)
            {
                SameBuilding = sameBuilding,
                SameOrganization = sameOrganization,
                QuestLocality = questLocality,
            };
        }

        if (target.StartsWith("direction:site:", StringComparison.Ordinal))
        {
            string[] parts = target[15..].Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int region) || !int.TryParse(parts[1], out int index)) return null;
            DaggerfallSiteId id = new(region, index);
            DaggerfallSiteRecord place;
            try { place = site.Require(id); }
            catch (InvalidOperationException) { return null; }
            if (place.Id.Region != active.Id.Region) return null;
            bool known = site.IsDiscovered(id);
            return new(target, place.Name, "on the map", known);
        }
        return null;
    }

    private (string Target, string Name)? QuestDirectionTarget(string key)
    {
        string[] parts = key[16..].Split(':');
        if (parts.Length != 2) return null;
        string instance = Uri.UnescapeDataString(parts[0]), symbol = Uri.UnescapeDataString(parts[1]);
        if (questSubjects().SingleOrDefault(value => value.InstanceId == instance && value.Symbol == symbol) is not { } subject) return null;
        foreach (long id in subject.People)
            if (Resolve($"direction:npc:{id}") is not null) return ($"direction:npc:{id}", subject.Name);
        if (subject.Site is not { } place) return null;
        if (site.ActiveSite?.Id == place && subject.Building is { } building)
            return ($"direction:building:{building.BlockX}:{building.BlockY}:{building.Index}", subject.Name);
        return ($"direction:site:{place.Region}:{place.Index}", subject.Name);
    }

    /// <summary>
    /// Retains the donor's organization relation: a parent, child, shared hierarchy, or an
    /// authored ally/enemy relation is a known organization subject. The check walks the catalog's
    /// current links with cycle protection instead of equating only exact faction identities.
    /// </summary>
    private bool FactionsShareOrganization(int firstId, int secondId)
    {
        if (firstId == 0 || secondId == 0) return false;
        if (!definitions.Factions.Factions.ContainsKey(firstId) || !definitions.Factions.Factions.ContainsKey(secondId)) return false;

        HashSet<int> firstHierarchy = FactionHierarchy(firstId);
        HashSet<int> secondHierarchy = FactionHierarchy(secondId);
        if (firstHierarchy.Overlaps(secondHierarchy)) return true;

        foreach (int ancestorId in firstHierarchy)
        {
            if (!definitions.Factions.Factions.TryGetValue(ancestorId, out DaggerfallFactionDefinition? ancestor)) continue;
            foreach (int relatedId in secondHierarchy)
                if (ancestor.Allies.Contains(relatedId) || ancestor.Enemies.Contains(relatedId)) return true;
        }
        return false;
    }

    private HashSet<int> FactionHierarchy(int factionId)
    {
        HashSet<int> hierarchy = [];
        int current = factionId;
        while (hierarchy.Add(current)
            && definitions.Factions.Factions.TryGetValue(current, out DaggerfallFactionDefinition? faction)
            && faction.Parent != 0)
            current = faction.Parent;
        return hierarchy;
    }

    /// <summary>Applies a site disclosure only after the dialogue owner has admitted the answer.</summary>
    internal bool Disclose(string target)
    {
        if (target.StartsWith("direction:quest:", StringComparison.Ordinal))
            return QuestDirectionTarget(target) is { } quest && Disclose(quest.Target);
        if (target.StartsWith("direction:building:", StringComparison.Ordinal))
        {
            if (site.Active is not { } active || target[19..].Split(':') is not [var x, var y, var ordinal]
                || !int.TryParse(x, out int blockX) || !int.TryParse(y, out int blockY) || !int.TryParse(ordinal, out int buildingIndex)) return false;
            try
            {
                DaggerfallSiteBuildingId building = new(blockX, blockY, buildingIndex);
                site.DiscoverBuilding(active, building); site.SelectBuilding(active, building); return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        if (!target.StartsWith("direction:site:", StringComparison.Ordinal)) return false;
        string[] parts = target[15..].Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int region) || !int.TryParse(parts[1], out int index)) return false;
        DaggerfallSiteId id = new(region, index);
        DaggerfallSiteRecord? current = site.ActiveSite;
        if (current is null || current.Id.Region != region) return false;
        try { _ = site.Require(id); }
        catch (InvalidOperationException) { return false; }
        if (!site.IsDiscovered(id)) site.Discover(id);
        return true;
    }

    private string BuildingHint(DaggerfallSiteId place, DaggerfallSiteBuildingId building)
    {
        // Inside a building the directions start at its door on the map, as the donor's do from the
        // player's map position, since the interior's own coordinates are not the town's.
        DaggerfallWorldProfileKey profile = activeProfile();
        WorldPoint? start = profile.Kind == DaggerfallWorldProfileKind.Interior
            && DaggerfallWorldProfileIds.TryParse(profile.LogicalId, out _, out DaggerfallSiteBuildingId? inside) && inside is { } here
                ? site.BuildingMapPosition(place, here)
            : player.Position is WorldPoint standing ? exteriorSitePosition(standing) : null;
        if (start is not { } origin) return "here";
        return CardinalHint(site.BuildingMapPosition(place, building), origin);
    }

    private static string CardinalHint(WorldPoint target, WorldPoint start)
    {
        return MathF.Abs(target.X - start.X) < 1 && MathF.Abs(target.Z - start.Z) < 1
            ? "here" : DaggerfallCompass.Word(MathF.Atan2(target.X - start.X, start.Z - target.Z));
    }
}
