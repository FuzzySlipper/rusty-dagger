using Rusty.Engine;

namespace WorldRpg.Kit;

/// <summary>Where an entry-screen import is.</summary>
public enum InstalledContentImportState
{
    /// <summary>Nothing has been started, or the player cancelled the folder picker.</summary>
    Idle,
    /// <summary>The folder picker is open.</summary>
    Choosing,
    /// <summary>The tool is running on the chosen folder.</summary>
    Running,
    /// <summary>The tool exited cleanly, so the container is installed.</summary>
    Installed,
    /// <summary>The picker could not be shown, or the tool failed, was cancelled or could not start.</summary>
    Failed,
}

/// <summary>
/// Produces a container from the player's own files: asks the host for a folder, runs the product's declared tool
/// on it in the installed-content directory, and follows the tool's output and outcome. The host does both on its
/// own threads; <see cref="Follow"/> reads them once per update, so no product thread is needed.
/// </summary>
/// <remarks>
/// Which tool, what it is asked and what the container means are the ruleset's (<see cref="InstalledContentImport"/>);
/// opening the installed container again is the host's. Disposing a running import cancels its tool.
/// </remarks>
public sealed class InstalledContentImporter : IDisposable
{
    private readonly IHostService _host;
    private readonly InstalledContentImport _import;
    private FolderChoice? _choice;
    private ToolRun? _run;
    private ulong _shownLines;

    public InstalledContentImporter(IHostService host, InstalledContentImport import)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _import = import ?? throw new ArgumentNullException(nameof(import));
    }

    public InstalledContentImportState State { get; private set; } = InstalledContentImportState.Idle;

    /// <summary>The folder the tool runs on, once chosen.</summary>
    public string Folder { get; private set; } = "";

    /// <summary>Why the import failed, once <see cref="InstalledContentImportState.Failed"/>.</summary>
    public string Failure { get; private set; } = "";

    /// <summary>The tool's recent output lines, oldest first.</summary>
    public string Output { get; private set; } = "";

    /// <summary>Whether a picker or a tool is going, so the import can neither start again nor be left.</summary>
    public bool Busy => State is InstalledContentImportState.Choosing or InstalledContentImportState.Running;

    /// <summary>Opens the folder picker; ignored while the import is busy or has installed the container.</summary>
    public void Start()
    {
        if (Busy || State == InstalledContentImportState.Installed) return;
        _choice = _host.ChooseFolder(_import.FolderPrompt);
        (State, Failure, Output, _shownLines) = (InstalledContentImportState.Choosing, "", "", 0);
    }

    /// <summary>Asks a running tool to stop; it reads as failed once it has.</summary>
    public void Cancel()
    {
        if (_run is not null) _host.CancelToolRun(_run);
    }

    /// <summary>Reads the picker and the tool as they were when this product call began; true when anything shown changed.</summary>
    public bool Follow() => FollowChoice() | FollowRun();

    private bool FollowChoice()
    {
        if (_choice is null) return false;
        FolderChoiceReadout readout = _host.ReadFolderChoice(_choice);
        if (readout.State == FolderChoiceState.Pending) return false;
        string folder = _host.ReadPathText(_choice);
        string diagnostic = _host.ReadDiagnosticText(_choice);
        _choice.Dispose();
        _choice = null;
        switch (readout.State)
        {
            case FolderChoiceState.Chosen:
                Folder = folder;
                _run = _host.RunTool(_import.Tool, [.. _import.Arguments(folder, _host.ReadInstalledContentText())]);
                State = InstalledContentImportState.Running;
                break;
            case FolderChoiceState.Cancelled:
                State = InstalledContentImportState.Idle;
                break;
            default:
                Fail($"No folder picker could be shown: {diagnostic}");
                break;
        }
        return true;
    }

    private bool FollowRun()
    {
        if (_run is null) return false;
        ToolRunReadout readout = _host.ReadToolRun(_run);
        bool changed = readout.OutputLines != _shownLines;
        if (changed)
        {
            Output = _host.ReadOutputText(_run);
            _shownLines = readout.OutputLines;
        }
        if (readout.State == ToolRunState.Running) return changed;
        _run.Dispose();
        _run = null;
        if (readout.State == ToolRunState.Exited && readout.ExitCode == 0) State = InstalledContentImportState.Installed;
        else Fail(readout.State switch
        {
            ToolRunState.Exited => $"The import failed (exit code {readout.ExitCode}); its last output says why.",
            ToolRunState.Cancelled => "The import was cancelled.",
            _ => "The import tool could not start; its output says why.",
        });
        return true;
    }

    private void Fail(string failure) => (State, Failure) = (InstalledContentImportState.Failed, failure);

    public void Dispose()
    {
        _choice?.Dispose();
        _run?.Dispose();
        (_choice, _run) = (null, null);
    }
}
