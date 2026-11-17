using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic career record format, which the monster archive's enemy configurations share: a supplied record
/// decodes, and a malformed one is refused with its source named.
/// </summary>
public sealed class ClassCfgDecoderTests
{
    [CorpusFact]
    public void Decodes_a_supplied_career_record()
    {
        ClassCfgRecord career = ClassCfgDecoder.Decode(ReadClass("CLASS00.CFG"), "CLASS00.CFG");

        // The first supplied record is the Mage template: the magic primaries, the lowest
        // hit points per level and the high intelligence and willpower the corpus carries.
        Assert.Equal("Mage", career.Name);
        Assert.Equal([27, 25, 26], new[] { career.PrimarySkill1, career.PrimarySkill2, career.PrimarySkill3 });
        Assert.Equal(6, career.HitPointsPerLevel);
        Assert.Equal(1.0390625f, career.AdvancementMultiplier, 6);
        Assert.Equal(8, career.Attributes.Length);
        Assert.Equal(42, career.Attributes[0]);
        Assert.Equal(65, career.Attributes[2]);
    }

    [CorpusFact]
    public void Decodes_every_supplied_career_record()
    {
        string[] files = [.. Directory.EnumerateFiles(TestData.CorpusRoot, "CLASS*.CFG").Order(StringComparer.Ordinal)];

        Assert.Equal(19, files.Length);
        foreach (string file in files)
        {
            ClassCfgRecord career = ClassCfgDecoder.Decode(File.ReadAllBytes(file), Path.GetFileName(file));
            Assert.False(string.IsNullOrWhiteSpace(career.Name));
            Assert.True(career.HitPointsPerLevel > 0);
            Assert.True(career.AdvancementMultiplier > 0f);
        }
    }

    [Fact]
    public void Rejects_a_career_record_that_is_not_the_classic_length()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ClassCfgDecoder.Decode(new byte[73], "CLASS00.CFG"));

        Assert.Equal("CLASS00.CFG", error.SourceName);
        Assert.Contains("74", error.Message, StringComparison.Ordinal);
    }

    [CorpusFact]
    public void The_decoder_reports_a_skill_slot_outside_the_classic_space()
    {
        // The carrier is well formed and the index space is what the value exceeds, so the
        // decoder reads the record and reports the slot: the enemy family tolerates it because
        // one supplied configuration carries one.
        byte[] bytes = ReadClass("CLASS00.CFG");
        bytes[19] = 40;

        ClassCfgRecord record = ClassCfgDecoder.Decode(bytes, "CLASS00.CFG");

        Assert.Equal([40], record.SkillIndicesBeyondTerminal);
        Assert.Equal(40, record.MajorSkill1);
    }

    [CorpusFact]
    public void Rejects_a_career_record_without_a_printable_name()
    {
        byte[] bytes = ReadClass("CLASS00.CFG");
        bytes[28] = 0;

        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ClassCfgDecoder.Decode(bytes, "CLASS00.CFG"));

        Assert.Equal(28, error.Offset);
    }

    private static byte[] ReadClass(string name) => File.ReadAllBytes(TestData.Corpus(name));
}
