namespace WorldRpg.Kit.Combat;

/// <summary>
/// What kind of harm reaches a target, as the ruleset defines it. Kit owns only the identity; the
/// channel set and what each channel means belong to the ruleset's own definitions.
/// </summary>
public readonly record struct HarmChannelId
{
    public HarmChannelId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }
    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>
/// Delivery's contact step: whether harm physically reached this target. The delivery owner supplies
/// the base answer (an admitted target, a projectile collision); contributions may change it.
/// </summary>
public sealed class ContactEvent(CombatParticipants participants, bool contact)
{
    public CombatParticipants Participants { get; } = participants;
    public bool Contact { get; set; } = contact;
}

/// <summary>
/// Mitigation's first step: whether the target can be harmed on this channel at all. It is decided from
/// target properties before the harm is calculated, so an immune target never has its harm rolled.
/// </summary>
public sealed class ImmunityEvent(CombatParticipants participants, HarmChannelId channel)
{
    public CombatParticipants Participants { get; } = participants;
    public HarmChannelId Channel { get; } = channel;
    public bool Immune { get; set; }
}

/// <summary>
/// Mitigation's reduction step over the calculated harm. Deflection is harm turned aside before it
/// lands; absorption is harm taken by the target's protection. Both are non-negative amounts the
/// ruleset and contributions accumulate; the remaining damage is never below zero.
/// </summary>
public sealed class MitigationEvent(CombatParticipants participants, HarmChannelId channel, int incoming)
{
    public CombatParticipants Participants { get; } = participants;
    public HarmChannelId Channel { get; } = channel;
    /// <summary>The calculated harm that reached mitigation.</summary>
    public int Incoming { get; } = Math.Max(0, incoming);
    public int Deflected { get; set => field = Math.Max(0, value); }
    public int Absorbed { get; set => field = Math.Max(0, value); }
    public int Damage => Math.Max(0, Incoming - Deflected - Absorbed);
}

/// <summary>What one harm resolution leaves for application.</summary>
public enum HarmOutcome
{
    /// <summary>Admission refused it or it never touched the target.</summary>
    NoContact,
    /// <summary>It connected with a target the channel cannot harm; nothing was calculated.</summary>
    Immune,
    /// <summary>It connected and mitigation took all of it.</summary>
    Negated,
    /// <summary>It connected and mitigation took part of it.</summary>
    Reduced,
    /// <summary>It connected and mitigation took none of it.</summary>
    Unmitigated,
}

/// <summary>
/// One target's pass through delivery and mitigation, before application. Each stage that ran is
/// retained so the ruleset can report the roll, the contact and the mitigation it presents.
/// </summary>
public sealed record HarmResolution(CombatParticipants Participants, HarmChannelId Channel, TryHitEvent? Admission,
    ContactEvent? Contact, ImmunityEvent? Immunity, DamageEvent? Calculation, MitigationEvent? Mitigation)
{
    public bool Connected => Contact?.Contact == true;
    public bool Immune => Immunity?.Immune == true;
    /// <summary>The amount application receives; zero unless the harm connected and was not negated.</summary>
    public int Damage => Mitigation?.Damage ?? 0;
    public HarmOutcome Outcome => !Connected ? HarmOutcome.NoContact
        : Immune ? HarmOutcome.Immune
        : Mitigation is not { } mitigation || mitigation.Damage == mitigation.Incoming ? HarmOutcome.Unmitigated
        : mitigation.Damage == 0 ? HarmOutcome.Negated
        : HarmOutcome.Reduced;
}
