using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The dispel and Create Item choices built alone over their inputs; their spells and saves are the session's.</summary>
public sealed class DaggerfallSpellChoicesTests
{
    [Fact]
    public void A_restored_dispel_choice_answers_only_its_own_revision_and_cancelling_closes_it()
    {
        using ActorsState actors = new();
        var player = TestPayload.Definitions.RequireActor(new("player"));
        actors.CreatePlayer(1, new EntityTypeId("player"), new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, TestPayload.Definitions.Catalogs.RequireCareer("class00"))),
            "health", DaggerActorFactory.PlayerCapabilities);
        PresentationState presentation = new("", 5d);
        DaggerfallDispel dispel = new(new DaggerfallEffectLifecycle(actors, DaggerfallEffectCatalog.Empty), RandomMinimum.Create(),
            presentation, new DaggerfallDispelRequest("cast.7.1", 40));

        DaggerfallDispelView view = dispel.View!;
        Assert.Equal("cast.7.1", view.Revision);
        Assert.Empty(view.Options);
        dispel.Choose("stale", "bundle");
        Assert.Equal("Dispel choice is no longer current.", presentation.LastOutcome);
        dispel.Choose(view.Revision, "bundle");
        Assert.Equal("That effect bundle is no longer active.", presentation.LastOutcome);
        Assert.NotNull(dispel.Pending);
        dispel.Choose(view.Revision, null);
        Assert.Equal("Dispel cancelled.", presentation.LastOutcome);
        Assert.Null(dispel.View);
    }

    [Fact]
    public void Create_item_choices_queue_in_order_and_a_chosen_item_is_conjured_until_the_spells_minutes_run_out()
    {
        using GuildMakerFixture f = new();
        PresentationState presentation = new("", 5d);
        DaggerfallCreateItem create = new(f.Definitions, RandomMinimum.Create(), f.Inventory, f.Instances,
            new DaggerfallEncumbrancePolicy(f.Inventory, f.Stats), f.UniqueItems, f.Character, () => f.Calendar, presentation, null);
        create.Request(new("first", 30));
        create.Request(new("second", 60));

        DaggerfallCreateItemView view = create.View!;
        Assert.Equal("first", view.Revision);
        Assert.Equal(29, view.Options.Count);
        create.Choose("stale", "weapon-113");
        Assert.Equal("Create Item choice is no longer current.", presentation.LastOutcome);
        create.Cancel("first");
        Assert.Equal("second", create.View!.Revision);
        create.Choose("second", "weapon-113");
        Assert.Equal("Created Steel Dagger for 60 minutes.", presentation.LastOutcome);
        Assert.Null(create.View);
        var dagger = Assert.Single(f.Instances.UniqueItems.Select(entry => entry.Value), item => item.Conjuration is not null);
        Assert.Equal(f.Calendar.MinuteNumber + 60, dagger.Conjuration!.ExpiresAtMinute);
    }
}
