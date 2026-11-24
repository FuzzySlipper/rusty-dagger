using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Unified questor admission: active placed questors materialize as canonical actors in EntityStore and retire on unload or party escort.</summary>
public sealed class DaggerfallNpcProjectionTests
{
    [Fact]
    public void Active_placed_questors_of_the_site_materialize_as_canonical_actors_and_retire_when_hidden_or_with_party()
    {
        using var fixture = new SanguineRoseSessionTests.Fixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteProfile profile = fixture.Inputs;
        var address = profile.BillboardSprites.Keys.First();
        DaggerfallNpcAppearance look = new("breton", "Male", address.Archive, address.Record, 7, 0);
        DaggerfallSiteRecord site = TestPayload.Definitions.Locations.Records.Single(record => record.Id == profile.Site!.Value);
        DaggerfallNpcSite npcSite = new(site.Region, site.Name, string.Empty, profile.ProfileKey.LogicalId);

        long questor = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "questor-unified", npcSite, look, "quest person", ["talk"]);
        session.State.Npcs.Place(questor, profile.ProfileKey, new WorldPoint(2f, 0f, 3f));
        long unplaced = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "unplaced-questor", npcSite, look, "quest person", ["talk"]);

        session.ReconcileNpcProjection();
        Assert.True(session.State.Actors.TryGet(questor, out ActorState? live));
        Assert.False(session.State.Actors.TryGet(unplaced, out _));
        Assert.Equal(new WorldPoint(2f, 0f, 3f), live!.Position);
        Assert.True(live.Actor.Has<StatsComponent>());
        Assert.Equal(ActorHarmPolicyKind.Protected, live.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.Equal(ActorInteractionPolicyKind.FullDialogue, live.Actor.Get<ActorInteractionPolicy>().Policy);
        Assert.True(live.Actor.Get<ActorOverlayComponent>().IsQuestor);
        Assert.Equal(ActorVisualKind.Billboard, live.Actor.Get<ActorRenderComponent>().Spec.Kind);

        // Hiding retires the body while preserving its profile pose so unhiding rematerializes it at the same spot.
        session.State.Npcs.SetPresence(questor, DaggerfallNpcPresence.Hidden);
        session.ReconcileNpcProjection();
        Assert.False(session.State.Actors.TryGet(questor, out _));

        session.State.Npcs.SetPresence(questor, DaggerfallNpcPresence.Active);
        session.ReconcileNpcProjection();
        Assert.True(session.State.Actors.TryGet(questor, out ActorState? returned));
        Assert.Equal(new WorldPoint(2f, 0f, 3f), returned!.Position);

        // Marking WithParty in ActorLedger unloads the world body so proximity queries never see it.
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            questor,
            ActorOrigin.MintedActor(DaggerfallActorKinds.StaticNpc),
            Location: ActorLocationOverride.WithParty(ActorPartyRole.Escort)));
        session.ReconcileNpcProjection();
        Assert.False(session.State.Actors.TryGet(questor, out _));
    }
}
