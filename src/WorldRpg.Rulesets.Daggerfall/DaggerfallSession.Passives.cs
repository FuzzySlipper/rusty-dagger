using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Career passives consume the same calendar, actor tracks and environment as spells and rest.</summary>
internal sealed partial class DaggerfallSession
{
    private bool CareerAdvantage(string id, string? target = null)
    {
        var traits = State.Character.CustomCareer?.Advantages;
        return id is "rapid-healing" or "regenerate-health" or "spell-absorption" or "increased-magery"
            ? traits?.LastOrDefault(trait => trait.Id == id) is { } selected && (target is null || selected.Target == target)
            : traits?.Any(trait => trait.Id == id && (target is null || trait.Target == target)) == true;
    }
    private bool CareerDisadvantage(string id, string? target = null)
    {
        var traits = State.Character.CustomCareer?.Disadvantages;
        return id is "darkness-powered-magery" or "light-powered-magery"
            ? traits?.LastOrDefault(trait => trait.Id == id) is { } selected && (target is null || selected.Target == target)
            : traits?.Any(trait => trait.Id == id && (target is null || trait.Target == target)) == true;
    }

    internal float EnemyAudibleRange => CareerAdvantage("acute-hearing")
        ? (State.HeldEnchantments.Talents.AcuteHearing ? 24F : 20F) : 16F;

    private DaggerfallMagicDefense CareerMagicDefense(long actorId)
    {
        bool dark = !_time.Calendar.IsDay || _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior;
        return actorId == DaggerfallActorIdentity.PlayerEntityId && (CareerAdvantage("spell-absorption", "general")
            || CareerAdvantage("spell-absorption", dark ? "darkness" : "light"))
            ? new(100, 0, []) : DaggerfallMagicDefense.None;
    }

    private void RefreshPassiveMagery()
    {
        var actor = State.Actors.Player;
        Track magicka = actor.Stats.GetTrack(TrackId.Parse("magicka"));
        var identity = new IntrinsicSourceIdentity(actor.Actor.Entity, SourceInstanceId.Parse("daggerfall.passive-magery"));
        Stat maximum = magicka.Maximum;
        bool dark = !_time.Calendar.IsDay || _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior;
        string trait = dark ? "light-powered-magery" : "darkness-powered-magery";
        bool unable = CareerDisadvantage(trait, "unable");
        bool reduced = CareerDisadvantage(trait, "reduced");
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
        bool regeneration = CareerAdvantage("regenerate-health");
        // Classic travel advances atomically and adjusts vulnerable arrivals to dusk before
        // its catch-up pass. Our interruptible journey must not apply daylight exposure en route.
        bool sunDamage = CareerDisadvantage("damage", "sunlight") && !State.Travel.IsExecuting;
        bool holyDamage = CareerDisadvantage("damage", "holy-places") && InHolyPlace();
        if (!regeneration && !sunDamage && !holyDamage) return;
        var player = State.Actors.Player;
        long first = (4 - roundBefore % 4) % 4;
        for (long offset = first; offset < Math.Min(minutes, DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds) && !player.IsDefeated; offset += 4)
        {
            var calendar = _time.Calendar;
            bool dark = !calendar.IsDay || _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon;
            bool sunlight = calendar.IsDay && _activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior;
            if (regeneration && (CareerAdvantage("regenerate-health", "general")
                || CareerAdvantage("regenerate-health", dark ? "darkness" : "light")
                || CareerAdvantage("regenerate-health", "immersed") && State.Swimming.IsSwimming))
                _vitality.RestoreSpellTrack(player.Actor, TrackId.Parse("health"), 1);
            int damage = (sunDamage && sunlight ? 12 : 0) + (holyDamage ? 12 : 0);
            if (damage > 0) AppendEffectDamage(new(_vitality.ResolvePassiveDamage(player.Actor, damage)));
        }
    }
}
