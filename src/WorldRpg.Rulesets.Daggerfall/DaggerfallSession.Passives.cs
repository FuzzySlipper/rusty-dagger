using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Career passives consume the same calendar, actor tracks and environment as spells and rest.</summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>The committed player career's special abilities, preset or custom alike.</summary>
    private DaggerfallCareerSpecials PlayerSpecials => State.Character.Career.Specials;

    /// <summary>
    /// The special abilities of any actor's career: the player's committed career, or the career
    /// class an enemy definition names. A creature without a career has none.
    /// </summary>
    private DaggerfallCareerSpecials CareerSpecialsFor(long actorId) =>
        actorId == State.Actors.Player.DurableId ? PlayerSpecials
            : _roster.Definitions.TryGetValue(actorId, out var definition) && definition.Career is string career
                ? _definitions.Catalogs.RequireCareer(career).Specials : DaggerfallCareerSpecials.None;

    internal float EnemyAudibleRange => PlayerSpecials.AcuteHearing
        ? (State.HeldEnchantments.Talents.AcuteHearing ? 24F : 20F) : 16F;

    /// <summary>
    /// Career absorption for any target, as the donor applies it to every entity: the light and
    /// dark context is where the player is, since everything is where the player is.
    /// </summary>
    private DaggerfallMagicDefense CareerMagicDefense(long actorId)
    {
        bool light = _time.Calendar.IsDay && _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior;
        return CareerSpecialsFor(actorId).AbsorbsSpells(light) ? new(100, 0, []) : DaggerfallMagicDefense.None;
    }

    private void RefreshPassiveMagery()
    {
        var actor = State.Actors.Player;
        Track magicka = actor.Stats.GetTrack(TrackId.Parse("magicka"));
        var identity = new IntrinsicSourceIdentity(actor.Actor.Entity, SourceInstanceId.Parse("daggerfall.passive-magery"));
        Stat maximum = magicka.Maximum;
        bool dark = !_time.Calendar.IsDay || _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior;
        int penalty = PlayerSpecials.MageryPenalty(dark);
        bool unable = penalty == DaggerfallCareerSpecials.MageryUnable;
        bool reduced = penalty == DaggerfallCareerSpecials.MageryReduced;
        var existing = maximum.Sources.SingleOrDefault(source => source.Identity == identity);
        if (!unable && !reduced && existing is null) return;
        double reduction = -(int)(maximum.BaseValue * .33f);
        if (existing?.Contributions.Count == 1 && (unable && existing.Contributions[0].Contribution is StatContribution.Maximum { Value: 0 }
            || !unable && reduced && existing.Contributions[0].Contribution is StatContribution.Add add && add.Amount == reduction)) return;
        var sources = maximum.Sources.Where(source => source.Identity != identity).ToList();
        if (unable || reduced)
            sources.Add(new(identity, SourceDefinitionId.Parse("daggerfall.passive-magery"), 0,
                [new(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value), StackingGroupId.Parse("daggerfall.passive-magery"), MechanicsStackingPolicy.Sum,
                    unable ? new StatContribution.Maximum(0) : new StatContribution.Add(reduction))]));
        maximum.SetSources(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value), sources);
    }

    private void AdvancePassiveRounds(long roundBefore, long minutes)
    {
        DaggerfallCareerSpecials specials = PlayerSpecials;
        bool regeneration = specials.Regeneration != 0;
        // Classic travel advances atomically and adjusts vulnerable arrivals to dusk before
        // its catch-up pass. Our interruptible journey must not apply daylight exposure en route.
        bool sunDamage = (specials.SunDamage || State.RacialOverrides.Current?.IsVampire == true) && !State.Travel.IsExecuting;
        bool holyDamage = (specials.HolyDamage || State.RacialOverrides.Current?.IsVampire == true) && InHolyPlace();
        if (!regeneration && !sunDamage && !holyDamage) return;
        var player = State.Actors.Player;
        long first = (4 - roundBefore % 4) % 4;
        for (long offset = first; offset < Math.Min(minutes, DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds) && !player.IsDefeated; offset += 4)
        {
            var calendar = _time.Calendar;
            bool dark = !calendar.IsDay || _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon;
            bool sunlight = calendar.IsDay && _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior;
            if (regeneration && specials.Regenerates(dark, State.Swimming.IsSwimming))
                _vitality.RestoreSpellTrack(player.Actor, TrackId.Parse("health"), 1);
            int damage = (sunDamage && sunlight ? 12 : 0) + (holyDamage ? 12 : 0);
            if (damage > 0) AppendEffectDamage(new(_vitality.ResolvePassiveDamage(player.Actor, damage)));
        }
    }
}
