using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What a quest's messages name: each resource's name, place, role, item and faction, read from its binding (the place's
/// site, the person's role, the item it holds or held) or, before it binds, from its declaration; and the oath and god
/// its people swear by, the region and its rulers, the player's guild title, and the names and jokes its people tell
/// (QuestMCP and MacroHelper).
/// </summary>
/// <param name="guildTitle">The player's rank title in a guild faction, or null when the player is not a member.</param>
/// <param name="direction">The way from the player to a site, or to a building of it, or null when there is none to give.</param>
internal sealed class DaggerfallQuestTextContext(DaggerfallDefinitions definitions, DaggerfallSiteContext sites, DaggerfallNpcRegistry npcs,
    DaggerfallItemInstances items, Func<DaggerfallCreatedItem, string> describeItem, Func<DaggerfallTextContext> current,
    Func<DaggerfallInteriorBuilding?> building, IRandomService random, Func<int, string?> guildTitle,
    Func<DaggerfallSiteId, DaggerfallSiteBuildingId?, string?> direction)
{
    private const int TempleBuildingType = 15;
    private const int ProvinceFactionType = 7;
    private const int IndividualFactionType = 4;
    private const string RandomScope = "daggerfall.quest.text";
    // A province's ruler code names its title (MacroHelper.GetRulerTitle); an unknown code is a Lord.
    private static readonly string[] RulerTitles = ["King", "Queen", "Duke", "Duchess", "Marquis", "Marquise", "Count", "Countess", "Baron", "Baroness", "Lord", "Lady"];
    private readonly DaggerfallNames _names = new(definitions, random);

    internal DaggerfallQuestMessageContext For(DaggerfallQuestRuntimeInstance instance)
    {
        Dictionary<string, DaggerfallQuestResourceTextContext> resources = [];
        foreach (DaggerfallQuestResourceState resource in instance.Resources)
        {
            DaggerfallQuestResourceDefinition? declared = definitions.QuestSources.Resources
                .SingleOrDefault(value => value.SourceFile == instance.SourceFile
                    && value.CanonicalId == DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource"));
            if (resource.Text is { } boundText)
            {
                resources[DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource")] = Referenced(boundText, resource, declared);
                continue;
            }
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
            resources[DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource")] = Referenced(new(
                Name: name,
                NameTwo: place,
                NameThree: place,
                NameFour: place,
                Details: item ?? declared.SourceText,
                Binding: actorRole ?? place,
                Faction: faction), resource, declared);
        }
        DaggerfallTextContext text = current();
        DaggerfallFactionDefinition? province = sites.Region is int region ? definitions.Factions.Factions.Values
            .Where(value => value.Type == ProvinceFactionType && value.Region == region).OrderBy(value => value.Id).FirstOrDefault() : null;
        string? ruler = province?.Children.Select(child => definitions.Factions.Factions.GetValueOrDefault(child))
            .FirstOrDefault(child => child?.Type == IndividualFactionType)?.Name;
        return new(text with
        {
            Player = text.Player with { GuildTitle = instance.FactionId == 0 ? text.Player.Name : guildTitle(instance.FactionId) ?? text.Player.Name },
            Faction = text.Faction with
            {
                Oath = Oath(instance), God = God(instance), RegionInContext = text.Faction.RegionInContext ?? text.Location.Region,
                RegentTitle = province is null ? null : Internal(RulerTitles.ElementAtOrDefault(province.Ruler - 1) ?? "Lord"),
                RegentName = ruler ?? Name(instance, "regent", Draw(instance, "regent/gender", 2) == 1),
                // The region's lord is the province's first individual, else a name of the ruler's gender (GetLordNameForFaction).
                CurrentRegionLeader = province is null ? null : province.Children.Select(child => definitions.Factions.Factions.GetValueOrDefault(child))
                    .FirstOrDefault() is { Type: IndividualFactionType } first ? first.Name : Name(instance, "lord", province.Ruler % 2 == 0),
                FactionOrder = definitions.Factions.Factions.GetValueOrDefault(instance.FactionId)?.Name is { } order
                    ? order.StartsWith("The ", StringComparison.Ordinal) ? order[4..] : order : null,
            },
            Story = text.Story with
            {
                Joke = Joke(instance),
                Name = Name(instance, "name", Draw(instance, "name/gender", 2) == 1),
                FemaleName = Name(instance, "female", true),
                MaleName = Name(instance, "male", false),
                Pronoun = Internal("pronounHe"), ObjectPronoun = Internal("pronounHim"),
                ReflexivePronoun = Internal("pronounHimself"), PossessiveAdjective = Internal("pronounHis"),
                // The journal dates %qdt by its entry; any other message by the quest's start (Quest.GetCurrentLogMessageTime).
                QuestDate = text.Story.QuestDate ?? World.DaggerfallCalendar.FromAbsoluteSeconds(instance.StartedAtSeconds).DescribeDate(),
            },
        }, resources);
    }

    /// <summary>
    /// What a message reads once it names this resource: the pronouns of its person, or the male ones for anything
    /// else, and the way to its place.
    /// </summary>
    private DaggerfallQuestResourceTextContext Referenced(DaggerfallQuestResourceTextContext text, DaggerfallQuestResourceState resource,
        DaggerfallQuestResourceDefinition? declared)
    {
        DaggerfallNpc? person = resource.Binding.ActorIds.Select(id => npcs.All.FirstOrDefault(npc => npc.DurableId == id))
            .FirstOrDefault(value => value is not null);
        bool female = person is not null ? person.Appearance.Gender == "Female"
            : string.Equals(declared?.Person?.Gender, "female", StringComparison.OrdinalIgnoreCase);
        // A place resource's site and building; a person's home, which its __name_ names: the one the quest allocated, else
        // where the person lives.
        (DaggerfallSiteId Site, DaggerfallSiteBuildingId? Building)? where = null;
        DaggerfallQuestResourceBinding? placed = resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Place ? resource.Binding
            : resource.SelectedPerson?.Home?.Binding;
        if (placed?.Places.FirstOrDefault() is { Region: int region, Index: int index })
            where = (new(region, index), placed.Building is { } claim ? new(claim.BlockX, claim.BlockY, claim.Index) : null);
        else if (person is not null && sites.Records.FirstOrDefault(site => site.Id.Region == person.Site.Region && site.Name == person.Site.Location) is { } home)
            where = (home.Id, person.Site.Building.Split('/') is [var x, var y, var i] && int.TryParse(x, out int blockX)
                && int.TryParse(y, out int blockY) && int.TryParse(i, out int number) ? new(blockX, blockY, number) : null);
        string? way = where is { } at ? direction(at.Site, at.Building) : null;
        // A local place in a town the player has left is named with the way to it (QuestMCP.Direction).
        if (way is not null && where is { } there && declared?.PlaceKind == "local" && sites.ActiveSite?.Id != there.Site
            && sites.TryFind(there.Site, out DaggerfallSiteRecord? town))
            way = town.Name + Internal("comma") + way;
        return text with
        {
            Pronouns = new(Internal(female ? "pronounShe" : "pronounHe"), Internal(female ? "pronounHer" : "pronounHim"),
                Internal(female ? "pronounHerself" : "pronounHimself"), Internal(female ? "pronounHer2" : "pronounHis")),
            Direction = way,
        };
    }

    private string Internal(string key) => DaggerfallTextContexts.Internal(definitions, key);

    /// <summary>%jok: one of the jokes of text record 200 (MacroHelper.Joke).</summary>
    private string? Joke(DaggerfallQuestRuntimeInstance instance) =>
        definitions.Text.Resolve(new(DaggerfallTextKind.Resource, "200"), out DaggerfallTextValue? jokes) == DaggerfallTextResolution.Resolved
            && jokes!.TextRuns.ToArray() is { Length: > 0 } runs ? runs[Draw(instance, "joke", runs.Length)] : null;

    /// <summary>A full name from the region's name bank, the same for the quest on every redraw (QuestMCP.Name and its kin).</summary>
    private string? Name(DaggerfallQuestRuntimeInstance instance, string role, bool female) =>
        sites.Region is int region && definitions.BuildingNames.TryGetNameBank(region, out int bank)
            ? _names.FullName(bank, female, $"quest/{instance.InstanceId}/{role}") : null;

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
