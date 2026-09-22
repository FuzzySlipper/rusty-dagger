using System.Buffers;
using System.Text;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Values whose meaning belongs to the player rather than to a text record.</summary>
internal sealed record DaggerfallTextPlayerContext(
    string? Name = null, string? Race = null, string? FirstName = null, string? LastName = null,
    string? GuildTitle = null, string? Honorific = null, string? GoldCarried = null,
    string? DamageModifier = null, string? EncumbranceMaximum = null, string? Magicka = null,
    string? MagickaMaximum = null, string? MasteredSkill = null, string? MagicResistance = null,
    string? ToHitModifier = null, string? HitPointModifier = null, string? HealingRateModifier = null,
    string? Strength = null, string? Intelligence = null, string? Willpower = null,
    string? Agility = null, string? Endurance = null, string? Personality = null,
    string? Speed = null, string? Luck = null, string? AttributeRating = null,
    string? PlayerPronoun = null, string? PlayerObjectPronoun = null,
    string? PlayerReflexivePronoun = null, string? PlayerPossessiveAdjective = null,
    string? PlayerPossessivePronoun = null);

/// <summary>Calendar values formatted by the Daggerfall calendar owner for text presentation.</summary>
internal sealed record DaggerfallTextCalendarContext(
    string? Date = null, string? Time = null, string? DayNumber = null, string? DayName = null,
    string? DayWithSuffix = null, string? MonthNumber = null, string? MonthName = null,
    string? Year = null, string? Minute = null, string? Hour = null, string? Sign = null,
    string? Season = null);

/// <summary>Current-site and explicitly selected-place values used by global text.</summary>
internal sealed record DaggerfallTextLocationContext(
    string? City = null, string? AlternateCity = null, string? Region = null, string? CityType = null,
    string? Direction = null, string? Dungeon = null, string? LocalProvince = null, string? NearbyTavern = null,
    string? MarkedLocation = null, string? MapRevealedLocation = null, string? RegionalBuildingLocation = null,
    string? DialogSubject = null, string? DialogHint = null, string? AlternateDialogHint = null,
    string? CurrentBuilding = null, string? HomeProvince = null, string? HomeGeographicalFeature = null,
    string? HomeRegion = null, string? PotentialQuestorLocation = null, string? RoomHoursLeft = null);

/// <summary>Faction, court, and social values selected by the owning interaction.</summary>
internal sealed record DaggerfallTextFactionContext(
    string? NpcAlly = null, string? NpcEnemy = null, string? NpcFaction = null, string? FactionName = null,
    string? PlayerFaction = null, string? FactionOrder = null, string? NewsFactionOne = null,
    string? NewsFactionTwo = null, string? OldFactionLeader = null, string? FactionLeaderOne = null,
    string? FactionLeaderTwo = null, string? CurrentRegionLeader = null, string? FactionLeaderTitle = null,
    string? OldLeaderFate = null, string? RegionInContext = null, string? RegentName = null,
    string? RegentTitle = null, string? Crime = null, string? Penalty = null, string? GoldToPay = null,
    string? DaysInPrison = null, string? LegalReputation = null, string? CommonersReputation = null,
    string? MerchantsReputation = null, string? ScholarsReputation = null, string? NobilityReputation = null,
    string? UnderworldReputation = null, string? God = null, string? GodDescription = null,
    string? Daedra = null, string? Oath = null, string? VampireClan = null, string? NpcVampireClan = null);

/// <summary>Item, shop, book, painting, and effect values selected by a concrete caller.</summary>
internal sealed record DaggerfallTextItemContext(
    string? ItemName = null, string? BookAuthor = null,
    string? Amount = null, string? ShopName = null, string? MaximumLoan = null, string? Worth = null,
    string? Material = null, string? Condition = null, string? Weight = null, string? WeaponDamage = null,
    string? ArmourModifier = null, string? Potion = null, string? HeldSoul = null,
    string? PaintingAdjective = null, string? ArtistName = null, string? PaintingPrefixOne = null,
    string? PaintingPrefixTwo = null, string? PaintingSubject = null, string? MagicPowers = null,
    string? DurationBase = null, string? DurationPlus = null, string? DurationPerLevel = null,
    string? ChanceBase = null, string? ChancePlus = null, string? ChancePerLevel = null,
    string? MagnitudeBaseMinimum = null, string? MagnitudeBaseMaximum = null,
    string? MagnitudePlusMinimum = null, string? MagnitudePlusMaximum = null,
    string? MagnitudePerLevel = null);

/// <summary>Biography and talk values whose selected record is explicit at the presentation call.</summary>
internal sealed record DaggerfallTextStoryContext(
    string? Name = null, string? FemaleName = null, string? AlternateFemaleName = null, string? MaleName = null,
    string? AlternateMaleName = null, string? Surname = null, string? ImperialName = null,
    string? GreetingOrFollowUp = null, string? Joke = null, string? PotentialQuestorName = null,
    string? Pronoun = null, string? ObjectPronoun = null, string? PossessiveAdjective = null,
    string? QuestionOne = null, string? QuestionTwo = null, string? QuestionThree = null,
    string? QuestionFour = null, string? QuestionFive = null, string? QuestionSix = null,
    string? QuestionSeven = null, string? QuestionEight = null, string? QuestionNine = null,
    string? QuestionTen = null, string? QuestionEleven = null, string? QuestionTwelve = null,
    string? QuestionOneA = null, string? QuestionTwoA = null, string? QuestionThreeA = null,
    string? QuestionFourA = null, string? QuestionFiveA = null, string? QuestionSixA = null,
    string? QuestionSevenA = null, string? QuestionEightA = null, string? QuestionNineA = null,
    string? QuestionTenA = null, string? QuestionElevenA = null, string? QuestionTwelveA = null,
    string? QuestionOneB = null, string? QuestionTwoB = null, string? QuestionThreeB = null,
    string? QuestionFourB = null, string? QuestionFiveB = null, string? QuestionSixB = null,
    string? QuestionSevenB = null, string? QuestionEightB = null, string? QuestionNineB = null,
    string? QuestionTenB = null, string? QuestionElevenB = null, string? QuestionTwelveB = null,
    string? QuestDate = null);

/// <summary>All global text context is explicit. Quest bindings intentionally layer on this type later.</summary>
internal sealed record DaggerfallTextContext(
    DaggerfallTextPlayerContext Player,
    DaggerfallTextCalendarContext Calendar,
    DaggerfallTextLocationContext Location,
    DaggerfallTextFactionContext Faction,
    DaggerfallTextItemContext Item,
    DaggerfallTextStoryContext Story)
{
    /// <summary>For records that carry no global symbols; macro-bearing records still diagnose absent values.</summary>
    internal static DaggerfallTextContext Empty { get; } = new(new(), new(), new(), new(), new(), new());
}

internal enum DaggerfallTextDiagnosticKind { MissingText, MalformedText, MissingContext, DonorUnresolvedMacro, UnrecognisedMacro, UnknownLayoutToken }
/// <summary>A deliberate diagnostic; rendering retains its marker so story text is never silently erased.</summary>
internal sealed record DaggerfallTextDiagnostic(DaggerfallTextDiagnosticKind Kind, DaggerfallTextKey Key, string Detail);
internal sealed record DaggerfallTextRenderResult(string Text, IReadOnlyList<DaggerfallTextDiagnostic> Diagnostics) { internal bool IsComplete => Diagnostics.Count == 0; }

/// <summary>Resolves normalized base-language values for thin UI presentation with one explicit context.</summary>
internal sealed class DaggerfallTextResolver(DaggerfallTextSet text)
{
    private static readonly SearchValues<char> MacroTerminators = SearchValues.Create(" %.,'?!/(){}[]\";:|".AsSpan());

    internal DaggerfallTextRenderResult Resolve(DaggerfallTextKey key, DaggerfallTextContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        List<DaggerfallTextDiagnostic> diagnostics = [];
        if (text.Resolve(key, out DaggerfallTextValue? value) is DaggerfallTextResolution.Missing)
            return new(string.Empty, [new(DaggerfallTextDiagnosticKind.MissingText, key, $"The normalized pack has no text value for '{key}'.")]);
        if (value!.State == DaggerfallTextState.Malformed)
            return new(string.Empty, [new(DaggerfallTextDiagnosticKind.MalformedText, key, value.Reason)]);
        StringBuilder output = new();
        foreach (DaggerfallTextElement token in value.Tokens)
        {
            if (token.Code == DaggerfallTextCode.Text) Expand(output, token.Text!, key, context, diagnostics);
            else if (token.Code is DaggerfallTextCode.NewLineOffset or DaggerfallTextCode.EndOfPage or DaggerfallTextCode.SubrecordSeparator) output.Append('\n');
            else if (token.Code is not (DaggerfallTextCode.SameLineOffset or DaggerfallTextCode.PullPreceeding or DaggerfallTextCode.InputCursorPositioner or DaggerfallTextCode.FontPrefix or DaggerfallTextCode.PositionPrefix or DaggerfallTextCode.JustifyLeft or DaggerfallTextCode.JustifyCenter))
                diagnostics.Add(new(DaggerfallTextDiagnosticKind.UnknownLayoutToken, key, $"The normalized text value carries unsupported layout token '{token.Code}'."));
        }
        return new(output.ToString(), diagnostics);
    }

    private static void Expand(StringBuilder output, string source, DaggerfallTextKey key, DaggerfallTextContext context, List<DaggerfallTextDiagnostic> diagnostics)
    {
        Dictionary<string, string> cache = new(StringComparer.Ordinal);
        for (int position = 0; position < source.Length;)
        {
            int marker = source.IndexOf('%', position);
            if (marker < 0) { output.Append(source, position, source.Length - position); return; }
            output.Append(source, position, marker - position);
            // The normalized presentation spelling uses a doubled marker for a literal percent. It
            // prevents the following text from being read as an accidental symbol and remains plain
            // text for the DOM; the donor's single-marker handler is still accepted below.
            if (marker + 1 < source.Length && source[marker + 1] == '%')
            {
                output.Append('%');
                position = marker + 2;
                continue;
            }
            int end = marker + 1;
            while (end < source.Length && !MacroTerminators.Contains(source[end])) end++;
            string symbol = source[marker..end];
            if (!cache.TryGetValue(symbol, out string? value)) { value = ResolveMacro(symbol, key, context, diagnostics); cache.Add(symbol, value); }
            output.Append(value);
            position = end < source.Length && source[end] == '|' ? end + 1 : end;
        }
    }

    private static string ResolveMacro(string symbol, DaggerfallTextKey key, DaggerfallTextContext c, List<DaggerfallTextDiagnostic> diagnostics)
    {
        string? value = symbol switch
        {
            "%" => "%",
            "%pcn" => c.Player.Name, "%pcf" => c.Player.FirstName ?? FirstName(c.Player.Name), "%pcl" => c.Player.LastName ?? LastName(c.Player.Name), "%ra" => c.Player.Race, "%pct" or "%lev" => c.Player.GuildTitle, "%hnr" => c.Player.Honorific, "%gii" => c.Player.GoldCarried, "%dam" => c.Player.DamageModifier, "%enc" => c.Player.EncumbranceMaximum, "%spc" => c.Player.Magicka, "%spt" => c.Player.MagickaMaximum, "%ski" => c.Player.MasteredSkill, "%mad" => c.Player.MagicResistance, "%thd" => c.Player.ToHitModifier, "%hea" => c.Player.HitPointModifier, "%hmd" => c.Player.HealingRateModifier, "%str" => c.Player.Strength, "%int" => c.Player.Intelligence, "%wil" => c.Player.Willpower, "%agi" => c.Player.Agility, "%end" => c.Player.Endurance, "%per" => c.Player.Personality, "%spd" => c.Player.Speed, "%luc" => c.Player.Luck, "%ark" => c.Player.AttributeRating, "%pg" or "%pg1" => c.Player.PlayerPronoun, "%pg2" => c.Player.PlayerObjectPronoun, "%pg2self" => c.Player.PlayerReflexivePronoun, "%pg3" => c.Player.PlayerPossessiveAdjective, "%pg4" => c.Player.PlayerPossessivePronoun,
            "%dat" => c.Calendar.Date, "%tim" => c.Calendar.Time, "%day" => c.Calendar.DayNumber, "%dayn" => c.Calendar.DayName, "%days" => c.Calendar.DayWithSuffix, "%mon" => c.Calendar.MonthNumber, "%monn" => c.Calendar.MonthName, "%year" => c.Calendar.Year, "%min" => c.Calendar.Minute, "%hour" => c.Calendar.Hour, "%sign" => c.Calendar.Sign, "%sea" => c.Calendar.Season,
            "%cn" => c.Location.City, "%cn2" => c.Location.AlternateCity, "%crn" => c.Location.Region, "%ct" => c.Location.CityType, "%di" => c.Location.Direction, "%dng" => c.Location.Dungeon, "%lp" => c.Location.LocalProvince, "%nt" => c.Location.NearbyTavern, "%loc" => c.Location.MarkedLocation, "%map" => c.Location.MapRevealedLocation, "%fcn" => c.Location.RegionalBuildingLocation, "%key" => c.Location.DialogSubject, "%hnt" => c.Location.DialogHint, "%hnt2" => c.Location.AlternateDialogHint, "%cbd" => c.Location.CurrentBuilding, "%hpn" => c.Location.HomeProvince, "%hpw" => c.Location.HomeGeographicalFeature, "%hrn" => c.Location.HomeRegion, "%pqp" => c.Location.PotentialQuestorLocation, "%dwr" => c.Location.RoomHoursLeft,
            "%fa" or "%fea" => c.Faction.NpcAlly, "%fe" or "%fae" => c.Faction.NpcEnemy, "%fnpc" => c.Faction.NpcFaction, "%fpa" => c.Faction.FactionName, "%fpc" => c.Faction.PlayerFaction, "%fon" or "%kno" => c.Faction.FactionOrder, "%fx1" => c.Faction.NewsFactionOne, "%fx2" => c.Faction.NewsFactionTwo, "%ol1" => c.Faction.OldFactionLeader, "%fl1" => c.Faction.FactionLeaderOne, "%fl2" => c.Faction.FactionLeaderTwo, "%nrn" => c.Faction.CurrentRegionLeader, "%lt1" => c.Faction.FactionLeaderTitle, "%olf" => c.Faction.OldLeaderFate, "%reg" => c.Faction.RegionInContext, "%rn" => c.Faction.RegentName, "%rt" or "%t" => c.Faction.RegentTitle, "%cri" => c.Faction.Crime, "%pen" => c.Faction.Penalty, "%gtp" => c.Faction.GoldToPay, "%dip" => c.Faction.DaysInPrison, "%ltn" => c.Faction.LegalReputation, "%r1" => c.Faction.CommonersReputation, "%r2" => c.Faction.MerchantsReputation, "%r3" => c.Faction.ScholarsReputation, "%r4" => c.Faction.NobilityReputation, "%r5" => c.Faction.UnderworldReputation, "%god" => c.Faction.God, "%gdd" => c.Faction.GodDescription, "%dae" => c.Faction.Daedra, "%oth" => c.Faction.Oath, "%vam" => c.Faction.VampireClan, "%vcn" => c.Faction.NpcVampireClan,
            "%it" or "%arm" or "%wep" or "%bt" => c.Item.ItemName, "%ba" => c.Item.BookAuthor, "%a" => c.Item.Amount, "%cpn" => c.Item.ShopName, "%ml" => c.Item.MaximumLoan, "%wth" => c.Item.Worth, "%mat" => c.Item.Material, "%qua" => c.Item.Condition, "%kg" => c.Item.Weight, "%wdm" => c.Item.WeaponDamage, "%mod" => c.Item.ArmourModifier, "%po" => c.Item.Potion, "%hs" => c.Item.HeldSoul, "%adj" => c.Item.PaintingAdjective, "%an" => c.Item.ArtistName, "%pp1" => c.Item.PaintingPrefixOne, "%pp2" => c.Item.PaintingPrefixTwo, "%sub" => c.Item.PaintingSubject, "%mpw" => c.Item.MagicPowers, "%bdr" => c.Item.DurationBase, "%adr" => c.Item.DurationPlus, "%cld" => c.Item.DurationPerLevel, "%bch" => c.Item.ChanceBase, "%ach" => c.Item.ChancePlus, "%clc" => c.Item.ChancePerLevel, "%1bm" => c.Item.MagnitudeBaseMinimum, "%2bm" => c.Item.MagnitudeBaseMaximum, "%1am" => c.Item.MagnitudePlusMinimum, "%2am" => c.Item.MagnitudePlusMaximum, "%clm" => c.Item.MagnitudePerLevel,
            "%n" or "%nam" or "%bn" => c.Story.Name, "%fn" => c.Story.FemaleName, "%fn2" => c.Story.AlternateFemaleName, "%mn" => c.Story.MaleName, "%mn2" => c.Story.AlternateMaleName, "%ln" => c.Story.Surname, "%imp" => c.Story.ImperialName, "%1com" => c.Story.GreetingOrFollowUp, "%jok" => c.Story.Joke, "%pqn" => c.Story.PotentialQuestorName, "%g" => c.Story.Pronoun, "%g2" => c.Story.ObjectPronoun, "%g3" => c.Story.PossessiveAdjective, "%qdt" => c.Story.QuestDate,
            "%q1" => c.Story.QuestionOne, "%q2" => c.Story.QuestionTwo, "%q3" => c.Story.QuestionThree, "%q4" => c.Story.QuestionFour, "%q5" => c.Story.QuestionFive, "%q6" => c.Story.QuestionSix, "%q7" => c.Story.QuestionSeven, "%q8" => c.Story.QuestionEight, "%q9" => c.Story.QuestionNine, "%q10" => c.Story.QuestionTen, "%q11" => c.Story.QuestionEleven, "%q12" => c.Story.QuestionTwelve,
            "%q1a" => c.Story.QuestionOneA, "%q2a" => c.Story.QuestionTwoA, "%q3a" => c.Story.QuestionThreeA, "%q4a" => c.Story.QuestionFourA, "%q5a" => c.Story.QuestionFiveA, "%q6a" => c.Story.QuestionSixA, "%q7a" => c.Story.QuestionSevenA, "%q8a" => c.Story.QuestionEightA, "%q9a" => c.Story.QuestionNineA, "%q10a" => c.Story.QuestionTenA, "%q11a" => c.Story.QuestionElevenA, "%q12a" => c.Story.QuestionTwelveA,
            "%q1b" => c.Story.QuestionOneB, "%q2b" => c.Story.QuestionTwoB, "%q3b" => c.Story.QuestionThreeB, "%q4b" => c.Story.QuestionFourB, "%q5b" => c.Story.QuestionFiveB, "%q6b" => c.Story.QuestionSixB, "%q7b" => c.Story.QuestionSevenB, "%q8b" => c.Story.QuestionEightB, "%q9b" => c.Story.QuestionNineB, "%q10b" => c.Story.QuestionTenB, "%q11b" => c.Story.QuestionElevenB, "%q12b" => c.Story.QuestionTwelveB,
            _ => null,
        };
        if (value is not null) return value;
        if (DonorUnresolved.Contains(symbol)) { diagnostics.Add(new(DaggerfallTextDiagnosticKind.DonorUnresolvedMacro, key, symbol)); return $"{symbol}[donor-unresolved]"; }
        if (KnownGlobal.Contains(symbol)) { diagnostics.Add(new(DaggerfallTextDiagnosticKind.MissingContext, key, symbol)); return $"{symbol}[missing-context]"; }
        diagnostics.Add(new(DaggerfallTextDiagnosticKind.UnrecognisedMacro, key, symbol));
        return $"{symbol}[unrecognised]";
    }

    private static string? FirstName(string? name) => SplitName(name, first: true);
    private static string? LastName(string? name) => SplitName(name, first: false);
    private static string? SplitName(string? name, bool first)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : first ? parts[0] : parts.Length > 1 ? parts[1] : parts[0];
    }

    private static readonly HashSet<string> DonorUnresolved = new(StringComparer.Ordinal) { "%1hn", "%2hn", "%3hn", "%cbl", "%dts", "%ef", "%hol", "%hrg", "%htwn", "%key2", "%mit", "%on", "%pdg", "%plq", "%pnq", "%ptm", "%qot", "%tcn", "%vn", "%wpn" };
    // The normalized corpus's retained global catalog, not an extension mechanism.
    private static readonly HashSet<string> KnownGlobal = new(StringComparer.Ordinal) { "%", "%1am", "%1bm", "%1com", "%2am", "%2bm", "%a", "%ach", "%adj", "%adr", "%agi", "%an", "%ark", "%arm", "%ba", "%bch", "%bdr", "%bn", "%bt", "%clc", "%cld", "%clm", "%cn", "%cn2", "%cpn", "%cri", "%crn", "%ct", "%dae", "%dam", "%dat", "%di", "%dip", "%dng", "%dwr", "%enc", "%end", "%fa", "%fae", "%fcn", "%fe", "%fea", "%fl1", "%fl2", "%fn", "%fn2", "%fnpc", "%fon", "%fpa", "%fpc", "%fx1", "%fx2", "%g", "%g2", "%g3", "%gdd", "%gii", "%god", "%gtp", "%hea", "%hmd", "%hnr", "%hnt", "%hnt2", "%hpn", "%hpw", "%hs", "%imp", "%int", "%it", "%jok", "%key", "%kg", "%lev", "%ln", "%loc", "%lp", "%lt1", "%ltn", "%luc", "%mad", "%map", "%mat", "%ml", "%mn", "%mn2", "%mod", "%mpw", "%n", "%nrn", "%nt", "%ol1", "%olf", "%oth", "%pcf", "%pcn", "%pct", "%pen", "%per", "%po", "%pp1", "%pp2", "%pqn", "%pqp", "%q1", "%q10", "%q12", "%q1a", "%q1b", "%q2", "%q2a", "%q3", "%q3a", "%q3b", "%q4", "%q5", "%q6", "%q8", "%q9", "%qdt", "%qua", "%r1", "%r2", "%r3", "%r4", "%r5", "%ra", "%reg", "%rn", "%ski", "%spc", "%spd", "%spt", "%str", "%sub", "%t", "%thd", "%tim", "%wdm", "%wep", "%wil", "%wth" };
}
