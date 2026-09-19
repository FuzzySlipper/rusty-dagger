using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Kit.Combat;

/// <summary>Actual participants, with live stats/effects rather than copied bonuses.</summary>
public sealed record CombatParticipants(Actor Attacker, Actor Target, string Action)
{
    public StatsComponent AttackerStats => Attacker.Get<StatsComponent>();
    public StatsComponent TargetStats => Target.Get<StatsComponent>();
    public EffectsComponent AttackerEffects => Attacker.Get<EffectsComponent>();
    public EffectsComponent TargetEffects => Target.Get<EffectsComponent>();
}
public sealed class TryHitEvent(CombatParticipants participants)
{
    public CombatParticipants Participants { get; } = participants;
    public int Chance { get; set; }
    public int Roll { get; set; }
    private bool? _hit;
    /// <summary>Chance contributions affect the result unless a rule explicitly overrides the hit.</summary>
    public bool Hit { get => _hit ?? Roll <= Chance; set => _hit = value; }
}
public sealed class DamageEvent(CombatParticipants participants)
{
    public CombatParticipants Participants { get; } = participants;
    public int Body { get; set; }
    public int Damage { get; set; }
    public bool Allowed { get; set; } = true;
}
public sealed class ApplyHitEvent(CombatParticipants participants, int damage, int body)
{
    public CombatParticipants Participants { get; } = participants;
    public int Damage { get; set; } = damage;
    public int Body { get; } = body;
    public int AppliedDamage { get; set; }
    public bool Killed { get; set; }
}

/// <summary>Explicit participant contributions, in registration order after each base calculation and before application.</summary>
public interface ICombatContribution
{
    void Hit(TryHitEvent interaction) { }
    void Damage(DamageEvent interaction) { }
    void Applying(ApplyHitEvent interaction) { }
}

/// <summary>Attached contributions belong to their actor/item. Effect owners add/remove their own active contributions here.</summary>
public sealed class CombatContributions
{
    public List<ICombatContribution> Rules { get; } = [];
}

/// <summary>Typed synchronous resolution. Notifications are separate; application is never an eventual subscriber.</summary>
public sealed class CombatResolution
{
    private readonly Dictionary<string, IReadOnlyList<ICombatContribution>> _actions = new(StringComparer.Ordinal);
    public void RegisterAction(string action, params ICombatContribution[] contributions) => _actions.Add(action, contributions);
    public TryHitEvent TryHit(CombatParticipants participants, Action<TryHitEvent> resolve)
    {
        TryHitEvent interaction = new(participants);
        resolve(interaction);
        foreach (ICombatContribution rule in Gather(participants)) rule.Hit(interaction);
        return interaction;
    }
    public DamageEvent Damage(CombatParticipants participants, Action<DamageEvent> resolve)
    {
        DamageEvent interaction = new(participants);
        resolve(interaction);
        foreach (ICombatContribution rule in Gather(participants)) rule.Damage(interaction);
        return interaction;
    }
    public ApplyHitEvent Apply(CombatParticipants participants, int damage, int body, Action<ApplyHitEvent> apply)
    {
        ApplyHitEvent interaction = new(participants, damage, body);
        foreach (ICombatContribution rule in Gather(participants)) rule.Applying(interaction);
        apply(interaction);
        return interaction;
    }
    private IEnumerable<ICombatContribution> Gather(CombatParticipants participants)
    {
        foreach (ICombatContribution rule in FromActor(participants.Attacker)) yield return rule;
        foreach (ICombatContribution rule in FromActor(participants.Target)) yield return rule;
        if (_actions.TryGetValue(participants.Action, out IReadOnlyList<ICombatContribution>? action))
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
