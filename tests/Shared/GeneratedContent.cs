using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WorldRpg.Tests.Support;

// Content derived from the operator's Arena2 files is not tracked: scripts/regenerate-content.sh writes
// it from local/arena2. A test that reads it skips at discovery with that command named, instead of
// failing on an I/O error in a clone that has not been regenerated.

/// <summary>A fact that reads content scripts/regenerate-content.sh generates.</summary>
internal sealed class GeneratedContentFactAttribute : FactAttribute
{
    public GeneratedContentFactAttribute() => Skip = TestData.MissingGeneratedContent();
}

/// <summary>A theory that reads content scripts/regenerate-content.sh generates.</summary>
internal sealed class GeneratedContentTheoryAttribute : TheoryAttribute
{
    public GeneratedContentTheoryAttribute() => Skip = TestData.MissingGeneratedContent();
}

/// <summary>A fact that needs the named Arena2 corpus entries and also reads generated content.</summary>
internal sealed class CorpusAndGeneratedContentFactAttribute : FactAttribute
{
    public CorpusAndGeneratedContentFactAttribute(params string[] entries) =>
        Skip = TestData.MissingCorpus(entries) ?? TestData.MissingGeneratedContent();
}

/// <summary>A theory that needs the named Arena2 corpus entries and also reads generated content.</summary>
internal sealed class CorpusAndGeneratedContentTheoryAttribute : TheoryAttribute
{
    public CorpusAndGeneratedContentTheoryAttribute(params string[] entries) =>
        Skip = TestData.MissingCorpus(entries) ?? TestData.MissingGeneratedContent();
}

/// <summary>
/// The test framework of a suite whose facts read generated content almost throughout: when the content
/// is absent, every test is reported skipped with the regeneration command named, rather than each one
/// failing on the file it could not open. With the content present it discovers exactly as xunit does.
/// </summary>
/// <remarks>A suite opts in with <c>[assembly: TestFramework("WorldRpg.Tests.Support.GeneratedContentTestFramework", "ASSEMBLY")]</c>.</remarks>
internal sealed class GeneratedContentTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo) =>
        new Discoverer(assemblyInfo, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Discoverer(IAssemblyInfo assemblyInfo, ISourceInformationProvider sourceProvider, IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkDiscoverer(assemblyInfo, sourceProvider, diagnosticMessageSink)
    {
        protected override bool FindTestsForMethod(ITestMethod testMethod, bool includeSourceInformation, IMessageBus messageBus, ITestFrameworkDiscoveryOptions discoveryOptions)
        {
            if (TestData.MissingGeneratedContent() is not { } reason || !testMethod.Method.GetCustomAttributes(typeof(FactAttribute)).Any())
                return base.FindTestsForMethod(testMethod, includeSourceInformation, messageBus, discoveryOptions);
            XunitSkippedDataRowTestCase skipped = new(
                DiagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                reason);
            return ReportDiscoveredTestCase(skipped, includeSourceInformation, messageBus);
        }
    }
}
