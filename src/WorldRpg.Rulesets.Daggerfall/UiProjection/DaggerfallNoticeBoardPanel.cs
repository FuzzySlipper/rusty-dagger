using System.Globalization;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Wire projection of one actionable lead button on an open notice board entry.</summary>
internal sealed record DaggerfallNoticeBoardLeadUiView(
    string Kind,
    string ActionLabel,
    bool PickedUp,
    string? TargetSummary = null,
    int? RewardGold = null);

/// <summary>Wire projection of one entry displayed on an open physical town/tavern notice board.</summary>
internal sealed record DaggerfallNoticeBoardEntryUiView(
    string Id,
    string Kind,
    string KindLabel,
    string Heading,
    string Body,
    DaggerfallNoticeBoardLeadUiView? Lead = null);

/// <summary>Wire projection of the open physical Town Notice Board modal (transient; zero save bytes).</summary>
internal sealed record DaggerfallNoticeBoardUiView(
    string Revision,
    string Title,
    string TownName,
    string RegionName,
    string? AuthorityName,
    string? Status,
    IReadOnlyList<DaggerfallNoticeBoardEntryUiView> Entries);

/// <summary>
/// Transient UI panel owner for physical in-world Town Notice Board interaction (#11082).
/// Opens when the player activates an in-world notice board, projects live entries from
/// <see cref="DaggerfallNoticeBoard.BuildForSite"/>, and handles direct pickup of posted leads
/// (courier delivery contracts via <see cref="DaggerfallCourierDispatches"/> and regional bounty/decree
/// leads recorded idempotently in <see cref="DaggerfallBookNotebook"/>). Holds zero save bytes of its own.
/// </summary>
internal sealed class DaggerfallNoticeBoardPanel(
    DaggerfallNoticeBoard noticeBoard,
    DaggerfallCourierDispatches courierDispatches,
    DaggerfallBookNotebook notebook,
    Func<DaggerfallSiteRecord?> activeSite,
    Func<long> currentMinute,
    PresentationState presentation)
{
    private bool _open;
    private string? _boardLabel;
    private string? _status;
    private ulong _revision;

    internal bool IsOpen => _open;

    internal DaggerfallActivationOutcome Open(string? boardLabel = null)
    {
        if (activeSite() is not { } site)
            return new(false, "There is no town notice board here.");
        _open = true;
        _boardLabel = !string.IsNullOrWhiteSpace(boardLabel) ? boardLabel : $"{site.Name} Notice Board";
        _status = null;
        _revision++;
        string message = $"You read the {_boardLabel}.";
        presentation.SetOutcome(message);
        return new(true, message);
    }

    internal void Close(string? revision = null)
    {
        if (!_open) return;
        if (revision is not null && !MatchesRevision(revision))
            return;
        _open = false;
        _status = null;
        _revision++;
    }

    internal DaggerfallActivationOutcome TakeLead(string? revision, string? entryId)
    {
        if (!_open)
            return Report(false, "The notice board is no longer open.");
        if (!MatchesRevision(revision))
            return Report(false, "The notice board has changed. Choose the posting again.");
        if (string.IsNullOrWhiteSpace(entryId) || activeSite() is not { } site)
            return Report(false, "That posted lead is no longer available.");

        DaggerfallTownNoticeBoardView view = noticeBoard.BuildForSite(site, currentMinute());
        DaggerfallNoticeEntry? entry = view.Entries.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, entryId));
        if (entry?.Lead is not { } lead || lead.Kind == DaggerfallNoticeLeadKind.None)
            return Report(false, "That posting has no actionable lead to take.");

        if (lead.Kind == DaggerfallNoticeLeadKind.CourierDelivery)
        {
            if (string.IsNullOrWhiteSpace(lead.DispatchId))
                return Report(false, "That courier dispatch is no longer available.");
            DaggerfallDispatchActionResult result = courierDispatches.AcceptDelivery(lead.DispatchId, currentMinute());
            if (result.Accepted)
                _revision++;
            return Report(result.Accepted, result.Message);
        }

        string noteText = FormatLeadNote(view, entry, lead);
        if (!notebook.TryAddNote(lead.LeadId, noteText))
            return Report(false, "You have already recorded this lead in your journal.");

        _revision++;
        string taken = lead.Kind == DaggerfallNoticeLeadKind.RegionalBounty
            ? $"Bounty lead recorded in your journal: {entry.Heading}."
            : $"Notice recorded in your journal: {entry.Heading}.";
        return Report(true, taken);
    }

    internal DaggerfallNoticeBoardUiView? Read()
    {
        if (!_open || activeSite() is not { } site)
            return null;

        DaggerfallTownNoticeBoardView view = noticeBoard.BuildForSite(site, currentMinute());
        DaggerfallNoticeBoardEntryUiView[] entries = [.. view.Entries.Select(ProjectEntry)];
        return new(
            Revision: _revision.ToString(CultureInfo.InvariantCulture),
            Title: _boardLabel ?? $"{view.TownName} Notice Board",
            TownName: view.TownName,
            RegionName: view.RegionName,
            AuthorityName: view.AuthorityName,
            Status: _status,
            Entries: entries);
    }

    private DaggerfallNoticeBoardEntryUiView ProjectEntry(DaggerfallNoticeEntry entry)
    {
        (string kindKey, string kindLabel) = entry.Kind switch
        {
            DaggerfallNoticeEntryKind.Decree => ("decree", "Decree"),
            DaggerfallNoticeEntryKind.Bounty => ("bounty", "Bounty"),
            DaggerfallNoticeEntryKind.OfficeNotice => ("office-notice", "Proclamation"),
            _ => ("sign-rumor", "Notice"),
        };

        DaggerfallNoticeBoardLeadUiView? leadView = null;
        if (entry.Lead is { Kind: not DaggerfallNoticeLeadKind.None } lead)
        {
            bool pickedUp = IsLeadPickedUp(lead);
            string leadKindKey = lead.Kind switch
            {
                DaggerfallNoticeLeadKind.CourierDelivery => "courier-delivery",
                DaggerfallNoticeLeadKind.RegionalBounty => "regional-bounty",
                _ => "public-lead",
            };
            string actionLabel = pickedUp
                ? (lead.Kind == DaggerfallNoticeLeadKind.CourierDelivery ? "Dispatch carried" : "Recorded in journal")
                : lead.ActionLabel;
            leadView = new(leadKindKey, actionLabel, pickedUp, lead.TargetSummary, lead.RewardGold);
        }

        return new(entry.Id, kindKey, kindLabel, entry.Heading, entry.Body, leadView);
    }

    private bool IsLeadPickedUp(DaggerfallNoticeLead lead)
    {
        if (lead.Kind == DaggerfallNoticeLeadKind.CourierDelivery)
        {
            return !string.IsNullOrWhiteSpace(lead.DispatchId)
                && courierDispatches.TryGet(lead.DispatchId, out DaggerfallCourierDispatchSave? dispatch)
                && dispatch?.Status == DaggerfallDispatchStatus.CarriedByPlayer;
        }
        return notebook.HasNote(lead.LeadId);
    }

    private static string FormatLeadNote(DaggerfallTownNoticeBoardView view, DaggerfallNoticeEntry entry, DaggerfallNoticeLead lead) =>
        !string.IsNullOrWhiteSpace(lead.TargetSummary)
            ? $"[{view.TownName} Notice Board] {entry.Heading} — {lead.TargetSummary}. {entry.Body}"
            : $"[{view.TownName} Notice Board] {entry.Heading} — {entry.Body}";

    private bool MatchesRevision(string? revision) =>
        string.Equals(revision, _revision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private DaggerfallActivationOutcome Report(bool applied, string message)
    {
        _status = message;
        presentation.SetOutcome(message);
        return new(applied, message);
    }
}
