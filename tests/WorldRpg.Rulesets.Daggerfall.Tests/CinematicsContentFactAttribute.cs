using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Tests.Support;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Whether the data container carries converted cinematics. An import converts them only when asked
/// (<c>import --cinematics</c>), so the store's data has none and a test of their playback skips, naming the flag.
/// </summary>
internal static class TestCinematics
{
    internal static bool Carried =>
        TestData.MissingGeneratedContent() is null &&
        TestContentFiles.AllFiles(Path.Combine(TestData.RepositoryRoot, "content", DaggerfallCinematicPresentation.Root)).Length > 0;

    internal static string? Missing() => TestData.MissingGeneratedContent() ?? (Carried ? null
        : $"the data container carries no cinematics under {DaggerfallCinematicPresentation.Root}; an import converts them only with `import --cinematics`.");
}

/// <summary>A fact that plays or checks the converted cinematics the data container carries.</summary>
internal sealed class CinematicsContentFactAttribute : FactAttribute
{
    public CinematicsContentFactAttribute() => Skip = TestCinematics.Missing();
}

/// <summary>A theory that plays or checks the converted cinematics the data container carries.</summary>
internal sealed class CinematicsContentTheoryAttribute : TheoryAttribute
{
    public CinematicsContentTheoryAttribute() => Skip = TestCinematics.Missing();
}
