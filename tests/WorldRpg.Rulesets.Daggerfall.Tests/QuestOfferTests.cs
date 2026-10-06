using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestOfferTests
{
    [Fact]
    public void Coven_work_uses_nonmember_witches_pool_and_actual_offer_acceptance()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = SummonRandom.Create();
        using var session = fixture.Start(fixture.ExteriorProfile);
        var faction = TestPayload.Definitions.Factions.Factions[419];
        var facts = DaggerfallNpcServiceFacts.Resolve(TestPayload.Definitions, faction, -1, 0, "witch");
        Assert.Contains("quest", facts.Services);
        Assert.Contains("daedra-summoning", facts.Services);
        var site = session.Site.ActiveSite!;
        long provider = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "coven-work", new(site.Region, site.Name, string.Empty),
            new("breton", "Female", 184, 8, 0, faction.Id), facts.Role, facts.Services);
        var pool = session.State.Quests.OrdinaryWorkPool(faction.Id, false, 20, 100, 0, DaggerfallCharacterGender.Female);
        Assert.Equal(10, pool.Length);
        Assert.All(pool, row => Assert.Equal("Witches", row.Group));
        string text = session.OfferQuestWork(provider);
        Assert.True(session.State.Quests.PendingOffer is not null, text);
        var offer = Assert.IsType<DaggerfallQuestOfferSave>(session.State.Quests.PendingOffer);
        Assert.StartsWith("Q0", offer.Quest.DefinitionName);
        session.AnswerQuestOffer(offer.Quest.InstanceId, true);
        Assert.Single(session.State.Quests.All);
        using var restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(offer.Quest.InstanceId, Assert.Single(restored.State.Quests.All).InstanceId);
    }

    [Theory]
    [InlineData(40)] [InlineData(510)]
    public void Real_provider_offer_preserves_selected_resources_through_encoded_restore_and_acceptance(int faction)
    {
        using var f = new SanguineRoseSessionTests.Fixture(random: SummonRandom.Create(), prepareComposition: c => c with {
            Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))) });
        var s = f.Session;
        long giver = Provider(s, faction);
        var npc = s.State.Npcs.Require(giver);
        DaggerfallQuestOfferSave? offered = null;
        List<string> errors = [];
        for (int day = 0; day < 30 && offered is null; day++)
        {
            try { offered = s.State.Quests.PrepareWorkOffer(giver, npc.Site, faction, false, 10, 20, 0, DaggerfallCharacterGender.Female, day); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { errors.Add(error.Message); }
        }
        Assert.True(offered is not null, string.Join("\n", errors.Distinct()));
        Assert.Empty(s.State.Quests.All);
        Assert.Equal(giver, offered!.Quest.QuestorId);
        Assert.NotEmpty(offered.Quest.Resources);
        Assert.Equal(offered, s.State.Quests.PrepareWorkOffer(giver, npc.Site, faction, false, 10, 20, 0, DaggerfallCharacterGender.Female, 100));
        using var restored = f.Restore();
        Assert.Equal(s.State.Quests.ReadOffer()!.Text, restored.State.Quests.ReadOffer()!.Text);
        Assert.Equal(offered.Quest.Resources.Select(x => x.Text), restored.State.Quests.PendingOffer!.Quest.Resources.Select(x => x.Text));
        restored.AnswerQuestOffer(offered.Quest.InstanceId, true);
        Assert.Null(restored.State.Quests.PendingOffer);
        var accepted = Assert.Single(restored.State.Quests.All);
        Assert.Equal(offered.Quest.SourceFile, accepted.SourceFile);
        Assert.Equal(offered.Quest.Resources.Select(x => x.Text), accepted.Resources.Select(x => x.Text));
        restored.AnswerQuestOffer(offered.Quest.InstanceId, true);
        Assert.Single(restored.State.Quests.All);
        Assert.True(restored.State.Quests.ProviderHasActiveWork(giver));
    }

    [Fact]
    public void Ordinary_work_dialogue_prepares_an_offer_and_refusal_admits_no_quest()
    {
        using var f = new SanguineRoseSessionTests.Fixture(random: FirstWorkOffer(), prepareComposition: c => c with {
            Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))) });
        long giver = Provider(f.Session, 510);
        string text = f.Session.OfferQuestWork(giver);
        Assert.Equal("K0C00Y02.txt", f.Session.State.Quests.PendingOffer!.Quest.SourceFile);
        Assert.Equal(text, f.Session.State.Quests.ReadOffer()!.Text);
        string id = f.Session.State.Quests.PendingOffer!.Quest.InstanceId;
        f.Session.AnswerQuestOffer(id, false);
        Assert.Null(f.Session.State.Quests.PendingOffer);
        Assert.Empty(f.Session.State.Quests.All);
        Assert.Empty(f.Session.State.Quests.Capture().AcceptedOneTimeSources);
        Assert.NotNull(DaggerfallUiAction.Parse(System.Text.Encoding.UTF8.GetBytes("{\"action\":\"quest-offer-answer\",\"questInstance\":\"" + id + "\",\"confirm\":true}")));
    }

    [Fact]
    public void Missing_local_quest_destination_reports_unavailable_without_admitting_work()
    {
        using var f = new SanguineRoseSessionTests.Fixture(random: SummonRandom.Create());
        long giver = Provider(f.Session, 41);
        Assert.Contains("unavailable", f.Session.OfferQuestWork(giver), StringComparison.OrdinalIgnoreCase);
        Assert.Null(f.Session.State.Quests.PendingOffer);
        Assert.Empty(f.Session.State.Quests.All);
    }

    [Fact]
    public void Offer_expires_when_its_provider_is_unavailable()
    {
        using var f = new SanguineRoseSessionTests.Fixture(random: FirstWorkOffer(), prepareComposition: c => c with {
            Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))) });
        long giver = Provider(f.Session, 510);
        f.Session.OfferQuestWork(giver);
        string id = f.Session.State.Quests.PendingOffer!.Quest.InstanceId;
        Assert.Contains("no longer available", f.Session.State.Quests.AnswerOffer(id, true, false));
        Assert.Null(f.Session.State.Quests.PendingOffer);
        Assert.Empty(f.Session.State.Quests.All);
    }

    [Fact]
    public void Source_guild_questor_exposes_work_and_accepts_selected_corpus_offer_through_dialogue()
    {
        using var f = SourceBackedGuildBankSessionFixture.Create();
        using var session = f.Start(f.MagesProfile);
        var npc = Assert.Single(session.State.Npcs.All, npc => npc.Kind == DaggerfallNpcKind.Static
            && npc.Presence == DaggerfallNpcPresence.Active && npc.Services.Contains("quests"));
        var target = Assert.Single(session.Dialogue.NpcTargets(), target => target.Identity.Value == checked((ulong)npc.DurableId));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        var dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        Assert.Contains(dialogue.Topics, topic => topic.Id == "work");
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "dialogue-topic", revision = dialogue.Revision, topic = "work" }))]));
        var offer = Assert.IsType<DaggerfallQuestOfferSave>(session.State.Quests.PendingOffer);
        Assert.Equal(npc.DurableId, offer.Quest.QuestorId);
        Assert.NotEmpty(offer.Quest.Resources);
        using var restored = f.Restore(session.CaptureSave());
        restored.AnswerQuestOffer(offer.Quest.InstanceId, true);
        Assert.Single(restored.State.Quests.All);
        Assert.Null(restored.State.Quests.PendingOffer);
    }

    [Fact]
    public void Source_social_contact_is_consumed_by_refusal_and_stays_consumed_after_restore()
    {
        using var f = SourceBackedGuildBankSessionFixture.Create(DaggerfallTuning.Defaults with {
            QuestOffers = new(100, 100) });
        using var session = f.Start(f.BankProfile);
        var npc = session.State.Npcs.All.First(npc => npc.Kind == DaggerfallNpcKind.Static
            && npc.Presence == DaggerfallNpcPresence.Active && npc.Services.Contains("quest-candidate")
            && !DaggerfallNpcServiceFacts.IsChild(npc.Appearance));
        string text = session.OfferQuestWork(npc.DurableId);
        var offer = Assert.IsType<DaggerfallQuestOfferSave>(session.State.Quests.PendingOffer);
        session.AnswerQuestOffer(offer.Quest.InstanceId, false);
        Assert.Empty(session.State.Quests.All);
        Assert.Contains("no work", session.OfferQuestWork(npc.DurableId));
        using var restored = f.Restore(session.CaptureSave());
        Assert.Contains("no work", restored.OfferQuestWork(npc.DurableId));
        Assert.Null(restored.State.Quests.PendingOffer);
        Assert.True(restored.State.Quests.Capture().WorkPool.Contacts.Single(contact => contact.Npc == npc.DurableId).Consumed);
    }

    [Theory]
    [InlineData(false, "O0A0AL00")]
    [InlineData(true, "L0A01L00")]
    public void Due_criminal_invitation_is_admitted_by_the_outside_calendar_once(bool murder, string source)
    {
        using var f = SourceBackedGuildBankSessionFixture.Create();
        f.Random = SummonRandom.Create();
        using var session = f.Start(f.ExteriorProfile);
        var credit = murder ? Crime.DaggerfallCrimeGuildCredit.CivilianMurder : Crime.DaggerfallCrimeGuildCredit.Thieving;
        int count = murder ? 3 : Crime.DaggerfallCrimeState.ThievingInvitationThreshold;
        var calendar = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        long minute = new World.DaggerfallCalendar(calendar.Year, calendar.Month, calendar.Day, calendar.Hour, calendar.Minute, calendar.Second).ToAbsoluteSeconds() / 60;
        for (int i = 0; i < count; i++) session.State.Crime.RecordGuildRequirementProgress("invitation:" + i, credit, minute);
        session.AdvanceElapsedTime((Crime.DaggerfallCrimeState.InvitationDelayMinutes + 1) * 60);
        Assert.True(session.State.Quests.All.Any(quest => quest.SourceFile == source + ".txt"), session.Presentation.LastOutcome);
        using var restored = f.Restore(session.CaptureSave());
        restored.AdvanceElapsedTime(60);
        Assert.Single(restored.State.Quests.All, quest => quest.SourceFile == source + ".txt");
    }

    /// <summary>
    /// Selects the first quest of the provider's ordinary pool (K0C00Y02), which this fixture's site can
    /// place; the pool's other quest needs a local home the fixture's town does not carry.
    /// </summary>
    private static Rusty.Engine.IRandomService FirstWorkOffer()
    {
        var (random, fake) = WorkOfferRandom.Create();
        fake.WorkOffer = _ => 0;
        return random;
    }

    private static long Provider(DaggerfallSession session, int faction)
    {
        var site = session.Site.ActiveSite!;
        return session.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "offer-provider:" + faction,
            new(site.Region, site.Name, ""), new("breton", "Female", 0, 0, 9, faction), "Quest provider", ["talk", "quest"]);
    }
}
