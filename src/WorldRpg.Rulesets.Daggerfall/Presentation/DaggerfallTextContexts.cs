using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Current owner values formatted for the donor's global text macros.</summary>
internal static class DaggerfallTextContexts
{
    internal static string Internal(DaggerfallDefinitions definitions, string key) =>
        string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, key)).TextRuns);

    internal static string[] List(DaggerfallDefinitions definitions, string key) =>
        Internal(definitions, key).Split('\n');

    internal static string Race(DaggerfallDefinitions definitions, string id)
    {
        _ = definitions.Catalogs.RequireRace(id);
        string[] words = id.Split('-');
        string key = words[0] + string.Concat(words.Skip(1).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        return Internal(definitions, key);
    }

    internal static DaggerfallTextCalendarContext Calendar(DaggerfallDefinitions definitions, DaggerfallCalendar calendar)
    {
        int day = calendar.Day + 1;
        string suffix = DaggerfallCalendar.OrdinalSuffix(day);
        string dayName = calendar.DayName;
        string month = calendar.MonthName;
        string season = List(definitions, "seasonNames")[calendar.Season switch
            { DaggerfallSeason.Spring => 1, DaggerfallSeason.Summer => 2, DaggerfallSeason.Autumn => 0, _ => 3 }];
        return new(Date: string.Format(CultureInfo.InvariantCulture, Internal(definitions, "dateFormatString"), dayName, day, suffix, month),
            Time: calendar.DescribeTime(), DayNumber: Number(day), DayName: dayName,
            DayWithSuffix: Number(day) + suffix, MonthNumber: Number(calendar.Month + 1), MonthName: month,
            Year: Number(calendar.Year), Minute: Number(calendar.Minute), Hour: Number(calendar.Hour),
            Sign: List(definitions, "birthSignNames")[calendar.Month], Season: season);
    }

    internal static DaggerfallTextPlayerContext Player(DaggerfallDefinitions definitions, DaggerfallCharacterIdentity identity,
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
            MasteredSkill: mastered is null ? "BLANK" : DaggerfallInventoryPresentation.Label(mastered),
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

    internal static DaggerfallTextItemContext Item(DaggerfallDefinitions definitions, DaggerfallItemDefinition definition,
        DaggerfallItemInstanceMetadata metadata, string name, ulong quantity = 1, int? value = null)
    {
        int percentage = DaggerfallFormulaPolicy.ConditionPercentage(metadata.CurrentCondition, metadata.MaximumCondition);
        string condition = metadata.MaximumCondition > 0 && metadata.CurrentCondition <= metadata.MaximumCondition
            ? Internal(definitions, percentage switch
                { <= 1 => "Broken", <= 5 => "Useless", <= 15 => "Battered", <= 40 => "Worn", <= 60 => "Used",
                    <= 75 => "SlightlyUsed", <= 91 => "AlmostNew", _ => "New" })
            : Number(metadata.CurrentCondition);
        double weight = (metadata.WeightClassicUnits is ulong units ? units / 400d : definition.Weight / 4d) * quantity;
        int materialDamage = DaggerfallFormulaPolicy.WeaponMaterialDamageModifier(metadata.Material,
            DaggerfallFormulaPolicy.ClassicWeaponMaterialModifiers);
        string? damage = definition.Weapon is { } weapon
            ? $"{weapon.MinimumDamage + materialDamage} - {weapon.MaximumDamage + materialDamage}" : null;
        int? armor = definition.Shield?.Armor ?? (definition.Armor is not null
            ? definitions.ArmorValuesByMaterial[metadata.Material] : null);
        string? author = metadata.BookId is int book ? definitions.Books.Books[book].Author : null;
        string? potion = metadata.PotionRecipeKey is int recipe ? definitions.Magic.PotionRecipes[recipe].Name : null;
        if (potion is not null && definition.Template?.Index != 278)
            potion = Internal(definitions, "potionOf").Replace("%po", potion, StringComparison.Ordinal);
        string? soul = metadata.CapturedSoulMobileId is int mobile
            ? DaggerfallInventoryPresentation.Label(definitions.Actors.Values.First(actor => actor.MobileId == mobile).Id.Value) : Internal(definitions, "Nothing");
        return new(ItemName: metadata.BookId is int bookId ? definitions.Books.Books[bookId].Title : name, BookAuthor: author, Amount: quantity.ToString(CultureInfo.InvariantCulture),
            Worth: checked((decimal)(value ?? definition.Value) * quantity).ToString(CultureInfo.InvariantCulture),
            Material: definition.Weapon is not null || definition.Armor is not null ? Internal(definitions, metadata.Material) : null,
            Condition: condition, Weight: weight.ToString(weight % 1 == 0 ? "F0" : "F2", CultureInfo.InvariantCulture),
            WeaponDamage: damage, ArmourModifier: armor is int protection ? Signed(protection) : null,
            Potion: potion, HeldSoul: soul);
    }

    internal static DaggerfallTextContext Book(DaggerfallBookDefinition book) => DaggerfallTextContext.Empty with
        { Item = new(ItemName: book.Title, BookAuthor: book.Author) };

    internal static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    internal static string Signed(int value) => value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
}
