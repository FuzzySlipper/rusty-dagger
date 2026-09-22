using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Facts;

namespace WorldRpg.Kit.Combat;

public readonly record struct AttackRequest(long AttackerId, long? TargetId, ulong Generation, ulong SimulationStep,
    double FixedDeltaSeconds, bool Delayed, string? Action = null);
public readonly record struct AttackOutcome(bool Hit, bool Allowed, int Body, int Damage, int Roll, int Chance);
public readonly record struct PreparedAttack(double CooldownSeconds, AttackOutcome Outcome);
public readonly record struct PendingAttack(AttackRequest Request, PreparedAttack Attack);
/// <summary>A resolved delayed attack whose authored impact has been released for delivery.</summary>
public readonly record struct DeferredAttackImpact(AttackRequest Request, PreparedAttack Attack);
public readonly record struct AttackCooldown(long AttackerId, ulong RemainingSteps);
public readonly record struct AttackImpactNotice(long AttackerId, long TargetId, ulong Generation, ulong SimulationStep, bool Expired);
public enum AttackRefusal { UnknownActor, TargetDefeated, Cooldown }

public sealed class AttackState
{
    public ulong Generation { get; internal set; }
    public ulong ReadyAtStep { get; internal set; }
    public ulong RestoredRemainingSteps { get; internal set; }
    public PendingAttack? Pending { get; internal set; }
}

/// <summary>Ruleset availability, costs and resolution policy. Execution owns when each operation occurs.</summary>
public interface IAttackRules<TFact> where TFact : IWorldRpgFact
{
    bool TryPrepare(AttackRequest request, FactBuffer<TFact> facts, out PreparedAttack attack);
    void Refused(AttackRefusal reason, FactBuffer<TFact> facts);
    void Started(AttackRequest request, PreparedAttack attack, FactBuffer<TFact> facts);
    void Apply(AttackRequest request, PreparedAttack attack, FactBuffer<TFact> facts);
}

/// <summary>Single live owner for readiness, pending impacts and cancellation, over attached actor state.</summary>
public sealed class AttackExecution<TFact>(ActorsState actors, IAttackRules<TFact> rules,
    Func<DeferredAttackImpact, FactBuffer<TFact>, bool>? deferImpact = null) where TFact : IWorldRpgFact
{
    private AttackState State(long actor) => actors.Get(actor).Actor.Get<AttackState>();
    private bool Exists(long actor) => actors.Entities.TryResolve(ActorsState.Identity(actor), out _);
    private bool Defeated(long actor) => actor == actors.Player.DurableId ? actors.Player.IsDefeated : actors.Get(actor).IsDefeated;
    public bool IsReady(long actor, ulong generation, ulong step)
    {
        if (!Exists(actor) || Defeated(actor)) return false;
        AttackState state = State(actor);
        return (state.Pending is not PendingAttack pending || pending.Request.Generation != generation)
            && (state.Generation != generation || step >= state.ReadyAtStep);
    }
    public bool Start(AttackRequest request, FactBuffer<TFact> facts)
    {
        if (!Exists(request.AttackerId) || request.TargetId is long target && !Exists(target))
        { rules.Refused(AttackRefusal.UnknownActor, facts); return false; }
        if (Defeated(request.AttackerId) || request.TargetId is long victim && Defeated(victim))
        { rules.Refused(AttackRefusal.TargetDefeated, facts); return false; }
        AttackState state = State(request.AttackerId);
        if (state.Pending is PendingAttack pending && pending.Request.Generation == request.Generation) return false;
        if (!IsReady(request.AttackerId, request.Generation, request.SimulationStep))
        { rules.Refused(AttackRefusal.Cooldown, facts); return false; }
        if (!rules.TryPrepare(request, facts, out PreparedAttack attack)) return false;
        state.Generation = request.Generation;
        state.ReadyAtStep = checked(request.SimulationStep + (ulong)Math.Max(1d, Math.Ceiling(attack.CooldownSeconds / request.FixedDeltaSeconds)));
        if (request.Delayed) state.Pending = new PendingAttack(request, attack);
        rules.Started(request, attack, facts);
        if (!request.Delayed) rules.Apply(request, attack, facts);
        return true;
    }
    public void Interrupt(long attacker, ulong generation)
    {
        if (Exists(attacker) && State(attacker).Pending is PendingAttack pending && pending.Request.Generation == generation)
            State(attacker).Pending = null;
    }
    public void ApplyImpacts(IReadOnlyList<AttackImpactNotice> notices, ulong generation, FactBuffer<TFact> facts)
    {
        foreach (AttackImpactNotice notice in notices)
        {
            if (!Exists(notice.AttackerId)) continue;
            AttackState state = State(notice.AttackerId);
            if (state.Pending is not PendingAttack pending || pending.Request.Generation != notice.Generation
                || pending.Request.SimulationStep != notice.SimulationStep || pending.Request.TargetId != notice.TargetId) continue;
            state.Pending = null;
            if (notice.Expired || notice.Generation != generation || Defeated(notice.AttackerId)
                || !Exists(notice.TargetId) || Defeated(notice.TargetId)) continue;
            DeferredAttackImpact impact = new(pending.Request, pending.Attack);
            if (deferImpact?.Invoke(impact, facts) == true) continue;
            rules.Apply(impact.Request, impact.Attack, facts);
        }
    }
    /// <summary>Applies a deferred release once its ruleset-owned delivery has arrived.</summary>
    public void ApplyDeferredImpact(DeferredAttackImpact impact, FactBuffer<TFact> facts) =>
        rules.Apply(impact.Request, impact.Attack, facts);
    public IReadOnlyList<AttackCooldown> CaptureCooldowns(ulong? generation, ulong? step) => AllActors()
        .Select(id => (id, state: State(id)))
        .Select(v => new AttackCooldown(v.id, generation == v.state.Generation && step is ulong now && v.state.ReadyAtStep > now
            ? v.state.ReadyAtStep - now : v.state.RestoredRemainingSteps))
        .Where(v => v.RemainingSteps > 0).OrderBy(v => v.AttackerId).ToArray();
    public void RestoreCooldowns(IEnumerable<AttackCooldown> values)
    {
        foreach (AttackCooldown value in values) State(value.AttackerId).RestoredRemainingSteps = value.RemainingSteps;
    }
    public void ObserveTimeline(ulong generation, ulong step)
    {
        foreach (long id in AllActors())
        {
            AttackState state = State(id);
            if (state.RestoredRemainingSteps == 0) continue;
            state.Generation = generation;
            state.ReadyAtStep = checked(step + state.RestoredRemainingSteps);
            state.RestoredRemainingSteps = 0;
        }
    }
    private IEnumerable<long> AllActors() => actors.All.Select(a => a.DurableId).Prepend(actors.Player.DurableId);
}
