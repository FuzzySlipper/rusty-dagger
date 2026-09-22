using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class InternalStringsReaderTests
{
    [Fact]
    public void Preserves_multiline_values_escaped_quotes_and_source_spans()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\ufeffKey,Value\r\nStoresA,The %ef\r\nTavernsA,\"The \"\"Golden\"\" Cup\"\r\nTavernsB,\"at %cn\r\n%rt's Rest\"\r\n");

        InternalStringsCatalog catalog = InternalStringsReader.Read(bytes, "donor/internal.csv");

        Assert.Equal(14, catalog.HeaderLength);
        Assert.Equal(["StoresA", "TavernsA", "TavernsB"], catalog.Records.Select(record => record.Key));
        Assert.Equal("The %ef", catalog.Records[0].Value);
        Assert.Equal("The \"Golden\" Cup", catalog.Records[1].Value);
        Assert.Equal("at %cn\r\n%rt's Rest", catalog.Records[2].Value);
        Assert.Equal(0, catalog.Records[0].Index);
        Assert.Equal(catalog.HeaderLength, catalog.Records[0].Offset);
        Assert.Equal(bytes.Length, catalog.Records.Sum(record => record.ByteLength) + catalog.HeaderLength);
    }

    [Theory]
    [InlineData("Key,Value\nName,One\nName,Two\n", "twice")]
    [InlineData("Key,Value\nName,One,Two\n", "more than two")]
    [InlineData("Key,Value\nName,\"One\n", "inside a quoted")]
    [InlineData("Name,Value\nName,One\n", "exact CSV header")]
    public void Refuses_a_table_that_cannot_supply_one_unambiguous_value(string csv, string expected)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            InternalStringsReader.Read(Encoding.UTF8.GetBytes(csv), "fixture/Internal_Strings.csv"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merges_internal_strings_as_the_shared_text_family()
    {
        DaggerfallText baseText = DaggerfallTextBuilder.Build(
            Resource((1, "Classic"u8.ToArray())),
            "local/arena2/TEXT.RSC",
            [new Daggerfall.Import.Publication.SourceInventoryRow("CNT-016", "family", "CNT-016", "text", "local/arena2/TEXT.RSC", "", "", "")],
            "en");

        DaggerfallText merged = DaggerfallInternalStringsBuilder.Merge(
            baseText,
            Encoding.UTF8.GetBytes("Key,Value\nStoresA,The %ef\n"),
            "donor/daggerfall-unity/Internal_Strings.csv",
            "en");

        DaggerfallTextRecord internalRecord = Assert.Single(merged.Records, record => record.Key.Kind == DaggerfallTextKind.Internal);
        Assert.Equal("StoresA", internalRecord.Key.Id);
        Assert.Equal("The %ef", Assert.Single(internalRecord.Tokens).Text);
        Assert.Contains(merged.Macros, macro => macro.Symbol == "%ef" && macro.Records == 1);
        Assert.Contains(merged.Sources, source => source.Kind == DaggerfallTextKind.Internal && source.RecordId == DaggerfallInternalStringsBuilder.SourceRecordId);
    }

    [Fact]
    public void Extracts_the_exact_breton_and_redguard_region_bank_table()
    {
        int[] source = [.. Enumerable.Repeat(0, 62)];
        source[0] = 1;
        source[7] = 1;
        DaggerfallBuildingNameInputs inputs = DaggerfallBuildingNameInputsBuilder.Build(
            Encoding.UTF8.GetBytes($"private static readonly byte[] regionRaces = {{ {string.Join(", ", source)} }};"),
            "donor/MapsFile.cs");

        Assert.Equal(62, inputs.RegionNameBanks.Count);
        Assert.Equal(1, inputs.RegionNameBanks[0]);
        Assert.Equal(1, inputs.RegionNameBanks[7]);
    }

    private static byte[] Resource(params (int Id, byte[] Text)[] records)
    {
        const int Header = TextResourceReader.HeaderLengthBytes;
        const int Entry = TextResourceReader.DirectoryEntryBytes;
        int headerLength = Entry * (records.Length + TextResourceReader.DirectoryExtraEntries);
        int directory = Header + Entry * (records.Length + TextResourceReader.DirectoryExtraEntries);
        int length = directory + records.Sum(record => record.Text.Length + 1);
        byte[] bytes = new byte[length];
        Write16(bytes, 0, headerLength);
        int offset = directory;
        for (int index = 0; index < records.Length; index++)
        {
            Write16(bytes, Header + index * Entry, records[index].Id);
            Write32(bytes, Header + index * Entry + 2, offset);
            records[index].Text.CopyTo(bytes, offset);
            offset += records[index].Text.Length + 1;
        }

        int sentinel = Header + records.Length * Entry;
        Write16(bytes, sentinel, Arena2FormatConstants.ClassicDirectorySentinelId);
        Write32(bytes, sentinel + 2, bytes.Length);
        return bytes;
    }

    private static void Write16(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void Write32(byte[] bytes, int offset, int value)
    {
        Write16(bytes, offset, value);
        Write16(bytes, offset + 2, value >> 16);
    }
}
