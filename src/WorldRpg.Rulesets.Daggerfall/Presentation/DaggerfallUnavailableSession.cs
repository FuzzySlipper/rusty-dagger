using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The session the product runs when no Daggerfall game can be played: the data it needs was not imported, its
/// container cannot be read, or it was imported for another build. It shows the entry screen with each problem
/// and the step that fixes it, and offers nothing to play.
/// </summary>
/// <remarks>
/// It publishes the HUD contract the DOM reads everywhere else, with the title mode and the problems, and no
/// world, character or art: the art is itself derived from the data that is missing.
/// </remarks>
internal sealed class DaggerfallUnavailableSession : IGameSession
{
    private readonly UiStream _hud;
    private readonly IUiService _ui;
    private readonly IReadOnlyList<string> _problems;

    internal DaggerfallUnavailableSession(GameUnavailableContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Problems.Count == 0) throw new ArgumentException("An unavailable session states at least one problem.", nameof(context));
        _ui = context.Engine.Ui;
        _problems = [.. context.Problems];
        _hud = _ui.OpenStream(new UiStreamRequest("dagger.hud", "dagger.ui.snapshot.v1"));
    }

    /// <summary>The problems the entry screen states, in order.</summary>
    internal IReadOnlyList<string> Problems => _problems;

    public void PublishInitial() => Publish();

    /// <summary>Nothing changes while the product runs: the screen stays as first published.</summary>
    public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;

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
                ("problems", builder.Array([.. _problems.Select(builder.String)])))));
        _ui.PublishProjection(new UiProjection(_hud, builder.Build(root)));
    }

    public void Dispose() => _hud.Dispose();
}
