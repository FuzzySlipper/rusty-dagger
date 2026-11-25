using System.Globalization;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Live developer commands for authoring, inspecting, and clearing stubbed world-condition tokens, reading
/// the active town notice board, inspecting word spread, and staging/inspecting board-game courier dispatches.
/// </summary>
internal sealed class DaggerfallWorldConditionCommands(
    DaggerfallWorldConditions conditions,
    DaggerfallScrutinyPolicy scrutiny,
    DaggerfallNoticeBoard noticeBoard,
    DaggerfallWordSpread wordSpread,
    DaggerfallCourierDispatches courierDispatches,
    DaggerfallDefinitions definitions,
    DaggerfallSiteContext site,
    Func<DaggerfallCalendar> calendar) : IDeveloperWorldConditionCommands
{
    public DebugCommandResult ListConditions()
    {
        int? region = site.Region;
        int day = checked((int)calendar().DayNumber);
        IReadOnlyList<DaggerfallWorldConditionToken> active = region is int r
            ? conditions.ActiveForRegion(r, day)
            : [.. conditions.AuthoredTokens.Where(t => !t.IsExpired(day)).OrderBy(t => t.TokenId, StringComparer.Ordinal)];
        DaggerfallGateScrutinyAssessment? gate = region is int gr ? scrutiny.EvaluateGateScrutiny(gr, site.ActiveSite) : null;

        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            if (region is int reg) w.WriteNumber("region", reg);
            else w.WriteNull("region");
            w.WriteNumber("day", day);
            if (gate is not null)
            {
                w.WriteString("gateScrutiny", gate.Level.ToString().ToLowerInvariant());
                w.WriteBoolean("watchChallenge", gate.WatchChallengeActive);
                w.WriteNumber("wantedThreshold", gate.EffectiveWantedThreshold);
            }
            w.WriteStartArray("conditions");
            foreach (DaggerfallWorldConditionToken token in active)
            {
                w.WriteStartObject();
                w.WriteString("tokenId", token.TokenId);
                w.WriteString("kind", DaggerfallWorldConditionToken.KindSlug(token.Kind));
                if (token.Region is int tr) w.WriteNumber("region", tr);
                else w.WriteNull("region");
                w.WriteNumber("severity", token.Severity);
                if (token.SourceFactionId is int sf) w.WriteNumber("sourceFactionId", sf);
                if (token.TargetFactionId is int tf) w.WriteNumber("targetFactionId", tf);
                if (token.SubjectTag is not null) w.WriteString("subjectTag", token.SubjectTag);
                if (token.ExpiresDay is int exp) w.WriteNumber("expiresDay", exp);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult SetCondition(string kind, int severity, string tag, int days)
    {
        if (site.Region is not int region)
            return DaggerfallDeveloperReplies.Refused("No active region is loaded.");
        if (!DaggerfallWorldConditionToken.TryParseKind(kind, out DaggerfallWorldConditionKind parsedKind))
            return DaggerfallDeveloperReplies.Refused("Unknown condition kind. Use grain-shortage, border-tension, monster-surge, crime-crackdown, guild-embargo, succession-crisis, plague-scare, or decree.");
        if (severity is < DaggerfallWorldConditionToken.MinSeverity or > DaggerfallWorldConditionToken.MaxSeverity)
            return DaggerfallDeveloperReplies.Refused($"Severity must be between {DaggerfallWorldConditionToken.MinSeverity} and {DaggerfallWorldConditionToken.MaxSeverity}.");
        if (days < 0)
            return DaggerfallDeveloperReplies.Refused("Duration days cannot be negative (use 0 for no expiry).");

        string trimmedTag = tag?.Trim() ?? string.Empty;
        bool hasTag = trimmedTag.Length > 0 && !trimmedTag.Equals("none", StringComparison.OrdinalIgnoreCase);
        int? sourceFactionId = null;
        string? subjectTag = null;
        if (hasTag)
        {
            if (int.TryParse(trimmedTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int factionId) && factionId > 0)
                sourceFactionId = factionId;
            else
                subjectTag = trimmedTag;
        }

        int day = checked((int)calendar().DayNumber);
        string tokenId = DaggerfallWorldConditions.RegionTokenId(parsedKind, region, hasTag ? trimmedTag.ToLowerInvariant() : null);
        DaggerfallWorldConditionToken token = new(
            TokenId: tokenId,
            Kind: parsedKind,
            Region: region,
            SourceFactionId: sourceFactionId,
            TargetFactionId: null,
            SubjectTag: subjectTag,
            Severity: severity,
            StartedDay: day,
            ExpiresDay: days > 0 ? checked(day + days) : null);
        conditions.Set(token);
        return ListConditions();
    }

    public DebugCommandResult ClearCondition(string target)
    {
        string trimmed = target?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Clear();
            return ListConditions();
        }
        if (!conditions.Remove(trimmed))
            return DaggerfallDeveloperReplies.Refused($"No authored world condition '{trimmed}' is active.");
        return ListConditions();
    }

    public DebugCommandResult NoticeBoard()
    {
        if (site.ActiveSite is not { } active)
            return DaggerfallDeveloperReplies.Refused("No active site is loaded.");
        DaggerfallTownNoticeBoardView view = noticeBoard.BuildForSite(active, calendar().MinuteNumber);
        return DebugCommandResult.Success(DaggerfallNoticeBoard.FormatBoardText(view));
    }

    public DebugCommandResult ListWordSpread()
    {
        DaggerfallSiteRecord? active = site.ActiveSite;
        long nowMinute = calendar().MinuteNumber;
        int nowDay = checked((int)calendar().DayNumber);
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("instantBubblePixels", wordSpread.Tuning.InstantNewsBubblePixels);
            w.WriteNumber("courierSpeedPixelsPerHour", wordSpread.Tuning.CourierSpeedPixelsPerHour);
            w.WriteNumber("minute", nowMinute);
            w.WriteStartArray("wavefronts");
            foreach (DaggerfallWordWavefrontSave wf in wordSpread.Wavefronts.OrderBy(v => v.EmittedAtMinute).ThenBy(v => v.EventId, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("eventId", wf.EventId);
                w.WriteString("kind", wf.Kind.ToString());
                w.WriteNumber("originRegion", wf.OriginRegion);
                w.WriteNumber("delta", wf.Delta);
                if (wf.FactionId is int fid) w.WriteNumber("factionId", fid);
                if (wf.GuildRank is int rank) w.WriteNumber("guildRank", rank);
                w.WriteBoolean("intercepted", wf.CourierIntercepted);
                if (active is not null)
                {
                    DaggerfallWordArrivalEvaluation eval = wordSpread.EvaluateAt(wf, active, nowMinute, nowDay);
                    w.WriteString("statusAtActiveSite", eval.Status.ToString());
                    w.WriteNumber("distancePixels", Math.Round(eval.DistancePixels, 2));
                    w.WriteNumber("disruptionMultiplier", Math.Round(eval.CourierDisruptionMultiplier, 2));
                    w.WriteNumber("remainingMinutes", eval.RemainingMinutes == long.MaxValue ? -1 : eval.RemainingMinutes);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult ListDispatches() =>
        DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("dispatches");
            foreach (DaggerfallCourierDispatchSave d in courierDispatches.All.OrderBy(v => v.DispatchedAtMinute).ThenBy(v => v.DispatchId, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("dispatchId", d.DispatchId);
                w.WriteString("seatId", d.SeatId);
                w.WriteString("actionKind", d.ActionKind);
                w.WriteString("status", d.Status.ToString());
                w.WriteNumber("originRegion", d.OriginSite.Require().Region);
                w.WriteNumber("destinationRegion", d.DestinationSite.Require().Region);
                w.WriteNumber("expectedArrivalMinute", d.ExpectedArrivalMinute);
                if (d.CourierActorId is long cid) w.WriteNumber("courierActorId", cid);
                if (d.OverlandBandId is not null) w.WriteString("overlandBandId", d.OverlandBandId);
                w.WriteBoolean("deliveredByPlayer", d.DeliveredByPlayer);
                w.WriteBoolean("revealed", d.Revealed);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("resolutions");
            foreach (DaggerfallDispatchResolution r in courierDispatches.Resolutions)
            {
                w.WriteStartObject();
                w.WriteString("dispatchId", r.DispatchId);
                w.WriteString("seatId", r.SeatId);
                w.WriteString("actionKind", r.ActionKind);
                w.WriteString("outcome", r.Outcome.ToString());
                w.WriteBoolean("moveCancelled", r.MoveCancelled);
                w.WriteBoolean("moveAccelerated", r.MoveAccelerated);
                w.WriteBoolean("moveRevealed", r.MoveRevealed);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });

    public DebugCommandResult SendDispatch(string seatId, string actionKind, int destinationRegion)
    {
        if (site.ActiveSite is not { } origin)
            return DaggerfallDeveloperReplies.Refused("No active site is loaded.");
        if (string.IsNullOrWhiteSpace(seatId) || string.IsNullOrWhiteSpace(actionKind))
            return DaggerfallDeveloperReplies.Refused("Seat id and action kind are required.");
        if (destinationRegion is < 0 or > 61)
            return DaggerfallDeveloperReplies.Refused("Destination region must be between 0 and 61.");

        DaggerfallSiteRecord? destination = definitions.Locations.Records
            .Where(r => r.Id.Region == destinationRegion && r.Id != origin.Id && r.Exterior is not null)
            .OrderByDescending(r => r.Kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage)
            .ThenBy(r => r.Id.Index)
            .FirstOrDefault();
        if (destination is null)
            return DaggerfallDeveloperReplies.Refused($"No exterior town found in region {destinationRegion}.");

        long nowMinute = calendar().MinuteNumber;
        int nowDay = checked((int)calendar().DayNumber);
        string trimmedSeat = seatId.Trim();
        string trimmedAction = actionKind.Trim();
        int? sourceFactionId = DaggerfallBoardSeatCatalog.TryGetBySeatId(trimmedSeat, out DaggerfallBoardSeatDefinition? seat) && seat is not null
            ? seat.OfficeFactionId
            : null;
        string dispatchId = string.Create(CultureInfo.InvariantCulture, $"dev-dispatch:{trimmedSeat}:{nowMinute}:{courierDispatches.Count + 1}");
        string summary = $"{trimmedSeat} {trimmedAction} orders bound for {destination.Name}";
        _ = courierDispatches.StageDispatch(
            dispatchId: dispatchId,
            seatId: trimmedSeat,
            actionKind: trimmedAction,
            summary: summary,
            origin: origin,
            destination: destination,
            currentMinute: nowMinute,
            currentDay: nowDay,
            sourceFactionId: sourceFactionId);
        return ListDispatches();
    }
}
