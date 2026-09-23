using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDialogueTests
{
    [Fact]
    public void Registered_talk_target_resolves_directions_and_social_skill_use_persists()
    {
        using NormalizedRuntimeSeamTests.ConditionSessionFixture fixture = new();
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

    [Theory]
    [InlineData("moved")]
    [InlineData("removed")]
    public void Choice_is_rejected_when_its_registered_target_is_no_longer_live_here(string change)
    {
        using NormalizedRuntimeSeamTests.ConditionSessionFixture fixture = new();
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
        using NormalizedRuntimeSeamTests.ConditionSessionFixture fixture = new();
        TalkTarget talk = new(fixture.Session, fixture.Definitions);
        Assert.True(talk.Service.ActivateNpc(new(DaggerfallActivationMode.Talk, talk.Target)).Applied);
        string revision = Assert.IsType<DaggerfallDialogueView>(talk.View).Revision;

        Assert.True(talk.Service.ApplyAction(new("dialogue-close", Revision: revision)).Applied);
        Assert.Null(talk.View);
        Assert.False(talk.Service.ApplyAction(new("dialogue-topic", Revision: revision, Topic: "directions")).Applied);
        Assert.Null(talk.View);
    }

    private sealed class TalkTarget
    {
        private DaggerfallDialogueView? _view;

        internal TalkTarget(DaggerfallSession session, DaggerfallDefinitions definitions)
        {
            DaggerfallSiteRecord site = session.Site.ActiveSite
                ?? throw new InvalidOperationException("The focused talk test needs the admitted fixture site.");
            const long id = 2000;
            DaggerfallNpc npc = new(id, DaggerfallNpcKind.Static, "dialogue-test-guard",
                new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty),
                new DaggerfallNpcAppearance("Breton", "Female", 0, 0, 0, 0), "guard", ["talk"],
                DaggerfallNpcPresence.Active, null, null, null);
            session.State.Npcs.Restore([npc]);
            Npc = npc;
            Actor = session.State.Actors.Get(id);
            Service = new DaggerfallDialogueService(
                session.State.Npcs,
                session.State.Actors,
                session.State.Social,
                session.State.SkillUses,
                session.State.Actors.Player.Stats,
                definitions,
                RandomMinimum.Create(),
                () => session.Site.ActiveSite,
                () => session.State.Character.Identity,
                view => _view = view,
                _ => { });
            Target = Assert.Single(Service.NpcTargets());
        }

        internal DaggerfallNpc Npc { get; }
        internal ActorState Actor { get; }
        internal DaggerfallDialogueService Service { get; }
        internal DaggerfallActivationTarget Target { get; }
        internal DaggerfallDialogueView? View => _view;
    }

    private class RandomMinimum : DispatchProxy
    {
        internal static IRandomService Create() => DispatchProxy.Create<IRandomService, RandomMinimum>();

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
            method?.Name == nameof(IRandomService.DrawKeyed)
                ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
                : throw new NotSupportedException(method?.Name);
    }
}
