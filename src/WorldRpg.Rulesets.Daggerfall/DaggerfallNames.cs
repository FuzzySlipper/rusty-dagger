using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Donor name-bank composition over normalized fragments and the admitted random service.</summary>
internal sealed class DaggerfallNames(DaggerfallDefinitions definitions, IRandomService random, Func<string, int, int>? draw = null)
{
    internal string FirstName(int bank, bool female, string identity)
    {
        if (bank is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(bank));
        if (bank != 1) return Parts(bank, identity + ".first", female ? [2, 3] : [0, 1]);
        string first = Parts(bank, identity, [0, 1, 2]);
        return female || Draw(identity + ".suffix-chance", 100) < 75 ? first + Fragment(bank, female ? 4 : 3, identity + ".suffix") : first;
    }

    internal string Surname(int bank, string identity) => bank switch
    {
        1 => string.Empty,
        2 => Parts(bank, identity, [0, 1]) + Localized("nordSurnameImmutableSuffix"),
        >= 0 and <= 7 => Parts(bank, identity, [4, 5]),
        _ => throw new ArgumentOutOfRangeException(nameof(bank)),
    };

    internal string FullName(int bank, bool female, string identity)
    {
        string first = FirstName(bank, female, identity);
        string last = Surname(bank, identity + ".last");
        return last.Length > 0 ? first + " " + last : first;
    }

    internal string MonsterName(bool female, string identity)
    {
        int bank = 8 + Draw(identity + "/bank", 2);
        string a = Fragment(bank, 0, identity + "/part0");
        string b = Draw(identity + "/middle", 50) < 25 ? Fragment(bank, 1, identity + "/part1") : string.Empty;
        string c = Fragment(bank, 2, identity + "/part2");
        string d = bank == 9 && female ? Fragment(bank, 3, identity + "/part3") : string.Empty;
        return a + b + c + d;
    }

    internal string Residence(string identity, int region)
    {
        if (!definitions.BuildingNames.TryGetNameBank(region, out int bank))
            throw new NotSupportedException($"Quest residence region {region} has no published name bank.");
        string surname = Surname(bank, identity);
        if (surname.Length == 0) surname = FirstName(bank, false, identity); // Place.cs chooses male for the Redguard residence fallback.
        return Localized("theNamedResidence").Replace("%s", surname, StringComparison.Ordinal);
    }

    private string Parts(int bank, string identity, IReadOnlyList<int> sets) =>
        string.Concat(sets.Select((set, index) => Fragment(bank, set, identity + "." + index)));

    private string Fragment(int bank, int setIndex, string identity)
    {
        DaggerfallNameBankDefinition table = definitions.Names.Banks.SingleOrDefault(value => value.Bank == bank)
            ?? throw new NotSupportedException($"Name bank {bank} is not published.");
        DaggerfallNameSetDefinition set = table.Sets.SingleOrDefault(value => value.Set == setIndex)
            ?? throw new NotSupportedException($"Name bank {bank} fragment set {setIndex} is not published.");
        if (set.Keys.Count == 0) throw new NotSupportedException($"Name bank {bank} fragment set {setIndex} is empty.");
        return Text(set.Keys[Draw(identity, set.Keys.Count)]);
    }
    private int Draw(string key, int count) => draw is null
        ? checked((int)random.DrawKeyed(new(0, "daggerfall.names", key, 0, count - 1)).Value)
        : draw(key, count);
    private string Localized(string key) => Text(new(DaggerfallTextKind.Internal, key));
    private string Text(DaggerfallTextKey key) => string.Concat(definitions.Text.Require(key).TextRuns);
}
