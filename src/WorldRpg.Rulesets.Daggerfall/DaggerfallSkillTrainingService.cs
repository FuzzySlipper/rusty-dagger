using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallSkillTrainingDenial
{
    None,
    UnsupportedProvider,
    SkillNotOffered,
    SkillAtLimit,
    TrainingTooSoon,
    QuoteChanged,
}

internal sealed record DaggerfallSkillTrainingSkillView(string Id, int PermanentValue, int MaximumValue);

internal sealed record DaggerfallSkillTrainingProviderView(
    DaggerfallServiceProvider Provider,
    int ProviderFactionId,
    int MembershipFactionId,
    bool IsMember,
    int Rank,
    ulong Price,
    IReadOnlyList<DaggerfallSkillTrainingSkillView> Skills,
    long DurationSeconds,
    long CooldownReadySecond);

internal sealed record DaggerfallSkillTrainingQuote(
    DaggerfallServiceQuote ServiceQuote,
    int ProviderFactionId,
    int MembershipFactionId,
    string SkillId,
    int PermanentValue,
    int MaximumValue,
    int PlayerLevel,
    bool WasMember,
    ulong Price,
    long DurationSeconds);

internal sealed record DaggerfallSkillTrainingQuoteResult(
    DaggerfallSkillTrainingQuote? Quote,
    DaggerfallSkillTrainingDenial TrainingDenial,
    DaggerfallServiceOutcome? ServiceOutcome)
{
    internal bool IsQuoted => Quote is not null;
}

internal sealed record DaggerfallSkillTrainingOutcome(
    bool Accepted,
    DaggerfallSkillTrainingDenial TrainingDenial,
    DaggerfallServiceOutcome? ServiceOutcome,
    string? SkillId = null,
    int? PermanentValue = null,
    long? CompletedAtSecond = null);

/// <summary>Quotes and applies paid Daggerfall skill training over the current service and progression owners.</summary>
internal sealed class DaggerfallSkillTrainingService
{
    private readonly DaggerfallServiceTransactions _transactions;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallSocialState _social;
    private readonly ProgressionState _progression;
    private readonly DaggerfallSkillUseReactions _skillProgression;
    private readonly DaggerfallQuestTrainingState _training;
    private readonly StatsComponent _stats;
    private readonly DaggerfallLocomotionTuning _locomotion;
    private readonly Func<DaggerfallCalendar> _calendar;
    private readonly Action<long> _advanceElapsed;

    internal DaggerfallSkillTrainingService(
        DaggerfallServiceTransactions transactions,
        DaggerfallNpcRegistry npcs,
        DaggerfallSocialState social,
        ProgressionState progression,
        DaggerfallSkillUseReactions skillProgression,
        DaggerfallQuestTrainingState training,
        StatsComponent stats,
        DaggerfallLocomotionTuning locomotion,
        Func<DaggerfallCalendar> calendar,
        Action<long> advanceElapsed)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _skillProgression = skillProgression ?? throw new ArgumentNullException(nameof(skillProgression));
        _training = training ?? throw new ArgumentNullException(nameof(training));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _locomotion = (locomotion ?? throw new ArgumentNullException(nameof(locomotion))).Validate();
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _advanceElapsed = advanceElapsed ?? throw new ArgumentNullException(nameof(advanceElapsed));
    }

    /// <summary>Lists exactly the skills available from this current Daggerfall trainer.</summary>
    internal DaggerfallSkillTrainingProviderView? ReadProvider(DaggerfallServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        DaggerfallSkillTrainingProviderPolicy? policy = ResolveProvider(provider, out DaggerfallNpc? npc);
        if (policy is null || npc is null) return null;

        DaggerfallGuildEligibility membership = _social.GuildEligibility(policy.MembershipFactionId);
        DaggerfallCalendar now = _calendar();
        long nowSecond = now.ToAbsoluteSeconds();
        long readyAt = _training.LastSkillTrainingSecond is long previous
            ? checked(previous + DaggerfallSkillTrainingPolicy.TrainingCooldownSeconds)
            : nowSecond;
        DaggerfallSkillTrainingSkillView[] skills = policy.Skills
            .Select(skill => new DaggerfallSkillTrainingSkillView(skill, _skillProgression.PermanentSkillValue(skill), policy.MaximumPermanentSkill))
            .ToArray();

        return new(provider, policy.NpcServiceFactionId, policy.MembershipFactionId, membership.IsMember, membership.Rank,
            DaggerfallSkillTrainingPolicy.Price(_progression.Level, membership.IsMember), Array.AsReadOnly(skills),
            DaggerfallSkillTrainingPolicy.TrainingDurationSeconds, readyAt);
    }

    /// <summary>Quotes one offered skill without changing gold, progression, time, fatigue, or cooldown.</summary>
    internal DaggerfallSkillTrainingQuoteResult Quote(DaggerfallServiceRequest request, string skillId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        DaggerfallSkillTrainingProviderPolicy? policy = ResolveProvider(request.Provider, out DaggerfallNpc? npc);
        if (policy is null || npc is null)
            return LocalDenial(DaggerfallSkillTrainingDenial.UnsupportedProvider);
        if (!policy.Skills.Contains(skillId, StringComparer.Ordinal))
            return LocalDenial(DaggerfallSkillTrainingDenial.SkillNotOffered);

        int skillValue = _skillProgression.PermanentSkillValue(skillId);
        if (skillValue >= policy.MaximumPermanentSkill)
            return LocalDenial(DaggerfallSkillTrainingDenial.SkillAtLimit);
        long nowSecond = _calendar().ToAbsoluteSeconds();
        if (IsCoolingDown(nowSecond)) return LocalDenial(DaggerfallSkillTrainingDenial.TrainingTooSoon);

        DaggerfallGuildEligibility membership = _social.GuildEligibility(policy.MembershipFactionId);
        ulong price = DaggerfallSkillTrainingPolicy.Price(_progression.Level, membership.IsMember);
        DaggerfallServiceEligibility eligibility = new(policy.RequiresMembership, MinimumRank: 0, RequiredFaction: policy.MembershipFactionId);
        DaggerfallServiceQuoteResult transaction = _transactions.Quote(request, eligibility, new DaggerfallServicePrice(price));
        if (transaction.Quote is not { } serviceQuote) return new(null, DaggerfallSkillTrainingDenial.None, transaction.Outcome);

        DaggerfallSkillTrainingQuote quote = new(serviceQuote, policy.NpcServiceFactionId, policy.MembershipFactionId,
            skillId, skillValue, policy.MaximumPermanentSkill, _progression.Level, membership.IsMember, price,
            DaggerfallSkillTrainingPolicy.TrainingDurationSeconds);
        return new(quote, DaggerfallSkillTrainingDenial.None, transaction.Outcome);
    }

    /// <summary>Revalidates the quoted skill and shared training cooldown before one atomic service payment.</summary>
    internal DaggerfallSkillTrainingOutcome Commit(DaggerfallSkillTrainingQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        DaggerfallServiceProvider provider = quote.ServiceQuote.Request.Provider;
        DaggerfallSkillTrainingProviderPolicy? policy = ResolveProvider(provider, out DaggerfallNpc? npc);
        if (policy is null || npc is null || policy.NpcServiceFactionId != quote.ProviderFactionId
            || policy.MembershipFactionId != quote.MembershipFactionId
            || !policy.Skills.Contains(quote.SkillId, StringComparer.Ordinal)
            || policy.MaximumPermanentSkill != quote.MaximumValue)
            return LocalOutcome(DaggerfallSkillTrainingDenial.QuoteChanged, quote.SkillId);

        int currentValue = _skillProgression.PermanentSkillValue(quote.SkillId);
        if (currentValue >= policy.MaximumPermanentSkill)
            return LocalOutcome(DaggerfallSkillTrainingDenial.SkillAtLimit, quote.SkillId, currentValue);
        if (currentValue != quote.PermanentValue)
            return LocalOutcome(DaggerfallSkillTrainingDenial.QuoteChanged, quote.SkillId, currentValue);

        long nowSecond = _calendar().ToAbsoluteSeconds();
        if (IsCoolingDown(nowSecond)) return LocalOutcome(DaggerfallSkillTrainingDenial.TrainingTooSoon, quote.SkillId, currentValue);
        DaggerfallGuildEligibility membership = _social.GuildEligibility(policy.MembershipFactionId);
        ulong currentPrice = DaggerfallSkillTrainingPolicy.Price(_progression.Level, membership.IsMember);
        if (_progression.Level != quote.PlayerLevel || membership.IsMember != quote.WasMember || currentPrice != quote.Price)
            return LocalOutcome(DaggerfallSkillTrainingDenial.QuoteChanged, quote.SkillId, currentValue);

        DaggerfallServiceOutcome payment = _transactions.Commit(quote.ServiceQuote);
        if (!payment.Accepted) return new(false, DaggerfallSkillTrainingDenial.None, payment, quote.SkillId, currentValue);

        // The accepted transaction has already revalidated its provider, membership/rank, and funds.
        // This single admitted update cannot interleave another skill mutation between this cap check
        // and the progression owner's permanent-base write.
        if (!_skillProgression.TryTrainPermanentSkill(quote.SkillId, policy.MaximumPermanentSkill, out int trainedValue))
            throw new InvalidOperationException($"Accepted training for '{quote.SkillId}' could not apply its prevalidated permanent skill increase.");

        _training.Record(nowSecond);
        _advanceElapsed(DaggerfallSkillTrainingPolicy.TrainingDurationSeconds);
        Track fatigue = _stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        long fatigueCost = checked((long)_locomotion.IdleFatiguePerGameMinute * 180);
        _ = fatigue.Spend(Math.Min(fatigue.Current, fatigueCost));
        return new(true, DaggerfallSkillTrainingDenial.None, payment, quote.SkillId, trainedValue, _calendar().ToAbsoluteSeconds());
    }

    private DaggerfallSkillTrainingProviderPolicy? ResolveProvider(DaggerfallServiceProvider provider, out DaggerfallNpc? npc)
    {
        npc = null;
        if (!string.Equals(provider.Service, DaggerfallSkillTrainingPolicy.ServiceName, StringComparison.Ordinal)) return null;
        try { npc = _npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return null; }
        return DaggerfallSkillTrainingPolicy.TryGetProvider(npc.Appearance.FactionId, out DaggerfallSkillTrainingProviderPolicy policy)
            ? policy
            : null;
    }

    private bool IsCoolingDown(long nowSecond) => _training.LastSkillTrainingSecond is long previous
        && nowSecond - previous < DaggerfallSkillTrainingPolicy.TrainingCooldownSeconds;

    private static DaggerfallSkillTrainingQuoteResult LocalDenial(DaggerfallSkillTrainingDenial denial) => new(null, denial, null);

    private static DaggerfallSkillTrainingOutcome LocalOutcome(DaggerfallSkillTrainingDenial denial, string skillId, int? value = null) =>
        new(false, denial, null, skillId, value);
}
