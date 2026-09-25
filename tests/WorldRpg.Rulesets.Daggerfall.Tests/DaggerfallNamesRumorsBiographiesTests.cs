using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published name tables, rumor catalog and biographies: what each section resolves, how the
/// references check against the text, and what the reader refuses. The mutations rewrite the real
/// payload rather than a fixture, because what is under test is the reader of the sections the
/// product ships.
/// </summary>
public sealed class DaggerfallNamesRumorsBiographiesTests
{
    [Fact]
    public void Loads_eleven_banks_with_donor_identities_and_composition()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(11, definitions.Names.Banks.Count);
        DaggerfallNameBankDefinition breton = definitions.Names.Banks[0];
        Assert.Equal(0, breton.Bank);
        Assert.Equal("Breton", breton.Name);
        Assert.Equal(DaggerfallNameBankKind.Standard, breton.Kind);
        Assert.Equal([0, 1, 2, 3, 4, 5], breton.Sets.Select(set => set.Set));
        Assert.Equal("Theod", definitions.Text.Require(breton.Sets[0].Keys[0]).TextRuns.Single());
        DaggerfallNameBankDefinition redguard = definitions.Names.Banks.Single(bank => bank.Name == "Redguard");
        Assert.Equal(DaggerfallNameBankKind.Redguard, redguard.Kind);
        DaggerfallNameBankDefinition nord = definitions.Names.Banks.Single(bank => bank.Name == "Nord");
        Assert.Equal(DaggerfallNameBankKind.Nord, nord.Kind);
        Assert.Equal(3, definitions.Names.Banks.Count(bank => bank.Kind == DaggerfallNameBankKind.Monster));
    }

    [Fact]
    public void Loads_thirty_one_rumors_with_region_type_and_text()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(31, definitions.Rumors.Entries.Count);
        Assert.Equal(Enumerable.Range(0, 31), definitions.Rumors.Entries.Select(entry => entry.Index));
        DaggerfallRumorDefinition first = definitions.Rumors.Entries[0];
        Assert.Equal(27, first.Type);
        Assert.Equal("EnemySignMessage", first.TypeName);
        Assert.Equal(DaggerfallRumorTypeDisposition.Known, first.TypeDisposition);
        Assert.Equal(0, first.Region);
        Assert.True(first.IsSignMessage);
        Assert.False(first.IsQuestRumor);
        Assert.Equal(508, first.Faction1);
        Assert.Equal(810, first.Faction2);
        Assert.Equal(566670, first.TimeLimit);
        Assert.Contains("Temple Treasurers", definitions.Text.Require(first.TextKey).TextRuns.First(), StringComparison.Ordinal);
    }

    [Fact]
    public void Loads_eighteen_questionnaires_with_links_and_the_recorded_miss()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(18, definitions.Biographies.Biographies.Count);
        Assert.Equal(34, definitions.Biographies.DefaultLines);
        DaggerfallBiographyDefinition mage = definitions.Biographies.Biographies[0];
        Assert.Equal(0, mage.ClassIndex);
        Assert.Equal(4116, mage.BackstoryId);
        Assert.False(mage.BackstoryExplicit);
        Assert.Equal(DaggerfallBiographyLinkDisposition.Resolved, mage.BackstoryDisposition);
        Assert.Equal(12, mage.Questions.Count);
        Assert.Contains("What school of magic have you been", definitions.Text.Require(mage.Questions[0].TextKeys[0]).TextRuns.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Destruction", definitions.Text.Require(mage.Questions[0].Answers[0].TextKey).TextRuns.Single());
        Assert.False(mage.Image.Published);
        Assert.Contains("No media publication", mage.Image.Reason, StringComparison.Ordinal);
        Assert.Contains(mage.Warnings, warning => warning.StartsWith("unimplemented command 'AE", StringComparison.Ordinal));
        Assert.Equal(12, definitions.Biographies.Biographies
            .SelectMany(biography => biography.Warnings)
            .Count(warning => warning.StartsWith("invalid command '&'", StringComparison.Ordinal)));

        // The School of Destruction answer's backstory token names a record the text resource
        // does not carry: the miss is published, not refused.
        DaggerfallBiographyEffectDefinition miss = mage.Questions
            .SelectMany(question => question.Answers)
            .SelectMany(answer => answer.Effects)
            .Single(effect => effect.MacroTarget is not null && effect.MacroTargetDisposition == DaggerfallBiographyLinkDisposition.Unresolved);
        Assert.Equal(new DaggerfallTextKey(DaggerfallTextKind.Resource, "4178"), miss.MacroTarget);
        Assert.Contains("no record 4178", miss.MacroTargetReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_missing_section_and_a_reference_with_no_text()
    {
        DaggerfallContentException missing = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => payload.Remove("names"))));
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Contains("publishes no names section", StringComparison.Ordinal));

        DaggerfallContentException dangling = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Names(payload)["banks"]!.AsArray()[0]!.AsObject()["sets"]!.AsArray()[0]!.AsObject()["keys"]!.AsArray()[0] = "Name:99-9-99")));
        Assert.Contains(dangling.Diagnostics, diagnostic => diagnostic.Contains("names text key 'Name:99-9-99'", StringComparison.Ordinal));

        DaggerfallContentException biography = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Biographies(payload)["biographies"]!.AsArray()[0]!.AsObject()["questions"]!.AsArray()[0]!.AsObject()["textKeys"]!.AsArray()[0] = "Biography:99-9-q99-l9")));
        Assert.Contains(biography.Diagnostics, diagnostic => diagnostic.Contains("names text key 'Biography:99-9-q99-l9'", StringComparison.Ordinal));
    }

    private static DaggerfallDefinitions Definitions() =>
        TestPayload.Definitions;

    private static byte[] Payload(Action<JsonObject> mutate)
    {
        JsonObject payload = JsonNode.Parse(File.ReadAllBytes(PackPath()))!.AsObject();
        mutate(payload);
        return Encoding.UTF8.GetBytes(payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static JsonObject Names(JsonObject payload) => payload["names"]!.AsObject();

    private static JsonObject Biographies(JsonObject payload) => payload["biographies"]!.AsObject();

    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
