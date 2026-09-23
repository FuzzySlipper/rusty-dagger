using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>The three text action families retained by the Arena2 RDB action resource.</summary>
internal enum DaggerfallDungeonTextActionKind : byte
{
    ShowText,
    ShowTextWithInput,
    DoorText,
}

/// <summary>Outcome of one text action or one semantic input submission.</summary>
internal enum DaggerfallDungeonTextOutcome : byte
{
    Presented,
    AwaitingAnswer,
    AcceptedAnswer,
    RejectedAnswer,
    Cancelled,
    SkippedDoorText,
    Busy,
    StaleSubmission,
    NoPendingAnswer,
    MissingText,
    MissingAnswers,
    InvalidAction,
}

/// <summary>
/// The projection a thin UI needs for one action. The UI owns the modal itself; this record only
/// carries resolved text and the revision needed to reject a stale submission.
/// </summary>
internal sealed record DaggerfallDungeonTextProjection(
    string ActionId,
    DaggerfallDungeonTextActionKind Kind,
    int TextId,
    string Text,
    string[] Answers,
    ulong Revision,
    bool IsHud,
    bool ClickAnywhereToClose);

/// <summary>
/// Current answer wait state. Answers are retained with the wait so restoring a current save does
/// not depend on a DOM callback or a Unity object that no longer exists.
/// </summary>
internal sealed record DaggerfallDungeonTextPendingSave(
    string ActionId,
    int TextId,
    ulong Revision,
    string[] Answers)
{
    internal DaggerfallDungeonTextPendingSave Validate()
    {
        if (string.IsNullOrWhiteSpace(ActionId)) throw new ArgumentException("A pending dungeon text action requires an action id.", nameof(ActionId));
        if (TextId < 0) throw new ArgumentOutOfRangeException(nameof(TextId));
        if (Revision == 0) throw new ArgumentOutOfRangeException(nameof(Revision));
        ArgumentNullException.ThrowIfNull(Answers);
        if (Answers.Length == 0 || Answers.Any(answer => string.IsNullOrEmpty(answer)))
            throw new ArgumentException("A pending dungeon text action requires non-empty answer strings.", nameof(Answers));
        if (Answers.Distinct(StringComparer.Ordinal).Count() != Answers.Length)
            throw new ArgumentException("A pending dungeon text action cannot repeat an answer string.", nameof(Answers));
        return this;
    }
}

/// <summary>Durable text-action state carried by the current product save.</summary>
internal sealed record DaggerfallDungeonTextSnapshot(
    ulong Revision,
    DaggerfallDungeonTextPendingSave? Pending)
{
    internal DaggerfallDungeonTextSnapshot Validate()
    {
        if (Pending is not null)
        {
            Pending.Validate();
            if (Pending.Revision != Revision)
                throw new ArgumentException("A pending dungeon text action must use the snapshot revision.", nameof(Pending));
        }
        return this;
    }
}

/// <summary>Semantic input from the text modal. The revision is the published projection revision.</summary>
internal sealed record DaggerfallDungeonTextInput(string ActionId, string Revision, string Answer);

/// <summary>
/// Result returned to the graph/session seam. <see cref="ContinueLinkedAction"/> is deliberately
/// explicit: ordinary text and door text follow the donor's eager link, while input text waits for
/// an accepted answer.
/// </summary>
internal sealed record DaggerfallDungeonTextActionResult(
    DaggerfallDungeonTextOutcome Outcome,
    DaggerfallDungeonTextProjection? Projection = null,
    DaggerfallDungeonTextPendingSave? Pending = null,
    bool ContinueLinkedAction = false,
    bool ApplyDoorTrespass = false,
    string? Diagnostic = null,
    IReadOnlyList<DaggerfallTextDiagnostic>? Diagnostics = null);

/// <summary>
/// Interprets the donor's dungeon text action leaves without owning DOM state or action-chain
/// dispatch. It resolves text through the normalized Daggerfall text set, exposes an explicit
/// pending-answer save shape, and returns the continuation decision to the action graph owner.
/// </summary>
internal sealed class DaggerfallDungeonTextActions
{
    internal const int ShowTextBaseId = 8600;
    internal const int ShowTextWithInputBaseId = 5400;
    internal const int DoorTextBaseId = 7700;

    private static readonly IReadOnlyDictionary<int, string[]> LegacyAnswers =
        new Dictionary<int, string[]>
        {
            [5404] = ["bow", "bow arrow", "crossbow", "bows", "crossbows"],
            [5406] = ["one", "1"],
            [5423] = ["benefactor", "the benefactor"],
            [5424] = ["shut up", "shutup", "shaddup"],
            [5464] = ["yes", "oK", "i agree", "y", "agreed", "done", "fine", "okay", "sure", "yep"],
        };

    private readonly DaggerfallTextResolver _text;
    private ulong _revision;
    private DaggerfallDungeonTextPendingSave? _pending;

    internal DaggerfallDungeonTextActions(DaggerfallTextResolver text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>The waiting identity currently owned by the ruleset, if one is open.</summary>
    internal DaggerfallDungeonTextPendingSave? Pending => _pending;

    /// <summary>The monotonically increasing projection/save revision.</summary>
    internal ulong Revision => _revision;

    /// <summary>
    /// Presents one text action. <paramref name="activationCount"/> is the graph's one-based
    /// durable count after admission; it is used only by DoorText's first-activation rule.
    /// </summary>
    internal DaggerfallDungeonTextActionResult Execute(
        DaggerfallDungeonActionDefinition action,
        ulong activationCount,
        DaggerfallTextContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        DaggerfallDungeonTextActionKind? kind = KindOf(action.ActionFlag);
        if (kind is null)
        {
            return new(DaggerfallDungeonTextOutcome.InvalidAction,
                Diagnostic: $"Dungeon action '{action.Id}' is not a text action (flag {action.ActionFlag}).");
        }

        if (_pending is not null)
        {
            return Busy($"Dungeon text action '{_pending.ActionId}' is waiting for an answer at revision {_pending.Revision}.");
        }

        return kind.Value switch
        {
            DaggerfallDungeonTextActionKind.ShowText => ShowText(action, context),
            DaggerfallDungeonTextActionKind.ShowTextWithInput => ShowTextWithInput(action, context),
            DaggerfallDungeonTextActionKind.DoorText => DoorText(action, activationCount, context),
            _ => new(DaggerfallDungeonTextOutcome.InvalidAction, Diagnostic: $"Dungeon action '{action.Id}' has no admitted text family."),
        };
    }

    /// <summary>
    /// Rebuilds a restored input projection without incrementing its revision or activating the
    /// action again. The caller must supply the still-loaded action definition.
    /// </summary>
    internal DaggerfallDungeonTextActionResult Resume(
        DaggerfallDungeonActionDefinition action,
        DaggerfallTextContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        if (_pending is null)
            return new(DaggerfallDungeonTextOutcome.NoPendingAnswer, Diagnostic: "No dungeon text answer is waiting to resume.");
        if (!string.Equals(_pending.ActionId, action.Id, StringComparison.Ordinal)
            || action.ActionFlag != (byte)DaggerfallDungeonActionFlag.ShowTextWithInput
            || TextId(action, DaggerfallDungeonTextActionKind.ShowTextWithInput) != _pending.TextId)
        {
            return new(DaggerfallDungeonTextOutcome.StaleSubmission,
                Pending: _pending,
                Diagnostic: $"Saved dungeon text answer '{_pending.ActionId}' does not match the loaded action '{action.Id}'.");
        }

        DaggerfallTextRenderResult rendered = ResolveResource(_pending.TextId, context);
        if (HasMissingOrMalformedText(rendered))
            return MissingText(_pending.TextId, rendered, continueLinkedAction: false, pending: _pending);

        return new(
            DaggerfallDungeonTextOutcome.AwaitingAnswer,
            Projection: new(action.Id, DaggerfallDungeonTextActionKind.ShowTextWithInput, _pending.TextId,
                rendered.Text, [.. _pending.Answers], _pending.Revision, IsHud: false, ClickAnywhereToClose: false),
            Pending: _pending);
    }

    /// <summary>
    /// Applies a semantic answer exactly once. Matching follows the donor's case-insensitive,
    /// otherwise exact comparison; whitespace is not silently normalized.
    /// </summary>
    internal DaggerfallDungeonTextActionResult Submit(DaggerfallDungeonTextInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_pending is null)
            return new(DaggerfallDungeonTextOutcome.NoPendingAnswer, Diagnostic: "No dungeon text answer is waiting.");
        if (!MatchesPending(input.ActionId, input.Revision, out DaggerfallDungeonTextPendingSave pending))
            return new(DaggerfallDungeonTextOutcome.StaleSubmission, Pending: pending,
                Diagnostic: "The dungeon text answer belongs to an older or different projection.");

        bool accepted = pending.Answers.Any(answer => string.Equals(input.Answer, answer, StringComparison.OrdinalIgnoreCase));
        _pending = null;
        AdvanceRevision();
        return accepted
            ? new(DaggerfallDungeonTextOutcome.AcceptedAnswer, ContinueLinkedAction: true,
                Diagnostic: $"Dungeon text answer accepted for '{pending.ActionId}'.")
            : new(DaggerfallDungeonTextOutcome.RejectedAnswer,
                Diagnostic: $"Dungeon text answer rejected for '{pending.ActionId}'.");
    }

    /// <summary>Revision-typed convenience overload for session callers that already parsed the UI action.</summary>
    internal DaggerfallDungeonTextActionResult Submit(string actionId, ulong revision, string answer) =>
        Submit(new(actionId, revision.ToString(CultureInfo.InvariantCulture), answer));

    /// <summary>Cancels one input modal without advancing its linked action.</summary>
    internal DaggerfallDungeonTextActionResult Cancel(string actionId, ulong revision)
    {
        if (_pending is null)
            return new(DaggerfallDungeonTextOutcome.NoPendingAnswer, Diagnostic: "No dungeon text answer is waiting.");
        if (!string.Equals(_pending.ActionId, actionId, StringComparison.Ordinal) || _pending.Revision != revision)
        {
            return new(DaggerfallDungeonTextOutcome.StaleSubmission, Pending: _pending,
                Diagnostic: "The dungeon text cancellation belongs to an older or different projection.");
        }

        string pendingActionId = _pending.ActionId;
        _pending = null;
        AdvanceRevision();
        return new(DaggerfallDungeonTextOutcome.Cancelled,
            Diagnostic: $"Dungeon text answer cancelled for '{pendingActionId}'.");
    }

    /// <summary>
    /// Explicit unload path when the session chooses transient modal state rather than save
    /// integration. It never advances a linked action.
    /// </summary>
    internal void CancelPendingOnUnload()
    {
        if (_pending is null) return;
        _pending = null;
        AdvanceRevision();
    }

    internal DaggerfallDungeonTextSnapshot Capture() =>
        new(_revision, _pending is null ? null : _pending with { Answers = [.. _pending.Answers] });

    internal void Restore(DaggerfallDungeonTextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        _revision = snapshot.Revision;
        _pending = snapshot.Pending is null
            ? null
            : snapshot.Pending with { Answers = [.. snapshot.Pending.Answers] };
    }

    private DaggerfallDungeonTextActionResult ShowText(
        DaggerfallDungeonActionDefinition action,
        DaggerfallTextContext context)
    {
        int textId = TextId(action, DaggerfallDungeonTextActionKind.ShowText);
        DaggerfallTextRenderResult rendered = ResolveResource(textId, context);
        if (HasMissingOrMalformedText(rendered))
            return MissingText(textId, rendered, continueLinkedAction: true);

        ulong revision = AdvanceRevision();
        return new(
            DaggerfallDungeonTextOutcome.Presented,
            Projection: new(action.Id, DaggerfallDungeonTextActionKind.ShowText, textId, rendered.Text, [], revision,
                IsHud: false, ClickAnywhereToClose: true),
            ContinueLinkedAction: true,
            Diagnostics: rendered.Diagnostics.Count == 0 ? null : rendered.Diagnostics);
    }

    private DaggerfallDungeonTextActionResult ShowTextWithInput(
        DaggerfallDungeonActionDefinition action,
        DaggerfallTextContext context)
    {
        int textId = TextId(action, DaggerfallDungeonTextActionKind.ShowTextWithInput);
        DaggerfallTextRenderResult rendered = ResolveResource(textId, context);
        if (HasMissingOrMalformedText(rendered))
            return MissingText(textId, rendered, continueLinkedAction: false);

        (string[] answers, IReadOnlyList<DaggerfallTextDiagnostic> diagnostics) = Answers(textId, context);
        if (answers.Length == 0)
        {
            return new(DaggerfallDungeonTextOutcome.MissingAnswers,
                Diagnostic: $"Dungeon text action '{action.Id}' has no accepted answers for text {textId}.",
                Diagnostics: diagnostics.Count == 0 ? rendered.Diagnostics : diagnostics);
        }

        ulong revision = AdvanceRevision();
        _pending = new(action.Id, textId, revision, [.. answers]);
        return new(
            DaggerfallDungeonTextOutcome.AwaitingAnswer,
            Projection: new(action.Id, DaggerfallDungeonTextActionKind.ShowTextWithInput, textId, rendered.Text, [.. answers], revision,
                IsHud: false, ClickAnywhereToClose: false),
            Pending: _pending,
            Diagnostics: CombineDiagnostics(rendered.Diagnostics, diagnostics));
    }

    private DaggerfallDungeonTextActionResult DoorText(
        DaggerfallDungeonActionDefinition action,
        ulong activationCount,
        DaggerfallTextContext context)
    {
        if (activationCount == 0)
        {
            return new(DaggerfallDungeonTextOutcome.InvalidAction,
                ContinueLinkedAction: true,
                Diagnostic: $"Dungeon DoorText action '{action.Id}' requires a one-based activation count.");
        }

        int sourceTextId = TextId(action, DaggerfallDungeonTextActionKind.DoorText);
        if (SkipDoorText(sourceTextId))
        {
            return new(DaggerfallDungeonTextOutcome.SkippedDoorText,
                ContinueLinkedAction: true,
                ApplyDoorTrespass: action.Axis > 5,
                Diagnostic: $"Dungeon DoorText source id {sourceTextId} has no donor text and skips directly to the door check.");
        }

        int textId = AliasDoorText(sourceTextId);
        if (activationCount > 1)
        {
            return new(DaggerfallDungeonTextOutcome.SkippedDoorText,
                ContinueLinkedAction: true,
                ApplyDoorTrespass: action.Axis > 5,
                Diagnostic: $"Dungeon DoorText action '{action.Id}' has already displayed its first-activation text.");
        }

        DaggerfallTextRenderResult rendered = ResolveResource(textId, context);
        if (HasMissingOrMalformedText(rendered))
            return MissingText(textId, rendered, continueLinkedAction: true);

        ulong revision = AdvanceRevision();
        return new(
            DaggerfallDungeonTextOutcome.Presented,
            Projection: new(action.Id, DaggerfallDungeonTextActionKind.DoorText, textId, rendered.Text, [], revision,
                IsHud: true, ClickAnywhereToClose: true),
            ContinueLinkedAction: true,
            Diagnostics: rendered.Diagnostics.Count == 0 ? null : rendered.Diagnostics);
    }

    private (string[] Answers, IReadOnlyList<DaggerfallTextDiagnostic> Diagnostics) Answers(int textId, DaggerfallTextContext context)
    {
        DaggerfallTextKey key = new(DaggerfallTextKind.Internal, $"answers_{textId.ToString(CultureInfo.InvariantCulture)}");
        DaggerfallTextRenderResult rendered = _text.Resolve(key, context);
        string[] answers = rendered.Text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Where(answer => answer.Length > 0)
            .ToArray();
        if (answers.Length > 0 && !HasMissingOrMalformedText(rendered))
            return (answers, rendered.Diagnostics);
        if (LegacyAnswers.TryGetValue(textId, out string[]? fallback))
            // The donor treats a missing localized list as an ordinary fallback path. Do not turn
            // that successful compatibility path into a visible text diagnostic.
            return ([.. fallback], []);
        return ([], rendered.Diagnostics);
    }

    private DaggerfallTextRenderResult ResolveResource(int textId, DaggerfallTextContext context) =>
        _text.Resolve(new DaggerfallTextKey(DaggerfallTextKind.Resource, textId.ToString(CultureInfo.InvariantCulture)), context);

    private bool MatchesPending(string actionId, string revision, out DaggerfallDungeonTextPendingSave pending)
    {
        pending = _pending!;
        return _pending is not null
            && string.Equals(_pending.ActionId, actionId, StringComparison.Ordinal)
            && ulong.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
            && parsed == _pending.Revision;
    }

    private static DaggerfallDungeonTextActionKind? KindOf(byte actionFlag) => actionFlag switch
    {
        (byte)DaggerfallDungeonActionFlag.ShowText => DaggerfallDungeonTextActionKind.ShowText,
        (byte)DaggerfallDungeonActionFlag.ShowTextWithInput => DaggerfallDungeonTextActionKind.ShowTextWithInput,
        (byte)DaggerfallDungeonActionFlag.DoorText => DaggerfallDungeonTextActionKind.DoorText,
        _ => null,
    };

    private static int TextId(DaggerfallDungeonActionDefinition action, DaggerfallDungeonTextActionKind kind) =>
        checked((kind switch
        {
            DaggerfallDungeonTextActionKind.ShowText => ShowTextBaseId,
            DaggerfallDungeonTextActionKind.ShowTextWithInput => ShowTextWithInputBaseId,
            DaggerfallDungeonTextActionKind.DoorText => DoorTextBaseId,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        }) + action.SoundIndex);

    private static int AliasDoorText(int textId) => textId is >= 7701 and <= 7704 ? 7705 : textId;

    private static bool SkipDoorText(int textId) => textId is 7700 or 7706 or 7711 or 7712 or 7715 or 7717 or 7719;

    private static bool HasMissingOrMalformedText(DaggerfallTextRenderResult rendered) =>
        rendered.Diagnostics.Any(diagnostic => diagnostic.Kind is DaggerfallTextDiagnosticKind.MissingText or DaggerfallTextDiagnosticKind.MalformedText);

    private static IReadOnlyList<DaggerfallTextDiagnostic> CombineDiagnostics(
        IReadOnlyList<DaggerfallTextDiagnostic> first,
        IReadOnlyList<DaggerfallTextDiagnostic> second)
    {
        if (first.Count == 0) return second.Count == 0 ? [] : [.. second];
        if (second.Count == 0) return [.. first];
        return [.. first, .. second];
    }

    private static DaggerfallDungeonTextActionResult MissingText(
        int textId,
        DaggerfallTextRenderResult rendered,
        bool continueLinkedAction,
        DaggerfallDungeonTextPendingSave? pending = null) =>
        new(DaggerfallDungeonTextOutcome.MissingText,
            Pending: pending,
            ContinueLinkedAction: continueLinkedAction,
            Diagnostic: $"Dungeon text id {textId} could not be resolved from the normalized text set.",
            Diagnostics: rendered.Diagnostics);

    private static DaggerfallDungeonTextActionResult Busy(string diagnostic) =>
        new(DaggerfallDungeonTextOutcome.Busy, Diagnostic: diagnostic);

    private ulong AdvanceRevision() => checked(++_revision);
}
