using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSkillTrainingServiceTests
{
    [Fact]
    public void Provider_policy_matches_donor_guild_and_temple_skill_lists_and_limits()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();

        DaggerfallSkillTrainingProviderPolicy mages = Assert.Single(DaggerfallSkillTrainingPolicy.AllProviders,
            provider => provider.NpcServiceFactionId == 61);
        DaggerfallSkillTrainingProviderPolicy fighters = Assert.Single(DaggerfallSkillTrainingPolicy.AllProviders,
            provider => provider.NpcServiceFactionId == 849);
        DaggerfallSkillTrainingProviderPolicy arkay = Assert.Single(DaggerfallSkillTrainingPolicy.AllProviders,
            provider => provider.NpcServiceFactionId == 241);

        Assert.Equal(50, mages.MaximumPermanentSkill);
        Assert.True(mages.RequiresMembership);
        Assert.False(arkay.RequiresMembership);
        Assert.Contains("alteration", mages.Skills);
        Assert.Contains("thaumaturgy", mages.Skills);
        Assert.Contains("swimming", fighters.Skills);
        Assert.Contains("medical", arkay.Skills);
        Assert.Equal(100UL, DaggerfallSkillTrainingPolicy.Price(1, member: true));
        Assert.Equal(400UL, DaggerfallSkillTrainingPolicy.Price(1, member: false));
        Assert.Equal(10_800, DaggerfallSkillTrainingPolicy.TrainingDurationSeconds);
        Assert.Equal(43_200, DaggerfallSkillTrainingPolicy.TrainingCooldownSeconds);
        Assert.All(DaggerfallSkillTrainingPolicy.AllProviders.SelectMany(provider => provider.Skills), skill =>
            Assert.Contains(definitions.Vocabulary.Skills, known => known.Value == skill));
    }

    [Fact]
    public void Accepted_training_quotes_cost_and_advances_permanent_skill_shared_time_fatigue_and_cooldown()
    {
        DaggerfallSkillTrainingProviderPolicy provider = PickProvider(requiresMembership: true, out string skill, out DaggerfallCareerDefinition career);
        using Fixture f = new(provider, skill, career, grantGold: true, start: new(405, 5, 0, 23, 0, 0), level: 3);
        f.Social.JoinGuild(provider.MembershipFactionId, 0);
        f.SetSkill(skill, 7);
        DaggerfallServiceRequest request = f.Request("train-first");
        DaggerfallSkillTrainingProviderView view = Assert.IsType<DaggerfallSkillTrainingProviderView>(f.TrainingService.ReadProvider(request.Provider));
        Assert.Equal(300UL, view.Price);
        Assert.Equal(0, view.Rank);
        Assert.Equal(DaggerfallSkillTrainingPolicy.TrainingDurationSeconds, view.DurationSeconds);
        Assert.Contains(view.Skills, option => option.Id == skill && option.PermanentValue == 7 && option.MaximumValue == 50);

        int currentSkillSum = f.SkillProgression.CurrentLevelUpSkillSum;
        ulong goldBefore = f.Currency.Read().Gold;
        double fatigueBefore = f.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current;
        long trainedAt = f.Calendar.ToAbsoluteSeconds();
        DaggerfallSkillTrainingQuote quote = Assert.IsType<DaggerfallSkillTrainingQuote>(f.TrainingService.Quote(request, skill).Quote);
        Assert.Equal(300UL, quote.Price);
        Assert.Equal(DaggerfallSkillTrainingPolicy.TrainingDurationSeconds, quote.DurationSeconds);

        DaggerfallSkillTrainingOutcome outcome = f.TrainingService.Commit(quote);

        Assert.True(outcome.Accepted);
        Assert.Equal(DaggerfallSkillTrainingDenial.None, outcome.TrainingDenial);
        Assert.Equal(skill, outcome.SkillId);
        Assert.Equal(8, outcome.PermanentValue);
        Assert.Equal(8, f.SkillProgression.PermanentSkillValue(skill));
        Assert.Equal(currentSkillSum + 1, f.SkillProgression.CurrentLevelUpSkillSum);
        Assert.Equal(0, f.Progression.SkillUses[skill]);
        Assert.Equal(goldBefore - 300, f.Currency.Read().Gold);
        Assert.Equal(300UL, Assert.IsType<DaggerfallServiceOutcome>(outcome.ServiceOutcome).PaidGold);
        Assert.Equal(trainedAt, f.TrainingState.LastSkillTrainingSecond);
        Assert.Equal([DaggerfallSkillTrainingPolicy.TrainingDurationSeconds], f.Elapsed);
        Assert.Equal(new(405, 5, 1, 2, 0, 0), f.Calendar);
        Assert.Equal(trainedAt + DaggerfallSkillTrainingPolicy.TrainingDurationSeconds, outcome.CompletedAtSecond);
        Assert.Equal(fatigueBefore - Math.Min(fatigueBefore, (long)f.Locomotion.IdleFatiguePerGameMinute * 180),
            f.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);

        DaggerfallSkillTrainingQuoteResult tooSoon = f.TrainingService.Quote(f.Request("train-second"), skill);
        Assert.Equal(DaggerfallSkillTrainingDenial.TrainingTooSoon, tooSoon.TrainingDenial);
        Assert.Null(tooSoon.Quote);
    }

    [Fact]
    public void Cooldown_survives_midnight_and_saved_training_state_reopens_after_twelve_hours()
    {
        DaggerfallSkillTrainingProviderPolicy provider = PickProvider(requiresMembership: true, out string skill, out DaggerfallCareerDefinition career);
        using Fixture f = new(provider, skill, career, grantGold: true, start: new(405, 5, 0, 23, 0, 0));
        f.Social.JoinGuild(provider.MembershipFactionId, 0);
        DaggerfallSkillTrainingQuote first = Assert.IsType<DaggerfallSkillTrainingQuote>(f.TrainingService.Quote(f.Request("late-training"), skill).Quote);
        Assert.True(f.TrainingService.Commit(first).Accepted);
        Assert.Equal(new(405, 5, 1, 2, 0, 0), f.Calendar);

        DaggerfallQuestTrainingSave saved = f.TrainingState.Capture();
        DaggerfallQuestTrainingState restored = new(saved);
        Assert.Equal(f.TrainingState.LastSkillTrainingSecond, restored.LastSkillTrainingSecond);
        DaggerfallSkillTrainingService afterLoad = f.CreateTrainingService(restored);
        Assert.Equal(DaggerfallSkillTrainingDenial.TrainingTooSoon,
            afterLoad.Quote(f.Request("after-load-too-soon"), skill).TrainingDenial);

        f.Calendar = f.Calendar.Advance(9 * DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour, out _);
        DaggerfallSkillTrainingQuoteResult ready = afterLoad.Quote(f.Request("twelve-hours-later"), skill);
        Assert.True(ready.IsQuoted);
        Assert.Equal(12 * 60, checked((int)((f.Calendar.ToAbsoluteSeconds() - restored.LastSkillTrainingSecond!.Value) / 60)));
    }

    [Fact]
    public void Cap_boundary_and_changed_quote_refuse_before_payment_or_time()
    {
        DaggerfallSkillTrainingProviderPolicy provider = PickProvider(requiresMembership: true, out string skill, out DaggerfallCareerDefinition career);
        using Fixture f = new(provider, skill, career, grantGold: true);
        f.Social.JoinGuild(provider.MembershipFactionId, 0);
        f.SetSkill(skill, 49);
        DaggerfallSkillTrainingQuote atBoundary = Assert.IsType<DaggerfallSkillTrainingQuote>(f.TrainingService.Quote(f.Request("at-49"), skill).Quote);
        f.SetSkill(skill, 50);
        ulong goldBefore = f.Currency.Read().Gold;
        DaggerfallCalendar timeBefore = f.Calendar;

        DaggerfallSkillTrainingOutcome stale = f.TrainingService.Commit(atBoundary);
        Assert.False(stale.Accepted);
        Assert.Equal(DaggerfallSkillTrainingDenial.SkillAtLimit, stale.TrainingDenial);
        Assert.Equal(goldBefore, f.Currency.Read().Gold);
        Assert.Equal(timeBefore, f.Calendar);

        DaggerfallSkillTrainingQuoteResult atLimit = f.TrainingService.Quote(f.Request("at-50"), skill);
        Assert.Equal(DaggerfallSkillTrainingDenial.SkillAtLimit, atLimit.TrainingDenial);
        Assert.Null(atLimit.Quote);
    }

    [Fact]
    public void Insufficient_funds_and_missing_membership_preserve_skill_payment_time_and_training_state()
    {
        DaggerfallSkillTrainingProviderPolicy provider = PickProvider(requiresMembership: true, out string skill, out DaggerfallCareerDefinition career);
        using Fixture f = new(provider, skill, career, grantGold: false);
        f.Social.JoinGuild(provider.MembershipFactionId, 0);
        int initialSkill = f.SkillProgression.PermanentSkillValue(skill);
        DaggerfallCalendar initialTime = f.Calendar;
        DaggerfallSkillTrainingQuoteResult poor = f.TrainingService.Quote(f.Request("poor"), skill);
        Assert.Equal(DaggerfallServiceDenial.InsufficientFunds, poor.ServiceOutcome?.Denial);
        Assert.Equal(initialSkill, f.SkillProgression.PermanentSkillValue(skill));
        Assert.Equal(0UL, f.Currency.Read().Gold);
        Assert.Equal(initialTime, f.Calendar);
        Assert.Null(f.TrainingState.LastSkillTrainingSecond);

        f.GrantGold(500);
        f.Social.ExpelGuild(provider.MembershipFactionId);
        DaggerfallSkillTrainingQuoteResult noMember = f.TrainingService.Quote(f.Request("not-a-member"), skill);
        Assert.Equal(DaggerfallServiceDenial.NotMember, noMember.ServiceOutcome?.Denial);
        Assert.Equal(initialSkill, f.SkillProgression.PermanentSkillValue(skill));
        Assert.Equal(500UL, f.Currency.Read().Gold);
        Assert.Equal(initialTime, f.Calendar);
        Assert.Null(f.TrainingState.LastSkillTrainingSecond);
    }

    [Fact]
    public void Temple_training_is_available_without_membership_and_uses_the_nonmember_price()
    {
        DaggerfallSkillTrainingProviderPolicy provider = Assert.Single(DaggerfallSkillTrainingPolicy.AllProviders,
            candidate => candidate.NpcServiceFactionId == 247);
        string skill = provider.Skills[0];
        DaggerfallCareerDefinition career = CareerFor(skill);
        using Fixture f = new(provider, skill, career, grantGold: true);

        DaggerfallSkillTrainingProviderView view = Assert.IsType<DaggerfallSkillTrainingProviderView>(f.TrainingService.ReadProvider(f.Provider));
        Assert.False(view.IsMember);
        Assert.Equal(400UL, view.Price);
        DaggerfallSkillTrainingQuoteResult quote = f.TrainingService.Quote(f.Request("temple-training"), skill);
        Assert.True(quote.IsQuoted);
        Assert.Equal(400UL, quote.Quote!.Price);
        Assert.True(f.TrainingService.Commit(quote.Quote).Accepted);
        Assert.Equal(600UL, f.Currency.Read().Gold);
    }

    private static DaggerfallSkillTrainingProviderPolicy PickProvider(bool requiresMembership, out string skill, out DaggerfallCareerDefinition career)
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        foreach (DaggerfallSkillTrainingProviderPolicy provider in DaggerfallSkillTrainingPolicy.AllProviders.Where(provider => provider.RequiresMembership == requiresMembership))
        foreach (string candidate in provider.Skills)
        foreach (DaggerfallCareerDefinition candidateCareer in definitions.Catalogs.Careers)
            if (candidateCareer.PrimarySkills.Contains(candidate, StringComparer.Ordinal))
            {
                skill = candidate;
                career = candidateCareer;
                return provider;
            }

        throw new InvalidOperationException("No classic career primary skill is offered by a matching Daggerfall trainer.");
    }

    private static DaggerfallCareerDefinition CareerFor(string skill)
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        return definitions.Catalogs.Careers.First(career => career.PrimarySkills.Contains(skill, StringComparer.Ordinal));
    }

    private static DaggerfallDefinitions LoadDefinitions()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EntityDirectory _entities = new();
        private readonly DaggerfallUniqueItemAllocator _unique = new(1_000);
        private readonly DaggerfallEncumbrancePolicy _encumbrance;
        private readonly DaggerfallNpcSite _site = new(0, "Daggerfall", "guild");
        private DaggerfallSkillUseReactions? _skillProgression;

        internal DaggerfallDefinitions Definitions { get; }
        internal DaggerfallSkillTrainingProviderPolicy ProviderPolicy { get; }
        internal string Skill { get; }
        internal DaggerfallCareerDefinition Career { get; }
        internal ProgressionState Progression { get; } = new();
        internal StatsComponent Stats { get; }
        internal DaggerfallSkillUseReactions SkillProgression { get; }
        internal DaggerfallQuestTrainingState TrainingState { get; } = new();
        internal DaggerfallSocialState Social { get; }
        internal DaggerfallNpcRegistry Npcs { get; } = new();
        internal MechanicsInventoryCoordinator Inventory { get; }
        internal DaggerfallItemInstances Instances { get; } = new();
        internal DaggerfallCurrencyService Currency { get; }
        internal DaggerfallServiceTransactions Transactions { get; }
        internal DaggerfallServiceProvider Provider { get; }
        internal DaggerfallSkillTrainingService TrainingService { get; }
        internal DaggerfallCalendar Calendar { get; set; }
        internal DaggerfallLocomotionTuning Locomotion { get; } = DaggerfallLocomotionTuning.Classic;
        internal List<long> Elapsed { get; } = [];

        internal Fixture(DaggerfallSkillTrainingProviderPolicy provider, bool grantGold)
            : this(provider, provider.Skills[0], CareerFor(provider.Skills[0]), grantGold, DaggerfallCalendar.Start, 1)
        {
        }

        internal Fixture(DaggerfallSkillTrainingProviderPolicy provider, string skill, DaggerfallCareerDefinition career,
            bool grantGold, DaggerfallCalendar start = default, int level = 1)
        {
            ProviderPolicy = provider;
            Skill = skill;
            Career = career;
            Calendar = start == default ? new(405, 5, 0, 10, 0, 0) : start;
            if (level > 1) Progression.AdvanceTo(0, level);
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            Definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
            DaggerfallActorDefinition player = Definitions.RequireActor(new DaggerfallActorId("player"));
            DaggerfallCareerDefinition defaultCareer = Definitions.Catalogs.RequireCareer("class00");
            Stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, defaultCareer));
            SkillProgression = new DaggerfallSkillUseReactions(Progression, Stats, Definitions, () => Career);
            _skillProgression = SkillProgression;
            Social = new(Definitions.Factions);
            Provider = new(501, _site, DaggerfallSkillTrainingPolicy.ServiceName);
            Npcs.Restore([new DaggerfallNpc(501, DaggerfallNpcKind.Static, "trainer", _site,
                new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, provider.NpcServiceFactionId), "trainer",
                [DaggerfallSkillTrainingPolicy.ServiceName], DaggerfallNpcPresence.Active, null, null, null)]);

            Dictionary<InventoryItemId, ItemDefinition> items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            InventoryStore store = new();
            EntityId owner = _entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
            store.RegisterInventory(new InventoryState(owner, [new InventoryCapacityLimit(DaggerActorFactory.ClassicWeightMetric, ulong.MaxValue)]));
            InventoryComponent component = new(store, owner);
            _entities.Store.Add(owner, component);
            Inventory = new(component, _entities, items);
            _encumbrance = new(Inventory, Stats);
            Currency = new(Definitions, Inventory, Instances, _encumbrance, _unique);
            Transactions = new(Npcs, Social, Inventory, Instances, Currency, _unique, () => Calendar, () => _site);
            if (grantGold) GrantGold(1_000);
            TrainingService = CreateTrainingService(TrainingState);
        }

        internal DaggerfallSkillTrainingService CreateTrainingService(DaggerfallQuestTrainingState training) => new(
            Transactions, Npcs, Social, Progression, SkillProgression, training, Stats, Locomotion,
            () => Calendar, Advance);

        internal DaggerfallServiceRequest Request(string id) => new(id, Provider);

        internal void SetSkill(string skill, int value) => Stats.GetStat(StatId.Parse(skill)).BaseValue = value;

        internal void GrantGold(ulong amount)
        {
            DaggerfallItemDefinition gold = Definitions.RequireItem(new DaggerfallItemId("template-276"));
            Inventory.Grant(new InventoryGrant(new InventoryItemId(gold.Id.Value), InventoryStackId.Parse("coins"), amount));
        }

        private void Advance(long seconds)
        {
            Elapsed.Add(seconds);
            Calendar = Calendar.Advance(seconds, out _);
            _ = _skillProgression?.RaiseSkills(Calendar.ToAbsoluteSeconds());
        }

        public void Dispose() => _entities.Dispose();
    }
}
