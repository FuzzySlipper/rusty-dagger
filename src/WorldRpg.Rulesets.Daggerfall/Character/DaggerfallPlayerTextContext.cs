using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using static WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallTextContexts;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>The player's values formatted for the donor's player text macros.</summary>
internal static class DaggerfallPlayerTextContext
{
    internal static DaggerfallTextPlayerContext For(DaggerfallDefinitions definitions, DaggerfallCharacterIdentity identity,
        StatsComponent stats, ulong? gold = null, DaggerfallCareerDefinition? career = null)
    {
        int Read(DaggerfallStatId id) => stats.GetStat(StatId.Parse(id.Value)).ValueInt;
        int strength = Read(DaggerfallMechanicsIds.Strength), intelligence = Read(DaggerfallMechanicsIds.Intelligence),
            willpower = Read(DaggerfallMechanicsIds.Willpower), agility = Read(DaggerfallMechanicsIds.Agility),
            endurance = Read(DaggerfallMechanicsIds.Endurance), personality = Read(DaggerfallMechanicsIds.Personality),
            speed = Read(DaggerfallMechanicsIds.Speed), luck = Read(DaggerfallMechanicsIds.Luck);
        Track magicka = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
        bool female = identity.Gender == DaggerfallCharacterGender.Female;
        string Text(string key) => Internal(definitions, key);
        Dictionary<string, string> ratings = [];
        foreach (var (symbol, value) in new[] { ("str", strength), ("int", intelligence), ("wil", willpower), ("agi", agility),
            ("end", endurance), ("per", personality), ("spd", speed), ("luc", luck) })
        {
            string[] names = List(definitions, symbol + "Ratings");
            ratings["%" + symbol] = names[Math.Clamp(value / 10, 0, names.Length - 1)];
        }
        string? mastered = career?.PrimarySkills.FirstOrDefault(skill => stats.GetStat(StatId.Parse(skill)).BaseValue == 100);
        return new(Name: identity.Name, Race: Race(definitions, identity.RaceId), GoldCarried: gold?.ToString(CultureInfo.InvariantCulture),
            DamageModifier: Signed(DaggerfallFormulaPolicy.DamageModifier(strength)),
            EncumbranceMaximum: Number(DaggerfallFormulaPolicy.MaxEncumbrance(strength)),
            Magicka: Number(magicka.Current), MagickaMaximum: Number(magicka.Maximum.Value),
            MasteredSkill: mastered is null ? "BLANK" : DaggerfallIdLabel.For(mastered),
            MagicResistance: Number(DaggerfallFormulaPolicy.MagicResist(willpower)),
            ToHitModifier: Signed(DaggerfallFormulaPolicy.ToHitModifier(agility)),
            HitPointModifier: Signed(DaggerfallFormulaPolicy.HitPointsModifier(endurance)),
            HealingRateModifier: Signed(DaggerfallFormulaPolicy.HealingRateModifier(endurance)),
            Strength: Number(strength), Intelligence: Number(intelligence), Willpower: Number(willpower), Agility: Number(agility),
            Endurance: Number(endurance), Personality: Number(personality), Speed: Number(speed), Luck: Number(luck),
            PlayerPronoun: Text(female ? "pronounShe" : "pronounHe"), PlayerObjectPronoun: Text(female ? "pronounHer" : "pronounHim"),
            PlayerReflexivePronoun: Text(female ? "pronounHerself" : "pronounHimself"),
            PlayerPossessiveAdjective: Text(female ? "pronounHer2" : "pronounHis"),
            PlayerPossessivePronoun: Text(female ? "pronounHers" : "pronounHis2")) { AttributeRatings = ratings };
    }
}
