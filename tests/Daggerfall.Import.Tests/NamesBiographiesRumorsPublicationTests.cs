using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>The name, biography and rumor families join the shared text section with metadata.</summary>
public sealed class NamesBiographiesRumorsPublicationTests
{
    [Fact]
    public void Builds_the_supplied_corpus_with_links_and_one_pending_family()
    {
        (DaggerfallText text, DaggerfallNameTables names, DaggerfallRumorCatalog rumors, DaggerfallBiographies biographies) = BuildAll(CorpusBytes());

        // 1408 text-resource records plus every family value: 785 fragments, 34 default lines,
        // 31 rumor texts, and the questionnaire prose.
        Assert.Equal(1408 + 785 + 34 + 31 + BiographyProseCount(), text.Records.Count);
        Assert.Equal(1 + 1 + 1 + 1 + 18, text.Sources.Count);
        Assert.Equal(
            ["local/arena2/BIO.DAT", "local/arena2/BIOG00T0.TXT", "local/arena2/BIOG01T0.TXT", "local/arena2/BIOG02T0.TXT", "local/arena2/BIOG03T0.TXT", "local/arena2/BIOG04T0.TXT", "local/arena2/BIOG05T0.TXT", "local/arena2/BIOG06T0.TXT", "local/arena2/BIOG07T0.TXT", "local/arena2/BIOG08T0.TXT", "local/arena2/BIOG09T0.TXT", "local/arena2/BIOG10T0.TXT", "local/arena2/BIOG11T0.TXT", "local/arena2/BIOG12T0.TXT", "local/arena2/BIOG13T0.TXT", "local/arena2/BIOG14T0.TXT", "local/arena2/BIOG15T0.TXT", "local/arena2/BIOG16T0.TXT", "local/arena2/BIOG17T0.TXT", "local/arena2/NAMEGEN.DAT", "local/arena2/RUMOR.DAT", "local/arena2/TEXT.RSC"],
            text.Sources.Select(source => source.Path).OrderBy(path => path, StringComparer.Ordinal));
        DaggerfallTextPendingKind pending = Assert.Single(text.PendingKinds);
        Assert.Equal(DaggerfallTextKind.Book, pending.Kind);

        // The new families carry no macro symbols of their own; the index still agrees both ways
        // because the build derives it from every value.
        Assert.DoesNotContain(text.Records, record => record.Key.Kind != DaggerfallTextKind.Resource && record.Macros.Count != 0);

        Assert.Equal(11, names.Banks.Count);
        Assert.Equal(31, rumors.Entries.Count);
        Assert.Equal(18, biographies.Biographies.Count);
        Assert.Equal(34, biographies.DefaultLines);

        // Every questionnaire links its backstory record; the corpus states them all.
        Assert.All(biographies.Biographies, biography =>
            Assert.Equal(DaggerfallBiographyLinkDisposition.Resolved, biography.BackstoryDisposition));

        // Exactly one macro link in the corpus names a record the text resource does not carry:
        // the School of Destruction answer's backstory token, stated by two questionnaires.
        List<DaggerfallBiographyEffect> unresolved = biographies.Biographies
            .SelectMany(biography => biography.Questions)
            .SelectMany(question => question.Answers)
            .SelectMany(answer => answer.Effects)
            .Where(effect => effect.MacroTargetDisposition == DaggerfallBiographyLinkDisposition.Unresolved)
            .ToList();
        Assert.Equal(2, unresolved.Count);
        Assert.All(unresolved, effect =>
        {
            Assert.Equal("Resource:4178", effect.MacroTarget);
            Assert.Contains("no record 4178", effect.MacroTargetReason, StringComparison.Ordinal);
        });

        // The backdrop is supplied but admitted nowhere, so every questionnaire records that.
        Assert.All(biographies.Biographies, biography =>
        {
            Assert.Equal("BIOG00I0", biography.Image.MediaId);
            Assert.Equal("local/arena2/BIOG00I0.IMG", biography.Image.Source);
            Assert.False(biography.Image.Published);
            Assert.Contains("No media publication", biography.Image.Reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Biography_keys_address_every_prose_line_deterministically()
    {
        (_, _, _, DaggerfallBiographies biographies) = BuildAll(CorpusBytes());
        (DaggerfallText text, _, _, _) = BuildAll(CorpusBytes());

        HashSet<string> keys = text.Records.Select(record => record.Key.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (DaggerfallBiography biography in biographies.Biographies)
        {
            foreach (DaggerfallBiographyQuestion question in biography.Questions)
            {
                foreach (string key in question.TextKeys)
                {
                    Assert.Contains(key, keys);
                }

                foreach (DaggerfallBiographyAnswer answer in question.Answers)
                {
                    Assert.Contains(answer.TextKey, keys);
                }
            }

            Assert.Contains(biography.BackstoryKey, keys);
        }

        // Spot shape: class 0, biography 0, question 1, first line and first answer.
        Assert.Contains("Biography:00-0-q01-l0", keys);
        Assert.Contains("Biography:00-0-q01-a", keys);
        Assert.Contains("Biography:default-00", keys);
        Assert.Contains("Rumor:00-00", keys);
        Assert.Contains("Name:00-0-00", keys);
    }

    [Fact]
    public void Fixture_inputs_refuse_unknown_labels_and_broken_links()
    {
        CorpusFiles corpus = CorpusBytes();
        Assert.Throws<InvalidOperationException>(() => DaggerfallTextBuilder.BuildAll(
            corpus.Text, "local/arena2/TEXT.RSC",
            corpus.Names, "elsewhere/NAMEGEN.DAT",
            corpus.Rumors, "local/arena2/RUMOR.DAT",
            corpus.Bio, "local/arena2/BIO.DAT",
            corpus.Questionnaires, corpus.Image, Inventory(), "en"));

        // A questionnaire whose backstory record the text resource does not carry records the
        // miss instead of refusing the file: the questionnaire exists either way.
        string twelve = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"{i}.\tWhy {i}?\na.\tBecause\n#9999"));
        (DaggerfallText _, _, _, DaggerfallBiographies biographies) = DaggerfallTextBuilder.BuildAll(
            Resource([(9000, "Backstory."u8.ToArray())]), "local/arena2/TEXT.RSC",
            corpus.Names, "local/arena2/NAMEGEN.DAT",
            corpus.Rumors, "local/arena2/RUMOR.DAT",
            corpus.Bio, "local/arena2/BIO.DAT",
            [(twelve, "local/arena2/BIOG00T0.TXT", 0, 0)],
            corpus.Image, Inventory(), "en");
        DaggerfallBiography only = Assert.Single(biographies.Biographies);
        Assert.Equal(DaggerfallBiographyLinkDisposition.Unresolved, only.BackstoryDisposition);
        DaggerfallBiographyEffect macro = only.Questions[0].Answers[0].Effects.Single(effect => effect.Kind == BiogEffectKind.TextMacro);
        Assert.Equal("Resource:9999", macro.MacroTarget);
        Assert.Equal(DaggerfallBiographyLinkDisposition.Unresolved, macro.MacroTargetDisposition);
    }

    private static (DaggerfallText Text, DaggerfallNameTables Names, DaggerfallRumorCatalog Rumors, DaggerfallBiographies Biographies) BuildAll(CorpusFiles corpus) =>
        DaggerfallTextBuilder.BuildAll(
            corpus.Text, "local/arena2/TEXT.RSC",
            corpus.Names, "local/arena2/NAMEGEN.DAT",
            corpus.Rumors, "local/arena2/RUMOR.DAT",
            corpus.Bio, "local/arena2/BIO.DAT",
            corpus.Questionnaires, corpus.Image, Inventory(), "en");

    private sealed record CorpusFiles(
        byte[] Text, byte[] Names, byte[] Rumors, byte[] Bio,
        IReadOnlyList<(string Text, string Label, int ClassIndex, int BiographyIndex)> Questionnaires,
        byte[] Image);

    private static CorpusFiles CorpusBytes()
    {
        List<(string Text, string Label, int ClassIndex, int BiographyIndex)> questionnaires = [];
        for (int cls = 0; cls <= 17; cls++)
        {
            string file = $"BIOG{cls:D2}T0.TXT";
            questionnaires.Add((File.ReadAllText(Corpus(file)), $"local/arena2/{file}", cls, 0));
        }

        return new CorpusFiles(
            File.ReadAllBytes(Corpus("TEXT.RSC")),
            File.ReadAllBytes(Corpus("NAMEGEN.DAT")),
            File.ReadAllBytes(Corpus("RUMOR.DAT")),
            File.ReadAllBytes(Corpus("BIO.DAT")),
            questionnaires,
            File.ReadAllBytes(Corpus("BIOG00I0.IMG")));
    }

    private static int BiographyProseCount()
    {
        int count = 0;
        for (int cls = 0; cls <= 17; cls++)
        {
            BiogQuestionnaire questionnaire = BiogQuestionnaireReader.Read(
                File.ReadAllText(Corpus($"BIOG{cls:D2}T0.TXT")), cls, 0, "corpus");
            count += questionnaire.Questions.Sum(question => question.Text.Count + question.Answers.Count);
        }

        return count;
    }

    /// <summary>A minimal text resource carrying the supplied records, laid out the way the file does.</summary>
    private static byte[] Resource(params (int Id, byte[] Text)[] records)
    {
        int headerLength = TextResourceReader.DirectoryEntryBytes * (records.Length + TextResourceReader.DirectoryExtraEntries);
        List<byte> bytes = [];
        bytes.AddRange(BitConverter.GetBytes((ushort)headerLength));
        int offset = TextResourceReader.HeaderLengthBytes + headerLength;
        foreach ((int id, byte[] text) in records)
        {
            bytes.AddRange(BitConverter.GetBytes((ushort)id));
            bytes.AddRange(BitConverter.GetBytes(offset));
            offset += text.Length + 1;
        }

        bytes.AddRange(BitConverter.GetBytes((ushort)Arena2FormatConstants.ClassicDirectorySentinelId));
        bytes.AddRange(BitConverter.GetBytes(offset));
        foreach ((int _, byte[] text) in records)
        {
            bytes.AddRange(text);
            bytes.Add(TextResourceReader.Terminator);
        }

        return [.. bytes];
    }

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
        SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
