using System.Globalization;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads the classic text resource, names, rumors, biographies and books into the imported payload, so a text
/// consumer resolves a value by the key the source gives it and reads the macros and variants the value
/// carries rather than a rendered string this tool would have had to choose.
/// </summary>
internal static class TextCommand
{
    private const int BiographyQuestionnaires = 18;

    public static ToolCommand Command { get; } = new("text",
        [Options.Arena2, Options.Pack, Options.Inventory, CommandOption.Required("--language", "LANG"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        List<(string Text, string Label, int ClassIndex, int BiographyIndex)> questionnaires = [];
        for (int classIndex = 0; classIndex < BiographyQuestionnaires; classIndex++)
        {
            string file = $"BIOG{classIndex:D2}T0.TXT";
            string path = Path.Combine(arena2, file);
            if (!File.Exists(path)) throw new ArgumentException($"the arena2 directory carries no {file}, so the biography questionnaires are incomplete");
            questionnaires.Add((File.ReadAllText(path), Options.Arena2Label(file), classIndex, 0));
        }

        string imagePath = Path.Combine(arena2, "BIOG00I0.IMG");
        byte[]? imageBytes = File.Exists(imagePath) ? File.ReadAllBytes(imagePath) : null;
        if (imageBytes is null) Console.WriteLine("biography backdrop: not supplied, so every questionnaire records an unresolved image link");

        (DaggerfallText text, DaggerfallNameTables names, DaggerfallRumorCatalog rumors, DaggerfallBiographies biographies, DaggerfallBooks books) = DaggerfallTextBuilder.BuildAll(
            File.ReadAllBytes(Path.Combine(arena2, TextResourceReader.FileName)),
            Options.Arena2Label(TextResourceReader.FileName),
            File.ReadAllBytes(Path.Combine(arena2, NameGenReader.FileName)),
            Options.Arena2Label(NameGenReader.FileName),
            File.ReadAllBytes(Path.Combine(arena2, RumorReader.FileName)),
            Options.Arena2Label(RumorReader.FileName),
            File.ReadAllBytes(Path.Combine(arena2, BioDatReader.FileName)),
            Options.Arena2Label(BioDatReader.FileName),
            questionnaires,
            imageBytes,
            ReadBooks(Path.Combine(arena2, "books"), Options.Arena2Label("books")),
            Options.ReadInventory(args),
            args["--language"]);

        DaggerfallTextSource publishedSource = text.Sources[0];
        Console.WriteLine($"text: {text.Records.Count} records from {publishedSource.Path} in {publishedSource.Language}, {text.Records.Count(record => record.State == Arena2TextState.Malformed)} malformed, {text.Records.Count(record => record.State == Arena2TextState.Read)} readable");
        foreach (IGrouping<TextMacroDisposition, DaggerfallTextMacro> disposition in text.Macros.GroupBy(macro => macro.Disposition).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {disposition.Count()} macros {disposition.Key.ToString().ToLowerInvariant()}");
        }

        string[] unrecognised = [.. text.Macros.Where(macro => macro.Disposition == TextMacroDisposition.Unrecognised).Select(macro => macro.Symbol)];
        if (unrecognised.Length != 0) Console.WriteLine($"  unrecognised symbols: {string.Join(", ", unrecognised)}");
        Console.WriteLine($"  declared key families: {string.Join(", ", text.PendingKinds.Select(pending => $"{pending.Kind.ToString().ToLowerInvariant()} (task #{pending.OwnerTask})"))}");
        Console.WriteLine($"  names: {names.Banks.Count} banks, {names.Banks.Sum(bank => bank.Sets.Sum(set => set.Parts.Count))} fragments");
        Console.WriteLine($"  rumors: {rumors.Entries.Count} records, {rumors.Entries.Count(entry => entry.TypeDisposition == DaggerfallRumorTypeDisposition.Unknown)} unknown types");
        Console.WriteLine($"  biographies: {biographies.Biographies.Count} questionnaires, {biographies.DefaultLines} default lines");
        Console.WriteLine($"  books: {books.Books.Count(book => book.Disposition == DaggerfallBookDisposition.Read)} supplied, {books.Books.Count(book => book.Disposition != DaggerfallBookDisposition.Read)} without files or unreadable");
        if (!args.Switch("--update")) return Options.ReportOnly("this text");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = JsonSerializer.Serialize(text, PublishedJson.Section),
            ["names"] = JsonSerializer.Serialize(names, PublishedJson.Section),
            ["rumors"] = JsonSerializer.Serialize(rumors, PublishedJson.Section),
            ["biographies"] = JsonSerializer.Serialize(biographies, PublishedJson.Section),
            ["books"] = JsonSerializer.Serialize(books, PublishedJson.Section),
        });
        return 0;
    }

    /// <summary>
    /// Reads the supplied book files: identity from the file stem, bytes under the logical path the
    /// repository documents. A stem that carries no identity is refused rather than published under a
    /// guessed one.
    /// </summary>
    private static IReadOnlyList<(int BookId, string Label, byte[] Bytes)> ReadBooks(string directory, string label)
    {
        List<(int BookId, string Label, byte[] Bytes)> books = [];
        foreach (string path in Directory.EnumerateFiles(directory, "BOK*.TXT").Order(StringComparer.Ordinal))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.Length != 8 || !int.TryParse(stem[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bookId))
            {
                throw new InvalidOperationException($"'{stem}' does not carry a book identity, so it cannot be enumerated as a book.");
            }

            books.Add((bookId, $"{label}/{Path.GetFileName(path)}", File.ReadAllBytes(path)));
        }

        return books;
    }
}
