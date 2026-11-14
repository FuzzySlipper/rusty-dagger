using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What a quest's messages name: each resource's name, place, role, item and faction, read from its binding (the place's
/// site, the person's role, the item it holds or held) or, before it binds, from its declaration.
/// </summary>
internal sealed class DaggerfallQuestTextContext(DaggerfallDefinitions definitions, DaggerfallSiteContext sites, DaggerfallNpcRegistry npcs,
    DaggerfallItemInstances items, Func<DaggerfallCreatedItem, string> describeItem, Func<DaggerfallTextContext> current)
{
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
        return new(current(), resources);
    }
}
