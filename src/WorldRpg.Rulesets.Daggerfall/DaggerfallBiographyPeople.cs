using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The named people selected for one biography; editing and restoring do not reroll them.</summary>
internal sealed record DaggerfallBiographyPeopleSave(string RaceId, string Name, string FemaleName, string MaleName, string ImperialName);

internal static class DaggerfallBiographyPeople
{
    private static readonly string[] ImperialNames = ["Pelagius", "Cephorus", "Uriel", "Cassynder", "Voragiel", "Trabbatus"];

    internal static DaggerfallBiographyPeopleSave Roll(DaggerfallDefinitions definitions, string race, IRandomService random, int sequence)
    {
        int bank = race switch
        {
            "breton" => 0, "redguard" => 1, "nord" => 2, "dark-elf" => 3,
            "high-elf" => 4, "wood-elf" => 5, "khajiit" => 6, "argonian" => 7,
            _ => throw new ArgumentException($"Race '{race}' supplies no biography name bank."),
        };
        var table = definitions.Names.Banks.Single(value => value.Bank == bank);
        int Draw(string key, int maximum) => checked((int)random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed, "daggerfall.character-creation.v1", $"{sequence}.biography.{key}", 0, maximum)).Value);
        string Part(int set, string key)
        {
            var values = table.Sets.Single(value => value.Set == set).Keys;
            return string.Concat(definitions.Text.Require(values[Draw(key, values.Count - 1)]).TextRuns);
        }
        string Name(string scope, bool female)
        {
            if (table.Kind == DaggerfallNameBankKind.Redguard)
                return Part(0, scope + ".0") + Part(1, scope + ".1") + Part(2, scope + ".2")
                    + (female || Draw(scope + ".suffix-chance", 99) < 75 ? Part(female ? 4 : 3, scope + ".suffix") : "");
            string first = Part(female ? 2 : 0, scope + ".first.0") + Part(female ? 3 : 1, scope + ".first.1");
            string last = table.Kind == DaggerfallNameBankKind.Nord
                ? Part(0, scope + ".last.0") + Part(1, scope + ".last.1") + Localized(definitions, "nordSurnameImmutableSuffix")
                : Part(4, scope + ".last.0") + Part(5, scope + ".last.1");
            return first + " " + last;
        }
        return new(race, Name("name", false), Name("female", true), Name("male", false), ImperialNames[Draw("imperial", ImperialNames.Length - 1)]);
    }

    internal static void Validate(DaggerfallBiographyPeopleSave people, string race)
    {
        ArgumentNullException.ThrowIfNull(people);
        if (people.RaceId != race)
            throw new ArgumentException($"Biography names were chosen for '{people.RaceId}', while this character is '{race}'; choose names for the current race before applying its biography.");
        if (new[] { people.Name, people.FemaleName, people.MaleName }.Any(string.IsNullOrWhiteSpace)
            || !ImperialNames.Contains(people.ImperialName, StringComparer.Ordinal))
            throw new ArgumentException("Saved biography people must carry real selected names and one classic Imperial name.");
    }

    internal static (string Province, string Feature) Home(DaggerfallDefinitions definitions, string race)
    {
        var (province, feature) = race switch
        {
            "argonian" => ("blackMarsh", "swamps"), "breton" => ("highRock", "rollingHills"),
            "dark-elf" => ("morrowind", "mountains"), "high-elf" => ("sumurset", "shores"),
            "khajiit" => ("elsweyr", "desertLand"), "nord" => ("skyrim", "mountains"),
            "redguard" => ("hammerfell", "desertLand"), "wood-elf" => ("valenwood", "forests"),
            _ => throw new ArgumentException($"Race '{race}' supplies no biography homeland."),
        };
        return (Localized(definitions, province), Localized(definitions, feature));
    }

    private static string Localized(DaggerfallDefinitions definitions, string key) =>
        string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, key)).TextRuns);
}
