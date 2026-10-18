using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Kit.Combat;

/// <summary>Actual participants, with live stats/effects rather than copied bonuses.</summary>
public sealed record CombatParticipants(Actor Source, Actor Target, string Cause)
{
    public StatsComponent SourceStats => Source.Get<StatsComponent>();
    public StatsComponent TargetStats => Target.Get<StatsComponent>();
    public EffectsComponent SourceEffects => Source.Get<EffectsComponent>();
    public EffectsComponent TargetEffects => Target.Get<EffectsComponent>();
}
/// <summary>
/// Delivery's pre-contact admission: one hit decision. The ruleset's base resolution decides <see cref="Hit"/>; Kit never compares
/// <see cref="Chance"/> and <see cref="Roll"/>, which are carried only for the ruleset's own policy
/// and for contributions that report or adjust them. A contribution that changes either value and
/// means it to decide the hit sets <see cref="Hit"/> itself.
/// </summary>
public sealed class TryHitEvent(CombatParticipants participants)
{
    public CombatParticipants Participants { get; } = participants;
    public int Chance { get; set; }
    public int Roll { get; set; }
    public bool Hit { get; set; }
}
public sealed class DamageEvent(CombatParticipants participants)
{
    public CombatParticipants Participants { get; } = participants;
    public int Damage { get; set; }
    /// <summary>Damage after base policy and ordered calculation contributions, before application contributions.</summary>
    public int CalculatedDamage { get => Damage; set => Damage = value; }
    public bool Allowed { get; set; } = true;
}
/// <summary>Ordinary damage or an admitted terminal health application, both using the same live track owner.</summary>
public enum HealthApplicationMode { Damage, Terminal }
public sealed class ApplyHitEvent(CombatParticipants participants, int damage, HealthApplicationMode mode = HealthApplicationMode.Damage)
{
    public HealthApplicationMode Mode { get; } = mode;
    public CombatParticipants Participants { get; } = participants;
    /// <summary>The calculated amount handed to application before application contributions may reduce it.</summary>
    public int CalculatedDamage { get; } = damage;
    public int Damage { get; set; } = damage;
    /// <summary>Health actually removed from the canonical live track; this is never inferred from calculated damage.</summary>
    public double ActualHealthLost { get; set; }
    /// <summary>True only when this application crossed a living target to its health minimum.</summary>
    public bool Defeated { get; set; }
    public DamageResult Result => new(Participants.Source, Participants.Target, Participants.Cause,
        CalculatedDamage, ActualHealthLost, Defeated);
}

/// <summary>Completed synchronous health application. The source, target, cause, calculation, live loss, and transition stay together.</summary>
public readonly record struct DamageResult(Actor Source, Actor Target, string Cause, int CalculatedDamage,
    double ActualHealthLost, bool Defeated);

/// <summary>An otherwise lethal application, after damage contributions and before canonical health changes.</summary>
public sealed class DefeatEvent(ApplyHitEvent application, double currentHealth, double minimumHealth)
{
    public ApplyHitEvent Application { get; } = application;
    public CombatParticipants Participants => Application.Participants;
    public double CurrentHealth { get; } = currentHealth;
    public double MinimumHealth { get; } = minimumHealth;
    /// <summary>A participant may retain living health; application bounds it to the health already held.</summary>
    public double RetainedHealth { get; set; } = minimumHealth;
}

/// <summary>Explicit participant contributions, in registration order after each base calculation and before application.</summary>
public interface ICombatContribution
{
    /// <summary>Delivery's pre-contact admission, such as a to-hit roll.</summary>
    void Hit(TryHitEvent interaction) { }
    /// <summary>Delivery's contact with this target.</summary>
    void Contact(ContactEvent interaction) { }
    /// <summary>Mitigation's immunity, decided before the harm is calculated.</summary>
    void Immunity(ImmunityEvent interaction) { }
    /// <summary>The source's harm calculation.</summary>
    void Damage(DamageEvent interaction) { }
    /// <summary>Mitigation's deflection and absorption of the calculated harm.</summary>
    void Mitigate(MitigationEvent interaction) { }
    void Applying(ApplyHitEvent interaction) { }
    void Defeating(DefeatEvent interaction) { }
}

/// <summary>Attached contributions belong to their actor/item. Effect owners add/remove their own active contributions here.</summary>
public sealed class CombatContributions
{
    public List<ICombatContribution> Rules { get; } = [];
}

/// <summary>
/// Typed synchronous resolution of the one harm pipeline: delivery (admission, then contact), mitigation
/// (immunity, the source's calculation, then deflection and absorption) and application. Each stage is
/// its own event, so a delivery owner whose timing splits the stages may raise them separately; the
/// order it must keep is admission before contact, immunity before calculation, mitigation after both
/// contact and calculation, and application last. Notifications are separate; application is never an
/// eventual subscriber.
/// </summary>
public sealed class CombatResolution
{
    private readonly Dictionary<string, IReadOnlyList<ICombatContribution>> _actions = new(StringComparer.Ordinal);
    public void RegisterAction(string action, params ICombatContribution[] contributions) => _actions.Add(action, contributions);
    /// <summary>
    /// Delivery and mitigation for one target in pipeline order, stopping at no contact or immunity.
    /// Without <paramref name="admit"/> there is no admission step; <paramref name="contact"/> is the
    /// delivery owner's own contact answer, which an admission refusal overrides.
    /// </summary>
    public HarmResolution Resolve(CombatParticipants participants, HarmChannelId channel, Action<TryHitEvent>? admit,
        Action<DamageEvent> calculate, bool contact = true, Action<ImmunityEvent>? immunity = null, Action<MitigationEvent>? mitigate = null)
    {
        ArgumentNullException.ThrowIfNull(calculate);
        TryHitEvent? admission = admit is null ? null : TryHit(participants, admit);
        ContactEvent touched = Contact(participants, contact && admission?.Hit != false);
        if (!touched.Contact) return new(participants, channel, admission, touched, null, null, null);
        ImmunityEvent immune = Immunity(participants, channel, immunity);
        if (immune.Immune) return new(participants, channel, admission, touched, immune, null, null);
        DamageEvent calculated = Damage(participants, calculate);
        MitigationEvent mitigated = Mitigate(participants, channel, calculated.Damage, mitigate);
        return new(participants, channel, admission, touched, immune, calculated, mitigated);
    }
    public TryHitEvent TryHit(CombatParticipants participants, Action<TryHitEvent> resolve)
    {
        TryHitEvent interaction = new(participants);
        resolve(interaction);
        foreach (ICombatContribution rule in Gather(participants)) rule.Hit(interaction);
        return interaction;
    }
    public ContactEvent Contact(CombatParticipants participants, bool contact)
    {
        ContactEvent interaction = new(participants, contact);
        foreach (ICombatContribution rule in Gather(participants)) rule.Contact(interaction);
        return interaction;
    }
    public ImmunityEvent Immunity(CombatParticipants participants, HarmChannelId channel, Action<ImmunityEvent>? resolve = null)
    {
        ImmunityEvent interaction = new(participants, channel);
        resolve?.Invoke(interaction);
        foreach (ICombatContribution rule in Gather(participants)) rule.Immunity(interaction);
        return interaction;
    }
    public DamageEvent Damage(CombatParticipants participants, Action<DamageEvent> resolve)
    {
        DamageEvent interaction = new(participants);
        resolve(interaction);
        foreach (ICombatContribution rule in Gather(participants)) rule.Damage(interaction);
        return interaction;
    }
    public MitigationEvent Mitigate(CombatParticipants participants, HarmChannelId channel, int incoming,
        Action<MitigationEvent>? resolve = null)
    {
        MitigationEvent interaction = new(participants, channel, incoming);
        resolve?.Invoke(interaction);
        foreach (ICombatContribution rule in Gather(participants)) rule.Mitigate(interaction);
        return interaction;
    }
    public ApplyHitEvent Apply(CombatParticipants participants, int damage, Action<ApplyHitEvent> apply,
        HealthApplicationMode mode = HealthApplicationMode.Damage)
    {
        ApplyHitEvent interaction = new(participants, damage, mode);
        // A contribution may expire its own source while applying (for example, a depleted pool).
        foreach (ICombatContribution rule in Gather(participants).ToArray()) rule.Applying(interaction);
        apply(interaction);
        return interaction;
    }
    /// <summary>
    /// Runs application contributions then mutates the supplied canonical health track once. A
    /// target already at its minimum has no second death transition and loses no additional health.
    /// </summary>
    public ApplyHitEvent ApplyToHealth(CombatParticipants participants, int damage, Track health,
        HealthApplicationMode mode = HealthApplicationMode.Damage)
    {
        ArgumentNullException.ThrowIfNull(health);
        return Apply(participants, damage, interaction =>
        {
            double before = health.Current;
            if (before <= health.Minimum) return;
            double after = interaction.Mode == HealthApplicationMode.Terminal ? health.Minimum
                : Math.Max(health.Minimum, before - Math.Max(0d, interaction.Damage));
            if (after <= health.Minimum)
            {
                DefeatEvent defeat = new(interaction, before, health.Minimum);
                foreach (ICombatContribution rule in Gather(participants).ToArray()) rule.Defeating(defeat);
                after = Math.Min(before, defeat.RetainedHealth);
            }
            health.SetCurrent(after, clamp: true);
            interaction.ActualHealthLost = before - health.Current;
            interaction.Defeated = before > health.Minimum && health.Current <= health.Minimum;
        }, mode);
    }
    private IEnumerable<ICombatContribution> Gather(CombatParticipants participants)
    {
        foreach (ICombatContribution rule in FromActor(participants.Source)) yield return rule;
        if (participants.Target.Entity != participants.Source.Entity)
            foreach (ICombatContribution rule in FromActor(participants.Target)) yield return rule;
        if (_actions.TryGetValue(participants.Cause, out IReadOnlyList<ICombatContribution>? action))
            foreach (ICombatContribution rule in action) yield return rule;
    }
    private static IEnumerable<ICombatContribution> FromActor(Actor actor)
    {
        if (actor.TryGet<CombatContributions>(out var contributions))
            foreach (ICombatContribution rule in contributions.Rules) yield return rule;
        if (!actor.TryGet<EquipmentComponent>(out var equipment)) yield break;
        // A multislot item contributes once.
        foreach (EntityId item in equipment.Assignments.Select(a => a.Item).Distinct())
            if (actor.Store.TryGet<CombatContributions>(item, out var itemRules))
                foreach (ICombatContribution rule in itemRules.Rules) yield return rule;
    }
}
