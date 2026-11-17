using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What a quest's messages name: each resource's name, place, role, item and faction, read from its binding (the place's
/// site, the person's role, the item it holds or held) or, before it binds, from its declaration; and the oath and god
/// its people swear by.
/// </summary>
internal sealed class DaggerfallQuestTextContext(DaggerfallDefinitions definitions, DaggerfallSiteContext sites, DaggerfallNpcRegistry npcs,
    DaggerfallItemInstances items, Func<DaggerfallCreatedItem, string> describeItem, Func<DaggerfallTextContext> current,
    Func<DaggerfallInteriorBuilding?> building, IRandomService random)
{
    private const int TempleBuildingType = 15;
    private const string RandomScope = "daggerfall.quest.text";

    internal DaggerfallQuestMessageContext For(DaggerfallQuestRuntimeInstance instance)
    {
        Dictionary<string, DaggerfallQuestResourceTextContext> resources = [];
        foreach (DaggerfallQuestResourceState resource in instance.Resources)
        {
            if (resource.Text is { } boundText)
            {
                resources[DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource")] = boundText;
                continue;
            }
            DaggerfallQuestResourceDefinition? declared = definitions.QuestSources.Resources
                .SingleOrDefault(value => value.SourceFile == instance.SourceFile
                    && value.CanonicalId == DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource"));
            if (declared is null) continue;
            string? place = resource.Binding.Places.Select(value => sites.TryFind(new DaggerfallSiteId(value.Region!.Value, value.Index!.Value), out var site) ? site.Name : null)
                .FirstOrDefault(value => value is not null);
            string? actorRole = resource.Binding.ActorIds.Select(id => npcs.All.FirstOrDefault(npc => npc.DurableId == id)?.Role)
                .FirstOrDefault(value => value is not null);
            // A quest can consume its item before ending. Its source meaning outlives the live item.
            string? item = resource.Binding.UniqueItemIds.Where(items.ContainsUnique)
                .Select(id =>
                {
                    var metadata = items.RequireUnique(id);
                    var definition = definitions.RequireItem(new(metadata.ItemId));
                    return describeItem(new(definition.Template?.Index ?? 0, new(metadata.ItemId), definition.IsFungible, 1, metadata));
                }).FirstOrDefault(value => value is not null);
            if (item is null && declared.Kind == "item")
                item = declared.Item?.Template is int template && definitions.ItemTemplateCatalog.Templates.TryGetValue(template, out var itemTemplate)
                    ? itemTemplate.Name : declared.TargetSourceSpelling?.Replace('_', ' ');
            string? named = declared.Person?.Named?.Replace('_', ' ');
            string? faction = declared.Person?.Faction?.Replace('_', ' ');
            string? group = declared.Person?.Group?.Replace('_', ' ');
            string? name = place ?? named ?? item ?? actorRole ?? group;
            resources[DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource")] = new(
                Name: name,
                NameTwo: place,
                NameThree: place,
                NameFour: place,
                Details: item ?? declared.SourceText,
                Binding: actorRole ?? place,
                Faction: faction);
        }
        DaggerfallTextContext text = current();
        return new(text with { Faction = text.Faction with { Oath = Oath(instance), God = God(instance) } }, resources);
    }

    /// <summary>%oth: an oath of the questor's people, or of the region's where the quest has no questor (QuestMCP.Oath).</summary>
    private string? Oath(DaggerfallQuestRuntimeInstance instance)
    {
        int faction = npcs.All.FirstOrDefault(npc => npc.DurableId == instance.QuestorId)?.Appearance.FactionId ?? 0;
        IReadOnlyList<string> oaths = DaggerfallOaths.Of(definitions, faction, sites.Region);
        return oaths.Count == 0 ? null : oaths[Draw(instance, "oath", oaths.Count)];
    }

    /// <summary>
    /// %god: the Divine of the temple the player stands in, else of the region's temple, else one of the eight
    /// (QuestMCP.God, which also draws one in a Fighters Guild hall that a region counts as its temple).
    /// </summary>
    private string God(DaggerfallQuestRuntimeInstance instance)
    {
        int temple = building() is { BuildingType: TempleBuildingType } inside ? inside.FactionId
            : sites.Region is int region && region < definitions.QuestSources.Tables.RegionTemples.Count
                ? definitions.QuestSources.Tables.RegionTemples[region] : 0;
        return (temple == DaggerfallConcreteGuildCatalog.FightersFactionId ? null : DaggerfallConcreteGuildCatalog.DivineOf(definitions.Factions, temple))
            ?? DaggerfallConcreteGuildCatalog.Divines.Values.ElementAt(Draw(instance, "god", DaggerfallConcreteGuildCatalog.Divines.Count));
    }

    // The donor draws each time it expands the macro; a quest keeps one draw so its messages read the same on every redraw.
    private int Draw(DaggerfallQuestRuntimeInstance instance, string macro, int count) => checked((int)random.DrawKeyed(
        new KeyedRngRequest(DaggerfallRandomKey.Seed, RandomScope, $"{instance.InstanceId}:{macro}", 0, count - 1)).Value);
}
