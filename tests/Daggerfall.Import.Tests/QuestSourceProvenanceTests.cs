using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>QRC source coordinates retain their physical donor line after section extraction.</summary>
public sealed class QuestSourceProvenanceTests
{
    private static readonly IReadOnlyDictionary<string, int> MessageIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["QuestorOffer"] = 1000,
    };

    [Fact]
    public void Retains_physical_QRC_and_QBN_lines_after_leading_metadata_and_blanks()
    {
        QuestSourceDocument document = QuestSourceReader.Read(
            "-- donor header\n\nQuest: Q\nDisplayName: Fixture\n\nQRC:\n\n-- QRC comment\nQuestorOffer: [not-an-id]\nOffer text\n\nQBN:\n\nClock timer 1 day 1\n",
            "fixture.txt", MessageIds, new Dictionary<string, int>());

        Assert.Equal(9, Assert.Single(document.Messages).FirstLine);
        Assert.Equal(14, Assert.Single(document.Blocks).FirstLine);
    }

    [Fact]
    public void Reports_malformed_QRC_header_at_its_physical_source_line()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => QuestSourceReader.Read(
            "-- donor header\n\nQuest: Q\n\nQRC:\n\n-- QRC comment\nUnrecognizedHeader: 7\nQBN:\nClock timer 1 day 1\n",
            "fixture.txt", MessageIds, new Dictionary<string, int>()));

        Assert.Equal(8, error.Offset);
        Assert.Contains("UnrecognizedHeader: 7", error.Message, StringComparison.Ordinal);
    }
}
