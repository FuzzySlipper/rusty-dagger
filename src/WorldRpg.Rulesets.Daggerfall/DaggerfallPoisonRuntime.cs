using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Holds one poison per afflicted actor and gives it its minute, applying each arm through the owner that
/// already guarantees that vital or attribute.
/// </summary>
/// <remarks>
/// The donor draws a sharp line through a poison's end, and this follows it. A drug's arms that help the
/// victim are <b>taken back</b> when the poison completes, while the attribute damage a poison does
/// <b>persists</b> until the victim is cured or heals the attribute back — so a completed affliction whose
/// arms are still on the actor stays here, holding the handles that will remove them, and only leaves when
/// nothing of it remains. Health, fatigue and magicka arms are one-off applications: the donor changes them
/// as they tick and has nothing to take back.
/// </remarks>
internal sealed class DaggerfallPoisonRuntime
{
    private readonly Func<int, int, int> _roll;
    private readonly DaggerfallVitalityConsequences _vitality;
    private readonly Dictionary<Actor, DaggerfallPoisonState> _states = [];

    /// <param name="vitality">The health boundary a poison's health arm goes through.</param>
    /// <param name="roll">Draws an inclusive lower and upper bound, the way an archetype window is written.</param>
    internal DaggerfallPoisonRuntime(DaggerfallVitalityConsequences vitality, Func<int, int, int> roll)
    {
        _vitality = vitality ?? throw new ArgumentNullException(nameof(vitality));
        _roll = roll ?? throw new ArgumentNullException(nameof(roll));
    }

    /// <summary>How many actors carry a poison, completed ones included while their damage persists.</summary>
    internal int Count => _states.Count;

    /// <summary>Whether an actor carries this poison or the damage it left.</summary>
    internal bool IsAfflicted(Actor actor) => _states.ContainsKey(actor);

    /// <summary>The affliction an actor carries, when it carries one.</summary>
    internal DaggerfallPoisonAffliction? Affliction(Actor actor) =>
        _states.TryGetValue(actor, out DaggerfallPoisonState? state) ? state.Affliction : null;

    /// <summary>Whether an actor's completed poison still holds attribute damage on it.</summary>
    internal bool HasPersistingDamage(Actor actor) =>
        _states.TryGetValue(actor, out DaggerfallPoisonState? state) && state.Applied.Count > 0;

    /// <summary>
    /// Afflicts an actor with one classic variant. An actor already carrying a poison keeps the worse of
    /// the two rather than being poisoned twice: the donor's incumbent rule lets the longer affliction win,
    /// which is what stops a second scratch from shortening a poison already running.
    /// </summary>
    internal bool Afflict(Actor actor, int variant)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!DaggerfallPoisonArchetypes.TryResolve(variant, out DaggerfallPoisonArchetype archetype)) return false;
        DaggerfallPoisonAffliction applying = new(
            archetype,
            _roll(archetype.MinimumOnsetMinutes, archetype.MaximumOnsetMinutes),
            _roll(archetype.MinimumDurationMinutes, archetype.MaximumDurationMinutes));
        if (_states.TryGetValue(actor, out DaggerfallPoisonState? existing))
        {
            if (!existing.Affliction.SupersededBy(applying)) return false;
            Reverse(actor, existing);
        }
        _states[actor] = new DaggerfallPoisonState(applying);
        return true;
    }

    /// <summary>
    /// Passes game minutes for every afflicted actor, the way the donor's poison walks the minutes that
    /// elapsed rather than assuming one. Answers how many arms acted.
    /// </summary>
    internal int AdvanceMinutes(int minutes)
    {
        if (minutes <= 0) return 0;
        int applied = 0;
        foreach ((Actor actor, DaggerfallPoisonState state) in _states.ToArray())
        {
            for (int minute = 0; minute < minutes; minute++)
            {
                foreach (DaggerfallPoisonEffect effect in state.Affliction.AdvanceMinute())
                {
                    Apply(actor, state, effect);
                    applied++;
                }
                if (state.Affliction.Phase != DaggerfallPoisonPhase.Complete) continue;
                Complete(actor, state);
                if (state.Applied.Count == 0) _states.Remove(actor);
                break;
            }
        }
        return applied;
    }

    /// <summary>Cures an actor: its poison ends and everything the poison still holds on it is removed.</summary>
    internal bool Cure(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!_states.TryGetValue(actor, out DaggerfallPoisonState? state)) return false;
        Reverse(actor, state);
        state.Affliction.Cure();
        _states.Remove(actor);
        return true;
    }

    /// <summary>
    /// A poison that has run its course: a drug's helping arms come back off, and attribute damage stays
    /// where the donor leaves it — on the actor, until a cure or healing takes it away.
    /// </summary>
    private static void Complete(Actor actor, DaggerfallPoisonState state)
    {
        if (state.Affliction.Archetype.Kind != DaggerfallPoisonKind.Drug) return;
        for (int index = state.Applied.Count - 1; index >= 0; index--)
        {
            AppliedPoisonArm arm = state.Applied[index];
            if (!arm.IsPositive) continue;
            arm.Stat!.RemoveModifier(arm.Handle);
            state.Applied.RemoveAt(index);
        }
    }

    private static void Reverse(Actor actor, DaggerfallPoisonState state)
    {
        foreach (AppliedPoisonArm arm in state.Applied) arm.Stat!.RemoveModifier(arm.Handle);
        state.Applied.Clear();
    }

    private void Apply(Actor actor, DaggerfallPoisonState state, DaggerfallPoisonEffect effect)
    {
        int amount = _roll(effect.Minimum, effect.Maximum);
        if (amount == 0) return;
        switch (effect.Target)
        {
            case DaggerfallPoisonTarget.Health when amount > 0:
                _ = _vitality.ResolvePoisonDamage(actor, amount);
                return;
            case DaggerfallPoisonTarget.Fatigue:
                Adjust(actor, DaggerfallMechanicsIds.Stamina, amount);
                return;
            case DaggerfallPoisonTarget.Magicka:
                Adjust(actor, DaggerfallMechanicsIds.Magicka, amount);
                return;
            case DaggerfallPoisonTarget.Attribute:
                Stat stat = actor.Get<StatsComponent>().GetStat(StatId.Parse(effect.Attribute!));
                state.Applied.Add(new AppliedPoisonArm(stat, stat.AddModifier(-amount), effect.IsPositive));
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// Moves a vital track by the arm's own sign, which is how the donor's resting, draining and restoring
    /// arms read: a negative arm takes what is there and no more, a positive one does not pass the maximum.
    /// </summary>
    private static void Adjust(Actor actor, DaggerfallTrackId track, int amount)
    {
        Track value = actor.Get<StatsComponent>().GetTrack(TrackId.Parse(track.Value));
        // The track clamps at its own ends, so an arm that asks for more than is there takes what is there
        // and a helping arm does not pass the maximum.
        value.SetCurrent(value.Current + amount, clamp: true);
    }

    /// <summary>
    /// One actor's poison between ticks: its affliction, and the modifier arms the poison has put on the
    /// actor which a completion, a cure or a superseding poison has to take back.
    /// </summary>
    private sealed class DaggerfallPoisonState(DaggerfallPoisonAffliction affliction)
    {
        internal DaggerfallPoisonAffliction Affliction { get; } = affliction;
        internal List<AppliedPoisonArm> Applied { get; } = [];
    }

    /// <summary>One arm of a poison that is on the actor as a modifier, with the handle that takes it off.</summary>
    private sealed record AppliedPoisonArm(Stat Stat, StatModifierHandle Handle, bool IsPositive);
}
