using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The screen shown when no Daggerfall game can be played, and the Import it offers.</summary>
public sealed class DaggerfallUnavailableSessionTests
{
    [Fact]
    public void The_entry_screen_Import_action_reaches_the_import_and_a_screen_without_one_ignores_it()
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        DaggerfallRuleset ruleset = new();
        InstalledContentRequirement data = ruleset.InstalledContent.Single(requirement => requirement.Required);
        Assert.Same(DaggerfallRuleset.DataImport, data.Import);
        Assert.Equal(["import", "--arena2", "/games/Daggerfall", "--install", "/release"],
            data.Import!.Arguments("/games/Daggerfall", "/release"));

        host.Call(engine =>
        {
            using DaggerfallUnavailableSession session = new(new GameUnavailableContext(engine, ["The Daggerfall data is not imported."], data));
            session.PublishInitial();
            Assert.Equal(InstalledContentImportState.Idle, session.ImportState);

            // The DOM's Import button sends this payload; the test host has no folder picker, so the import
            // reports why rather than waiting on one.
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"content-import"}""")]));
            Assert.NotEqual(InstalledContentImportState.Idle, session.ImportState);
            Assert.False(session.TakeReopenRequest());
        });

        host.Call(engine =>
        {
            using DaggerfallUnavailableSession session = new(new GameUnavailableContext(engine, ["No game can be composed."]));
            Assert.False(session.OffersImport);
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"content-import"}""")]));
            Assert.Null(session.ImportState);
        });
    }
}
