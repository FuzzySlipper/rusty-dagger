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

/// <summary>A fact that converts the named Arena2 cinematics, so needs the corpus entries and FFmpeg.</summary>
internal sealed class CinematicFactAttribute : FactAttribute
{
    public CinematicFactAttribute(params string[] entries) => Skip = TestData.MissingCorpus(entries) ?? TestData.MissingFfmpeg();
}

/// <summary>A theory that converts the named Arena2 cinematics, so needs the corpus entries and FFmpeg.</summary>
internal sealed class CinematicTheoryAttribute : TheoryAttribute
{
    public CinematicTheoryAttribute(params string[] entries) => Skip = TestData.MissingCorpus(entries) ?? TestData.MissingFfmpeg();
}

/// <summary>A fact that compares with a Daggerfall Unity checkout's named entries; it runs only when one is named.</summary>
internal sealed class DonorFactAttribute : FactAttribute
{
    public DonorFactAttribute(params string[] entries) => Skip = TestData.MissingDonor(entries);
}

/// <summary>A theory that compares with a Daggerfall Unity checkout's named entries; it runs only when one is named.</summary>
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
