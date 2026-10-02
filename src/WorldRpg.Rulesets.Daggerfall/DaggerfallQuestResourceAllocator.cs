using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Selected source meaning before a world actor or inventory item exists.</summary>
internal sealed record DaggerfallQuestFoeSelection(string Definition, int Count, bool Female);
internal sealed record DaggerfallQuestSelectionContext(int Level, string Race, string Gender, int Region);

/// <summary>Donor virtual resource generation through the current item/name/catalog owners.</summary>
internal sealed class DaggerfallQuestResourceAllocator(
    DaggerfallDefinitions definitions, IRandomService random, DaggerfallItemFactory items, DaggerfallNames names,
    Func<DaggerfallQuestSelectionContext> current,
    Func<int, DaggerfallGuildEligibility> membership, Func<int, int> regionalPrice,
    Func<DaggerfallCreatedItem, string> itemName)
{
    internal DaggerfallQuestResourceState Allocate(string instanceId, int factionId, DaggerfallQuestResourceDefinition declaration)
    {
        DaggerfallQuestSelectionContext context = current();
        string key = instanceId + "/" + declaration.CanonicalId;
        if (declaration.Kind == "foe")
        {
            int mobileId = definitions.QuestSources.Tables.ActorItemTables.Foes.Resolve(declaration.TargetSourceSpelling!).Id;
            if (!definitions.Mobiles.Mobiles.TryGetValue(mobileId, out var mobile))
                throw new NotSupportedException($"Quest foe '{declaration.CanonicalId}' has no published mobile {mobileId}.");
            var definition = definitions.RequireActor(DaggerfallEncounterActors.ActorFor(mobile));
            bool human = definition.Kind == DaggerfallActorKinds.EnemyClass;
            bool female = human && Draw(key + "/gender", 0, 99) >= 55;
            string display;
            if (human)
            {
                if (!definitions.BuildingNames.TryGetNameBank(context.Region, out int bank))
                    throw new NotSupportedException($"Quest foe region {context.Region} has no published name bank.");
                display = names.FullName(bank, female, key + "/name");
            }
            else display = names.MonsterName(false, key + "/name");
            return new(declaration.CanonicalId, DaggerfallQuestResourceBinding.Pending())
            {
                SelectedFoe = new(definition.Id.Value, Math.Clamp(declaration.Foe?.Count ?? 1, 1, 8), female),
                Text = new(Name: mobile.DonorName.Replace('_', ' '), Details: display),
            };
        }
        if (declaration.Kind != "item") throw new ArgumentException("Resource allocation requires an Item or Foe declaration.");
        var options = declaration.Item ?? throw new ArgumentException("A normalized Item requires its source options.");
        DaggerfallItemCreateRequest request;
        if (declaration.TargetCanonicalId == "gold")
            request = new("Currency", key, DaggerfallItemOwner.Player,
                Quantity: Gold(key, factionId, options, context), TemplateIndex: 276, Variant: 0, QuestId: instanceId, QuestSymbol: declaration.CanonicalId);
        else
        {
            int groupId, ordinal;
            if (options.Class is int explicitClass)
            { groupId = explicitClass; ordinal = options.Subclass ?? -1; }
            else
            {
                var source = definitions.QuestSources.Tables.ActorItemTables.Items.Resolve(declaration.TargetSourceSpelling!);
                groupId = source.P1; ordinal = source.P2;
            }
            if (groupId is 4 or 5)
            {
                var pool = definitions.Magic.MagicItems.Values.Where(value => groupId == 4 ? value.Type == 0 : value.Type != 0)
                    .OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
                int selected = ordinal == -1 ? Draw(key + "/magic", 0, pool.Length - 1) : ordinal;
                if (selected < 0 || selected >= pool.Length)
                    throw new NotSupportedException($"Quest Item '{declaration.CanonicalId}' has unavailable magic group {groupId} ordinal {ordinal}.");
                request = new("Magic", key, DaggerfallItemOwner.Player, Quantity: 1, Level: context.Level,
                    Race: context.Race, Gender: context.Gender, MagicItemKey: pool[selected].Key,
                    QuestId: instanceId, QuestSymbol: declaration.CanonicalId);
            }
            else
            {
                if (!definitions.ItemTemplateCatalog.Groups.TryGetValue(groupId, out var group) || !group.TemplateIndices)
                    throw new NotSupportedException($"Quest Item '{declaration.CanonicalId}' requires published native group {groupId} ordinals.");
                // Books ignore subclass; literal class/template is already a native index.
                int template;
                if (groupId == 7) template = 277;
                else if (options.Template is int native) template = native;
                else
                {
                    int selected = ordinal == -1 ? Draw(key + "/subclass", 0, group.Values.Count - 1) : ordinal;
                    if (selected < 0 || selected >= group.Values.Count)
                        throw new NotSupportedException($"Quest Item '{declaration.CanonicalId}' has unavailable group {groupId} ordinal {ordinal}.");
                    template = group.Values[selected];
                }
                bool clothing = groupId is 6 or 12;
                int? potion = template is 83 or 278
                    ? options.Key ?? DaggerfallLootPolicy.ChooseClassicPotionRecipe((low, high) => Draw(key + "/recipe", low, high)) : null;
                request = new(group.Name, key, DaggerfallItemOwner.Player, Quantity: 1, TemplateIndex: template, Variant: 0,
                    Material: groupId == 2 ? "leather" : groupId == 3 && template != 131 ? "iron" : null, Level: context.Level,
                    Race: clothing || groupId == 2 ? context.Race : null,
                    Gender: groupId == 6 ? "male" : groupId == 12 ? "female" : groupId == 2 ? context.Gender : null,
                    RandomizeClothingDye: clothing && options.Template is null,
                    BookId: groupId == 7 ? options.Key : null, PotionRecipeKey: potion,
                    QuestId: instanceId, QuestSymbol: declaration.CanonicalId);
            }
        }
        DaggerfallCreatedItem selectedItem = items.Create(request);
        return new(declaration.CanonicalId, DaggerfallQuestResourceBinding.Pending())
        { SelectedItem = selectedItem, Text = new(Name: itemName(selectedItem)) };
    }

    private ulong Gold(string key, int factionId, DaggerfallQuestItemOptions options, DaggerfallQuestSelectionContext context)
    {
        int amount;
        if (options.RangeLow is int low && options.RangeHigh is int high)
            amount = Draw(key + "/gold", low, high);
        else
        {
            int playerMod = Math.Min(10, context.Level / 2 + 1), factionMod = 50;
            var guild = factionId == 0 ? default : membership(factionId);
            if (guild.IsMember)
            {
                playerMod = Math.Min(10, guild.Rank + 1);
                factionMod = definitions.Factions.Factions[factionId].Power;
            }
            amount = checked(Draw(key + "/gold", 150 * playerMod, 200 * playerMod)
                * (regionalPrice(context.Region) / 2 + 500) / 1000 * (factionMod + 50) / 100);
            if (guild.IsMember && factionId == DaggerfallConcreteGuildCatalog.FightersFactionId)
                amount = DaggerfallConcreteGuildPolicy.FightersReward(amount, guild.Rank);
        }
        return checked((ulong)Math.Max(1, amount));
    }

    private int Draw(string key, int low, int high) => low > high
        ? throw new ArgumentException($"Quest selection '{key}' has an empty range [{low},{high}].")
        : checked((int)random.DrawKeyed(new(0, "daggerfall.quest.resource-selection", key, low, high)).Value);
}
