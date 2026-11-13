using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The passive rounds of an interval: every fourth magic round, as many as the catch-up admits, regenerates the
/// player, harms the player by this much, or both. What a round does to health is the session's round list.
/// </summary>
internal readonly record struct DaggerfallPassiveRounds(long Count, bool Regenerates, int Damage)
{
    internal static DaggerfallPassiveRounds None => default;
}

/// <summary>
/// What a career's special abilities do from moment to moment: whose specials apply (the player's committed career,
/// or the career class an actor's definition names), hearing's reach, spell absorption, the magery penalty on
/// maximum magicka, and the passive rounds of regeneration and sun or holy damage. It is handed the character and
/// the session's actor definitions; where the player stands and what other domains know (day or dark, a vampire,
/// a journey, a holy place, the hearing talent) are call inputs.
/// </summary>
internal sealed class DaggerfallCareerPassives(DaggerfallCharacterState character, DaggerfallDefinitions definitions,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> actors, long playerId)
{
    private const string Magery = "daggerfall.passive-magery";

    /// <summary>The committed player career's special abilities, preset or custom alike.</summary>
    internal DaggerfallCareerSpecials PlayerSpecials => character.Career.Specials;

    /// <summary>
    /// The special abilities of any actor's career: the player's committed career, or the career class an enemy
    /// definition names. A creature without a career has none.
    /// </summary>
    internal DaggerfallCareerSpecials SpecialsFor(long actorId) =>
        actorId == playerId ? PlayerSpecials
            : actors.TryGetValue(actorId, out var definition) && definition.Career is string career
                ? definitions.Catalogs.RequireCareer(career).Specials : DaggerfallCareerSpecials.None;

    /// <summary>How far away the player hears enemies: acute hearing reaches further, and further still with its talent.</summary>
    internal float EnemyAudibleRange(bool acuteHearingTalent) => PlayerSpecials.AcuteHearing
        ? (acuteHearingTalent ? 24F : 20F) : 16F;

    /// <summary>
    /// Whether an actor's career absorbs spells, as the donor applies it to every entity: the light context is
    /// where the player is, since everything is where the player is.
    /// </summary>
    internal bool AbsorbsSpells(long actorId, bool light) => SpecialsFor(actorId).AbsorbsSpells(light);

    /// <summary>
    /// Sets the player's maximum magicka source for the career's magery in this light: none, a third less, or none
    /// at all. An unchanged source is left as it is.
    /// </summary>
    internal void RefreshMagery(StatsComponent stats, EntityId player, bool dark)
    {
        Track magicka = stats.GetTrack(TrackId.Parse("magicka"));
        var identity = new IntrinsicSourceIdentity(player, SourceInstanceId.Parse(Magery));
        Stat maximum = magicka.Maximum;
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
            sources.Add(new(identity, SourceDefinitionId.Parse(Magery), 0,
                [new(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value), StackingGroupId.Parse(Magery), MechanicsStackingPolicy.Sum,
                    unable ? new StatContribution.Maximum(0) : new StatContribution.Add(reduction))]));
        maximum.SetSources(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value), sources);
    }

    /// <summary>
    /// The passive rounds of <paramref name="minutes"/> magic rounds after <paramref name="roundBefore"/>, at most
    /// <paramref name="catchupRounds"/> of them. Regeneration follows the career in this dark and water; sunlight
    /// harms a career that suffers it, or a vampire, except on a journey, whose arrival the journey adjusts; a holy
    /// place harms a career that suffers it, or a vampire. <paramref name="inHolyPlace"/> is asked only when that harm applies.
    /// </summary>
    internal DaggerfallPassiveRounds Rounds(long roundBefore, long minutes, long catchupRounds, bool vampire, bool travelling,
        Func<bool> inHolyPlace, bool dark, bool sunlight, bool swimming)
    {
        DaggerfallCareerSpecials specials = PlayerSpecials;
        bool regeneration = specials.Regeneration != 0;
        // Classic travel advances atomically and adjusts vulnerable arrivals to dusk before
        // its catch-up pass. Our interruptible journey must not apply daylight exposure en route.
        bool sunDamage = (specials.SunDamage || vampire) && !travelling;
        bool holyDamage = (specials.HolyDamage || vampire) && inHolyPlace();
        if (!regeneration && !sunDamage && !holyDamage) return DaggerfallPassiveRounds.None;
        long first = (4 - roundBefore % 4) % 4;
        long end = Math.Min(minutes, catchupRounds);
        return new(end > first ? (end - first + 3) / 4 : 0,
            regeneration && specials.Regenerates(dark, swimming),
            (sunDamage && sunlight ? 12 : 0) + (holyDamage ? 12 : 0));
    }
}
