using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Rusty.Engine;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDialogueTests
{
    [Fact]
    public void Registered_talk_target_resolves_directions_and_social_skill_use_persists()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        TalkTarget talk = new(session, fixture.Definitions);
        DaggerfallActivationOutcome opened = talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target));

        Assert.True(opened.Applied);
        DaggerfallDialogueView opening = Assert.IsType<DaggerfallDialogueView>(talk.View);
        Assert.Equal("guard", opening.TargetLabel);
        Assert.NotEmpty(opening.Greeting);

        Assert.True(talk.Service.ApplyAction(new("dialogue-tone", Revision: opening.Revision, Tone: "polite")).Applied);
        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: opening.Revision, Topic: "directions")).Applied);
        DaggerfallDialogueView answer = Assert.IsType<DaggerfallDialogueView>(talk.View);
        Assert.False(string.IsNullOrWhiteSpace(answer.Question));
        Assert.False(string.IsNullOrWhiteSpace(answer.Reply));
        Assert.DoesNotContain("%", answer.Reply!, StringComparison.Ordinal);
        Assert.Equal(1, session.State.Progression.SkillUses["etiquette"]);

        // Repeating a question in the same conversation reuses the selected tone result's one skill use.
        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: opening.Revision, Topic: "directions")).Applied);
        Assert.Equal(1, session.State.Progression.SkillUses["etiquette"]);

        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: opening.Revision, Topic: "news")).Applied);
        DaggerfallDialogueView news = Assert.IsType<DaggerfallDialogueView>(talk.View);
        Assert.False(string.IsNullOrWhiteSpace(news.Reply));
        Assert.DoesNotContain("%", news.Reply!, StringComparison.Ordinal);

        DaggerfallSavePayload save = DaggerfallSavePayload.Read(session.CaptureSave());
        using DaggerfallSession restored = fixture.Restore(DaggerfallSavePayload.Encode(save));
        Assert.Equal(1, restored.State.Progression.SkillUses["etiquette"]);
    }

    [Fact]
    public void Rebased_talk_target_preserves_the_open_choice_but_still_rejects_real_movement()
    {
        using ConditionSessionFixture fixture = new();
        TalkTarget talk = new(fixture.Session, fixture.Definitions);
        Assert.True(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
        string revision = Assert.IsType<DaggerfallDialogueView>(talk.View).Revision;
        var delta = new System.Numerics.Vector3(-1000, 0, -1000);
        talk.Actor.ApplyPose(new ActorPose(DaggerfallExteriorSessionOrigin.Shift(talk.Actor.Position, delta), talk.Actor.HeadingYawRadians));
        talk.Service.Rebase(delta);
        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: "directions")).Applied);
        talk.Actor.ApplyPose(new ActorPose(new WorldPoint(talk.Actor.Position.X + 1, talk.Actor.Position.Y, talk.Actor.Position.Z), talk.Actor.HeadingYawRadians));
        Assert.False(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: "news")).Applied);
    }

    [Theory]
    [InlineData("moved")]
    [InlineData("removed")]
    public void Choice_is_rejected_when_its_registered_target_is_no_longer_live_here(string change)
    {
        using ConditionSessionFixture fixture = new();
        TalkTarget talk = new(fixture.Session, fixture.Definitions);
        Assert.True(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
        string revision = Assert.IsType<DaggerfallDialogueView>(talk.View).Revision;

        if (change == "moved")
            talk.Actor.ApplyPose(new ActorPose(new WorldPoint(talk.Actor.Position.X + 1f, talk.Actor.Position.Y, talk.Actor.Position.Z), talk.Actor.HeadingYawRadians));
        else
            fixture.Session.State.Npcs.SetPresence(talk.Npc.DurableId, DaggerfallNpcPresence.Removed);

        DaggerfallActivationOutcome stale = talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: "news"));
        Assert.False(stale.Applied);
        Assert.Null(talk.View);
    }

    [Fact]
    public void Closed_dialogue_rejects_a_choice_from_its_previous_revision()
    {
        using ConditionSessionFixture fixture = new();
        TalkTarget talk = new(fixture.Session, fixture.Definitions);
        Assert.True(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
        string revision = Assert.IsType<DaggerfallDialogueView>(talk.View).Revision;

        Assert.True(talk.Service.ApplyAction(new("dialogue-close", Revision: revision)).Applied);
        Assert.Null(talk.View);
        Assert.False(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: "directions")).Applied);
        Assert.Null(talk.View);
    }

    [Fact]
    public void Region_condition_uses_the_live_variable_store_to_admit_donor_news()
    {
        using ConditionSessionFixture fixture = new();
        TalkTarget talk = new(fixture.Session, fixture.Definitions, attachVariables: true);
        int region = fixture.Session.Site.ActiveSite!.Id.Region;
        Assert.NotEmpty(fixture.Definitions.DialogueWorldRules.News);

        fixture.Session.State.Variables.Write(
            new DaggerfallVariableAddress(DaggerfallVariableScope.Region, region, 11), true);
        Assert.True(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
        string revision = Assert.IsType<DaggerfallDialogueView>(talk.View).Revision;

        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: "news")).Applied);
        DaggerfallDialogueView news = Assert.IsType<DaggerfallDialogueView>(talk.View);
        Assert.True(news.Reply?.Contains("criminal", StringComparison.OrdinalIgnoreCase) == true
            || news.Reply?.Contains("killed", StringComparison.OrdinalIgnoreCase) == true);

        DaggerfallSavePayload save = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        DaggerfallDialogueWorldRumorSave generated = Assert.Single(save.DialogueWorld.Rumors);
        Assert.Equal(DaggerfallDialogueWorldState.CrimeWaveType, generated.Type);
        Assert.Equal(DaggerfallDialogueWorldState.CrimeWaveTextId, generated.TextId);
        World.DaggerfallCalendar savedCalendar = new(save.Calendar.Year, save.Calendar.Month, save.Calendar.Day,
            save.Calendar.Hour, save.Calendar.Minute, save.Calendar.Second);
        Assert.Equal(savedCalendar.ToAbsoluteSeconds() / World.DaggerfallCalendar.SecondsPerMinute
            + DaggerfallDialogueWorldState.GeneratedRumorDurationMinutes, generated.ExpiresAtMinute);

        using DaggerfallSession restored = fixture.Restore(DaggerfallSavePayload.Encode(save));
        DaggerfallDialogueWorldRumorSave restoredGenerated = Assert.Single(
            DaggerfallSavePayload.Read(restored.CaptureSave()).DialogueWorld.Rumors);
        Assert.Equal(generated, restoredGenerated);
    }

    [Fact]
    public void Unknown_site_direction_refuses_without_discovery_and_repeated_knowledge_is_stable()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallDialogueDestination destination = new("direction:site:0:1", "Far place", "on the map", Known: false);
        KeyedRandomFake random = KeyedRandomFake.Create(20);
        bool disclosed = false;
        TalkTarget talk = new(fixture.Session, fixture.Definitions, random: random.Service,
            destination: destination, disclose: _ => { disclosed = true; return true; });
        Assert.True(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
        string revision = Assert.IsType<DaggerfallDialogueView>(talk.View).Revision;

        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: destination.Id)).Applied);
        DaggerfallDialogueView first = Assert.IsType<DaggerfallDialogueView>(talk.View);
        Assert.False(disclosed);
        Assert.False(string.IsNullOrWhiteSpace(first.Reply));
        string[] knowledgeKeys = [.. random.Requests
            .Where(request => request.Key.StartsWith("knowledge:npc:", StringComparison.Ordinal))
            .Select(request => request.Key)];
        Assert.Single(knowledgeKeys);
        Assert.Contains($":topic:{destination.Id}", knowledgeKeys[0], StringComparison.Ordinal);

        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: destination.Id)).Applied);
        Assert.False(disclosed);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<DaggerfallDialogueView>(talk.View).Reply));
        string[] repeatedKnowledgeKeys = [.. random.Requests
            .Where(request => request.Key.StartsWith("knowledge:npc:", StringComparison.Ordinal))
            .Select(request => request.Key)];
        Assert.Equal([knowledgeKeys[0], knowledgeKeys[0]], repeatedKnowledgeKeys);
    }

    [Fact]
    public void Known_site_direction_discloses_once_then_reuses_the_committed_state()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSiteRecord active = fixture.Session.Site.ActiveSite
            ?? throw new InvalidOperationException("The focused direction test needs an admitted site.");
        DaggerfallSiteRecord destination = fixture.Session.Site.Records
            .Where(place => place.Id.Region == active.Id.Region
                && place.Id != active.Id
                && !fixture.Session.Site.IsDiscovered(place.Id))
            .OrderBy(place => place.Id.Index)
            .First();
        string target = $"direction:site:{destination.Id.Region}:{destination.Id.Index}";
        bool known = false;
        DaggerfallDialogueDestination Destination() => new(target, destination.Name, "on the map", known);
        int disclosures = 0;
        TalkTarget talk = new(fixture.Session, fixture.Definitions, random: RandomMinimum.Create(),
            destinationFactory: Destination, disclose: _ =>
            {
                disclosures++;
                known = true;
                fixture.Session.Site.Discover(destination.Id);
                return true;
            });
        Assert.True(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
        string revision = Assert.IsType<DaggerfallDialogueView>(talk.View).Revision;

        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: target)).Applied);
        Assert.Equal(1, disclosures);
        Assert.True(known);
        Assert.True(fixture.Session.Site.IsDiscovered(destination.Id));

        DaggerfallSavePayload save = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        using DaggerfallSession restored = fixture.Restore(DaggerfallSavePayload.Encode(save));
        Assert.True(restored.Site.IsDiscovered(destination.Id));

        Assert.True(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: target)).Applied);
        Assert.Equal(1, disclosures);
    }

    [Fact]
    public void Dialogue_directory_and_activation_reject_a_live_actor_from_an_old_profile()
    {
        using ConditionSessionFixture fixture = new();
        TalkTarget talk = new(fixture.Session, fixture.Definitions);
        DaggerfallWorldProfileKey stale = fixture.Session.Sites.ActiveProfile with { LogicalId = "stale-profile" };
        fixture.Session.State.Npcs.Place(talk.Npc.DurableId, stale, talk.Actor.Position);

        Assert.Empty(talk.Service.NpcTargets());
        DaggerfallActivationOutcome refused = talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target));
        Assert.False(refused.Applied);
        Assert.Contains("no longer available", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dialogue_directory_rejects_a_registered_npc_when_its_actor_entity_is_deleted()
    {
        using ConditionSessionFixture fixture = new();
        TalkTarget talk = new(fixture.Session, fixture.Definitions);
        Assert.NotEmpty(talk.Service.NpcTargets());

        Assert.True(fixture.Session.State.Actors.Entities.Destroy(ActorsState.Identity(talk.Npc.DurableId)));

        Assert.Empty(talk.Service.NpcTargets());
        Assert.False(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
    }

    private sealed class TalkTarget
    {
        private DaggerfallDialogueView? _view;

        internal TalkTarget(DaggerfallSession session, DaggerfallDefinitions definitions, bool attachVariables = false,
            IRandomService? random = null, DaggerfallDialogueDestination? destination = null,
            Func<DaggerfallDialogueDestination>? destinationFactory = null, Func<string, bool>? disclose = null)
        {
            DaggerfallSiteRecord site = session.Site.ActiveSite
                ?? throw new InvalidOperationException("The focused talk test needs the admitted fixture site.");
            (int archive, int record) = session.Sites.Projection.Inputs.BillboardSprites.Keys
                .OrderBy(key => key.Item1).ThenBy(key => key.Item2).First();
            long id = session.State.Npcs.RegisterCivilian(
                new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty),
                new DaggerfallNpcAppearance("Breton", "Female", archive, record, 0, 0), "guard", ["talk"]);
            session.MaterializeNpcActor(id, session.State.Actors.Get(2000).Pose);
            session.State.Npcs.Place(id, session.Sites.ActiveProfile, session.State.Actors.Get(id).Position);
            Npc = session.State.Npcs.Require(id);
            Actor = session.State.Actors.Get(id);
            Service = new DaggerfallDialogueService(
                session.State.Npcs,
                session.State.Actors,
                session.State.Social,
                session.State.SkillUses,
                session.State.Actors.Player.Stats,
                definitions,
                random ?? RandomMinimum.Create(),
                () => session.Site.ActiveSite,
                () => session.State.Character.Identity,
                view => _view = view,
                _ => { },
                resolveDirection: destinationFactory is not null ? _ => destinationFactory() : destination is not null ? _ => destination : null,
                variables: attachVariables ? () => session.State.Variables : null,
                activeProfile: () => session.Sites.ActiveProfile,
                discloseDirection: disclose,
                dialogueWorld: session.State.DialogueWorld);
            Target = Assert.Single(Service.NpcTargets());
        }

        internal DaggerfallNpc Npc { get; }
        internal ActorState Actor { get; }
        internal DaggerfallDialogueService Service { get; }
        internal DaggerfallActivationTarget Target { get; }
        internal DaggerfallDialogueView? View => _view;
    }
}
