// Nearly every fact in this suite reads content a real import packed into the data container. Without that
// content the suite reports every test skipped with the command named, instead of failing on each file
// it cannot open; with it, discovery is ordinary xunit discovery.
[assembly: Xunit.TestFramework("WorldRpg.Tests.Support.GeneratedContentTestFramework", "WorldRpg.Rulesets.Daggerfall.Tests")]
