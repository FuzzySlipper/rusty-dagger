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

/// <summary>How a target turned a whole harm away. Presentation reads it to tell the outcomes apart.</summary>
public enum HarmNegation
{
    None,
    /// <summary>The channel cannot harm the target at all.</summary>
    Immune,
    /// <summary>Turned aside before it landed, possibly back at its source.</summary>
    Deflected,
    /// <summary>Taken by the target's protection.</summary>
    Absorbed,
    /// <summary>Withstood by the target's resistance.</summary>
    Resisted,
}

/// <summary>
/// Mitigation's first step: whether the target turns the whole harm away on this channel before it is
/// calculated, from target properties (immunity) or the target's own chance to deflect, absorb or
/// resist it. A negated harm is never calculated.
/// </summary>
public sealed class NegationEvent(CombatParticipants participants, HarmChannelId channel)
{
    public CombatParticipants Participants { get; } = participants;
    public HarmChannelId Channel { get; } = channel;
    public HarmNegation Negation { get; set; }
    public bool Negated => Negation != HarmNegation.None;
}

/// <summary>
/// Mitigation's reduction step over the calculated harm. Deflection is harm turned aside before it
/// lands, absorption is harm taken by the target's protection, and resistance is harm the target
/// withstands. Each is a non-negative amount the ruleset and contributions accumulate; a
/// <see cref="Negation"/> turns the whole harm away whatever its amount. The remaining damage is never
/// below zero.
/// </summary>
public sealed class MitigationEvent(CombatParticipants participants, HarmChannelId channel, int incoming)
{
    public CombatParticipants Participants { get; } = participants;
    public HarmChannelId Channel { get; } = channel;
    /// <summary>The calculated harm that reached mitigation.</summary>
    public int Incoming { get; } = Math.Max(0, incoming);
    public int Deflected { get; set => field = Math.Max(0, value); }
    public int Absorbed { get; set => field = Math.Max(0, value); }
    public int Resisted { get; set => field = Math.Max(0, value); }
    public HarmNegation Negation { get; set; }
    public int Damage => Negation != HarmNegation.None ? 0 : Math.Max(0, Incoming - Deflected - Absorbed - Resisted);
}

/// <summary>What one harm resolution leaves for application.</summary>
public enum HarmOutcome
{
    /// <summary>Admission refused it or it never touched the target.</summary>
    NoContact,
    /// <summary>It connected and was turned away whole, before or after calculation.</summary>
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
    ContactEvent? Contact, NegationEvent? PreCalculation, DamageEvent? Calculation, MitigationEvent? Mitigation)
{
    public bool Connected => Contact?.Contact == true;
    /// <summary>How the whole harm was turned away, before or after calculation; none when any of it remains.</summary>
    public HarmNegation Negation => PreCalculation is { Negated: true } before ? before.Negation : Mitigation?.Negation ?? HarmNegation.None;
    /// <summary>Whether the harm was turned away before it was calculated.</summary>
    public bool NegatedBeforeCalculation => PreCalculation?.Negated == true;
    /// <summary>The amount application receives; zero unless the harm connected and was not negated.</summary>
    public int Damage => Mitigation?.Damage ?? 0;
    public HarmOutcome Outcome => !Connected ? HarmOutcome.NoContact
        : Negation != HarmNegation.None ? HarmOutcome.Negated
        : Mitigation is not { } mitigation || mitigation.Damage == mitigation.Incoming ? HarmOutcome.Unmitigated
        : mitigation.Damage == 0 ? HarmOutcome.Negated
        : HarmOutcome.Reduced;
}
