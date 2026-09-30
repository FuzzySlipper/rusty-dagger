using Xunit;

namespace WorldRpg.Tests.Support;

// A test that reads operator-supplied data skips at discovery, naming what is missing and the
// variable that relocates it, instead of passing without asserting or failing on an I/O error.

/// <summary>A fact that needs the named Arena2 corpus entries (or just the corpus root when none are named).</summary>
internal sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute(params string[] entries) => Skip = TestData.MissingCorpus(entries);
}

/// <summary>A theory that needs the named Arena2 corpus entries (or just the corpus root when none are named).</summary>
internal sealed class CorpusTheoryAttribute : TheoryAttribute
{
    public CorpusTheoryAttribute(params string[] entries) => Skip = TestData.MissingCorpus(entries);
}

/// <summary>A fact that needs the named donor-checkout entries (or just the donor root when none are named).</summary>
internal sealed class DonorFactAttribute : FactAttribute
{
    public DonorFactAttribute(params string[] entries) => Skip = TestData.MissingDonor(entries);
}

/// <summary>A theory that needs the named donor-checkout entries (or just the donor root when none are named).</summary>
internal sealed class DonorTheoryAttribute : TheoryAttribute
{
    public DonorTheoryAttribute(params string[] entries) => Skip = TestData.MissingDonor(entries);
}

/// <summary>A fact that needs both the Arena2 corpus and the donor checkout.</summary>
internal sealed class CorpusAndDonorFactAttribute : FactAttribute
{
    public CorpusAndDonorFactAttribute(string[] corpus, string[] donor) =>
        Skip = TestData.MissingCorpus(corpus) is { } missing ? missing : TestData.MissingDonor(donor);
}
