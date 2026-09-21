using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a name bank composes its sets, in the donor's documented patterns.</summary>
public enum DaggerfallNameBankKind
{
    /// <summary>Male first from sets 0+1, female first from 2+3, surnames from 4+5.</summary>
    Standard,
    /// <summary>One name from sets 0+1+2, plus set 3 for most males and all females.</summary>
    Redguard,
    /// <summary>First names as standard; surnames from sets 0+1 plus the localized suffix.</summary>
    Nord,
    /// <summary>Quest monster names from the donor's monster patterns.</summary>
    Monster,
}

/// <summary>One fragment set with the text keys its parts resolve through.</summary>
/// <param name="Set">The set's position in its bank.</param>
/// <param name="Parts">The set's fragments, in file order.</param>
/// <param name="Keys">The text keys per fragment, in the same order, in the pack's key spelling.</param>
public sealed record DaggerfallNameSet(int Set, IReadOnlyList<string> Parts, IReadOnlyList<string> Keys)
{
    public void Validate()
    {
        if (Set < 0 || Set >= NameGenReader.SetsPerBank)
        {
            throw new ArgumentOutOfRangeException(nameof(Set), Set, "A published name set names a set slot the table does not declare.");
        }

        if (Parts.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Parts), Parts.Count, "A published name set carries no fragments.");
        }

        if (!Parts.Count.Equals(Keys.Count))
        {
            throw new InvalidOperationException($"Name set {Set} carries {Parts.Count} fragments for {Keys.Count} text keys.");
        }

        foreach (string key in Keys)
        {
            NormalizedImportDocument.RequireLogicalId(key, nameof(Keys));
        }
    }
}

/// <summary>One name bank: its donor identity, its composition, and its present sets.</summary>
/// <param name="Bank">The bank's position in the file.</param>
/// <param name="Name">The bank's donor name, in the donor's bank order.</param>
/// <param name="Kind">How the bank composes its sets.</param>
/// <param name="Sets">The bank's present sets, in file order.</param>
public sealed record DaggerfallNameBank(int Bank, string Name, DaggerfallNameBankKind Kind, IReadOnlyList<DaggerfallNameSet> Sets)
{
    public void Validate()
    {
        if (Bank < 0 || Bank >= NameGenReader.BankCount)
        {
            throw new ArgumentOutOfRangeException(nameof(Bank), Bank, "A published name bank names a bank the table does not declare.");
        }

        NormalizedImportDocument.RequireLogicalId(Name, nameof(Name));
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A published name bank names a composition the contract does not declare.");
        }

        if (Sets.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Sets), Sets.Count, $"Name bank '{Name}' carries no sets.");
        }

        NormalizedImportDocument.ValidateUnique(Sets, set => set.Set.ToString(), $"name sets of bank '{Name}'");
        foreach (DaggerfallNameSet set in Sets)
        {
            set.Validate();
        }
    }
}

/// <summary>The normalized name tables: every bank the source states, with text keys per fragment.</summary>
/// <param name="Source">The source the tables were read from.</param>
/// <param name="Banks">The banks, in file order.</param>
public sealed record DaggerfallNameTables(DaggerfallTextSource Source, IReadOnlyList<DaggerfallNameBank> Banks)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Source.Kind != DaggerfallTextKind.Name)
        {
            throw new InvalidOperationException($"Name tables cite source '{Source.Path}' of family '{Source.Kind}'.");
        }

        if (Banks.Count != NameGenReader.BankCount)
        {
            throw new InvalidOperationException($"Name tables carry {Banks.Count} banks for a {NameGenReader.BankCount}-bank table.");
        }

        NormalizedImportDocument.ValidateUnique(Banks, bank => bank.Bank.ToString(), "name banks");
        int fragments = 0;
        foreach (DaggerfallNameBank bank in Banks)
        {
            bank.Validate();
            fragments += bank.Sets.Sum(set => set.Parts.Count);
        }

        if (fragments != Source.Records)
        {
            throw new InvalidOperationException($"Name source '{Source.Path}' declares {Source.Records} records and publishes {fragments}.");
        }
    }
}

/// <summary>
/// Builds the normalized name tables from the classic name-generation file. Text values join the
/// shared text section through the returned records; the tables keep the bank, set and fragment
/// identities those values resolve through, plus the composition each bank kind documents.
/// </summary>
public static class DaggerfallNameTablesBuilder
{
    public const string FamilyId = "CNT-014";

    /// <summary>The donor's bank names in file order, byte-verified against the donor's own database.</summary>
    public static readonly IReadOnlyList<string> BankNames =
        ["Breton", "Redguard", "Nord", "DarkElf", "HighElf", "WoodElf", "Khajiit", "Imperial", "Monster1", "Monster2", "Monster3"];

    /// <summary>The composition per bank, in the donor's documented patterns.</summary>
    public static readonly IReadOnlyList<DaggerfallNameBankKind> BankKinds =
    [
        DaggerfallNameBankKind.Standard,
        DaggerfallNameBankKind.Redguard,
        DaggerfallNameBankKind.Nord,
        DaggerfallNameBankKind.Standard,
        DaggerfallNameBankKind.Standard,
        DaggerfallNameBankKind.Standard,
        DaggerfallNameBankKind.Standard,
        DaggerfallNameBankKind.Standard,
        DaggerfallNameBankKind.Monster,
        DaggerfallNameBankKind.Monster,
        DaggerfallNameBankKind.Monster,
    ];

    /// <summary>Builds the tables and the text records their fragments resolve through.</summary>
    public static (DaggerfallNameTables Tables, IReadOnlyList<DaggerfallTextRecord> Records) Build(
        ReadOnlySpan<byte> bytes,
        string label,
        IReadOnlyList<SourceInventoryRow> inventory,
        string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        string recordId = RequireFile(inventory, label);
        NameGenCatalog catalog = NameGenReader.Read(bytes, label);
        List<DaggerfallNameBank> banks = [];
        List<DaggerfallTextRecord> records = [];
        int ordinal = 0;
        foreach (NameGenBank bank in catalog.Banks)
        {
            List<DaggerfallNameSet> sets = [];
            foreach (NameGenSet? set in bank.Sets)
            {
                if (set is null)
                {
                    continue;
                }

                List<string> keys = [];
                foreach ((string part, int index) in set.Parts.Select((part, index) => (part, index)))
                {
                    string id = $"{bank.Bank:D2}-{set.Set}-{index:D2}";
                    string key = new DaggerfallTextKey(DaggerfallTextKind.Name, id).ToString();
                    keys.Add(key);
                    records.Add(FamilyTextRecords.Plain(
                        DaggerfallTextKind.Name, id, label, ordinal, set.Offset + index * NameGenReader.FieldBytes, NameGenReader.FieldBytes, part));
                    ordinal++;
                }

                sets.Add(new DaggerfallNameSet(set.Set, set.Parts, keys));
            }

            banks.Add(new DaggerfallNameBank(bank.Bank, BankNames[bank.Bank], BankKinds[bank.Bank], sets));
        }

        DaggerfallNameTables tables = new(
            new DaggerfallTextSource(DaggerfallTextKind.Name, recordId, label, language, bytes.Length, 0, records.Count),
            banks);
        tables.Validate();
        foreach (DaggerfallTextRecord record in records)
        {
            record.Validate();
        }

        return (tables, records);
    }

    private static string RequireFile(IReadOnlyList<SourceInventoryRow> inventory, string label)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.FirstOrDefault(row => row.FamilyId == FamilyId && StringComparer.Ordinal.Equals(row.PathOrPattern, label))?.Id
            ?? throw new InvalidOperationException($"The documented inventory does not carry '{label}', so the name tables cite no provenance.");
    }
}
