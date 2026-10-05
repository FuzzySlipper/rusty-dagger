using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallPotionIngredientView(int Template, string Name, ulong Quantity);
internal sealed record DaggerfallPotionRecipeView(int Key, string Name, int[] Ingredients, bool Available);
internal sealed record DaggerfallPotionMakingResult(bool Accepted, string Outcome, int? Recipe = null);

/// <summary>Recipe and ingredient policy over the player's and wagon's real Engine inventories.</summary>
internal sealed class DaggerfallPotionMaking(DaggerfallDefinitions definitions, DaggerfallState state,
    IRandomService random, Func<DaggerfallCalendar> calendar)
{
    private long _nextPotion;
    private sealed record Ingredient(EntityId Owner, DaggerfallItemOwner MetadataOwner, InventoryStack Stack, int Template);

    internal bool CanUse(DaggerfallServiceProvider provider)
    {
        DaggerfallNpc npc;
        try { npc = state.Npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return false; }
        var guild = DaggerfallConcreteGuildCatalog.All.FirstOrDefault(guild => guild.TryGetService(DaggerfallConcreteGuildService.MakePotions, out var service)
            && service.ProviderFactionId == npc.Appearance.FactionId);
        return guild is not null && state.ConcreteGuildServices.Evaluate(guild.FactionId, DaggerfallConcreteGuildService.MakePotions,
            checked((int)calendar().DayNumber), new(provider)).CanUse;
    }

    private IEnumerable<(EntityId Entity, DaggerfallItemOwner Owner)> Owners()
    {
        yield return (state.Inventory.Component.Owner, DaggerfallItemOwner.Player);
        if (state.Wagon.Current is { } wagon) yield return (wagon.Owner, DaggerfallItemOwner.Wagon(wagon.Id));
    }

    private IEnumerable<Ingredient> Ingredients() => Owners().SelectMany(owner => state.Containers.Read(owner.Entity).Stacks
        .Where(stack => definitions.RequireItem(new(stack.Definition.Value)).Template is { IsIngredient: true }
            && !state.ItemInstances.RequireStack(owner.Owner, stack.Id).HasEnchantment)
        .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
        .Select(stack => new Ingredient(owner.Entity, owner.Owner, stack, definitions.RequireItem(new(stack.Definition.Value)).Template!.Index)));

    internal DaggerfallPotionIngredientView[] ReadIngredients() => [.. Ingredients().GroupBy(value => value.Template)
        .Select(group => new DaggerfallPotionIngredientView(group.Key, definitions.ItemTemplateCatalog.Templates[group.Key].Name,
            group.Aggregate(0UL, (total, value) => checked(total + value.Stack.Quantity))))
        .OrderBy(value => value.Name, StringComparer.Ordinal)];

    internal DaggerfallPotionRecipeView[] ReadRecipes()
    {
        var keys = new HashSet<int>();
        foreach (var owner in Owners())
            foreach (var item in state.Containers.Read(owner.Entity).UniqueItems)
            {
                var metadata = state.ItemInstances.RequireUnique(state.Containers.GetDurableItemId(item.Entity).Value);
                if (definitions.RequireItem(new(item.Definition.Value)).Template?.Index == 278 && metadata.PotionRecipeKey is int recipe) keys.Add(recipe);
            }
        var available = ReadIngredients().ToDictionary(value => value.Template, value => value.Quantity);
        return [.. keys.Select(key => definitions.Magic.PotionRecipes[key]).OrderBy(recipe => recipe.Name, StringComparer.Ordinal)
            .Select(recipe => new DaggerfallPotionRecipeView(recipe.Key, recipe.Name,
                [.. recipe.Ingredients.SelectMany(value => Enumerable.Repeat(value.Template, value.Count))],
                recipe.Ingredients.All(value => available.GetValueOrDefault(value.Template) >= (ulong)value.Count)))];
    }

    internal DaggerfallPotionMakingResult Mix(DaggerfallServiceProvider provider, int[] selected)
    {
        if (!CanUse(provider)) return new(false, "ProviderUnavailable");
        if (selected is null || selected.Length is < 1 or > 8) return new(false, "InvalidIngredients");
        var ingredients = Ingredients().ToArray();
        List<(Ingredient Ingredient, ulong Quantity)> consumed = [];
        foreach (var group in selected.GroupBy(value => value))
        {
            ulong remaining = (ulong)group.Count();
            foreach (var ingredient in ingredients.Where(value => value.Template == group.Key))
            {
                ulong quantity = Math.Min(remaining, ingredient.Stack.Quantity);
                if (quantity > 0) consumed.Add((ingredient, quantity));
                remaining -= quantity;
                if (remaining == 0) break;
            }
            if (remaining != 0) return new(false, "InsufficientIngredients");
        }
        var recipe = definitions.Magic.PotionRecipes.Values.SingleOrDefault(value => value.Match(selected).Matches);
        void Consume(InventoryEdit candidate)
        {
            foreach (var (ingredient, quantity) in consumed) candidate.Consume(ingredient.Owner, ingredient.Stack.Id, quantity);
        }
        try
        {
            if (recipe is null)
            {
                // An unsuccessful mixture still uses the ingredients, as in the source maker.
                using var candidate = state.Inventory.Component.Store.Prepare();
                Consume(candidate); candidate.Publish();
            }
            else
            {
                InventoryStackId stack;
                do { stack = InventoryStackId.Parse($"daggerfall.potion.made.{checked(++_nextPotion)}"); }
                while (state.ItemInstances.ContainsStack(DaggerfallItemOwner.Player, stack));
                var factory = new DaggerfallItemFactory(definitions, random);
                var item = factory.Create(new("UselessItems1", stack.Value, DaggerfallItemOwner.Player, Quantity: 1, TemplateIndex: 83, PotionRecipeKey: recipe.Key));
                state.Containers.Seed(state.Inventory.Component.Owner, [new(item.Item, 1, Stack: stack)], Consume);
                state.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, item.Metadata);
            }
        }
        catch (MechanicsException failure) when (failure.Reason == MechanicsRefusal.Capacity)
        { return new(false, "Capacity"); }
        foreach (var (ingredient, _) in consumed)
            if (!state.Containers.Read(ingredient.Owner).Stacks.Any(value => value.Id == ingredient.Stack.Id))
                state.ItemInstances.RemoveStack(ingredient.MetadataOwner, ingredient.Stack.Id);
        return new(true, recipe is null ? "MixtureFailed" : "PotionMade", recipe?.Key);
    }
}
