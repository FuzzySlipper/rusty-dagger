using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTemplePolicyTests
{
    [Fact]
    public void Blessing_table_covers_the_eight_source_deities_and_keeps_Arkay_cure_only()
    {
        Assert.Equal(DaggerfallTempleBlessingTarget.Speed, Target(DaggerfallConcreteGuildCatalog.AkatoshFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Luck, Target(DaggerfallConcreteGuildCatalog.DibellaFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Intelligence, Target(DaggerfallConcreteGuildCatalog.JulianosFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Endurance, Target(DaggerfallConcreteGuildCatalog.KynarethFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Personality, Target(DaggerfallConcreteGuildCatalog.MaraFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.LegalReputation, Target(DaggerfallConcreteGuildCatalog.StendarrFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Mercantile, Target(DaggerfallConcreteGuildCatalog.ZenitharFactionId));
        Assert.True(DaggerfallTemplePolicy.TryResolveTarget(DaggerfallConcreteGuildCatalog.ArkayFactionId, out DaggerfallTempleBlessingTarget arkay));
        Assert.Equal(DaggerfallTempleBlessingTarget.None, arkay);
        Assert.False(DaggerfallTemplePolicy.TryResolveTarget(999_999, out _));

        static DaggerfallTempleBlessingTarget Target(int deity)
        {
            Assert.True(DaggerfallTemplePolicy.TryResolveTarget(deity, out DaggerfallTempleBlessingTarget target));
            return target;
        }
    }

    [Fact]
    public void Donation_formula_uses_rank_for_magnitude_and_caps_only_duration()
    {
        Assert.Equal(2, DaggerfallTemplePolicy.CalculateTempleBlessing(1, rank: 0));
        Assert.Equal(11, DaggerfallTemplePolicy.CalculateTempleBlessing(1, rank: 9));
        Assert.Equal(1, DaggerfallTemplePolicy.BlessingDurationMinutes(1));
        Assert.Equal(1440, DaggerfallTemplePolicy.BlessingDurationMinutes(1440));
        Assert.Equal(1440, DaggerfallTemplePolicy.BlessingDurationMinutes(ulong.MaxValue));
        Assert.Equal(250, DaggerfallTemplePolicy.ReducedCureCost(250, DaggerfallConcreteGuildCatalog.ZenitharFactionId, 9, member: true));
        Assert.Equal(24, DaggerfallTemplePolicy.ReducedCureCost(250, DaggerfallConcreteGuildCatalog.ArkayFactionId, 9, member: true));
    }

    [Fact]
    public void Blessing_state_rejects_missing_or_nonfinite_current_expiry()
    {
        using JsonDocument missingExpiry = JsonDocument.Parse(
            "{\"DeityFactionId\":1,\"Target\":1,\"Region\":-1,\"Magnitude\":2,\"DurationMinutes\":1}");
        Assert.Throws<JsonException>(() => DaggerfallTempleBlessingEffects.Read(missingExpiry.RootElement));
        Assert.Throws<ArgumentException>(() => new DaggerfallTempleBlessingState(
            DaggerfallConcreteGuildCatalog.AkatoshFactionId,
            DaggerfallTempleBlessingTarget.Speed, -1, 2, 1, double.NaN).Validate());
    }

    [Fact]
    public void Held_donation_quote_starts_a_full_duration_when_payment_is_accepted()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);

        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        session.AdvanceElapsedTime(30);

        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
        Assert.Equal(90d, DaggerfallTempleBlessingEffects.Read(Assert.Single(session.State.Effects.Active).State).ExpiresAtGameSecond);
        session.AdvanceElapsedTime(59);
        Assert.Single(session.State.Effects.Active);
        session.AdvanceElapsedTime(1);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Restore_rejects_a_future_blessing_beyond_its_paid_duration()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);
        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);

        DaggerfallSavePayload payload = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallActiveEffectSave saved = Assert.Single(payload.ActiveEffects);
        DaggerfallTempleBlessingState malformed = DaggerfallTempleBlessingEffects.Read(saved.State) with
        {
            ExpiresAtGameSecond = DaggerfallWorldTimeAbsolute(session) + (2 * DaggerfallCalendar.SecondsPerMinute),
        };
        RulesetSavePayload malformedSave = DaggerfallSavePayload.Encode(payload with
        {
            ActiveEffects = [saved with { State = DaggerfallTempleBlessingEffects.Encode(malformed) }],
        });

        Assert.Throws<ArgumentException>(() => fixture.Restore(malformedSave));
    }

    [Fact]
    public void Source_temple_building_faction_retains_its_deity_parent()
    {
        DaggerfallConcreteGuildDefinition arkay = DaggerfallConcreteGuildCatalog.ForFaction(
            DaggerfallConcreteGuildCatalog.ArkayTempleFactionId);

        Assert.Equal(DaggerfallGuildMembershipKind.TempleDeity, arkay.MembershipKind);
        Assert.Equal(DaggerfallConcreteGuildCatalog.ArkayFactionId, arkay.ParentFactionId);
        Assert.Equal(arkay.FactionId,
            DaggerfallConcreteGuildCatalog.ForDeity(DaggerfallConcreteGuildCatalog.ArkayFactionId).FactionId);
    }

    [Fact]
    public void Regional_blessing_sources_are_live_and_do_not_change_saved_base_reputation()
    {
        DaggerfallSocialState social = new(TestPayload.Definitions.Factions);
        EffectSourceIdentity source = new(new EntityId(1), EffectInstanceId.Parse("temple-test"), 1,
            SourceDefinitionId.Parse("daggerfall.temple-blessing"));
        int before = social.RegionalReputation(17);
        social.SetRegionalReputationSource(17, source, 7);
        Assert.Equal(before + 7, social.RegionalReputation(17));
        _ = social.ChangeRegionalReputation(17, 3);
        Assert.Equal(before + 3 + 7, social.RegionalReputation(17));
        social.RemoveRegionalReputationSource(17, source);
        Assert.Equal(before + 3, social.RegionalReputation(17));
    }

    [Fact]
    public void One_gold_blessing_survives_seconds_59_and_expires_at_seconds_60()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(59);
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);
        int before = Stat(session, DaggerfallMechanicsIds.Speed.Value);

        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);

        DaggerfallActiveEffect active = Assert.Single(session.State.Effects.Active);
        DaggerfallTempleBlessingState state = DaggerfallTempleBlessingEffects.Read(active.State);
        Assert.Equal(119d, state.ExpiresAtGameSecond);
        Assert.Null(active.Lifecycle.RemainingRounds);
        Assert.Equal(before + 2, Stat(session, DaggerfallMechanicsIds.Speed.Value));

        session.AdvanceElapsedTime(59);
        Assert.Single(session.State.Effects.Active);
        Assert.Equal(before + 2, Stat(session, DaggerfallMechanicsIds.Speed.Value));

        session.AdvanceElapsedTime(1);
        Assert.Empty(session.State.Effects.Active);
        Assert.Equal(before, Stat(session, DaggerfallMechanicsIds.Speed.Value));
    }

    [Fact]
    public void Two_gold_blessing_expires_at_the_second_minute_boundary()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 2);
        int before = Stat(session, DaggerfallMechanicsIds.Speed.Value);

        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 2, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
        DaggerfallTempleBlessingState state = DaggerfallTempleBlessingEffects.Read(Assert.Single(session.State.Effects.Active).State);
        Assert.Equal(120d, state.ExpiresAtGameSecond);
        Assert.Null(Assert.Single(session.State.Effects.Active).Lifecycle.RemainingRounds);

        session.AdvanceElapsedTime(119);
        Assert.Single(session.State.Effects.Active);
        Assert.Equal(before + 2, Stat(session, DaggerfallMechanicsIds.Speed.Value));

        session.AdvanceElapsedTime(1);
        Assert.Empty(session.State.Effects.Active);
        Assert.Equal(before, Stat(session, DaggerfallMechanicsIds.Speed.Value));
    }

    [Fact]
    public void One_gold_blessing_round_trips_mid_minute_and_expires_at_the_next_boundary()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);
        int before = Stat(session, DaggerfallMechanicsIds.Speed.Value);

        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
        session.AdvanceElapsedTime(30);
        RulesetSavePayload save = session.CaptureSave();
        DaggerfallActiveEffectSave savedEffect = Assert.Single(DaggerfallSavePayload.Read(save).ActiveEffects);
        Assert.Null(savedEffect.RemainingRounds);
        Assert.Equal(60d, DaggerfallTempleBlessingEffects.Read(savedEffect.State).ExpiresAtGameSecond);

        using DaggerfallSession restored = fixture.Restore(save);
        Assert.Single(restored.State.Effects.Active);
        Assert.Equal(before + 2, Stat(restored, DaggerfallMechanicsIds.Speed.Value));
        restored.AdvanceElapsedTime(29);
        Assert.Single(restored.State.Effects.Active);
        restored.AdvanceElapsedTime(1);
        Assert.Empty(restored.State.Effects.Active);
        Assert.Equal(before, Stat(restored, DaggerfallMechanicsIds.Speed.Value));
    }

    [Fact]
    public void Blessing_duration_includes_the_world_time_remainder_at_admission()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        for (ulong step = 1; step <= 3; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));

        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);
        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
        Assert.Equal(60.6d, DaggerfallTempleBlessingEffects.Read(Assert.Single(session.State.Effects.Active).State).ExpiresAtGameSecond, 6);

        session.AdvanceElapsedTime(59);
        Assert.Single(session.State.Effects.Active);
        session.AdvanceElapsedTime(1);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Fractional_save_preserves_a_full_blessing_duration_after_restore()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        for (ulong step = 1; step <= 3; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));

        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);
        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
        session.AdvanceElapsedTime(30);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        restored.AdvanceElapsedTime(29);
        Assert.Single(restored.State.Effects.Active);
        restored.AdvanceElapsedTime(1);
        Assert.Empty(restored.State.Effects.Active);
    }

    [Fact]
    public void Replacement_restarts_absolute_expiry_and_cleans_the_previous_contribution()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);
        int before = Stat(session, DaggerfallMechanicsIds.Speed.Value);

        DaggerfallTempleDonationQuote first = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult firstRefusal));
        Assert.True(firstRefusal.Accepted, firstRefusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(first).Accepted);
        session.AdvanceElapsedTime(30);
        Assert.Equal(before + 2, Stat(session, DaggerfallMechanicsIds.Speed.Value));

        Assert.True(session.State.Currency.ReceiveGold(1));
        DaggerfallTempleDonationQuote replacement = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult replacementRefusal));
        Assert.True(replacementRefusal.Accepted, replacementRefusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(replacement).Accepted);
        DaggerfallActiveEffect active = Assert.Single(session.State.Effects.Active);
        Assert.Equal(before + 2, Stat(session, DaggerfallMechanicsIds.Speed.Value));
        Assert.Equal(90d, DaggerfallTempleBlessingEffects.Read(active.State).ExpiresAtGameSecond);

        session.AdvanceElapsedTime(59);
        Assert.Single(session.State.Effects.Active);
        session.AdvanceElapsedTime(1);
        Assert.Empty(session.State.Effects.Active);
        Assert.Equal(before, Stat(session, DaggerfallMechanicsIds.Speed.Value));
    }

    [Fact]
    public void Full_day_rest_expires_maximum_blessing_without_capping_unrelated_effect_rounds()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1440);
        int before = Stat(session, DaggerfallMechanicsIds.Speed.Value);

        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1440, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
        StartUnrelatedLongEffect(session);

        session.AdvanceElapsedTime(DaggerfallCalendar.SecondsPerDay, resting: true);

        Assert.Equal(before, Stat(session, DaggerfallMechanicsIds.Speed.Value));
        Assert.DoesNotContain(session.State.Effects.Active, effect => effect.Definition.Key == DaggerfallTempleBlessingEffects.Key);
        DaggerfallActiveEffect unrelated = Assert.Single(session.State.Effects.Active, effect => effect.Definition.Key == "resist-fire");
        Assert.Equal((uint)DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds - DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour,
            unrelated.Lifecycle.RemainingRounds);
    }

    [Fact]
    public void Paused_ordinary_update_does_not_advance_blessing_expiry()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DonationContext donation = PrepareDonation(session, DaggerfallConcreteGuildCatalog.AkatoshFactionId, 1);
        DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
            session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
        Assert.True(refusal.Accepted, refusal.Message);
        Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
        DaggerfallCalendar before = Calendar(session);

        session.ApplyProductMode(ProductMode.Paused);
        session.Update(new ProductUpdate(OuterUpdate(1) with { FixedDeltaSeconds = 60d }, []));

        Assert.Equal(before, Calendar(session));
        Assert.Single(session.State.Effects.Active);
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(60);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void All_seven_paid_blessings_apply_and_Stendarr_reputation_cleans_up_on_replacement()
    {
        (int Deity, DaggerfallTempleBlessingTarget Target, string? Stat)[] targets =
        [
            (DaggerfallConcreteGuildCatalog.AkatoshFactionId, DaggerfallTempleBlessingTarget.Speed, DaggerfallMechanicsIds.Speed.Value),
            (DaggerfallConcreteGuildCatalog.DibellaFactionId, DaggerfallTempleBlessingTarget.Luck, DaggerfallMechanicsIds.Luck.Value),
            (DaggerfallConcreteGuildCatalog.JulianosFactionId, DaggerfallTempleBlessingTarget.Intelligence, DaggerfallMechanicsIds.Intelligence.Value),
            (DaggerfallConcreteGuildCatalog.KynarethFactionId, DaggerfallTempleBlessingTarget.Endurance, DaggerfallMechanicsIds.Endurance.Value),
            (DaggerfallConcreteGuildCatalog.MaraFactionId, DaggerfallTempleBlessingTarget.Personality, DaggerfallMechanicsIds.Personality.Value),
            (DaggerfallConcreteGuildCatalog.StendarrFactionId, DaggerfallTempleBlessingTarget.LegalReputation, null),
            (DaggerfallConcreteGuildCatalog.ZenitharFactionId, DaggerfallTempleBlessingTarget.Mercantile, "mercantile"),
        ];

        foreach ((int deity, DaggerfallTempleBlessingTarget target, string? stat) in targets)
        {
            // Group 17 permits only one temple membership at a time. Each source target therefore
            // gets its own real session; the Stendarr iteration additionally proves replacement
            // removes its regional source before the incoming one is applied.
            using ConditionSessionFixture fixture = new();
            DaggerfallSession session = fixture.Session;
            int region = session.Site.Region ?? throw new InvalidOperationException("The blessing test requires an active source site.");
            Assert.True(session.State.Currency.ReceiveGold(stat is null ? 2UL : 1UL));
            DonationContext donation = PrepareDonation(session, deity, 1);
            int baseRegional = session.State.Social.RegionalReputation(region);
            int baseStat = stat is null ? 0 : Stat(session, stat);
            DaggerfallTempleDonationQuote quote = Assert.IsType<DaggerfallTempleDonationQuote>(
                session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult refusal));
            Assert.True(refusal.Accepted, refusal.Message);
            Assert.True(session.State.TempleServices.CommitDonation(quote).Accepted);
            DaggerfallTempleBlessingState state = DaggerfallTempleBlessingEffects.Read(Assert.Single(session.State.Effects.Active).State);
            Assert.Equal(target, state.Target);
            Assert.Equal(2, state.Magnitude);
            if (stat is not null)
            {
                Assert.Equal(baseStat + 2, Stat(session, stat));
                Assert.Equal(baseRegional, session.State.Social.RegionalReputation(region));
            }
            else
            {
                Assert.Equal(baseRegional + 2, session.State.Social.RegionalReputation(region));
                DaggerfallTempleDonationQuote replacement = Assert.IsType<DaggerfallTempleDonationQuote>(
                    session.State.TempleServices.QuoteDonation(donation.Provider, donation.Building, 1, out DaggerfallTempleServiceResult replacementRefusal));
                Assert.True(replacementRefusal.Accepted, replacementRefusal.Message);
                Assert.True(session.State.TempleServices.CommitDonation(replacement).Accepted);
                Assert.Equal(baseRegional + 2, session.State.Social.RegionalReputation(region));
                Assert.True(session.State.Effects.Cancel(Assert.Single(session.State.Effects.Active).Lifecycle.Context.Instance));
                Assert.Equal(baseRegional, session.State.Social.RegionalReputation(region));
            }
        }

        // All seven target paths were admitted and the Stendarr source was retired through the
        // effect lifecycle's normal replacement/cleanup owner above.
    }

    private static DonationContext PrepareDonation(DaggerfallSession session, int deity, ulong gold)
    {
        DaggerfallSiteRecord site = session.Site.ActiveSite
            ?? throw new InvalidOperationException("The blessing test requires an active source site.");
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        long providerId = session.State.Npcs.RegisterStable(
            DaggerfallNpcKind.Static,
            "temple-lifetime-test-provider",
            npcSite,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 1, 810),
            "temple priest",
            ["donate"]);
        _ = session.State.Social.JoinGuild(DaggerfallTemplePolicy.MembershipFaction(deity), currentDay: 0);
        if (session.State.Currency.Read().Gold < gold) Assert.True(session.State.Currency.ReceiveGold(gold));
        DaggerfallServiceProvider provider = new(providerId, npcSite, "donate");
        DaggerfallInteriorBuilding building = new(0, 0, new("temple-lifetime-test", 0), 14,
            DaggerfallConcreteGuildCatalog.ForDeity(deity).FactionId);
        return new(provider, building);
    }

    private static int Stat(DaggerfallSession session, string id) =>
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(id)).ValueInt;

    private static DaggerfallCalendar Calendar(DaggerfallSession session)
    {
        DaggerfallCalendarSave saved = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        return new(saved.Year, saved.Month, saved.Day, saved.Hour, saved.Minute, saved.Second);
    }

    private static double DaggerfallWorldTimeAbsolute(DaggerfallSession session)
    {
        DaggerfallCalendarSave saved = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        return new DaggerfallCalendar(saved.Year, saved.Month, saved.Day, saved.Hour, saved.Minute, saved.Second).ToAbsoluteSeconds()
            + saved.RemainderSeconds;
    }

    private static void StartUnrelatedLongEffect(DaggerfallSession session)
    {
        DaggerfallSpellEffectDefinition settings = new("temple-unrelated", 8, 0, 10, 0, 1, 100, 0, 1, 0, 0, 0, 0, 1);
        JsonElement state = JsonSerializer.SerializeToElement(
            new DaggerfallCastEffectState(settings, 1, 0, 100),
            DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        _ = session.State.Effects.Start(new(
            "unrelated-duration", "resist-fire", "spell.unrelated", null,
            DaggerfallActorIdentity.PlayerEntityId, settings.Key, "Magic", null, 1,
            DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds + 1U, state));
    }

    private sealed record DonationContext(DaggerfallServiceProvider Provider, DaggerfallInteriorBuilding Building);
}
