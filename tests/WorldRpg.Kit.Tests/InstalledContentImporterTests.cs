using System.Text;
using Rusty.Engine;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>
/// The entry screen's import over the host: a folder is chosen, the declared tool runs on it with the
/// installed-content directory, its output is followed, and its outcome says whether the container is installed.
/// </summary>
public sealed class InstalledContentImporterTests
{
    private static readonly InstalledContentImport Import = new("DataImport", "Choose the game folder", (folder, install) => ["import", folder, install]);

    [Fact]
    public void A_chosen_folder_runs_the_declared_tool_and_a_clean_exit_installs_the_container()
    {
        ScriptedHost host = new();
        using InstalledContentImporter importer = new(host, Import);

        importer.Start();
        Assert.Equal(InstalledContentImportState.Choosing, importer.State);
        Assert.Equal("Choose the game folder", host.PickerTitle);
        Assert.False(importer.Follow());

        host.Choice = (FolderChoiceState.Chosen, "/games/Daggerfall");
        Assert.True(importer.Follow());
        Assert.Equal(InstalledContentImportState.Running, importer.State);
        Assert.Equal("DataImport", host.Tool);
        Assert.Equal(["import", "/games/Daggerfall", "/release"], host.Arguments);
        Assert.Equal("/games/Daggerfall", importer.Folder);
        Assert.True(host.ChoiceDisposed);

        host.Output = "step 1\nstep 2";
        host.Run = new(ToolRunState.Running, 0, 2);
        Assert.True(importer.Follow());
        Assert.Equal("step 1\nstep 2", importer.Output);
        Assert.False(importer.Follow());

        host.Run = new(ToolRunState.Exited, 0, 2);
        Assert.True(importer.Follow());
        Assert.Equal(InstalledContentImportState.Installed, importer.State);
        Assert.True(host.RunDisposed);

        // Once installed, Import does nothing more: the host opens the game.
        importer.Start();
        Assert.Equal(InstalledContentImportState.Installed, importer.State);
    }

    [Theory]
    [InlineData(ToolRunState.Exited, 3, "The import failed (exit code 3)")]
    [InlineData(ToolRunState.Cancelled, 0, "The import was cancelled.")]
    [InlineData(ToolRunState.FailedToStart, 0, "The import tool could not start")]
    public void A_tool_that_fails_is_cancelled_or_cannot_start_leaves_the_import_failed_and_startable(ToolRunState state, int exitCode, string failure)
    {
        ScriptedHost host = new();
        using InstalledContentImporter importer = new(host, Import);
        importer.Start();
        host.Choice = (FolderChoiceState.Chosen, "/games/Daggerfall");
        importer.Follow();

        importer.Cancel();
        Assert.True(host.CancelAsked);
        host.Run = new(state, exitCode, 0);
        importer.Follow();

        Assert.Equal(InstalledContentImportState.Failed, importer.State);
        Assert.StartsWith(failure, importer.Failure, StringComparison.Ordinal);
        importer.Start();
        Assert.Equal(InstalledContentImportState.Choosing, importer.State);
    }

    [Fact]
    public void A_cancelled_picker_returns_to_idle_and_one_that_cannot_open_says_why()
    {
        ScriptedHost host = new();
        using InstalledContentImporter importer = new(host, Import);

        importer.Start();
        host.Choice = (FolderChoiceState.Cancelled, "");
        importer.Follow();
        Assert.Equal(InstalledContentImportState.Idle, importer.State);
        Assert.Null(host.Tool);

        importer.Start();
        host.Choice = (FolderChoiceState.Unavailable, "");
        host.Diagnostic = "no portal answered";
        importer.Follow();
        Assert.Equal(InstalledContentImportState.Failed, importer.State);
        Assert.Equal("No folder picker could be shown: no portal answered", importer.Failure);
    }

    /// <summary>A host whose picker and tool run report what the test sets.</summary>
    private sealed class ScriptedHost : IHostService
    {
        internal string? PickerTitle { get; private set; }
        internal (FolderChoiceState State, string Path) Choice { get; set; } = (FolderChoiceState.Pending, "");
        internal string Diagnostic { get; set; } = "";
        internal string? Tool { get; private set; }
        internal string[] Arguments { get; private set; } = [];
        internal ToolRunReadout Run { get; set; } = new(ToolRunState.Running, 0, 0);
        internal string Output { get; set; } = "";
        internal bool ChoiceDisposed { get; private set; }
        internal bool RunDisposed { get; private set; }
        internal bool CancelAsked { get; private set; }

        public FolderChoice ChooseFolder(FolderChoiceRequest request)
        {
            PickerTitle = request.Title;
            Choice = (FolderChoiceState.Pending, "");
            return new FolderChoice(new FolderChoiceHandle(1), () => ChoiceDisposed = true);
        }

        public FolderChoiceReadout ReadFolderChoice(FolderChoice choice) => new(Choice.State);

        public ReadOnlyMemory<byte> ReadFolderChoicePath(FolderChoice choice) => Encoding.UTF8.GetBytes(Choice.Path);

        public ReadOnlyMemory<byte> ReadFolderChoiceDiagnostic(FolderChoice choice) => Encoding.UTF8.GetBytes(Diagnostic);

        public ReadOnlyMemory<byte> ReadInstalledContent(InstalledContentRequest request) => "/release"u8.ToArray();

        public ToolRun RunTool(ToolRunRequest request)
        {
            Tool = request.Tool;
            Arguments = [.. request.Arguments.ToArray().Select(argument => argument.Value)];
            return new ToolRun(new ToolRunHandle(2), () => RunDisposed = true);
        }

        public ToolRunReadout ReadToolRun(ToolRun run) => Run;

        public ReadOnlyMemory<byte> ReadToolRunOutput(ToolRun run) => Encoding.UTF8.GetBytes(Output);

        public void CancelToolRun(ToolRun run) => CancelAsked = true;

        public HostReadout Read() => throw new NotSupportedException();

        public void RequestExit(HostExitRequest request) => throw new NotSupportedException();
    }
}
