using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class PotionMakingSessionTests
{
    [Fact]
    public void Making_uses_pack_then_wagon_actual_ingredients_and_restores_two_distinct_payloads()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var provider = Provider(s, "make-potions", 840, 3);
        var recipes = new[] { TestPayload.Definitions.Magic.PotionRecipes[4975678], TestPayload.Definitions.Magic.PotionRecipes[5188896] };
        var wagon = s.State.Wagon.EnsureCreated();
        foreach (var recipe in recipes)
        {
            int index = 0;
            foreach (var ingredient in recipe.Ingredients)
                Ingredient(f, ingredient.Template, index++ % 2 == 0 ? null : wagon.Id);
            Assert.True(s.PotionMaker.Mix(provider, [.. recipe.Ingredients.Select(value => value.Template)]).Accepted);
        }
        Assert.Empty(s.PotionMaker.ReadIngredients());
        var potions = s.State.Inventory.Read().Stacks.Where(value => value.Definition.Value == "template-83").ToArray();
        Assert.Equal(2, potions.Length);
        Assert.Equal(new[] { 4975678, 5188896 }, potions.Select(value => s.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, value.Id).PotionRecipeKey!.Value).Order());
        using var restored = f.Restore();
        Assert.Empty(restored.PotionMaker.ReadIngredients());
        Assert.Equal(2, restored.State.Inventory.Read().Stacks.Count(value => value.Definition.Value == "template-83"));
    }

    [Fact]
    public void Missing_ingredients_and_membership_preserve_inventory_but_failed_experiment_uses_ingredients()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var provider = Provider(s, "make-potions", 840, 3);
        Ingredient(f, 62); Ingredient(f, 31, s.State.Wagon.EnsureCreated().Id);
        Assert.Equal("InsufficientIngredients", s.PotionMaker.Mix(provider, [62,31,56]).Outcome);
        Assert.Equal(2, s.PotionMaker.ReadIngredients().Length);
        s.State.Social.ExpelGuild(108);
        Assert.Equal("ProviderUnavailable", s.PotionMaker.Mix(provider, [62,31]).Outcome);
        Assert.Equal(2, s.PotionMaker.ReadIngredients().Length);
        s.State.Social.JoinGuild(108, 0); for(int rank=0;rank<3;rank++) s.State.Social.PromoteGuild(108,0);
        Assert.Equal("MixtureFailed", s.PotionMaker.Mix(provider, [62,31]).Outcome);
        Assert.Empty(s.PotionMaker.ReadIngredients());
        Assert.DoesNotContain(s.State.Inventory.Read().Stacks, item => item.Definition.Value == "template-83");
    }

    [Fact]
    public void Potion_sale_uses_guild_admission_real_stock_payment_and_restore()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var provider = Provider(s, "buy-potions", 841, 1);
        var context = new DaggerfallMerchantProviderContext(provider, 5, 0, 0, 0, 0);
        var view = s.State.Merchants.Read(context);
        Assert.True(view.CanBuy); Assert.False(view.CanSell); Assert.False(view.CanShoplift);
        Assert.Equal(6, view.Stock.Count); Assert.All(view.Stock, row => {Assert.StartsWith("Potion of ", row.Label);Assert.InRange(row.Quantity, 1UL, 4UL);});
        var offer = view.Stock[0];
        s.State.Currency.TrySpendGold(s.State.Currency.Read().Gold, []); view=s.State.Merchants.Read(context);
        Assert.Equal("InsufficientFunds", s.State.Merchants.Buy(context, view.Revision, offer.Key, 1).Outcome);
        Assert.Equal(offer.Quantity, s.State.Merchants.Read(context).Stock.Single(row=>row.Key==offer.Key).Quantity);
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        factory.Materialize(factory.Create(new("Currency", "potion.gold", DaggerfallItemOwner.Player, Quantity:10000, TemplateIndex:276)),
            s.State.Inventory, s.State.ItemInstances, InventoryStackId.Parse("potion.gold"));
        view = s.State.Merchants.Read(context); var before = s.State.Currency.Read().Gold;
        var purchase = s.State.Merchants.Buy(context, view.Revision, offer.Key, 1);
        Assert.True(purchase.Accepted); Assert.Equal(offer.UnitPrice, purchase.PaidGold);Assert.Equal(before-purchase.PaidGold,s.State.Currency.Read().Gold);
        Assert.Single(s.State.Inventory.Read().Stacks, row => row.Definition.Value=="template-83");
        using var restored=f.Restore();Assert.Single(restored.State.Inventory.Read().Stacks,row=>row.Definition.Value=="template-83");
        s.State.Social.ExpelGuild(108);view=s.State.Merchants.Read(context);Assert.False(view.CanBuy);
        Assert.Equal("ProviderUnavailable",s.State.Merchants.Buy(context,view.Revision,view.Stock[0].Key,1).Outcome);
    }

    [Fact]
    public void Recipe_selection_reads_real_pack_and_wagon_papers_and_reports_missing_ingredients()
    {
        using var f=new SanguineRoseSessionTests.Fixture();var s=f.Session;var wagon=s.State.Wagon.EnsureCreated();
        var factory=new DaggerfallItemFactory(TestPayload.Definitions,f.Engine.Context.Random);
        foreach(var owner in new[]{DaggerfallItemOwner.Player,DaggerfallItemOwner.Wagon(wagon.Id)})
        {
            var item=factory.Create(new("MiscItems","paper."+owner.Scope,owner,TemplateIndex:278,PotionRecipeKey:4975678));
            var identity=s.UniqueItemAllocator.AllocateReference();
            s.State.Containers.Seed(owner==DaggerfallItemOwner.Player?s.State.Inventory.Component.Owner:wagon.Owner,[new(item.Item,UniqueItem:identity)]);
            s.State.ItemInstances.RegisterUnique(identity.Value,item.Metadata);
        }
        var recipe=Assert.Single(s.PotionMaker.ReadRecipes());Assert.False(recipe.Available);
        foreach(int template in recipe.Ingredients) Ingredient(f,template);
        Assert.True(Assert.Single(s.PotionMaker.ReadRecipes()).Available);
        using var restored=f.Restore();Assert.True(Assert.Single(restored.PotionMaker.ReadRecipes()).Available);
    }

    private static DaggerfallServiceProvider Provider(DaggerfallSession s,string service,int faction,int rank)
    {
        s.State.Social.JoinGuild(108,0);for(int step=0;step<rank;step++) s.State.Social.PromoteGuild(108,0);var site=s.Site.ActiveSite!;var location=new DaggerfallNpcSite(site.Id.Region,site.Name,string.Empty);
        long id=s.State.Npcs.RegisterStable(DaggerfallNpcKind.Static,"potion."+service,location,new("Breton","Male",0,0,0,faction),"potion maker",["talk",service]);
        return new(id,location,service);
    }
    private static void Ingredient(SanguineRoseSessionTests.Fixture f,int template,long? wagon=null)
    {
        var s=f.Session;var owner=wagon is long id?DaggerfallItemOwner.Wagon(id):DaggerfallItemOwner.Player;
        var definition=TestPayload.Definitions.ItemTemplateCatalog.Templates[template];var key=$"ingredient.{owner.Scope}.{template}";
        var item=new DaggerfallItemFactory(TestPayload.Definitions,f.Engine.Context.Random).Create(new(definition.Groups[0],key,owner,Quantity:1,TemplateIndex:template));
        var stack=InventoryStackId.Parse(key);s.State.Containers.Seed(wagon is null?s.State.Inventory.Component.Owner:s.State.Wagon.Current!.Owner,[new(item.Item,1,Stack:stack)]);
        s.State.ItemInstances.RegisterStack(owner,stack,item.Metadata);
    }
}
