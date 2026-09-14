using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The donor's static mobile table and the published pack are two sides of one reconciliation; these
/// checks pin the differences the ledger is supposed to name.
/// </summary>
public sealed class MobileLedgerTests
{
    [Fact]
    public void NamesEveryDonorMobileNothingPublishes()
    {
        MobileLedger ledger = MobileLedgerBuilder.Build(Donor(), Pack());

        Assert.Equal(5, ledger.DonorEntries);
        Assert.Equal(3, ledger.PublishedActors);
        Assert.Equal(2, ledger.CatalogEntries);
        Assert.Equal(2, ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.Published));
        Assert.Equal(1, ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.PublishedVariant));
        Assert.Equal(1, ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.HumanClass));

        MobileLedgerEntry unpublished = Assert.Single(ledger.Unpublished);
        Assert.Equal(39, unpublished.Id);
        Assert.Contains("Horse", unpublished.Name, StringComparison.Ordinal);
        Assert.Contains("no published actor carries it", unpublished.Note, StringComparison.Ordinal);

        // The repeated donor name is published under the numbered variant the pack actually carries,
        // and the entry it repeats is the plain identity rather than missing coverage.
        Assert.Equal("dragonling-40", ledger.Entries.Single(entry => entry.Disposition == MobileLedgerDisposition.PublishedVariant).Identity);
        Assert.Contains(ledger.Entries, entry => entry.Disposition == MobileLedgerDisposition.Published && entry.Name == "Dragonling");
        // The human mobile is reported as a career-space fact rather than as missing coverage.
        Assert.Contains("careers", ledger.Entries.Single(entry => entry.Disposition == MobileLedgerDisposition.HumanClass).Note, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToClaimCoverageItCannotRead()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => MobileLedgerBuilder.Build("// nothing here", Pack()));
        Assert.Contains("static mobile table was not found", failure.Message, StringComparison.Ordinal);
    }

    private static string Donor() => """
        public static MobileEnemy[] Enemies = new MobileEnemy[]
        {
            // Rat
            new MobileEnemy()
            {
                ID = 0,
            },
            // Horse (unused, but can appear in merchant-sold soul traps)
            new MobileEnemy()
            {
                ID = 39,
            },
            // Dragonling
            new MobileEnemy()
            {
                ID = 40,
            },
            // Dragonling
            new MobileEnemy()
            {
                ID = 41,
            },
            // Mage
            new MobileEnemy()
            {
                ID = 130,
            },
        };

        """;

    private static string Pack() => """
        {
          "actors": [
            { "id": "rat" },
            { "id": "dragonling" },
            { "id": "dragonling-40" }
          ],
          "catalogs": {
            "enemies": [
              { "id": "rat" },
              { "id": "dragonling" }
            ]
          }
        }
        """;
}
