using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Consumer checks over profiles and providers admitted from the normalized source corpus. These
/// tests deliberately do not register or place a provider: the lifecycle must materialize the
/// source placement before a dialogue, training, or merchant caller can use it.
/// </summary>
public sealed class SourceBackedServiceSessionTests
{
    [Fact]
    public void Normalized_population_is_talkable_and_answers_a_source_directory_direction()
    {
        using SourceBackedServiceSessionFixture fixture = SourceBackedServiceSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.PopulationProfile);

        // Exterior civilians follow the donor's day gate. The normal admitted update reconciles the
        // source pool after this elapsed interval; no test-side RegisterCivilian or Place is used.
        session.AdvanceElapsedTime(6 * 60 * 60);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        DaggerfallNpc npc = session.State.Npcs.All.First(value =>
            value.Kind == DaggerfallNpcKind.Civilian
            && value.StableKey.StartsWith("population/", StringComparison.Ordinal)
            && value.Presence == DaggerfallNpcPresence.Active);
        Assert.Contains("talk", npc.Services);
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            value => value.Identity.Value == checked((ulong)npc.DurableId));
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(
            new(DaggerfallActivationMode.Talk, target));
        Assert.True(opened.Applied, opened.Message);

        DaggerfallDialogueView dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        DaggerfallDialogueTopicOption direction = dialogue.Topics.First(value =>
            value.Id.StartsWith("direction:npc:", StringComparison.Ordinal)
            && !StringComparer.Ordinal.Equals(value.Id, $"direction:npc:{npc.DurableId}"));
        SubmitUi(session, 2, $"{{\"action\":\"dialogue-topic\",\"revision\":\"{Escape(dialogue.Revision)}\",\"topic\":\"{Escape(direction.Id)}\"}}");

        DaggerfallDialogueView answered = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        Assert.False(string.IsNullOrWhiteSpace(answered.Question));
        Assert.False(string.IsNullOrWhiteSpace(answered.Reply));
    }

    [Fact]
    public void Source_training_provider_accepts_the_ui_action_and_persists_elapsed_skill_and_cooldown()
    {
        using SourceBackedServiceSessionFixture fixture = SourceBackedServiceSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.TrainingProvider.Profile);
        session.AdvanceElapsedTime(6 * 60 * 60);

        DaggerfallNpc provider = SourceNpc(session, fixture.TrainingProvider.Placement.Id);
        Assert.Contains("training", provider.Services);
        OpenSourceNpc(session, provider);
        DaggerfallDialogueView dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        DaggerfallSkillTrainingProviderView training = Assert.IsType<DaggerfallSkillTrainingProviderView>(dialogue.Training);
        DaggerfallSkillTrainingSkillView skill = training.Skills.First(value => value.PermanentValue < value.MaximumValue);

        // The provider remains source-backed even when the player setup joins the canonical guild
        // required by a source guild trainer. Temple trainers need no membership.
        if (DaggerfallSkillTrainingPolicy.TryGetProvider(provider.Appearance.FactionId, out DaggerfallSkillTrainingProviderPolicy policy)
            && policy.RequiresMembership && !training.IsMember)
        {
            _ = session.State.Social.JoinGuild(training.MembershipFactionId, 0);
            dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
            training = Assert.IsType<DaggerfallSkillTrainingProviderView>(dialogue.Training);
        }

        AddGold(session, checked(training.Price + 1_000));
        int skillBefore = session.State.SkillUses.PermanentSkillValue(skill.Id);
        ulong goldBefore = session.State.Currency.Read().Gold;
        DaggerfallSavePayload before = DaggerfallSavePayload.Read(session.CaptureSave());
        long beforeSecond = AbsoluteSecond(before.Calendar);

        SubmitUi(session, 3, $"{{\"action\":\"training-commit\",\"revision\":\"{Escape(dialogue.Revision)}\",\"key\":\"{Escape(skill.Id)}\",\"amount\":{training.Price},\"confirm\":true}}");

        Assert.Contains("Training complete", session.Presentation.LastOutcome, StringComparison.Ordinal);
        Assert.Equal(skillBefore + 1, session.State.SkillUses.PermanentSkillValue(skill.Id));
        Assert.Equal(goldBefore - training.Price, session.State.Currency.Read().Gold);
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.True(AbsoluteSecond(saved.Calendar) - beforeSecond >= DaggerfallSkillTrainingPolicy.TrainingDurationSeconds);
        Assert.Equal(beforeSecond, saved.QuestTraining.LastSkillTrainingSecond);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        DaggerfallNpc restoredProvider = SourceNpc(restored, fixture.TrainingProvider.Placement.Id);
        OpenSourceNpc(restored, restoredProvider);
        Assert.Equal(skillBefore + 1, restored.State.SkillUses.PermanentSkillValue(skill.Id));
        Assert.Equal(goldBefore - training.Price, restored.State.Currency.Read().Gold);
        Assert.Equal(saved.QuestTraining.LastSkillTrainingSecond,
            DaggerfallSavePayload.Read(restored.CaptureSave()).QuestTraining.LastSkillTrainingSecond);
    }

    [Fact]
    public void Source_merchant_uses_building_quality_for_buy_sell_payment_and_reload()
    {
        using SourceBackedServiceSessionFixture fixture = SourceBackedServiceSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.MerchantProvider.Profile);
        session.AdvanceElapsedTime(6 * 60 * 60);

        DaggerfallNpc provider = SourceNpc(session, fixture.MerchantProvider.Placement.Id);
        Assert.Contains(provider.Services, value => value is "shop" or "merchant" or "buy-items");
        Assert.Contains(provider.Services, value => value is "shop" or "merchant" or "sell-items");
        OpenSourceNpc(session, provider);
        DaggerfallDialogueView dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        DaggerfallMerchantView merchant = Assert.IsType<DaggerfallMerchantView>(dialogue.Merchant);
        DaggerfallSiteBuildingSource sourceBuilding = fixture.SourceBuilding(fixture.MerchantProvider.Profile);
        Assert.Equal(sourceBuilding.Quality, merchant.Quality);
        DaggerfallMerchantItemView stock = merchant.Stock.First(value => value.CanBuy && value.UnitPrice > 0);
        int mercantileBeforeBuy = SkillUseCount(session, "mercantile");
        int pickpocketBeforeShoplift = SkillUseCount(session, "pickpocket");

        AddGold(session, checked(stock.UnitPrice * 2 + 1_000));
        ulong goldBeforeBuy = session.State.Currency.Read().Gold;
        string buyAction = $"{{\"action\":\"merchant-buy\",\"revision\":\"{Escape(merchant.Revision)}\",\"item\":\"{Escape(stock.Key)}\",\"amount\":1}}";
        SubmitUi(session, 4, buyAction);

        Assert.Equal("Purchased", session.Presentation.LastOutcome);
        Assert.Equal(goldBeforeBuy - stock.UnitPrice, session.State.Currency.Read().Gold);
        Assert.Equal(mercantileBeforeBuy + 1, SkillUseCount(session, "mercantile"));
        SubmitUi(session, 5, buyAction);
        Assert.Equal("Stale", session.Presentation.LastOutcome);
        Assert.Equal(mercantileBeforeBuy + 1, SkillUseCount(session, "mercantile"));

        DaggerfallMerchantView afterBuy = Assert.IsType<DaggerfallMerchantView>(session.ActivationView.Dialogue!.Merchant);
        DaggerfallMerchantItemView sold = Assert.Single(afterBuy.PlayerItems,
            value => value.Definition == stock.Definition && value.CanSell && value.UnitPrice > 0);
        ulong goldBeforeSell = session.State.Currency.Read().Gold;
        string sellAction = $"{{\"action\":\"merchant-sell\",\"revision\":\"{Escape(afterBuy.Revision)}\",\"item\":\"{Escape(sold.Key)}\",\"amount\":1}}";
        SubmitUi(session, 6, sellAction);

        Assert.Equal("Sold", session.Presentation.LastOutcome);
        Assert.Equal(goldBeforeSell + sold.UnitPrice, session.State.Currency.Read().Gold);
        Assert.Equal(mercantileBeforeBuy + 2, SkillUseCount(session, "mercantile"));
        SubmitUi(session, 7, sellAction);
        Assert.Equal("Stale", session.Presentation.LastOutcome);
        Assert.Equal(mercantileBeforeBuy + 2, SkillUseCount(session, "mercantile"));

        // The real source caller must keep refusal paths from manufacturing a trade use. The sold
        // item is no longer in the player container, so this uses the current revision and reaches
        // the ordinary ItemUnavailable branch rather than replaying the stale accepted action.
        DaggerfallMerchantView afterSell = Assert.IsType<DaggerfallMerchantView>(session.ActivationView.Dialogue!.Merchant);
        SubmitUi(session, 8, $"{{\"action\":\"merchant-sell\",\"revision\":\"{Escape(afterSell.Revision)}\",\"item\":\"{Escape(sold.Key)}\",\"amount\":1}}");
        Assert.Equal("ItemUnavailable", session.Presentation.LastOutcome);
        Assert.Equal(mercantileBeforeBuy + 2, SkillUseCount(session, "mercantile"));

        ulong carriedGold = session.State.Currency.Read().Gold;
        Assert.True(carriedGold > 0);
        Assert.True(session.State.Currency.TrySpendGold(carriedGold, []));
        DaggerfallMerchantItemView unaffordable = afterSell.Stock.First(value => value.CanBuy && value.UnitPrice > 0);
        DaggerfallMerchantView noFunds = Assert.IsType<DaggerfallMerchantView>(session.ActivationView.Dialogue!.Merchant);
        SubmitUi(session, 9, $"{{\"action\":\"merchant-buy\",\"revision\":\"{Escape(noFunds.Revision)}\",\"item\":\"{Escape(unaffordable.Key)}\",\"amount\":1}}");
        Assert.Equal("InsufficientFunds", session.Presentation.LastOutcome);
        Assert.Equal(mercantileBeforeBuy + 2, SkillUseCount(session, "mercantile"));

        // Shoplifting is an admitted source-backed merchant action even when the deterministic
        // fixture roll catches the player. The attempt is recorded before the caught/success result.
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("pickpocket")).BaseValue = 100;
        DaggerfallMerchantView shopliftView = Assert.IsType<DaggerfallMerchantView>(session.ActivationView.Dialogue!.Merchant);
        DaggerfallMerchantItemView stealable = shopliftView.Stock.First(value => value.CanBuy && value.UnitPrice > 0);
        string shopliftAction = $"{{\"action\":\"merchant-shoplift\",\"revision\":\"{Escape(shopliftView.Revision)}\",\"item\":\"{Escape(stealable.Key)}\",\"amount\":1}}";
        SubmitUi(session, 10, shopliftAction);
        Assert.Equal("Caught", session.Presentation.LastOutcome);
        Assert.Equal(pickpocketBeforeShoplift + 1, SkillUseCount(session, "pickpocket"));

        ulong goldAfterSell = session.State.Currency.Read().Gold;
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallMerchantSave merchantSave = Assert.Single(saved.Merchants);
        Assert.Equal(sourceBuilding.Quality, merchantSave.Quality);
        Assert.Equal(fixture.MerchantProvider.Profile.Site!.Value.Region, merchantSave.ProviderRegion);
        Assert.Equal(mercantileBeforeBuy + 2, saved.SkillUses.Counters.Single(value => value.Skill == "mercantile").Uses);
        Assert.Equal(pickpocketBeforeShoplift + 1, saved.SkillUses.Counters.Single(value => value.Skill == "pickpocket").Uses);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        DaggerfallNpc restoredProvider = SourceNpc(restored, fixture.MerchantProvider.Placement.Id);
        OpenSourceNpc(restored, restoredProvider);
        DaggerfallMerchantView restoredMerchant = Assert.IsType<DaggerfallMerchantView>(restored.ActivationView.Dialogue!.Merchant);
        Assert.Equal(sourceBuilding.Quality, restoredMerchant.Quality);
        Assert.Equal(goldAfterSell, restored.State.Currency.Read().Gold);
        Assert.Equal(mercantileBeforeBuy + 2, SkillUseCount(restored, "mercantile"));
        Assert.Equal(pickpocketBeforeShoplift + 1, SkillUseCount(restored, "pickpocket"));
        Assert.Contains(restoredMerchant.Stock, value => value.Definition == sold.Definition);
        DaggerfallSavePayload restoredSave = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Equal(saved.RegionalPrices!.LastAdvancedDay, restoredSave.RegionalPrices!.LastAdvancedDay);
        Assert.Equal(saved.RegionalPrices.Factors, restoredSave.RegionalPrices.Factors);
        Assert.Contains(restoredSave.Merchants, value => value.Key == merchantSave.Key && value.Quality == merchantSave.Quality);
    }

    private static void OpenSourceNpc(DaggerfallSession session, DaggerfallNpc npc)
    {
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            value => value.Identity.Value == checked((ulong)npc.DurableId));
        DaggerfallActivationOutcome outcome = session.Dialogue.ActivateNpc(
            new(DaggerfallActivationMode.Talk, target));
        Assert.True(outcome.Applied, outcome.Message);
    }

    private static DaggerfallNpc SourceNpc(DaggerfallSession session, string stableKey) =>
        Assert.Single(session.State.Npcs.All, value => value.StableKey == stableKey
            && value.Kind == DaggerfallNpcKind.Static && value.Presence == DaggerfallNpcPresence.Active);

    private static void SubmitUi(DaggerfallSession session, ulong step, string action) =>
        session.Update(new ProductUpdate(OuterUpdate(step), [Ui(action)]));

    private static int SkillUseCount(DaggerfallSession session, string skill) =>
        session.State.SkillUses.Capture().Counters.Single(value => value.Skill == skill).Uses;

    private static void AddGold(DaggerfallSession session, ulong amount)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Currency", "source.consumer.test.gold", DaggerfallItemOwner.Player,
            Quantity: amount, TemplateIndex: 276)), session.State.Inventory, session.State.ItemInstances,
            InventoryStackId.Parse("source.consumer.test.gold"));
    }

    private static long AbsoluteSecond(DaggerfallCalendarSave calendar) =>
        new DaggerfallCalendar(calendar.Year, calendar.Month, calendar.Day, calendar.Hour, calendar.Minute, calendar.Second).ToAbsoluteSeconds();

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

internal sealed class SourceBackedServiceSessionFixture : IDisposable
{
    private const string SiteRole = "daggerfall.site";
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallSiteProfiles _profiles;
    private readonly DaggerfallSessionComposition _composition;
    private readonly DaggerfallSiteProfile[] _sites;

    internal DaggerfallSiteProfile PopulationProfile { get; }
    internal SourceProvider TrainingProvider { get; }
    internal SourceProvider MerchantProvider { get; }

    private SourceBackedServiceSessionFixture(
        DaggerfallDefinitions definitions,
        DaggerfallSiteProfile[] sites,
        DaggerfallSiteProfiles profiles,
        DaggerfallSessionComposition composition,
        DaggerfallSiteProfile populationProfile,
        SourceProvider trainingProvider,
        SourceProvider merchantProvider)
    {
        _definitions = definitions;
        _sites = sites;
        _profiles = profiles;
        _composition = composition;
        PopulationProfile = populationProfile;
        TrainingProvider = trainingProvider;
        MerchantProvider = merchantProvider;
    }

    internal static SourceBackedServiceSessionFixture Create()
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        ResolvedGameComposition resolved = GameCompositionResolver.Resolve(content,
            new GameBundleId("daggerfall.privateers-hold")).RequireComposition();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile[] sites = [.. resolved.ContentPacks
            .Where(pack => pack.Role == new ContentPackRoleId(SiteRole))
            .Select(pack => DaggerfallSiteContent.Read(content, pack.Payload, definitions))];
        DaggerfallSiteProfiles profiles = new(sites);
        DaggerfallSiteProfile population = sites
            .Where(profile => profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && profile.Population.Count > 0)
            .OrderBy(profile => profile.ProfileKey.LogicalId, StringComparer.Ordinal)
            .First();
        SourceProvider training = sites
            .SelectMany(profile => profile.StaticNpcs.Select(placement => new SourceProvider(profile, placement)))
            .Where(value => value.Placement.Services.Contains("training", StringComparer.Ordinal))
            .OrderBy(value => value.Profile.ProfileKey.LogicalId, StringComparer.Ordinal)
            .ThenBy(value => value.Placement.Id, StringComparer.Ordinal)
            .First();
        SourceProvider merchant = sites
            .SelectMany(profile => profile.StaticNpcs.Select(placement => new SourceProvider(profile, placement)))
            .Where(value => value.Placement.Services.Any(service => service is "shop" or "merchant" or "buy-items")
                && value.Placement.Services.Any(service => service is "shop" or "merchant" or "sell-items"))
            .OrderBy(value => value.Profile.ProfileKey.LogicalId, StringComparer.Ordinal)
            .ThenBy(value => value.Placement.Id, StringComparer.Ordinal)
            .First();
        ContentPack blocksPack = resolved.ContentPacks.Single(pack => pack.Role == new ContentPackRoleId("daggerfall.blocks"));
        DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(blocksPack.Payload);
        blocks.AdmitLocations(definitions.Locations);
        DaggerfallSessionComposition composition = new(definitions, population, DaggerfallTuning.Defaults, resolved.Identity)
        {
            Profiles = profiles,
            Blocks = blocks,
        };
        return new(definitions, sites, profiles, composition, population, training, merchant);
    }

    internal DaggerfallSession Start(DaggerfallSiteProfile profile)
    {
        List<string> releases = [];
        ContentFake content = new(releases);
        foreach (DaggerfallSiteProfile site in _sites) PopulateContent(content, site);
        SpatialFake spatial = SpatialFake.Create(profile.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        return DaggerfallSession.StartNew(engine.Context, _composition with { StartSite = profile });
    }

    internal DaggerfallSession Restore(RulesetSavePayload saved)
    {
        DaggerfallSavePayload payload = DaggerfallSavePayload.Read(saved);
        DaggerfallSiteProfile active = payload.Site.ActiveProfile is { } profile
            ? _profiles.Require(profile.Require())
            : payload.Site.Active is { } activeSite
                ? _profiles.RequireUniqueSite(new(activeSite.Region!.Value, activeSite.Index!.Value))
                : throw new InvalidOperationException("A source-backed session save must name its active site.");
        List<string> releases = [];
        ContentFake content = new(releases);
        foreach (DaggerfallSiteProfile site in _sites) PopulateContent(content, site);
        SpatialFake spatial = SpatialFake.Create(active.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        return DaggerfallSession.Restore(engine.Context, _composition with { StartSite = active }, saved);
    }

    internal DaggerfallSiteBuildingSource SourceBuilding(DaggerfallSiteProfile profile)
    {
        DaggerfallInteriorBuilding building = profile.InteriorBuilding
            ?? throw new InvalidOperationException($"Source provider profile '{profile.ProfileKey.LogicalId}' has no interior building.");
        DaggerfallSiteRecord site = _definitions.Locations.Records.Single(value => value.Id == profile.Site!.Value);
        DaggerfallSiteBuildingId id = new(building.BlockX, building.BlockY, building.Building.Index);
        return site.Exterior?.Buildings.GetValueOrDefault(id)
            ?? throw new InvalidOperationException($"Source provider profile '{profile.ProfileKey.LogicalId}' has no admitted building source '{id}'.");
    }

    public void Dispose() { }

    internal sealed record SourceProvider(DaggerfallSiteProfile Profile, DaggerfallStaticNpcPlacement Placement);
}
