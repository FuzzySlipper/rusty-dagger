using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Ruleset consequences of accepted Engine movement, kept at the same health application boundary as combat and effects.</summary>
internal sealed class DaggerfallVitalityConsequences
{
    private static readonly TrackId HealthTrack = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private readonly CombatResolution _combat;

    internal DaggerfallVitalityConsequences(CombatResolution combat) => _combat = combat ?? throw new ArgumentNullException(nameof(combat));

    internal DamageResult ResolveSpellHealth(Actor caster, Actor target, int amount, bool terminal)
    {
        Track health = target.Get<StatsComponent>().GetTrack(HealthTrack);
        int calculated = terminal ? checked((int)Math.Ceiling(health.Current - health.Minimum)) : amount;
        return _combat.ApplyToHealth(new(caster, target, terminal ? "disintegrate" : "spell health damage"),
            calculated, 0, health, terminal ? HealthApplicationMode.Terminal : HealthApplicationMode.Damage).Result;
    }

    internal DaggerfallSpellTrackResult ResolveSpellTrack(Actor caster, Actor target, TrackId trackId, int amount, bool permitted = true)
    {
        Track track = target.Get<StatsComponent>().GetTrack(trackId);
        double before = track.Current;
        if (permitted && target.Get<StatsComponent>().GetTrack(HealthTrack).Current > 0)
            track.SetCurrent(before - Math.Max(0, amount), clamp: true);
        return new(caster, target, trackId, amount, before - track.Current);
    }

    /// <summary>Direct loss precedes bounded caster recovery; classic transfer restores the admitted amount, not the bounded loss.</summary>
    internal DaggerfallSpellTransferResult ResolveSpellTransfer(Actor caster, Actor target, int magnitude, bool fatigue, bool permitsFatigueLoss)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(magnitude);
        int amount = fatigue ? DaggerfallFormulaPolicy.SpellFatigueDamage(magnitude) : magnitude;
        TrackId track = fatigue ? TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value) : HealthTrack;
        DamageResult? health = fatigue ? null : ResolveSpellHealth(caster, target, amount, terminal: false);
        DaggerfallSpellTrackResult? loss = fatigue ? ResolveSpellTrack(caster, target, track, amount, permitsFatigueLoss) : null;
        // A reflected terminal self-hit cannot resurrect its caster after accepted death.
        double restored = caster.Get<StatsComponent>().GetTrack(HealthTrack).Current > 0
            ? caster.Get<StatsComponent>().GetTrack(track).Restore(amount) : 0;
        return new(caster, target, track, amount, health, loss, restored);
    }

    /// <summary>
    /// Applies the damage a worn enchantment does to its wearer, at the same health boundary combat and
    /// movement use, so an enchantment that takes the last point of health defeats its wearer the way any
    /// other accepted damage does rather than leaving a track at zero.
    /// </summary>
    internal DamageResult ResolveHeldEnchantmentDamage(Actor player, int damage)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (damage <= 0) throw new ArgumentOutOfRangeException(nameof(damage));
        Track health = player.Get<StatsComponent>().GetTrack(HealthTrack);
        return _combat.ApplyToHealth(new CombatParticipants(player, player, "held enchantment"), damage, 0, health).Result;
    }

    /// <summary>
    /// Applies the damage a poison does to its victim, at the same health boundary combat and movement use,
    /// so a poison that takes the last point of health defeats its victim through the ordinary consequence
    /// path rather than leaving a track at zero.
    /// </summary>
    internal DamageResult ResolvePoisonDamage(Actor victim, int damage)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (damage <= 0) throw new ArgumentOutOfRangeException(nameof(damage));
        Track health = victim.Get<StatsComponent>().GetTrack(HealthTrack);
        return _combat.ApplyToHealth(new CombatParticipants(victim, victim, "poison"), damage, 0, health).Result;
    }

    /// <summary>Applies only the Engine-reported landing, never an input or a locally integrated trajectory.</summary>
    internal DamageResult? ResolveLanding(Actor player, DaggerfallLanding? landing, bool preventsFallDamage)
    {
        if (preventsFallDamage || landing is not { } accepted) return null;
        int damage = DaggerfallFormulaPolicy.FallDamage(accepted.Validate().Distance);
        if (damage == 0) return null;
        Track health = player.Get<StatsComponent>().GetTrack(HealthTrack);
        return _combat.ApplyToHealth(new CombatParticipants(player, player, "fall"), damage, 0, health).Result;
    }
}

internal sealed record DaggerfallSpellTrackResult(Actor Source, Actor Target, TrackId Track, int CalculatedLoss, double ActualLoss);

internal sealed record DaggerfallSpellTransferResult(Actor Caster, Actor Target, TrackId Track, int Amount,
    DamageResult? HealthDamage, DaggerfallSpellTrackResult? TrackDamage, double Restored)
{
    internal double ActualLoss => HealthDamage?.ActualHealthLost ?? TrackDamage?.ActualLoss ?? 0;
    internal bool TargetDefeated => HealthDamage?.Defeated == true;
}
