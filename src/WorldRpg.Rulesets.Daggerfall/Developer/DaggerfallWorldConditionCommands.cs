using System.Globalization;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Live developer commands for authoring, inspecting, and clearing stubbed world-condition tokens and reading
/// the active town notice board (<c>world.conditions</c>, <c>world.condition-set</c>, <c>world.condition-clear</c>,
/// <c>world.notice-board</c>).
/// </summary>
internal sealed class DaggerfallWorldConditionCommands(
    DaggerfallWorldConditions conditions,
    DaggerfallScrutinyPolicy scrutiny,
    DaggerfallNoticeBoard noticeBoard,
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
}
