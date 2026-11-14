using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Where a quest's resources stand, and its reward, built without the session: the placement and item placement owners
/// over the fixture's actors, roster, piles and site crossing.
/// </summary>
public sealed class DaggerfallQuestPlacementTests
{
    private static DaggerfallQuestItemPlacement Items(DaggerfallSession session) => new(session.State.Quests, session.State.QuestItems,
        session.State.ItemInstances, session.State.Actors, session.State.PlayerControl, session.Sites, session.GroundContainers, session.CorpseLoot,
        session.UniqueItemAllocator);

    private static DaggerfallQuestPlacement Placement(DaggerfallSession session, DaggerfallQuestItemPlacement items) => new(session.State.Actors,
        session.State.Npcs, session.Roster, session.Grounding, session.NpcProjection, session.Site, session.Sites, session.GroundContainers,
        session.UniqueItemAllocator, items, _ => throw new InvalidOperationException("No quest actor is kept in an unloaded site."));

    private static DaggerfallQuestInstanceSave Start(SanguineRoseSessionTests.Fixture fixture, DaggerfallDefinitions definitions)
    {
        DaggerfallSiteRecord site = definitions.Locations.Records.Single(value => value.Id == fixture.Inputs.Site);
        return fixture.Session.State.Quests.Start(new("world-test", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index))
                with { PlaceSelection = new(fixture.Inputs.ProfileKind, site.MapId, null, 0) })], []));
    }

    [Fact]
    public void A_queued_foe_and_item_stand_at_the_marker_once()
    {
        DaggerfallDefinitions definitions = QuestWorldAdmissionTests.Definitions();
        using SanguineRoseSessionTests.Fixture fixture = new(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        DaggerfallSession session = fixture.Session;
        DaggerfallQuestInstanceSave instance = Start(fixture, definitions);
        string item = instance.Resources.Single(value => value.SelectedItem is not null).Symbol;
        string foe = instance.Resources.Single(value => value.SelectedFoe is not null).Symbol;
        session.State.Quests.RequestPlacement(instance.InstanceId, "place-item", item, "location");
        session.State.Quests.RequestPlacement(instance.InstanceId, "place-foe", foe, "location");
        int actors = session.State.Actors.All.Count();
        DaggerfallQuestPlacement placement = Placement(session, Items(session));

        session.State.Quests.AdmitPlacements(fixture.Inputs, placement);
        session.State.Quests.AdmitPlacements(fixture.Inputs, placement);

        Assert.True(session.State.Quests.TryGet(instance.InstanceId, out DaggerfallQuestInstanceSave? placed));
        long actor = Assert.Single(placed!.Resources.Single(value => value.Symbol == foe).Binding.ActorIds);
        Assert.True(session.State.Actors.TryGet(actor, out _));
        Assert.Equal(actors + 1, session.State.Actors.All.Count());
        ulong unique = Assert.Single(placed.Resources.Single(value => value.Symbol == item).Binding.UniqueItemIds);
        Assert.Equal("ground", session.State.ItemInstances.RequireUnique(unique).Owner.Scope);
        Assert.All(placed.Placements, operation => Assert.Equal(fixture.Inputs.QuestMarkers.Single().Id, operation.Applied!.MarkerId));
    }

    [Fact]
    public void A_reward_is_set_on_the_ground_under_the_player_and_its_pile_answered()
    {
        DaggerfallDefinitions definitions = QuestWorldAdmissionTests.Definitions();
        using SanguineRoseSessionTests.Fixture fixture = new(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        DaggerfallSession session = fixture.Session;
        DaggerfallQuestInstanceSave started = Start(fixture, definitions);
        string item = started.Resources.Single(value => value.SelectedItem is not null).Symbol;
        DaggerfallQuestItemPlacement items = Items(session);

        // The reward reads the quest's resources from the instance it is handed, as the quest's own reward action hands it.
        DaggerfallQuestRuntimeInstance runtime = new(started, DaggerfallQuestTaskCompiler.Compile(definitions.QuestSources.Resolve(started.SourceFile)));
        long? pile = items.OfferReward(runtime, item);

        Assert.NotNull(pile);
        Assert.True(session.GroundContainers.TryGet(pile.Value, out _));
        Assert.Single(session.State.ItemInstances.UniqueItems, pair => pair.Value.Owner == DaggerfallItemOwner.Ground(pile.Value)
            && pair.Value.QuestItemSymbol == item);
    }
}
