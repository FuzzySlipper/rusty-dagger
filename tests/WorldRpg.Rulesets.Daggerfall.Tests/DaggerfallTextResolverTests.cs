using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTextResolverTests
{
    [Fact]
    public void Resolves_the_same_normalized_value_against_distinct_explicit_global_contexts()
    {
        DaggerfallTextResolver resolver = Resolver("Hello %pcn in %cn on %dat: %fpa offers %it.");

        DaggerfallTextRenderResult first = resolver.Resolve(Key, Context("Nulfaga", "Daggerfall", "3rd Sun's Dawn", "The Mages Guild", "an ebony dagger"));
        DaggerfallTextRenderResult second = resolver.Resolve(Key, Context("Morgiah", "Wayrest", "4th Sun's Dawn", "The Blades", "a map"));

        Assert.Equal("Hello Nulfaga in Daggerfall on 3rd Sun's Dawn: The Mages Guild offers an ebony dagger.", first.Text);
        Assert.Equal("Hello Morgiah in Wayrest on 4th Sun's Dawn: The Blades offers a map.", second.Text);
        Assert.True(first.IsComplete);
        Assert.True(second.IsComplete);
    }

    [Fact]
    public void Covers_every_retained_handled_symbol_in_the_published_macro_inventory()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(PackPath()));
        string[] symbols = definitions.Text.Macros.Where(macro => macro.Disposition == DaggerfallTextMacroDisposition.Handled).Select(macro => macro.Symbol).ToArray();
        DaggerfallTextResolver resolver = Resolver(string.Join(' ', symbols));

        DaggerfallTextRenderResult rendered = resolver.Resolve(Key, CompleteContext());

        Assert.Equal(141, symbols.Length);
        Assert.True(rendered.IsComplete, string.Join(", ", rendered.Diagnostics.Select(diagnostic => diagnostic.Detail)));
        Assert.DoesNotContain("[missing-context]", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_literal_text_and_layout_without_granting_markup_or_symbol_authority()
    {
        DaggerfallTextSet text = TextSet("<ce>%pcn|s 100%% ready", [
            new(DaggerfallTextCode.Text, "<ce>%pcn|s 100%% ready", null, null),
            new(DaggerfallTextCode.FontPrefix, null, 1, null),
            new(DaggerfallTextCode.NewLineOffset, null, null, null),
            new(DaggerfallTextCode.JustifyCenter, null, null, null),
            new(DaggerfallTextCode.Text, "<tag>", null, null),
            new(DaggerfallTextCode.EndOfPage, null, null, null),
            new(DaggerfallTextCode.Text, "next", null, null),
        ]);

        DaggerfallTextRenderResult rendered = new DaggerfallTextResolver(text).Resolve(Key, Context("Nulfaga", "Daggerfall", "date", "faction", "item"));

        // The donor consumes the pipe terminator for postfix composition: %pcn|s is Nulfagas.
        Assert.Equal("<ce>Nulfagas 100% ready\n<tag>\nnext", rendered.Text);
        Assert.True(rendered.IsComplete);
    }

    [Fact]
    public void Diagnoses_missing_records_context_and_donor_unresolved_symbols_without_erasing_text()
    {
        DaggerfallTextResolver resolver = Resolver("%pcn, %hol, and %notasymbol");
        DaggerfallTextRenderResult missingRecord = resolver.Resolve(new(DaggerfallTextKind.Resource, "absent"), CompleteContext());
        DaggerfallTextRenderResult rendered = resolver.Resolve(Key, new(new(), new(), new(), new(), new(), new()));
        DaggerfallTextSet malformed = new(new Dictionary<DaggerfallTextKey, DaggerfallTextValue>
        {
            [Key] = new(Key, "test", "en", 0, 0, 0, 0, DaggerfallTextState.Malformed, "source bytes are truncated", [], []),
        }, [], []);
        DaggerfallTextRenderResult malformedResult = new DaggerfallTextResolver(malformed).Resolve(Key, CompleteContext());

        Assert.Equal(DaggerfallTextDiagnosticKind.MissingText, Assert.Single(missingRecord.Diagnostics).Kind);
        Assert.Equal(DaggerfallTextDiagnosticKind.MalformedText, Assert.Single(malformedResult.Diagnostics).Kind);
        Assert.Equal("source bytes are truncated", malformedResult.Diagnostics[0].Detail);
        Assert.Equal("%pcn[missing-context], %hol[donor-unresolved], and %notasymbol[unrecognised]", rendered.Text);
        Assert.Equal([DaggerfallTextDiagnosticKind.MissingContext, DaggerfallTextDiagnosticKind.DonorUnresolvedMacro, DaggerfallTextDiagnosticKind.UnrecognisedMacro], rendered.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void Uses_the_donor_terminator_grammar_so_longer_symbols_do_not_collide()
    {
        DaggerfallTextRenderResult rendered = Resolver("%q1 %q10 %pcn,%it!").Resolve(Key, CompleteContext());

        Assert.Equal("one ten Player,item!", rendered.Text);
        Assert.True(rendered.IsComplete);
    }

    [Fact]
    public void Derives_player_name_parts_with_the_donor_name_rule_when_no_override_is_selected()
    {
        DaggerfallTextRenderResult rendered = Resolver("%pcf %pcl").Resolve(Key, new(new(Name: "Barenziah Ravenwatch"), new(), new(), new(), new(), new()));

        Assert.Equal("Barenziah Ravenwatch", rendered.Text);
        Assert.True(rendered.IsComplete);
    }

    private static readonly DaggerfallTextKey Key = new(DaggerfallTextKind.Resource, "test");
    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "content", "worldrpg"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Rusty Dagger repository root.");
    }
    private static DaggerfallTextResolver Resolver(string source) => new(TextSet(source, [new(DaggerfallTextCode.Text, source, null, null)]));
    private static DaggerfallTextSet TextSet(string source, IReadOnlyList<DaggerfallTextElement> tokens) => new(
        new Dictionary<DaggerfallTextKey, DaggerfallTextValue> { [Key] = new(Key, "test", "en", 0, 0, source.Length, 1, DaggerfallTextState.Read, string.Empty, [], tokens) }, [], []);

    private static DaggerfallTextContext Context(string player, string city, string date, string faction, string item) => new(
        new(Name: player, FirstName: player, LastName: player), new(Date: date), new(City: city), new(FactionName: faction), new(ItemName: item), new());

    private static DaggerfallTextContext CompleteContext() => new(
        new(Name: "Player", Race: "Race", FirstName: "First", LastName: "Last", GuildTitle: "Title", Honorific: "Honorific", GoldCarried: "Gold", DamageModifier: "Damage", EncumbranceMaximum: "Encumbrance", Magicka: "Magicka", MagickaMaximum: "MagickaMax", MasteredSkill: "Skill", MagicResistance: "Resistance", ToHitModifier: "ToHit", HitPointModifier: "Health", HealingRateModifier: "Healing", Strength: "Strength", Intelligence: "Intelligence", Willpower: "Willpower", Agility: "Agility", Endurance: "Endurance", Personality: "Personality", Speed: "Speed", Luck: "Luck", AttributeRating: "Rating", PlayerPronoun: "she", PlayerObjectPronoun: "her", PlayerReflexivePronoun: "herself", PlayerPossessiveAdjective: "her", PlayerPossessivePronoun: "hers"),
        new(Date: "Date", Time: "Time", DayNumber: "1", DayName: "Morndas", DayWithSuffix: "1st", MonthNumber: "1", MonthName: "Morning Star", Year: "405", Minute: "1", Hour: "2", Sign: "The Lady", Season: "Spring"),
        new(City: "City", AlternateCity: "Other City", Region: "Region", CityType: "Town", Direction: "north", Dungeon: "Dungeon", LocalProvince: "Province", NearbyTavern: "Tavern", MarkedLocation: "Marked", MapRevealedLocation: "Map", RegionalBuildingLocation: "Regional", DialogSubject: "Subject", DialogHint: "Hint", AlternateDialogHint: "Hint2", CurrentBuilding: "Building", HomeProvince: "Home Province", HomeGeographicalFeature: "Feature", HomeRegion: "Home Region", PotentialQuestorLocation: "Questor Place", RoomHoursLeft: "3"),
        new(NpcAlly: "Ally", NpcEnemy: "Enemy", NpcFaction: "NPC Faction", FactionName: "Faction", PlayerFaction: "Player Faction", FactionOrder: "Order", NewsFactionOne: "News One", NewsFactionTwo: "News Two", OldFactionLeader: "Old Leader", FactionLeaderOne: "Leader One", FactionLeaderTwo: "Leader Two", CurrentRegionLeader: "Region Leader", FactionLeaderTitle: "Leader Title", OldLeaderFate: "Fate", RegionInContext: "Context Region", RegentName: "Regent", RegentTitle: "King", Crime: "Crime", Penalty: "Penalty", GoldToPay: "Fine", DaysInPrison: "Days", LegalReputation: "Legal", CommonersReputation: "Commoners", MerchantsReputation: "Merchants", ScholarsReputation: "Scholars", NobilityReputation: "Nobility", UnderworldReputation: "Underworld", God: "God", GodDescription: "God Desc", Daedra: "Daedra", Oath: "Oath", VampireClan: "Vampire", NpcVampireClan: "NPC Vampire"),
        new(ItemName: "item", BookAuthor: "Author", Amount: "Amount", ShopName: "Shop", MaximumLoan: "Loan", Worth: "Worth", Material: "Material", Condition: "Condition", Weight: "Weight", WeaponDamage: "Damage", ArmourModifier: "Armor", Potion: "Potion", HeldSoul: "Soul", PaintingAdjective: "Adjective", ArtistName: "Artist", PaintingPrefixOne: "Prefix One", PaintingPrefixTwo: "Prefix Two", PaintingSubject: "Subject", MagicPowers: "Powers", DurationBase: "Duration Base", DurationPlus: "Duration Plus", DurationPerLevel: "Duration Level", ChanceBase: "Chance Base", ChancePlus: "Chance Plus", ChancePerLevel: "Chance Level", MagnitudeBaseMinimum: "Magnitude Base Min", MagnitudeBaseMaximum: "Magnitude Base Max", MagnitudePlusMinimum: "Magnitude Plus Min", MagnitudePlusMaximum: "Magnitude Plus Max", MagnitudePerLevel: "Magnitude Level"),
        new(Name: "Name", FemaleName: "Female", AlternateFemaleName: "Female2", MaleName: "Male", AlternateMaleName: "Male2", Surname: "Surname", ImperialName: "Imperial", GreetingOrFollowUp: "Greeting", Joke: "Joke", PotentialQuestorName: "Questor", Pronoun: "they", ObjectPronoun: "them", PossessiveAdjective: "their", QuestionOne: "one", QuestionTwo: "two", QuestionThree: "three", QuestionFour: "four", QuestionFive: "five", QuestionSix: "six", QuestionSeven: "seven", QuestionEight: "eight", QuestionNine: "nine", QuestionTen: "ten", QuestionEleven: "eleven", QuestionTwelve: "twelve", QuestionOneA: "one A", QuestionTwoA: "two A", QuestionThreeA: "three A", QuestionFourA: "four A", QuestionFiveA: "five A", QuestionSixA: "six A", QuestionSevenA: "seven A", QuestionEightA: "eight A", QuestionNineA: "nine A", QuestionTenA: "ten A", QuestionElevenA: "eleven A", QuestionTwelveA: "twelve A", QuestionOneB: "one B", QuestionTwoB: "two B", QuestionThreeB: "three B", QuestionFourB: "four B", QuestionFiveB: "five B", QuestionSixB: "six B", QuestionSevenB: "seven B", QuestionEightB: "eight B", QuestionNineB: "nine B", QuestionTenB: "ten B", QuestionElevenB: "eleven B", QuestionTwelveB: "twelve B", QuestDate: "Quest Date"));
}
