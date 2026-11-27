using System.Globalization;
using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Kind of institutional office transition triggered by a quest task outcome.</summary>
internal enum DaggerfallQuestOfficeActionKind { Depose, DeclareRegency, Restore }

/// <summary>One quest-driven office transition routed into <see cref="DaggerfallOfficeLedger"/>.</summary>
internal sealed record DaggerfallQuestOfficeAction(
    DaggerfallQuestOfficeActionKind Kind,
    long? TargetActorId,
    int? TargetFactionId,
    long? RegentActorId,
    int? RegentFactionId,
    string SourceFile,
    string InstanceId);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly Regex ChangeRepute = Header(@"^change\s+repute\s+with\s+(?<person>[a-zA-Z0-9_.-]+)\s+by\s+(?<amount>[+-]\d+)$");
    private static readonly Regex LegalRepute = Header(@"^legal\s+repute\s+(?<amount>[+-]?\d+)$");
    private static readonly Regex ReputeExceeds = Header(@"^repute\s+with\s+(?<person>[a-zA-Z0-9_.-]+)\s+exceeds\s+(?<amount>\d+)\s+do\s+(?<task>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex WhenRepute = Header(@"^when\s+repute\s+with\s+(?<person>[a-zA-Z0-9_.-]+)\s+is\s+at\s+least\s+(?<amount>\d+)$");
    private static readonly Regex SetCrime = Header(@"^setplayercrime\s+(?<crime>[a-zA-Z_]+)$");
    private static readonly Regex DeposeOffice = Header(@"^(?:depose|exile)\s+(?<person>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex DeclareRegency = Header(@"^declare\s+regency\s+(?<person>[a-zA-Z0-9_.-]+)(?:\s+under\s+(?<regent>[a-zA-Z0-9_.-]+))?$");
    private static readonly Regex RestoreOffice = Header(@"^restore\s+office\s+(?<person>[a-zA-Z0-9_.-]+)$");
    private static DaggerfallQuestTaskOperation? CompileSocial(string line, int sourceLine)
    {
        foreach (var (pattern, kind) in new[] { (ChangeRepute, DaggerfallQuestTaskOperationKind.ChangeRepute),
            (LegalRepute, DaggerfallQuestTaskOperationKind.LegalRepute), (ReputeExceeds, DaggerfallQuestTaskOperationKind.ReputeExceeds),
            (WhenRepute, DaggerfallQuestTaskOperationKind.WhenRepute), (SetCrime, DaggerfallQuestTaskOperationKind.SetCrime),
            (DeposeOffice, DaggerfallQuestTaskOperationKind.DeposeOffice), (DeclareRegency, DaggerfallQuestTaskOperationKind.DeclareRegency),
            (RestoreOffice, DaggerfallQuestTaskOperationKind.RestoreOffice) })
        {
            var match = pattern.Match(line);
            if (!match.Success) continue;
            var targets = new List<string>();
            if (match.Groups["person"].Success) targets.Add(Canonical(match.Groups["person"].Value));
            if (match.Groups["regent"].Success) targets.Add(Canonical(match.Groups["regent"].Value));
            if (match.Groups["task"].Success) targets.Add(Canonical(match.Groups["task"].Value));
            if (match.Groups["crime"].Success) targets.Add(match.Groups["crime"].Value);
            return new(kind, sourceLine, line, targets.ToArray(), [], null,
                Step: match.Groups["amount"].Success ? int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture) : null);
        }
        return null;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private DaggerfallSocialState? _social;
    private DaggerfallCrimeState? _crime;
    private Func<int?>? _socialRegion;
    private Action<int>? _admitInvitationGuild;
    private Action<long, int>? _adjustQuestorDisposition;
    private Action<int, bool, long?>? _factionQuestSettled;
    private Func<DaggerfallQuestOfficeAction, bool>? _applyOfficeAction;
    internal void BindSocial(DaggerfallSocialState social, DaggerfallCrimeState crime, Func<int?> region)
    { _social = social; _crime = crime; _socialRegion = region; }
    internal void BindGuildInvitationAdmission(Action<int> admit) => _admitInvitationGuild = admit;
    internal void BindQuestorDisposition(Action<long, int> adjust) => _adjustQuestorDisposition = adjust;
    /// <summary>Who hears that a quest given for a faction settled: its faction, whether it succeeded and its questor.</summary>
    internal void BindFactionQuestSettled(Action<int, bool, long?> settled) => _factionQuestSettled = settled;
    /// <summary>Applies quest-driven office deposition, regency, or exile restoration through <see cref="DaggerfallOfficeLedger"/>.</summary>
    internal void BindOfficeActions(Func<DaggerfallQuestOfficeAction, bool> applyOfficeAction) => _applyOfficeAction = applyOfficeAction;

    bool IDaggerfallQuestTaskLifecycle.SocialAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        if (operation.Kind is DaggerfallQuestTaskOperationKind.DeposeOffice
            or DaggerfallQuestTaskOperationKind.DeclareRegency
            or DaggerfallQuestTaskOperationKind.RestoreOffice)
        {
            Func<DaggerfallQuestOfficeAction, bool> handler = _applyOfficeAction
                ?? throw new NotSupportedException("No canonical quest office owner is composed.");
            (long? targetActorId, int? targetFactionId) = ResolveQuestPersonTarget(instance, operation.Targets[0]);
            (long? regentActorId, int? regentFactionId) = operation.Targets.Length > 1
                ? ResolveQuestPersonTarget(instance, operation.Targets[1])
                : (null, null);
            DaggerfallQuestOfficeActionKind actionKind = operation.Kind switch
            {
                DaggerfallQuestTaskOperationKind.DeposeOffice => DaggerfallQuestOfficeActionKind.Depose,
                DaggerfallQuestTaskOperationKind.DeclareRegency => DaggerfallQuestOfficeActionKind.DeclareRegency,
                _ => DaggerfallQuestOfficeActionKind.Restore,
            };
            return handler(new DaggerfallQuestOfficeAction(
                actionKind,
                targetActorId,
                targetFactionId,
                regentActorId,
                regentFactionId,
                instance.SourceFile,
                instance.InstanceId));
        }

        var social = _social ?? throw new NotSupportedException("No canonical quest social owner is composed.");
        switch (operation.Kind)
        {
            case DaggerfallQuestTaskOperationKind.ChangeRepute:
                social.ChangeFactionReputation(QuestFaction(instance, operation.Targets[0], false), operation.Step!.Value, DaggerfallFactionReputationChange.Propagate);
                return true;
            case DaggerfallQuestTaskOperationKind.LegalRepute:
                if (_socialRegion?.Invoke() is not int region) throw new NotSupportedException("Quest legal reputation needs the current region.");
                social.ChangeRegionalReputation(region, operation.Step!.Value);
                return true;
            case DaggerfallQuestTaskOperationKind.ReputeExceeds:
            case DaggerfallQuestTaskOperationKind.WhenRepute:
                return social.FactionReputation(QuestFaction(instance, operation.Targets[0], operation.Kind == DaggerfallQuestTaskOperationKind.WhenRepute)) >= operation.Step!.Value;
            case DaggerfallQuestTaskOperationKind.SetCrime:
                string name = operation.Targets[0].Replace("_", "", StringComparison.Ordinal);
                DaggerfallCrimeKind? crime = name.Equals("None", StringComparison.OrdinalIgnoreCase) ? null
                    : Enum.TryParse<DaggerfallCrimeKind>(name, true, out var parsed) && Enum.IsDefined(parsed) ? parsed
                    : throw new NotSupportedException($"Quest crime '{operation.Targets[0]}' is not a known crime.");
                (_crime ?? throw new NotSupportedException("No canonical quest crime owner is composed.")).SetScriptedCrime(crime);
                return true;
            default: throw new ArgumentException("This operation is not a quest social action.");
        }
    }

    private (long? ActorId, int? FactionId) ResolveQuestPersonTarget(DaggerfallQuestRuntimeInstance instance, string symbol)
    {
        DaggerfallQuestResourceState? resource = instance.Resources.SingleOrDefault(r => r.Symbol == symbol);
        if (resource?.SelectedPerson is { } person)
        {
            long? actorId = person.OfficeActorId
                ?? person.QuestorId
                ?? (resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Actor && resource.Binding.ActorIds.Length > 0
                    ? (long?)resource.Binding.ActorIds[0]
                    : null)
                ?? (!string.IsNullOrWhiteSpace(person.CastId)
                    ? (long?)DaggerfallPeopleSlots.CastIndividualIdentity(person.CastId)
                    : null);
            int? factionId = person.OfficeFactionId is > 0 ? person.OfficeFactionId
                : person.FactionId > 0 ? person.FactionId : null;
            return (actorId, factionId);
        }
        if (resource is not null
            && resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Actor
            && resource.Binding.ActorIds.Length > 0)
        {
            return (resource.Binding.ActorIds[0], null);
        }
        try
        {
            var factionRow = _definitions.QuestSources.Tables.ActorItemTables.Factions.Resolve(symbol);
            if (factionRow.P3 > 0)
                return (null, factionRow.P3);
        }
        catch (KeyNotFoundException)
        {
        }
        throw new NotSupportedException($"Quest office action requires selected Person or known faction '{symbol}'.");
    }

    private int QuestFaction(DaggerfallQuestRuntimeInstance instance, string symbol, bool individual)
    {
        int faction;
        if (individual)
        {
            try { faction = _definitions.QuestSources.Tables.ActorItemTables.Factions.Resolve(symbol).P3; }
            catch (KeyNotFoundException error) { throw new NotSupportedException($"Quest individual '{symbol}' is unavailable: {error.Message}"); }
        }
        else faction = instance.Resources.SingleOrDefault(resource => resource.Symbol == symbol)?.SelectedPerson?.FactionId
            ?? throw new NotSupportedException($"Quest reputation requires selected Person '{symbol}'.");
        if (!_definitions.Factions.Factions.TryGetValue(faction, out var definition) || individual && definition.Type != 4)
            throw new NotSupportedException($"Quest reputation target '{symbol}' has no {(individual ? "individual " : "")}faction definition.");
        return faction;
    }

    private void SettleFaction(DaggerfallQuestRuntimeInstance instance)
    {
        if (instance.FactionSettled) return;
        if (instance.FactionId != 0)
        {
            (_social ?? throw new InvalidOperationException("Quest retirement requires the canonical social owner."))
                .ChangeFactionReputation(instance.FactionId, instance.Succeeded == true ? 5 : -2, DaggerfallFactionReputationChange.Propagate);
            _factionQuestSettled?.Invoke(instance.FactionId, instance.Succeeded == true, instance.QuestorId);
        }
        if (instance.QuestorId is long questorId && questorId > 0)
            _adjustQuestorDisposition?.Invoke(questorId,
                instance.Succeeded == true
                    ? DaggerfallIntroductions.QuestSuccessDispositionBonus
                    : DaggerfallIntroductions.QuestFailureDispositionPenalty);
        if (instance.Succeeded == true && TryResolveInvitationGuildFaction(instance, out int invitedGuild))
            _admitInvitationGuild?.Invoke(invitedGuild);
        instance.FactionSettled = true;
    }

    private static bool TryResolveInvitationGuildFaction(DaggerfallQuestRuntimeInstance instance, out int factionId)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(instance.SourceFile, "O0A0AL00.txt")
            || StringComparer.Ordinal.Equals(instance.InstanceId, "crime-invitation:O0A0AL00"))
        {
            factionId = Guilds.DaggerfallConcreteGuildCatalog.ThievesFactionId;
            return true;
        }
        if (StringComparer.OrdinalIgnoreCase.Equals(instance.SourceFile, "L0A01L00.txt")
            || StringComparer.Ordinal.Equals(instance.InstanceId, "crime-invitation:L0A01L00"))
        {
            factionId = Guilds.DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId;
            return true;
        }
        if (instance.InstanceId.StartsWith("crime-invitation:", StringComparison.Ordinal) && instance.FactionId != 0)
        {
            factionId = instance.FactionId;
            return true;
        }
        factionId = 0;
        return false;
    }
}
