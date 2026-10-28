using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The session the product runs when no Daggerfall game can be played: the data it needs was not imported, its
/// container cannot be read, or it was imported for another build. It shows the entry screen with each problem
/// and the step that fixes it, and offers nothing to play but Import: the player chooses their Daggerfall folder,
/// the import tool converts it while its output is shown, and once the data is installed the host opens the game.
/// </summary>
/// <remarks>
/// It publishes the HUD contract the DOM reads everywhere else, with the title mode and the problems, and no
/// world, character or art: the art is itself derived from the data that is missing.
/// </remarks>
internal sealed class DaggerfallUnavailableSession : IInstalledContentImportSession
{
    private readonly UiStream _hud;
    private readonly IUiService _ui;
    private readonly IReadOnlyList<string> _problems;
    private readonly InstalledContentImporter? _import;
    private bool _reopen;

    internal DaggerfallUnavailableSession(GameUnavailableContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Problems.Count == 0) throw new ArgumentException("An unavailable session states at least one problem.", nameof(context));
        _ui = context.Engine.Ui;
        _problems = [.. context.Problems];
        if (context.Importable?.Import is { } import) _import = new InstalledContentImporter(context.Engine.Host, import);
        _hud = _ui.OpenStream(new UiStreamRequest("dagger.hud", "dagger.ui.snapshot.v1"));
    }

    /// <summary>The problems the entry screen states, in order.</summary>
    internal IReadOnlyList<string> Problems => _problems;

    public void PublishInitial() => Publish();

    /// <summary>Whether Import is offered.</summary>
    internal bool OffersImport => _import is not null;

    /// <summary>Where the offered import is; null when none is offered.</summary>
    internal InstalledContentImportState? ImportState => _import?.State;

    /// <summary>
    /// Acts on Import and Cancel, then follows the import: the screen is published again when its state or the
    /// tool's output changed, and an installed container asks the host to open the game.
    /// </summary>
    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (_import is null) return ProductUpdateResult.None;
        DaggerfallUiInput input = DaggerfallUiInput.Parse(update.Input);
        if (input.Contains(DaggerfallUiActionKind.ContentImport)) _import.Start();
        if (input.Contains(DaggerfallUiActionKind.ContentImportCancel)) _import.Cancel();
        bool acted = input.ContainsAny(DaggerfallUiActionKind.ContentImport, DaggerfallUiActionKind.ContentImportCancel);
        if (_import.Follow() || acted) Publish();
        if (_import.State == InstalledContentImportState.Installed) _reopen = true;
        return ProductUpdateResult.None;
    }

    public bool TakeReopenRequest()
    {
        bool reopen = _reopen;
        _reopen = false;
        return reopen;
    }

    private void Publish()
    {
        UiValueBuilder builder = new();
        uint root = builder.Object(
            ("resources", builder.Array([])),
            ("lastOutcome", builder.String(string.Empty)),
            ("messages", builder.Array([])),
            ("mode", builder.String("title")),
            ("composition", builder.Object(
                ("bundle", builder.String(string.Empty)),
                ("ruleset", builder.String(DaggerfallRuleset.Identity.Value)),
                ("contentPacks", builder.Array([])),
                ("tuning", builder.String(string.Empty)))),
            ("unavailable", builder.Object(
                ("title", builder.String("Daggerfall cannot start")),
                ("problems", builder.Array([.. _problems.Select(builder.String)])),
                ("import", _import is null ? builder.Null() : builder.Object(
                    ("state", builder.String(WireState(_import.State))),
                    ("status", builder.String(ImportStatus(_import))),
                    ("output", builder.String(_import.Output)))))));
        _ui.PublishProjection(new UiProjection(_hud, builder.Build(root)));
    }

    private static string WireState(InstalledContentImportState state) => state switch
    {
        InstalledContentImportState.Idle => "idle",
        InstalledContentImportState.Choosing => "choosing",
        InstalledContentImportState.Running => "running",
        InstalledContentImportState.Installed => "installed",
        _ => "failed",
    };

    /// <summary>What the screen says the import is doing.</summary>
    private static string ImportStatus(InstalledContentImporter import) => import.State switch
    {
        InstalledContentImportState.Idle => "Import converts your own copy of Daggerfall into the data the game reads. It takes about ten minutes.",
        InstalledContentImportState.Choosing => "Choose the folder Daggerfall is installed in, or its ARENA2 folder.",
        InstalledContentImportState.Running => $"Importing {import.Folder}…",
        InstalledContentImportState.Installed => "The Daggerfall data is imported. Opening the game…",
        _ => $"{import.Failure} Choose Import to try again.",
    };

    public void Dispose()
    {
        _import?.Dispose();
        _hud.Dispose();
    }
}
